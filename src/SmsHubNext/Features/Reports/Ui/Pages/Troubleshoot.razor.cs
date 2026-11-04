using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Reports.Ui.Pages;

public partial class Troubleshoot
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stack<MessageSearchCursor?> _history = new();
    private int _step = 1;
    private string _mobile = string.Empty;
    private string _range = "week";
    private string _from = string.Empty;
    private string _to = string.Empty;
    private string _fromTime = "۰۰:۰۰";
    private string _toTime = "۲۳:۵۹";
    private bool _loading, _focus, _copyFailed;
    private string? _error, _success, _previewError;
    private ElementReference _heading;
    private SearchMessagesRequest? _query;
    private MessageSearchCursor? _next;
    private IReadOnlyList<MessageSearchRow> _items = [];
    private TroubleshootingReport? _report;
    private ResendMessagePreview? _preview;

    private string TechnicalReport => _report is null ? string.Empty :
        TroubleshootingText.Redact(MessageDiagnosticText.Copy(_report.Diagnostics)) +
        "\nخطاهای دارای شناسه صریح همین پیام (فایل‌های موجود، حداکثر ۳۰ روز):\n" +
        (_report.LogError is not null ? "خطا در خواندن فایل: " + _report.LogError :
            _report.Errors.Count == 0 ? "خطای مرتبطی پیدا نشد؛ نبود خطا اثبات نمی‌شود." :
            string.Join("\n", _report.Errors.Select(item => MessageDiagnosticText.Time(item.Timestamp.UtcDateTime) + "\n" + item.Message + "\n" + item.Exception)));

    protected override void OnInitialized() => SetRange();

    private static string StepTitle(int step) => step switch
    {
        1 => "شماره و بازه بررسی", 2 => "انتخاب پیام", 3 => "بررسی و تشخیص", _ => "اقدام و پاسخ به تماس‌گیرنده",
    };

    private void SetRange()
    {
        if (_range == "custom") return;
        DateTime today = IranTime.FromUtc(Clock.GetUtcNow());
        _to = PersianDisplay.Date(today, "yyyy/MM/dd");
        _from = PersianDisplay.Date(today.AddDays(_range == "today" ? 0 : _range == "month" ? -29 : -6), "yyyy/MM/dd");
        _fromTime = "۰۰:۰۰";
        _toTime = "۲۳:۵۹";
    }

    private async Task Search()
    {
        if (_loading) return;
        SetRange();
        SearchMessagesRequest request = new()
        {
            Field = MessageSearchField.MobileNumber, Value = _mobile, FromJalali = _from, ToJalali = _to,
            FromTime = _fromTime, ToTime = _toTime, IranLocalTime = true,
        };
        _history.Clear();
        _items = [];
        _next = null;
        _report = null;
        if (await Load(request)) MoveTo(2);
    }

    private async Task<bool> Load(SearchMessagesRequest request)
    {
        _loading = true;
        _error = null;
        _success = null;
        try
        {
            UiResult<MessageSearchPage> result = await Service.Search(request, _lifetime.Token);
            if (!result.IsSuccess || result.Value is null) { _error = result.ErrorMessage; return false; }
            _query = request.Normalize();
            _items = result.Value.Items;
            _next = result.Value.Next;
            return true;
        }
        finally { _loading = false; }
    }

    private async Task Next()
    {
        if (_loading || _query is null || _next is null) return;
        MessageSearchCursor? before = _query.Before;
        if (await Load(_query with { Before = _next })) { _history.Push(before); _focus = true; }
    }

    private async Task Previous()
    {
        if (_loading || _query is null || _history.Count == 0) return;
        if (await Load(_query with { Before = _history.Peek() })) { _history.Pop(); _focus = true; }
    }

    private async Task ExpandRange()
    {
        _range = "month";
        await Search();
    }

    private async Task Select(long id)
    {
        if (_loading) return;
        _report = null;
        _preview = null;
        _previewError = null;
        _copyFailed = false;
        if (await Inspect(id, false)) MoveTo(3);
    }

    private async Task Live()
    {
        if (_loading || _report is null) return;
        await Inspect(_report.Diagnostics.Message.Id, true);
    }

    private async Task<bool> Inspect(long id, bool live)
    {
        _loading = true;
        _error = null;
        try
        {
            UiResult<TroubleshootingReport> result = await Service.Inspect(id, live, _lifetime.Token);
            if (!result.IsSuccess || result.Value is null) { _error = result.ErrorMessage; return false; }
            _report = result.Value;
            return true;
        }
        finally { _loading = false; }
    }

    private async Task Actions()
    {
        if (_loading || _report is null) return;
        _loading = true;
        _preview = null;
        _previewError = null;
        try
        {
            UiResult<ResendMessagePreview> result = await Delivery.ResendPreview(_report.Diagnostics.Message.Id, _lifetime.Token);
            _preview = result.Value;
            _previewError = result.ErrorMessage;
            MoveTo(4);
        }
        finally { _loading = false; }
    }

    private async Task Copy(bool technical)
    {
        if (_report is null) return;
        _success = null;
        _error = null;
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", technical ? TechnicalReport : TroubleshootingText.CustomerReply(_report.Diagnostics));
            _copyFailed = false;
            _success = "گزارش کپی شد.";
        }
        catch (JSException) { _copyFailed = true; _error = "کپی خودکار ممکن نشد؛ متن گزارش را انتخاب و کپی کنید."; }
    }

    private void MoveTo(int step) { _step = step; _focus = true; _error = null; _success = null; }
    private void BackToMessages() { if (!_loading) MoveTo(2); }
    private void BackToDiagnosis() { if (!_loading) MoveTo(3); }
    private void Edit()
    {
        if (_loading) return;
        _report = null;
        _preview = null;
        _previewError = null;
        _copyFailed = false;
        MoveTo(1);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focus) { _focus = false; await _heading.FocusAsync(); }
    }

    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
