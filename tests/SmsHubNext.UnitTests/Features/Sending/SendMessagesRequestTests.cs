using SmsHubNext.Features.Sending;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Sending;

public class SendMessagesRequestTests
{
    private static SendMessagesRequest Valid(params SendMessageItem[] messages) =>
        new() { SenderLine = "30001234", Messages = messages };

    private static SendMessageItem Item(string recipient = "989120000000", string text = "Hello") =>
        new() { Recipient = recipient, Text = text };

    [Fact]
    public void Valid_request_with_distinct_messages_passes()
    {
        var request = Valid(
            Item("989120000001", "Your OTP is 1234"),
            Item("989120000002", "Your invoice is ready"));

        Assert.True(request.Validate().IsSuccess);
    }

    [Fact]
    public void Sender_line_is_required()
    {
        var request = new SendMessagesRequest { SenderLine = " ", Messages = [Item()] };

        var result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.sender_line_required", result.Error!.Code);
    }

    [Fact]
    public void At_least_one_message_is_required()
    {
        var request = Valid();

        var result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.messages_required", result.Error!.Code);
    }

    [Fact]
    public void Each_item_must_have_a_recipient()
    {
        var request = Valid(Item(), Item(recipient: ""));

        var result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.recipient_required", result.Error!.Code);
        Assert.Contains("index 1", result.Error!.Message);
    }

    [Fact]
    public void Each_item_must_have_text()
    {
        var request = Valid(Item(text: ""));

        var result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.text_required", result.Error!.Code);
    }

    [Fact]
    public void Rejects_more_than_the_maximum_messages()
    {
        var tooMany = Enumerable.Range(0, SendMessagesRequest.MaxMessages + 1)
            .Select(_ => Item())
            .ToArray();
        var request = Valid(tooMany);

        var result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("sending.too_many_messages", result.Error!.Code);
    }
}
