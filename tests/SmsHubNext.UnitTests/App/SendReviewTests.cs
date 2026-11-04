using System.Reflection;
using SmsHubNext.Features.Sending.Ui;
using SmsHubNext.Features.Tariffs;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class SendReviewTests
{
    [Fact]
    public void Validation_identifies_the_exact_recipient_and_text_without_losing_other_rows()
    {
        List<SendMessageDraft> messages =
        [
            new() { Recipient = "۰۹۱۲۱۲۳۴۵۶۷", Text = "سلام" },
            new() { Recipient = "123", Text = " " },
        ];
        Dictionary<string, string> errors = SendDraftValidation.Errors(messages, 1, 1, "batch");
        Assert.Equal(2, errors.Count);
        Assert.Contains("۲", errors["recipient-1"]);
        Assert.Contains("۲", errors["text-1"]);
        Assert.Equal("سلام", messages[0].Text);
        Assert.Equal("۰۹۱۲۱۲۳۴۵۶۷", messages[0].Recipient);
    }

    [Fact]
    public void Editing_a_review_clears_both_price_and_confirmation()
    {
        using SendMessageForm form = new();
        Set(form, "_quotes", new List<CostQuote>());
        Set(form, "_reviewing", true);
        Invoke(form, "InvalidateReview");
        Assert.Null(Get(form, "_quotes"));
        Assert.Equal(false, Get(form, "_reviewing"));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Sending_requires_review_and_price_and_cannot_repeat_a_completed_send(bool review, bool price, bool completed)
    {
        // No sending service is injected: a guard failure must return before any backend call.
        using SendMessageForm form = new();
        Set(form, "_reviewing", review);
        if (price) Set(form, "_quotes", new List<CostQuote>());
        if (completed) Set(form, "_sentBatchId", (long?)12);
        await (Task)Invoke(form, "Send")!;
        Assert.Equal(false, Get(form, "_sending"));
    }

    [Fact]
    public void New_send_rotates_idempotency_key_only_after_an_explicit_new_send_action()
    {
        using SendMessageForm form = new();
        Set(form, "_clientBatchId", "previous");
        Set(form, "_sentBatchId", (long?)12);
        Invoke(form, "StartNew");
        Assert.NotEqual("previous", Get(form, "_clientBatchId"));
        Assert.Null(Get(form, "_sentBatchId"));
        Assert.Equal(false, Get(form, "_reviewing"));
    }

    [Fact]
    public void Changing_customer_filters_dedicated_lines_and_invalidates_cost_review()
    {
        using SendMessageForm form = new();
        Set(form, "_lines", new List<SmsHubNext.Features.ReferenceData.SenderLines.SenderLine>
        {
            new(1, 1, "30001", false, 7, 1, true),
            new(2, 1, "30002", false, 8, 1, true),
            new(3, 1, "30003", true, null, 1, true),
        });
        Set(form, "_customerId", (short)8);
        Set(form, "_senderLineId", (short)1);
        Set(form, "_quotes", new List<CostQuote>());
        Set(form, "_reviewing", true);
        Invoke(form, "CustomerChanged");
        Assert.Equal((short)2, Get(form, "_senderLineId"));
        Assert.Null(Get(form, "_quotes"));
        Assert.Equal(false, Get(form, "_reviewing"));
    }

    private static FieldInfo Field(string name) => typeof(SendMessageForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Set(SendMessageForm form, string field, object? value) => Field(field).SetValue(form, value);
    private static object? Get(SendMessageForm form, string field) => Field(field).GetValue(form);
    private static object? Invoke(SendMessageForm form, string method) => typeof(SendMessageForm)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
}
