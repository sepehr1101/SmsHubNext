using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Sending;

public class SendMessagesRequestTests
{
    private static SendMessagesRequest Valid(params SendMessageItem[] messages) =>
        new() { SenderLine = "30001234", MessageTypeId = 1, ClientBatchId = "unit-test-batch", Messages = messages };

    private static SendMessageItem Item(string recipient = "989120000000", string text = "Hello") =>
        new() { Recipient = recipient, Text = text };

    [Fact]
    public void Valid_request_with_distinct_messages_passes()
    {
        SendMessagesRequest request = Valid(
            Item("989120000001", "Your OTP is 1234"),
            Item("989120000002", "Your invoice is ready"));

        Assert.True(request.Validate().IsSuccess);
    }

    [Fact]
    public void Sender_line_is_required()
    {
        SendMessagesRequest request = new SendMessagesRequest { SenderLine = " ", Messages = [Item()] };

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.sender_line_required", result.Error!.Code);
    }

    [Fact]
    public void At_least_one_message_is_required()
    {
        SendMessagesRequest request = Valid();

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.messages_required", result.Error!.Code);
    }

    [Fact]
    public void Each_item_must_have_a_recipient()
    {
        SendMessagesRequest request = Valid(Item(), Item(recipient: ""));

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.recipient_required", result.Error!.Code);
        Assert.Contains("index 1", result.Error!.Message);
    }

    [Fact]
    public void Client_batch_id_is_required()
    {
        SendMessagesRequest request = Valid(Item());
        request = new SendMessagesRequest
        {
            SenderLine = request.SenderLine,
            MessageTypeId = request.MessageTypeId,
            Messages = request.Messages,
        };

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.client_batch_id_required", result.Error!.Code);
    }

    [Theory]
    [InlineData("9120000000")]
    [InlineData("98912000000x")]
    [InlineData("+989120000000")]
    public void Each_item_must_have_a_valid_iranian_mobile_number(string recipient)
    {
        SendMessagesRequest request = Valid(Item(recipient: recipient));

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.recipient_invalid", result.Error!.Code);
    }

    [Fact]
    public void Each_item_must_have_text()
    {
        SendMessagesRequest request = Valid(Item(text: ""));

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.text_required", result.Error!.Code);
    }

    [Fact]
    public void Rejects_more_than_the_maximum_messages()
    {
        SendMessageItem[] tooMany = Enumerable.Range(0, SendMessagesRequest.MaxMessages + 1)
            .Select(_ => Item())
            .ToArray();
        SendMessagesRequest request = Valid(tooMany);

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.too_many_messages", result.Error!.Code);
    }

    [Fact]
    public void Rejects_text_and_identifiers_that_exceed_storage_limits()
    {
        SendMessagesRequest longText = Valid(Item(text: new string('x', SendMessagesRequest.MaxTextLength + 1)));
        Assert.Equal("sending.text_too_long", longText.Validate().Error!.Code);

        SendMessagesRequest longCorrelation = Valid(new SendMessageItem
        {
            Recipient = "989120000000",
            Text = "Hello",
            ClientCorrelatedId = new string('x', SendMessagesRequest.MaxClientCorrelatedIdLength + 1),
        });
        Assert.Equal("sending.client_correlated_id_too_long", longCorrelation.Validate().Error!.Code);

        SendMessagesRequest longBill = Valid(new SendMessageItem
        {
            Recipient = "989120000000",
            Text = "Hello",
            BillId = new string('x', SendMessagesRequest.MaxBillIdLength + 1),
        });
        Assert.Equal("sending.bill_id_too_long", longBill.Validate().Error!.Code);

        SendMessagesRequest longPay = Valid(new SendMessageItem
        {
            Recipient = "989120000000",
            Text = "Hello",
            PayId = new string('x', SendMessagesRequest.MaxPayIdLength + 1),
        });
        Assert.Equal("sending.pay_id_too_long", longPay.Validate().Error!.Code);
    }
}
