using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Sending.Ui;

public static class SendDraftValidation
{
    public static Dictionary<string, string> Errors(IReadOnlyList<SendMessageDraft> messages,
        short senderLineId, byte messageTypeId, string batchId)
    {
        Dictionary<string, string> errors = new();
        if (senderLineId == 0) errors["line"] = "خط ارسال را انتخاب کنید.";
        if (messageTypeId == 0) errors["type"] = "نوع پیام را انتخاب کنید.";
        if (string.IsNullOrWhiteSpace(batchId)) errors["batch"] = "شناسه دسته نباید خالی باشد.";
        if (messages.Count == 0) errors["messages"] = "حداقل یک پیام وارد کنید.";
        for (int index = 0; index < messages.Count; index++)
        {
            SendMessageDraft message = messages[index];
            string number = PersianDisplay.Number(index + 1);
            if (!SendMessageItem.IsValidRecipient(PersianDisplay.SearchText(message.Recipient)))
                errors[$"recipient-{index}"] = $"شماره گیرنده پیام {number} معتبر نیست؛ مانند 09121234567 وارد کنید.";
            if (string.IsNullOrWhiteSpace(message.Text))
                errors[$"text-{index}"] = $"متن پیام {number} را وارد کنید.";
        }
        return errors;
    }
}
