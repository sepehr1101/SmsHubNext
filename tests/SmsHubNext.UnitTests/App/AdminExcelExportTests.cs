using System.IO.Compression;
using System.Xml.Linq;
using SmsHubNext.Features.Administration.Ui.Components;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class AdminExcelExportTests
{
    [Fact]
    public void Workbook_preserves_phone_numbers_and_formula_like_text_as_strings()
    {
        byte[] bytes = AdminExcelExport.Create(["شماره", "توضیح", "مبلغ"], [["09123456789", "=SUM(A1:A2)", 1200.25m]]);
        using MemoryStream stream = new(bytes);
        using ZipArchive archive = new(stream);
        using Stream sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XDocument document = XDocument.Load(sheet);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XElement[] cells = document.Descendants(ns + "c").ToArray();
        Assert.Equal("inlineStr", cells[3].Attribute("t")?.Value);
        Assert.Equal("09123456789", cells[3].Value);
        Assert.Equal("inlineStr", cells[4].Attribute("t")?.Value);
        Assert.Equal("=SUM(A1:A2)", cells[4].Value);
        Assert.Empty(document.Descendants(ns + "f"));
        Assert.Equal("1200.25", cells[5].Value);
        Assert.Equal("1", document.Descendants(ns + "sheetView").Single().Attribute("rightToLeft")?.Value);
        Assert.Equal("A1:C2", document.Descendants(ns + "autoFilter").Single().Attribute("ref")?.Value);
    }

    [Fact]
    public void Workbook_escapes_persian_text_and_supports_columns_after_Z()
    {
        string[] headers = Enumerable.Range(1, 28).Select(index => $"ستون {index}").ToArray();
        byte[] bytes = AdminExcelExport.Create(headers, [headers.Select(_ => (object?)"آب & برق <سازمان>").ToArray()]);
        using MemoryStream stream = new(bytes);
        using ZipArchive archive = new(stream);
        using Stream sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XDocument document = XDocument.Load(sheet);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XElement cell = document.Descendants(ns + "c").Single(item => item.Attribute("r")?.Value == "AB2");
        Assert.Equal("آب & برق <سازمان>", cell.Value);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/_rels/workbook.xml.rels"));
    }
}
