using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using SmsHubNext.Features.Sending;
using SmsHubNext.Features.Sending.Ui;
using SmsHubNext.Shared.Ui;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class DailyResendRenderTests
{
    [Fact]
    public Task Only_date_is_requested_enter_reviews_and_no_confirmation_is_shown_initially() => Render(async view =>
    {
        string html = view.Html;
        Assert.Single(Regex.Matches(html, "<input"));
        Assert.Contains("<form", html);
        Assert.Contains("type=\"submit\"", html);
        Assert.Contains("aria-describedby=\"daily-date-hint daily-date-error\"", html);
        Assert.DoesNotContain("تأیید هزینه و ثبت", html);
        Assert.DoesNotContain("RequestId", html);
        view.Panel.Date("۱۴۰۵/۰۸/۰۹");
        await view.Call("Begin");
        Assert.Equal("1405/08/09", view.Service.LastDay);
        Assert.Equal(1, view.Service.Begins);
        Assert.Equal(0, view.Service.Confirms);
        Assert.Contains("۲٬۰۰۰٫۱۲۵", view.Html);
        Assert.Contains("تأیید هزینه و ثبت ارسال مجدد", view.Html);
        Assert.DoesNotContain("ارسال مجدد ثبت‌شده در صف", view.Html);
        await WritePreview("ready", view.Html);
    });

    [Theory]
    [InlineData("")]
    [InlineData("1405/13/01")]
    [InlineData("1405/08/11")]
    public Task Invalid_date_is_announced_inline_without_a_service_request(string date) => Render(async view =>
    {
        view.Panel.Date(date);
        await view.Call("Begin");
        Assert.Contains("aria-invalid=\"true\"", view.Html);
        Assert.Contains("یک تاریخ شمسی معتبر", view.Html);
        Assert.Equal(0, view.Service.Begins);
        view.Panel.Date("1405/08/09");
        Assert.Contains("aria-invalid=\"false\"", view.Html);
        Assert.DoesNotContain("یک تاریخ شمسی معتبر", view.Html);
    });

    [Fact]
    public Task Editing_date_immediately_removes_cost_and_confirmation() => Render(async view =>
    {
        view.Panel.Date("1405/08/09");
        await view.Call("Begin");
        view.Panel.Date("1405/08/08");
        Assert.DoesNotContain("تأیید هزینه و ثبت", view.Html);
        Assert.DoesNotContain("۲٬۰۰۰٫۱۲۵", view.Html);
        await view.Call("Confirm");
        Assert.Equal(0, view.Service.Confirms);
    });

    [Theory]
    [InlineData(DailyResendState.Preparing)]
    [InlineData(DailyResendState.Running)]
    [InlineData(DailyResendState.Completed)]
    public Task Confirmation_cannot_run_from_an_unapproved_state(DailyResendState state) => Render(async view =>
    {
        view.Panel.Seed(Ready() with { State = state });
        await view.Call("Confirm");
        Assert.Equal(0, view.Service.Confirms);
        Assert.DoesNotContain("تأیید هزینه و ثبت", view.Html);
        if (state == DailyResendState.Preparing)
        {
            Assert.Contains("aria-busy=\"true\"", view.Html);
            Assert.DoesNotContain("شمارنده‌های ارسال مجدد", view.Html);
            await WritePreview("preparing", view.Html);
        }
    });

    [Fact]
    public Task Double_click_confirmation_calls_service_once_and_removes_button_after_acceptance() => Render(async view =>
    {
        DailyResendSummary ready = Ready();
        TaskCompletionSource<UiResult<DailyResendSummary>> response = Completion();
        view.Panel.Seed(ready);
        view.Service.ConfirmReply = (_, _) => response.Task;
        Task first = view.Panel.Start("Confirm");
        await view.Call("Confirm");
        Assert.Equal(1, view.Service.Confirms);
        Assert.Contains("disabled", Regex.Match(view.Html, "<button[^>]*class=\"[^\"]*daily-confirm[^\"]*\"[^>]*>").Value);
        response.SetResult(UiResult<DailyResendSummary>.Success(ready with { State = DailyResendState.Running }));
        await first;
        view.Panel.Redraw();
        Assert.Equal(ready.Id, view.Service.LastConfirmedId);
        Assert.DoesNotContain("daily-confirm", view.Html);
    });

    [Fact]
    public Task Refresh_is_nonblocking_and_late_old_day_response_never_restores_confirmation() => Render(async view =>
    {
        DailyResendSummary ready = Ready();
        view.Panel.Seed(ready);
        TaskCompletionSource<UiResult<DailyResendSummary>> response = Completion();
        view.Service.StatusReply = (_, _) => response.Task;
        Task refresh = view.Panel.Start("Refresh");
        Assert.DoesNotContain("disabled", Regex.Match(view.Html, "<input[^>]*>").Value);
        view.Panel.Date("1405/08/08");
        response.SetResult(UiResult<DailyResendSummary>.Success(ready));
        await refresh;
        view.Panel.Redraw();
        Assert.DoesNotContain("نتیجه بررسی روز", view.Html);
        Assert.DoesNotContain("daily-confirm", view.Html);
        Assert.Contains("value=\"1405/08/08\"", view.Html);
    });

    [Fact]
    public Task Old_poll_response_cannot_undo_a_new_confirmation() => Render(async view =>
    {
        DailyResendSummary ready = Ready();
        view.Panel.Seed(ready);
        TaskCompletionSource<UiResult<DailyResendSummary>> response = Completion();
        view.Service.StatusReply = (_, _) => response.Task;
        Task refresh = view.Panel.Start("Refresh");
        await view.Call("Confirm");
        response.SetResult(UiResult<DailyResendSummary>.Success(ready));
        await refresh;
        view.Panel.Redraw();
        Assert.Contains("ثبت ارسال مجدد در حال انجام است", view.Html);
        Assert.DoesNotContain("daily-confirm", view.Html);
    });

    [Fact]
    public Task Refresh_failure_preserves_previous_counts_and_a_manual_refresh_recovers() => Render(async view =>
    {
        DailyResendSummary progress = Ready() with { State = DailyResendState.Running, Queued = 2, Submitted = 1, Pending = 1 };
        view.Panel.Seed(progress);
        await WritePreview("running", view.Html);
        view.Service.StatusReply = (_, _) => Task.FromResult(UiResult<DailyResendSummary>.Failure("ارتباط قطع شده است."));
        await view.Call("Refresh");
        Assert.Contains("اطلاعات قبلی حفظ شده است", view.Html);
        Assert.Contains("ارسال مجدد ثبت‌شده در صف", view.Html);
        Assert.Contains("پذیرفته‌شده توسط سرویس‌دهنده", view.Html);
        Assert.DoesNotContain("daily-confirm", view.Html);
        view.Service.StatusReply = (_, _) => Task.FromResult(UiResult<DailyResendSummary>.Success(progress with { State = DailyResendState.Completed }));
        await view.Call("Refresh");
        Assert.DoesNotContain("ارتباط قطع شده است", view.Html);
        Assert.Contains("آخرین دریافت وضعیت", view.Html);
    });

    [Fact]
    public Task Failed_confirmation_is_not_hidden_by_ready_poll_but_running_status_proves_recovery() => Render(async view =>
    {
        view.Panel.Seed(Ready());
        view.Service.ConfirmReply = (_, _) => Task.FromResult(UiResult<DailyResendSummary>.Failure("نتیجه تأیید دریافت نشد."));
        await view.Call("Confirm");
        await view.Call("Refresh");
        Assert.Contains("نتیجه تأیید دریافت نشد", view.Html);
        view.Service.StatusReply = (_, _) => Task.FromResult(UiResult<DailyResendSummary>.Success(Ready() with { State = DailyResendState.Running }));
        await view.Call("Refresh");
        Assert.DoesNotContain("نتیجه تأیید دریافت نشد", view.Html);
        Assert.DoesNotContain("daily-confirm", view.Html);
    });

    [Fact]
    public Task Empty_result_and_zero_remaining_have_no_confirmation_or_false_success_claim() => Render(async view =>
    {
        view.Panel.Seed(new(Guid.NewGuid(), "1405/08/09", DailyResendState.Completed, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        Assert.Contains("پیامی قابل ارسال مجدد نیست", view.Html);
        Assert.Contains("گزارش قطعی عدم تحویل یافت نشد", view.Html);
        Assert.DoesNotContain("شرایط ایمنی", view.Html);
        Assert.DoesNotContain("شرایط امن", view.Html);
        Assert.DoesNotContain("daily-confirm", view.Html);
        Assert.DoesNotContain("شمارنده‌های ارسال مجدد", view.Html);
        await WritePreview("empty", view.Html);
        view.Panel.Seed(Ready() with { State = DailyResendState.Paused, Queued = 2, RemainingCost = 0 });
        await view.Call("Confirm");
        Assert.DoesNotContain("daily-confirm", view.Html);
        Assert.Equal(0, view.Service.Confirms);
    });

    [Fact]
    public Task Paused_partial_run_reviews_only_remaining_cost_and_explains_skips_and_missing_history() => Render(async view =>
    {
        view.Panel.Seed(new(Guid.NewGuid(), "1405/08/09", DailyResendState.Paused, 6, 4, 9000m, 2, 1, 0, 0, 2, 1, 500.25m)
        {
            SkipReasons = [new("sending.daily_ambiguous_recipient", 1), new("sending.resend_price_changed", 1)],
        });
        Assert.Contains("۵۰۰٫۲۵", view.Html);
        Assert.DoesNotContain("۹٬۰۰۰", view.Html);
        Assert.Contains("تأیید هزینهٔ باقی‌مانده و ادامه", view.Html);
        Assert.Contains("چند پیام برای یک شماره", view.Html);
        Assert.Contains("هزینه نسبت به مبلغ تأییدشده تغییر کرده است", view.Html);
        Assert.Contains("نتیجه ارسال آن‌ها قابل اثبات نیست", view.Html);
        Assert.DoesNotContain("sending.daily_ambiguous_recipient", view.Html);
        Assert.DoesNotContain("ارسال موفق", view.Html);
        await WritePreview("paused", view.Html);
    });

    [Fact]
    public Task Disposal_cancels_inflight_poll_and_ignores_its_late_response() => Render(async view =>
    {
        view.Panel.Seed(Ready());
        TaskCompletionSource<UiResult<DailyResendSummary>> response = Completion();
        CancellationToken observed = default;
        view.Service.StatusReply = (_, token) => { observed = token; return response.Task; };
        Task refresh = view.Panel.Start("Refresh");
        view.Panel.Dispose();
        Assert.True(observed.IsCancellationRequested);
        response.SetResult(UiResult<DailyResendSummary>.Success(Ready() with { State = DailyResendState.Running }));
        await refresh;
        view.Panel.Redraw();
        Assert.DoesNotContain("ثبت ارسال مجدد در حال انجام است", view.Html);
        await view.Call("Confirm");
        Assert.Equal(0, view.Service.Confirms);
    });

    [Fact]
    public Task Daily_mode_is_open_and_hides_the_unrelated_new_message_form() => Render(async view =>
    {
        Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page = await view.Renderer.RenderComponentAsync<DailyModePage>();
        string html = WebUtility.HtmlDecode(page.ToHtmlString());
        Assert.Contains("نوع عملیات ارسال", html);
        Assert.Single(Regex.Matches(html, "<input"));
        Assert.DoesNotContain("افزودن گیرنده", html);
        Assert.DoesNotContain("ارسال و ثبت دسته", html);
        Assert.Contains("open", Regex.Match(html, "<details[^>]*id=\"daily-resend\"[^>]*>").Value);
        await WritePreview("initial", html);
    });

    private static DailyResendSummary Ready() => new(Guid.Parse("a370e880-0915-49e1-a69e-0c344ac2a20f"), "1405/08/09",
        DailyResendState.Ready, 2, 2, 2000.125m, 0, 0, 0, 0, 0, 0, 2000.125m);
    private static TaskCompletionSource<UiResult<DailyResendSummary>> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Render(Func<View, Task> test)
    {
        FakeService service = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMudServices();
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddSingleton<DailyResendService>(service);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            TestPanel? panel = null;
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page = await renderer.RenderComponentAsync<TestPanel>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(TestPanel.Capture)] = (Action<TestPanel>)(value => panel = value) }));
            await test(new View(page, panel!, service, renderer));
        });
    }

    private sealed record View(Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent Page, TestPanel Panel, FakeService Service, HtmlRenderer Renderer)
    {
        public string Html => WebUtility.HtmlDecode(Page.ToHtmlString());
        public async Task Call(string method) { await Panel.Start(method); Panel.Redraw(); }
    }

    public sealed class TestPanel : DailyResendPanel
    {
        [Parameter] public Action<TestPanel> Capture { get; set; } = _ => { };
        protected override void OnInitialized() { InitiallyOpen = true; Capture(this); }
        public void Date(string value)
        {
            Set("_day", value);
            typeof(DailyResendPanel).GetMethod("DateChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, null);
            Redraw();
        }
        public void Seed(DailyResendSummary value) { Set("_summary", value); Set("_day", value.SubmitDateJalali); Redraw(); }
        public Task Start(string method)
        {
            object? result = typeof(DailyResendPanel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, null);
            Redraw();
            return result as Task ?? Task.CompletedTask;
        }
        public void Redraw() => StateHasChanged();
        private void Set(string field, object value) => typeof(DailyResendPanel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, value);
    }

    public sealed class DailyModePage : SmsHubNext.Features.Sending.Ui.Pages.SendMessages
    {
        protected override void OnInitialized() => typeof(SmsHubNext.Features.Sending.Ui.Pages.SendMessages)
            .GetField("_dailyMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, true);
    }

    private static async Task WritePreview(string name, string content)
    {
        string? output = Environment.GetEnvironmentVariable("SMSHUB_DAILY_PREVIEW");
        if (string.IsNullOrWhiteSpace(output)) return;
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SmsHubNext.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        Directory.CreateDirectory(Path.Combine(output, "fonts", "vazirmatn"));
        string assets = Path.Combine(root.FullName, "src", "SmsHubNext", "wwwroot");
        foreach (string file in new[] { "app.css", "admin.css" }) File.Copy(Path.Combine(assets, file), Path.Combine(output, file), true);
        File.Copy(Path.Combine(assets, "fonts", "vazirmatn", "Vazirmatn-Variable.woff2"), Path.Combine(output, "fonts", "vazirmatn", "Vazirmatn-Variable.woff2"), true);
        File.Copy(Path.Combine(root.FullName, "src", "SmsHubNext", "obj", "Release", "net10.0", "scopedcss", "bundle", "SmsHubNext.styles.css"), Path.Combine(output, "components.css"), true);
        string packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? "D:/.nuget/packages";
        File.Copy(Path.Combine(packages, "mudblazor", "9.11.0", "staticwebassets", "MudBlazor.min.css"), Path.Combine(output, "mud.css"), true);
        string document = """
            <!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <link rel="stylesheet" href="mud.css"><link rel="stylesheet" href="app.css"><link rel="stylesheet" href="admin.css"><link rel="stylesheet" href="components.css">
            <title>بازبینی ارسال مجدد روزانه</title><style>body{margin:0;background:#f5f7fb}.admin-shell{display:block}.admin-content{max-width:1140px;margin:auto}</style></head>
            <body><div class="admin-shell"><main class="admin-content">CONTENT</main></div></body></html>
            """;
        await File.WriteAllTextAsync(Path.Combine(output, name + ".html"), document.Replace("CONTENT", content, StringComparison.Ordinal));
    }

    private sealed class FakeService() : DailyResendService(null!, null!)
    {
        public int Begins { get; private set; }
        public int Confirms { get; private set; }
        public string? LastDay { get; private set; }
        public Guid? LastConfirmedId { get; private set; }
        public Func<Guid, CancellationToken, Task<UiResult<DailyResendSummary>>> StatusReply { get; set; } = (_, _) => Task.FromResult(UiResult<DailyResendSummary>.Success(Ready()));
        public Func<Guid, CancellationToken, Task<UiResult<DailyResendSummary>>> ConfirmReply { get; set; } = (_, _) => Task.FromResult(UiResult<DailyResendSummary>.Success(Ready() with { State = DailyResendState.Running }));
        public override Task<UiResult<DailyResendSummary>> Begin(string day, CancellationToken token)
        {
            Begins++; LastDay = day;
            return Task.FromResult(UiResult<DailyResendSummary>.Success(Ready()));
        }
        public override Task<UiResult<DailyResendSummary>> Confirm(Guid id, CancellationToken token) { Confirms++; LastConfirmedId = id; return ConfirmReply(id, token); }
        public override Task<UiResult<DailyResendSummary>> Status(Guid id, CancellationToken token) => StatusReply(id, token);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    }
}
