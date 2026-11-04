using Microsoft.AspNetCore.Components;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Sending.Ui;

public partial class DailyResendPanel
{
    [Parameter] public bool InitiallyOpen { get; set; }
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private string _day = "", _error = "", _dateError = "", _refreshError = "";
    private DailyResendSummary? _summary;
    private DateTime? _updatedAtUtc;
    private bool _busy, _refreshing, _disposed, _confirmationFailed;
    private int _revision;
    private Task? _poll;

    public DailyResendPanel() => _token = _lifetime.Token;

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) _poll = Poll();
        return Task.CompletedTask;
    }

    private void DateChanged()
    {
        _revision++;
        _summary = null;
        _updatedAtUtc = null;
        _error = _dateError = _refreshError = "";
        _confirmationFailed = false;
    }

    private Task Begin()
    {
        if (_disposed || _busy) return Task.CompletedTask;
        Result<string> date = DailyResendDate.Parse(_day, Clock.GetUtcNow().UtcDateTime);
        if (date.IsFailure)
        {
            DateChanged();
            _dateError = date.Error!.Message;
            return Task.CompletedTask;
        }
        string day = date.Value;
        DateChanged();
        return RunAction(() => Service.Begin(day, _token));
    }

    private Task Confirm()
    {
        if (_disposed || _busy || _summary is null || _summary.Remaining == 0 ||
            _summary.State is not (DailyResendState.Ready or DailyResendState.Paused)) return Task.CompletedTask;
        Guid id = _summary.Id;
        return RunAction(() => Service.Confirm(id, _token), confirmation: true);
    }

    private async Task RunAction(Func<Task<UiResult<DailyResendSummary>>> operation, bool confirmation = false)
    {
        int revision = ++_revision; // Invalidate a poll already in flight before confirming.
        _busy = true;
        _error = _refreshError = "";
        try
        {
            UiResult<DailyResendSummary> result = await operation();
            if (_disposed || revision != _revision) return;
            if (result.IsSuccess && result.Value is not null) Apply(result.Value);
            else
            {
                _error = result.ErrorMessage ?? "عملیات انجام نشد. برای بازیابی نتیجه، همان روز را دوباره بررسی کنید.";
                _confirmationFailed = confirmation;
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        finally { _busy = false; }
    }

    private async Task Refresh()
    {
        if (_disposed || _summary is null || _busy || _refreshing) return;
        Guid id = _summary.Id;
        int revision = _revision;
        _refreshing = true;
        try
        {
            UiResult<DailyResendSummary> result = await Service.Status(id, _token);
            if (_disposed || revision != _revision || _summary?.Id != id) return;
            if (result.IsSuccess && result.Value is not null)
            {
                Apply(result.Value);
                _refreshError = "";
                if (_confirmationFailed && result.Value.State is DailyResendState.Running or DailyResendState.Completed)
                {
                    _error = "";
                    _confirmationFailed = false;
                }
            }
            else _refreshError = result.ErrorMessage ?? "دریافت وضعیت ناموفق بود.";
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
        finally { _refreshing = false; }
    }

    private void Apply(DailyResendSummary summary)
    {
        _summary = summary;
        _updatedAtUtc = Clock.GetUtcNow().UtcDateTime;
    }

    private async Task Poll()
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(_token))
                await InvokeAsync(async () =>
                {
                    if (_summary is null || (_summary.State is not (DailyResendState.Preparing or DailyResendState.Running) && _summary.Pending == 0)) return;
                    await Refresh();
                    if (!_disposed) StateHasChanged();
                });
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
    }

    public static string StateLabel(DailyResendState state) => state switch
    {
        DailyResendState.Preparing => "در حال بررسی پیام‌ها و هزینه…",
        DailyResendState.Ready => "آمادهٔ تأیید هزینه",
        DailyResendState.Running => "ثبت ارسال مجدد در حال انجام است.",
        DailyResendState.Paused => "عملیات متوقف است؛ برای ادامه، تأیید مجدد مدیر لازم است.",
        _ => "ثبت عملیات پایان یافت؛ نتیجه تحویل را جداگانه پیگیری کنید.",
    };

    public static string SkipLabel(string code) => code switch
    {
        "sending.daily_ambiguous_recipient" => "چند پیام برای یک شماره در همان روز",
        "sending.resend_body_missing" => "متن پیام دیگر موجود نیست",
        "sending.daily_previously_resent" or "sending.daily_already_resent" => "ارسال مجدد قبلاً ثبت شده است",
        "sending.daily_unsafe" => "وضعیت پیام یا سابقه گیرنده پس از بررسی تغییر کرده است",
        "sending.resend_price_changed" => "هزینه نسبت به مبلغ تأییدشده تغییر کرده است",
        "sending.no_active_tariff" or "sending.no_tariff_rate_band" => "تعرفه قابل استفاده وجود ندارد",
        "sending.message_not_found" => "پیام اصلی دیگر موجود نیست",
        _ => "اعتبارسنجی مجدد ارسال ناموفق بود",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _revision++;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
