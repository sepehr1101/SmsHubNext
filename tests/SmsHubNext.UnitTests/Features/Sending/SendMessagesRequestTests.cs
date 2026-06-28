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
}
