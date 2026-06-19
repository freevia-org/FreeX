using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the XLTX template save adapter. It writes through the standard XLSX writer and flips only
/// the workbook content-type to the template type, so the tests assert (a) the content-type flip and
/// (b) that the package is still a readable workbook whose values round-trip.
/// </summary>
public sealed class XltxFileAdapterTests
{
    private const string TemplateMainContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml";
    private const string WorksheetMainContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";

    private static Workbook BuildSample()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(123.5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"));
        return wb;
    }

    private static string? ReadWorkbookContentType(byte[] package)
    {
        using var ms = new MemoryStream(package);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry("[Content_Types].xml")!;
        using var stream = entry.Open();
        var xml = XDocument.Load(stream);
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        return xml.Root!
            .Elements(ct + "Override")
            .FirstOrDefault(e => string.Equals(e.Attribute("PartName")?.Value, "/xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")?.Value;
    }

    [Fact]
    public void Save_FlipsWorkbookContentTypeToTemplate()
    {
        using var stream = new MemoryStream();
        new XltxFileAdapter().Save(BuildSample(), stream);

        var contentType = ReadWorkbookContentType(stream.ToArray());
        contentType.Should().Be(TemplateMainContentType);
        contentType.Should().NotBe(WorksheetMainContentType);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsValuesAndFormula()
    {
        var adapter = new XltxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(BuildSample(), stream);
        stream.Position = 0;

        var sheet = adapter.Load(stream).Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Header"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(123.5));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Be("A2*2");
    }

    [Fact]
    public void Save_ProducesPackageReadableByTheXlsxAdapter()
    {
        using var stream = new MemoryStream();
        new XltxFileAdapter().Save(BuildSample(), stream);
        stream.Position = 0;

        // The xltx package is structurally a workbook; the xlsx loader must open it without error.
        var sheet = new XlsxFileAdapter().Load(stream).Sheets.Single();
        sheet.Name.Should().Be("Data");
    }
}
