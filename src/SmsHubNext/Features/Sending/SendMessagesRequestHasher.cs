using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SmsHubNext.Features.Sending;

internal static class SendMessagesRequestHasher
{
    private const char Separator = '\u001F';

    public static byte[] Hash(SendMessagesRequest request)
    {
        StringBuilder canonical = new StringBuilder();
        Append(canonical, request.SenderLine.Trim());
        Append(canonical, request.MessageTypeId.ToString(CultureInfo.InvariantCulture));

        foreach (SendMessageItem message in request.Messages)
        {
            Append(canonical, SendMessageItem.NormalizeRecipient(message.Recipient));
            Append(canonical, message.Text);
            Append(canonical, message.ClientCorrelatedId);
            Append(canonical, message.BillId);
            Append(canonical, message.PayId);
            Append(canonical, message.GeoSectionId?.ToString(CultureInfo.InvariantCulture));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        return SHA256.HashData(bytes);
    }

    private static void Append(StringBuilder builder, string? value)
    {
        builder.Append(value ?? string.Empty);
        builder.Append(Separator);
    }
}
