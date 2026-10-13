using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace SmsHubNext.Features.Administration.Ui.Components;

public sealed record AdminExportColumn<T>(string Title, Func<T, object?> Value);

public static class AdminExcelExport
{
    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static byte[] Create(string[] headers, IEnumerable<object?[]> rows)
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
                """);
            Write(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            Write(zip, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="گزارش" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            Write(zip, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
                """);
            XElement data = new(Sheet + "sheetData");
            data.Add(Row(headers.Cast<object?>().ToArray(), 1));
            int index = 2;
            foreach (object?[] values in rows) data.Add(Row(values, index++));
            XElement worksheet = new(Sheet + "worksheet",
                new XElement(Sheet + "sheetViews", new XElement(Sheet + "sheetView", new XAttribute("workbookViewId", 0), new XAttribute("rightToLeft", 1), new XElement(Sheet + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
                new XElement(Sheet + "cols", new XElement(Sheet + "col", new XAttribute("min", 1), new XAttribute("max", headers.Length), new XAttribute("width", 24), new XAttribute("customWidth", 1))), data,
                new XElement(Sheet + "autoFilter", new XAttribute("ref", $"A1:{ColumnName(headers.Length - 1)}{index - 1}")));
            Write(zip, "xl/worksheets/sheet1.xml", worksheet.ToString());
        }
        return output.ToArray();
    }
    private static XElement Row(object?[] values, int index) => new(Sheet + "row", new XAttribute("r", index), values.Select((value, column) => Cell(value, $"{ColumnName(column)}{index}")));
    private static XElement Cell(object? value, string address)
    {
        if (value is byte or short or int or long or float or double or decimal)
            return new XElement(Sheet + "c", new XAttribute("r", address), new XElement(Sheet + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
        string text = value is DateTime date ? date.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return new XElement(Sheet + "c", new XAttribute("r", address), new XAttribute("t", "inlineStr"), new XElement(Sheet + "is", new XElement(Sheet + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
    }
    private static string ColumnName(int index)
    {
        string name = string.Empty;
        for (int number = index + 1; number > 0; number = (number - 1) / 26) name = (char)('A' + (number - 1) % 26) + name;
        return name;
    }
    private static void Write(ZipArchive zip, string path, string text)
    {
        using StreamWriter writer = new(zip.CreateEntry(path).Open(), new System.Text.UTF8Encoding(false));
        writer.Write(text);
    }
}
