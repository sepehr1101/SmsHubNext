using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using MiniExcelLibs;

namespace SmsHubNext.Features.Sending.Ui;

public static partial class ExcelMessageImport
{
    public const long MaxFileBytes = 5 * 1024 * 1024;

    public static List<SendMessageDraft> Read(Stream stream, string template, string recipientColumn, bool skipFirstRow, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > SendMessagesRequest.MaxTextLength)
            throw new InvalidDataException("قالب پیام الزامی است و حداکثر ۱۸۰۰ نویسه دارد.");
        recipientColumn = recipientColumn.Trim().ToUpperInvariant();
        if (!ColumnPattern().IsMatch(recipientColumn))
            throw new InvalidDataException("ستون شماره موبایل را با حروف اکسل، مانند D، مشخص کنید.");
        using (ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(entry => entry.Length) > 50 * 1024 * 1024)
                throw new InvalidDataException("حجم محتوای فایل بیش از حد مجاز است.");
        }
        stream.Position = 0;

        List<SendMessageDraft> messages = [];
        int rowNumber = 0;
        foreach (IDictionary<string, object> row in MiniExcel.Query(stream, useHeaderRow: false, excelType: ExcelType.XLSX).Cast<IDictionary<string, object>>())
        {
            token.ThrowIfCancellationRequested();
            rowNumber++;
            if (rowNumber > 10000) throw new InvalidDataException("فایل بیش از ۱۰۰۰۰ سطر دارد؛ سطرهای خالی اضافه را حذف کنید.");
            if (skipFirstRow && rowNumber == 1) continue;
            if (row.Values.All(value => string.IsNullOrWhiteSpace(CellText(value)))) continue;
            if (messages.Count == SendMessagesRequest.MaxMessages)
                throw new InvalidDataException("هر دسته حداکثر ۱۰۰۰ گیرنده دارد؛ فایل را به دسته‌های کوچک‌تر تقسیم کنید.");

            string recipient = GetCell(row, recipientColumn, rowNumber).Trim();
            if (recipient.Length == 10 && recipient.StartsWith('9')) recipient = "0" + recipient;
            if (!SendMessageItem.IsValidRecipient(recipient))
                throw new InvalidDataException($"شماره موبایل در سطر {rowNumber} معتبر نیست.");
            string text = PlaceholderPattern().Replace(template, match => GetCell(row, match.Groups[1].Value.ToUpperInvariant(), rowNumber));
            if (string.IsNullOrWhiteSpace(text) || text.Length > SendMessagesRequest.MaxTextLength)
                throw new InvalidDataException($"متن ساخته‌شده در سطر {rowNumber} خالی یا بیش از ۱۸۰۰ نویسه است.");
            messages.Add(new SendMessageDraft { Recipient = recipient, Text = text });
        }
        if (messages.Count == 0) throw new InvalidDataException("فایل هیچ سطر قابل ارسال ندارد.");
        return messages;
    }

    private static string GetCell(IDictionary<string, object> row, string column, int rowNumber)
    {
        if (!row.TryGetValue(column, out object? value))
            throw new InvalidDataException($"ستون {column} در سطر {rowNumber} وجود ندارد.");
        return CellText(value);
    }

    private static string CellText(object? value) => value is IFormattable formatted
        ? formatted.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? string.Empty;

    [GeneratedRegex(@"\[([A-Za-z]{1,3})\]")]
    private static partial Regex PlaceholderPattern();
    [GeneratedRegex(@"^[A-Z]{1,3}$")]
    private static partial Regex ColumnPattern();
}
