using MiniExcelLibs;
using SmsHubNext.Features.Sending.Ui;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class ExcelMessageImportTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Replaces_letter_columns_and_optionally_skips_headers(bool headers)
    {
        using MemoryStream stream = Workbook(headers, [new Dictionary<string, object> { ["Name"] = "علی", ["Bill"] = "00123", ["Note"] = "متشکریم", ["Mobile"] = "09123456789" }]);
        List<SendMessageDraft> messages = ExcelMessageImport.Read(stream, "مشترک [A]\nقبض [B]، [C]، [a]", "d", headers);
        Assert.Single(messages);
        Assert.Equal("09123456789", messages[0].Recipient);
        Assert.Equal("مشترک علی\nقبض 00123، متشکریم، علی", messages[0].Text);
    }

    [Fact]
    public void Invalid_later_row_rejects_entire_import()
    {
        using MemoryStream stream = Workbook(false, [Row("09123456789"), Row("bad")]);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ExcelMessageImport.Read(stream, "[A]", "D", false));
        Assert.Contains("سطر 2", error.Message);
    }

    [Fact]
    public void Missing_column_and_oversized_rendered_text_are_rejected()
    {
        using MemoryStream missing = Workbook(false, [Row("09123456789")]);
        Assert.Throws<InvalidDataException>(() => ExcelMessageImport.Read(missing, "[AA]", "D", false));
        using MemoryStream longText = Workbook(false, [Row("09123456789")]);
        Assert.Throws<InvalidDataException>(() => ExcelMessageImport.Read(longText, new string('x', 1796) + "[A]", "D", false));
    }

    [Fact]
    public void Numeric_local_mobile_restores_missing_initial_zero_and_limit_is_enforced()
    {
        Dictionary<string, object> row = Row("9123456789");
        using MemoryStream stream = Workbook(false, [row]);
        Assert.Equal("09123456789", ExcelMessageImport.Read(stream, "[A]", "D", false)[0].Recipient);
        using MemoryStream tooMany = Workbook(false, Enumerable.Repeat(row, 1001).ToArray());
        Assert.Throws<InvalidDataException>(() => ExcelMessageImport.Read(tooMany, "[A]", "D", false));
    }

    private static Dictionary<string, object> Row(string mobile) => new() { ["Name"] = "مشترک", ["Bill"] = "1", ["Note"] = "", ["Mobile"] = mobile };
    private static MemoryStream Workbook(bool headers, Dictionary<string, object>[] rows)
    {
        MemoryStream stream = new();
        MiniExcel.SaveAs(stream, rows, printHeader: headers, excelType: ExcelType.XLSX);
        stream.Position = 0;
        return stream;
    }
}
