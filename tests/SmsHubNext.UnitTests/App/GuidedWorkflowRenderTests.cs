using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using SmsHubNext.Extensions;
using SmsHubNext.Features.Dashboard.Ui.Pages;
using SmsHubNext.Features.DispatchOperations.Ui.Pages;
using SmsHubNext.Features.ReferenceData.Customers.Ui.Pages;
using SmsHubNext.Features.Sending.Ui;
using SmsHubNext.Features.Sending;
using SmsHubNext.Features.ReferenceData.SenderLines;
using SmsHubNext.Features.ReferenceData.MessageTypes;
using SmsHubNext.Shared.Ui;
using SmsHubNext.Shared.Sms;
using SmsHubNext.Features.Tariffs;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class GuidedWorkflowRenderTests
{
    [Fact]
    public async Task Batch_message_details_can_render_both_panels_and_change_selection_without_duplicate_keys()
    {
        await Render<ReadyBatchDetails>(async (page, details) =>
        {
            details.Select(9874, preview: false);
            Assert.Contains("عیب‌یابی پیام ۹۸۷۴", Decode(page.ToHtmlString()));
            details.Select(9874, preview: true);
            details.Refresh();
            string selected = Decode(page.ToHtmlString());
            Assert.Contains("ارسال مجدد پیام ۹۸۷۴", selected);
            Assert.DoesNotContain("_preview.Recipient", selected);
            Assert.Contains("۹۸۹۱۲۰۰۰۰۰۰۱", selected);
            details.Select(9875, preview: true);
            string changed = Decode(page.ToHtmlString());
            Assert.Contains("عیب‌یابی پیام ۹۸۷۵", changed);
            Assert.Contains("ارسال مجدد پیام ۹۸۷۵", changed);
            Assert.DoesNotContain("۹۸۷۴", changed);
            details.Close();
            Assert.DoesNotContain("ارسال مجدد پیام", Decode(page.ToHtmlString()));
            details.Select(9874, preview: true);
            Assert.Contains("ارسال مجدد پیام ۹۸۷۴", Decode(page.ToHtmlString()));
            await Task.CompletedTask;
        });
    }

    public sealed class ReadyBatchDetails : BatchDetailsPage
    {
        protected override Task OnParametersSetAsync() => Task.CompletedTask;
        public void Select(long id, bool preview)
        {
            SetField("_selectedMessage", id);
            SetField("_preview", preview ? new ResendMessagePreview(id, "989120000001", SmsHubNext.Shared.Enums.SendStatus.Submitted, SmsHubNext.Shared.Enums.DeliveryStatus.Delivered, true) : null);
            StateHasChanged();
        }
        public void Refresh() => StateHasChanged();
        public void Close() { SetField("_selectedMessage", null); SetField("_preview", null); StateHasChanged(); }
        private void SetField(string name, object? value) => typeof(BatchDetailsPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, value);
    }

    [Fact]
    public async Task Message_search_is_enabled_when_idle_and_disabled_only_while_loading()
    {
        await Render<ReadyMessageSearch>(async (page, search) =>
        {
            Assert.DoesNotContain("disabled", Button(Decode(page.ToHtmlString()), "جست‌وجو"));
            search.SetLoading(true);
            Assert.Contains("disabled", Button(Decode(page.ToHtmlString()), "جست‌وجو"));
            search.SetLoading(false);
            Assert.DoesNotContain("disabled", Button(Decode(page.ToHtmlString()), "جست‌وجو"));
            await Task.CompletedTask;
        });
    }

    public sealed class ReadyMessageSearch : SmsHubNext.Features.Reports.Ui.Pages.MessageSearch
    {
        public void SetLoading(bool loading)
        {
            typeof(SmsHubNext.Features.Reports.Ui.Pages.MessageSearch).GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, loading);
            StateHasChanged();
        }
    }

    [Fact]
    public async Task Send_form_requires_a_separate_review_and_clears_estimate_after_editing()
    {
        await Render<ReadySendForm>(async (page, form) =>
        {
            string entry = Decode(page.ToHtmlString());
            Assert.Contains("ادامه و بازبینی هزینه", entry);
            Assert.DoesNotContain("تأیید هزینه و ارسال", entry);
            Set(form, "_quotes", new List<CostQuote>());
            Set(form, "_reviewing", true);
            form.Refresh();
            string review = Decode(page.ToHtmlString());
            Assert.Contains("تأیید هزینه و ارسال", review);
            Assert.Contains("ux-fieldset\" disabled", review);
            Invoke(form, "InvalidateReview");
            form.Refresh();
            string edited = Decode(page.ToHtmlString());
            Assert.DoesNotContain("تأیید هزینه و ارسال", edited);
            Assert.Contains("پس از بازبینی محاسبه می‌شود", edited);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Send_validation_marks_the_specific_field_and_preserves_the_message()
    {
        await Render<ReadySendForm>(async (page, form) =>
        {
            Assert.Equal(false, Invoke(form, "Validate"));
            form.Refresh();
            string html = Decode(page.ToHtmlString());
            Assert.Contains("recipient-0-error", html);
            Assert.Contains("aria-invalid=\"true\"", html);
            Assert.Contains("متن آزمایشی", html);
            Assert.Contains("شماره گیرنده پیام ۱", html);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Dispatch_list_has_one_global_search_and_direct_send_and_message_lookup_actions()
    {
        await Render<ReadyDispatchOperations>(async (page, component) =>
        {
            string html = Decode(page.ToHtmlString());
            Assert.Single(Regex.Matches(html, "type=\"search\""));
            Assert.Contains("جست‌وجو در همه ارسال‌ها", html);
            Assert.Contains("/admin/send", html);
            Assert.Contains("/admin/reports/search", html);
            Assert.DoesNotContain("جست‌وجو در جدول", html);
            string navigation = Regex.Match(html, "<nav[^>]*aria-label=\"ارسال و تاریخچه تحویل\"[\\s\\S]*?</nav>").Value;
            Assert.Equal(5, Regex.Matches(navigation, "class=\"admin-menu-card\"").Count);
            Assert.Contains("/admin/dispatch-operations/troubleshoot", navigation);
            foreach (string label in new[] { "ارسال جدید", "پیدا کردن پیامک", "پایش فنی زنده", "تاریخچه تحویل" }) Assert.Contains(label, navigation);
            Assert.Contains("/admin/dispatch-live", navigation);
            Assert.Contains("/admin/delivery-reports", navigation);
            Assert.DoesNotContain("<details", navigation);
            Assert.DoesNotContain("ابزارهای پیشرفته", html);
            Assert.DoesNotContain("disabled", Button(html, "جست‌وجو"));
            await WritePreview("dispatch-menu", html);
            typeof(DispatchOperations).GetField("_appliedSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, "42");
            component.Refresh();
            Assert.DoesNotContain("disabled", Button(Decode(page.ToHtmlString()), "پاک‌کردن فیلترها"));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Dashboard_keeps_customer_selection_available_when_an_independent_panel_fails()
    {
        await Render<Dashboard>(async (page, component) =>
        {
            string html = Decode(page.ToHtmlString());
            Assert.Contains("مشتری مانده حساب", html);
            Assert.Contains("انتخاب مشتری", html);
            Assert.Contains("آمادگی ارائه‌دهندگان", html);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Customer_validation_is_visible_inside_the_modal_and_marks_both_missing_fields()
    {
        await Render<ReadyCustomers>(async (page, component) =>
        {
            typeof(Customers).GetMethod("OpenCreate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
            await (Task)typeof(Customers).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;
            component.Refresh();
            string html = Decode(page.ToHtmlString());
            Assert.Contains("aria-modal=\"true\"", html);
            Assert.Equal(2, Regex.Matches(html, "aria-invalid=\"true\"").Count);
            Assert.Contains("نام مشتری را وارد کنید", html);
            Assert.Contains("کد مشتری را وارد کنید", html);
        });
    }

    [Fact]
    public async Task Confirm_and_edit_buttons_are_enabled_and_double_click_creates_only_one_request()
    {
        FakeSendingService service = new();
        TaskCompletionSource<UiResult<SendMessagesResponse>> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SendReply = (_, _) => completion.Task;
        await Render<ReadySendForm>(async (page, form) =>
        {
            ValidDraft(form);
            await (Task)Invoke(form, "QuoteAll")!;
            form.Refresh();
            string review = Decode(page.ToHtmlString());
            Assert.DoesNotContain("disabled", Button(review, "تأیید هزینه و ارسال"));
            Assert.DoesNotContain("disabled", Button(review, "ویرایش پیام‌ها"));
            Assert.Contains("۱٬۵۰۰ ریال", review);
            Assert.Contains("aria-label=\"حذف پیام ۱\"", review);
            await WritePreview("send-review", review);
            Task first = (Task)Invoke(form, "Send")!;
            form.Refresh();
            Assert.Contains("disabled", Button(Decode(page.ToHtmlString()), "در حال ثبت…"));
            Assert.Contains("disabled", Button(Decode(page.ToHtmlString()), "ویرایش پیام‌ها"));
            await (Task)Invoke(form, "Send")!;
            Assert.Single(service.Requests);
            completion.SetResult(UiResult<SendMessagesResponse>.Success(new(42, 1)));
            await first;
            form.Refresh();
            Assert.Contains("ثبت شد", Decode(page.ToHtmlString()));
            Assert.DoesNotContain("تأیید هزینه و ارسال", Decode(page.ToHtmlString()));
            Assert.Equal("test-batch", Assert.Single(service.Requests).ClientBatchId);
        }, service);
    }

    [Fact]
    public async Task Send_failure_is_visible_beside_confirmation_and_retry_preserves_idempotency_key()
    {
        FakeSendingService service = new()
        {
            SendReply = (_, _) => Task.FromResult(UiResult<SendMessagesResponse>.Failure("نتیجه ثبت دریافت نشد؛ دوباره تلاش کنید.")),
        };
        await Render<ReadySendForm>(async (page, form) =>
        {
            ValidDraft(form);
            await (Task)Invoke(form, "QuoteAll")!;
            await (Task)Invoke(form, "Send")!;
            form.Refresh();
            string html = Decode(page.ToHtmlString());
            Assert.Contains("نتیجه ثبت دریافت نشد", Regex.Match(html, "<aside[^>]*class=\"send-summary\"[\\s\\S]*?</aside>").Value);
            Assert.Contains("class=\"send-submit-error\" role=\"alert\"", html);
            Assert.DoesNotContain("disabled", Button(html, "تأیید هزینه و ارسال"));
            await WritePreview("send-error", html);
            service.SendReply = (_, _) => Task.FromResult(UiResult<SendMessagesResponse>.Success(new(42, 1, true)));
            await (Task)Invoke(form, "Send")!;
            form.Refresh();
            Assert.Equal(2, service.Requests.Count);
            Assert.Equal(service.Requests[0].ClientBatchId, service.Requests[1].ClientBatchId);
            Assert.Contains("ثبت شد", Decode(page.ToHtmlString()));
        }, service);
    }

    private static void ValidDraft(SendMessageForm form)
    {
        Set(form, "_customerId", (short)7);
        Set(form, "_customers", new List<SmsHubNext.Features.ReferenceData.Customers.Customer> { new(7, "مشتری آزمایشی", "test", true, TimeProvider.System.GetUtcNow().UtcDateTime) });
        Set(form, "_lines", new List<SenderLine> { new(1, 1, "40401403", true, null, null, true, true) });
        Set(form, "_types", new List<MessageType> { new(1, "قبض", "bill", true) });
        List<SendMessageDraft> messages = (List<SendMessageDraft>)typeof(SendMessageForm).GetField("_messages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        messages[0].Recipient = "09121234567";
    }

    private static string Button(string html, string label)
    {
        Match button = Regex.Match(html, "<button\\b[^>]*>" + Regex.Escape(label) + "</button>");
        Assert.True(button.Success, "Expected visible button: " + label);
        return button.Value;
    }

    private static async Task WritePreview(string name, string html)
    {
        string? output = Environment.GetEnvironmentVariable("SMSHUB_DAILY_PREVIEW");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        string document = """
            <!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <link rel="stylesheet" href="mud.css"><link rel="stylesheet" href="app.css"><link rel="stylesheet" href="admin.css"><link rel="stylesheet" href="components.css">
            <title>بازبینی ارسال پیامک</title><style>body{margin:0;background:#f5f7fb}.admin-shell{display:block}.admin-content{max-width:1140px;margin:auto}</style></head>
            <body><div class="admin-shell"><main class="admin-content">CONTENT</main></div></body></html>
            """;
        await File.WriteAllTextAsync(Path.Combine(output, name + ".html"), document.Replace("CONTENT", html, StringComparison.Ordinal));
    }

    private static async Task Render<T>(Func<Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent, T, Task> check, SendingService? sendingService = null) where T : IComponent
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SmsHubNext"] = "Server=unused;Database=unused",
            ["BearerTokens:Key"] = new string('k', 64),
            ["BearerTokens:Issuer"] = "tests", ["BearerTokens:Audience"] = "tests",
            ["BearerTokens:AccessTokenExpirationMinutes"] = "15", ["BearerTokens:RefreshTokenExpirationMinutes"] = "60",
            ["Providers:Magfa:Enabled"] = "false", ["Providers:Kavenegar:Enabled"] = "false", ["Providers:SmsIr:Enabled"] = "false",
        });
        builder.Services.AddApplicationServices(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton<AuthenticationStateProvider, AnonymousState>();
        builder.Services.AddSingleton<NavigationManager, TestNavigation>();
        builder.Services.AddSingleton<IJSRuntime, NoScriptRuntime>();
        if (sendingService is not null) builder.Services.AddSingleton(sendingService);
        await using ServiceProvider provider = builder.Services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await using HtmlRenderer renderer = new(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            T? component = default;
            ParameterView parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Capture"] = (Action<T>)(value => component = value) });
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent page = await renderer.RenderComponentAsync<CaptureHost<T>>(parameters);
            await check(page, component!);
        });
    }

    public sealed class ReadySendForm : SendMessageForm
    {
        protected override Task OnInitializedAsync()
        {
            Set(this, "_loading", false);
            Set(this, "_clientBatchId", "test-batch");
            Set(this, "_senderLineId", (short)1);
            Set(this, "_messageTypeId", (byte)1);
            FieldInfo messages = typeof(SendMessageForm).GetField("_messages", BindingFlags.Instance | BindingFlags.NonPublic)!;
            List<SendMessageDraft> rows = (List<SendMessageDraft>)messages.GetValue(this)!;
            rows[0].Text = "متن آزمایشی";
            return Task.CompletedTask;
        }
        public void Refresh() => StateHasChanged();
    }
    public sealed class ReadyCustomers : Customers { public void Refresh() => StateHasChanged(); }
    public sealed class ReadyDispatchOperations : DispatchOperations { public void Refresh() => StateHasChanged(); }
    public sealed class CaptureHost<T> : ComponentBase where T : IComponent
    {
        [Parameter] public Action<T> Capture { get; set; } = null!;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<T>(0);
            builder.AddComponentReferenceCapture(1, instance => Capture((T)instance));
            builder.CloseComponent();
        }
    }
    private static string Decode(string html) => WebUtility.HtmlDecode(html);
    private static void Set(SendMessageForm form, string name, object value) => typeof(SendMessageForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, value);
    private static object? Invoke(SendMessageForm form, string name) => typeof(SendMessageForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
    private sealed class AnonymousState : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/send");
        protected override void NavigateToCore(string uri, bool forceLoad) { Uri = ToAbsoluteUri(uri).AbsoluteUri; NotifyLocationChanged(false); }
    }
    private sealed class NoScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    private sealed class FakeSendingService() : SendingService(null!, null!, null!)
    {
        public List<SendMessagesRequest> Requests { get; } = [];
        public Func<SendMessagesRequest, CancellationToken, Task<UiResult<SendMessagesResponse>>> SendReply { get; set; } =
            (_, _) => Task.FromResult(UiResult<SendMessagesResponse>.Success(new(42, 1)));
        public override Task<UiResult<CostQuote>> Quote(QuoteRequest request, CancellationToken token) =>
            Task.FromResult(UiResult<CostQuote>.Success(new(SmsEncoding.Ucs2, request.Text.Length, 1, 1, 1500m, 1500m, "IRR")));
        public override Task<UiResult<SendMessagesResponse>> Send(SendMessagesRequest request, short customerId, CancellationToken token)
        {
            Assert.Equal((short)7, customerId);
            Requests.Add(request);
            return SendReply(request, token);
        }
    }
}
