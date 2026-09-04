using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R79-io-sparkline-5-1: per the real CT_SparklineGroup OOXML schema, a sparkline group's
/// date-axis range is a bare &lt;xm:f&gt; that is a DIRECT child of &lt;x14:sparklineGroup&gt;
/// (after the color elements, before &lt;x14:sparklines&gt;), gated by the group's own boolean
/// <c>dateAxis="1"</c> attribute -- there is no wrapper element named "dateAxis". The mapper used
/// to look for a CHILD ELEMENT literally named "dateAxis" and an "f" grandchild inside it, a shape
/// that never occurs in a real Excel-produced file, so a genuine date-axis sparkline lost its axis
/// range on load, and the write side made the same mistake in reverse (fabricating the wrapper
/// instead of the attribute + bare formula), so FreeX-authored date-axis sparklines were invisible
/// to real Excel too. These tests pin the fixed, schema-correct shape on both the read and write
/// sides, independent of each other.
/// </summary>
public sealed class R79_SparklineDateAxisSchemaTests
{
    private const string X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string XmNs  = "http://schemas.microsoft.com/office/excel/2006/main";

    private static MemoryStream SaveXlsx(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static void RewriteWorksheetXml(MemoryStream packageStream, string worksheetPath, Action<XDocument> mutate)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry(worksheetPath)!;

        XDocument worksheetXml;
        using (var reader = new StreamReader(entry.Open()))
            worksheetXml = XDocument.Parse(reader.ReadToEnd());

        mutate(worksheetXml);

        entry.Delete();
        var newEntry = archive.CreateEntry(worksheetPath);
        using var writer = new StreamWriter(newEntry.Open());
        writer.Write(worksheetXml.ToString(SaveOptions.DisableFormatting));
    }

    private static XDocument ReadWorksheetXml(Stream packageStream, string worksheetPath)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(worksheetPath)!;
        using var reader = new StreamReader(entry.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    private static XElement SparklineGroup(XDocument wsXml) =>
        wsXml.Descendants().Single(e =>
            string.Equals(e.Name.LocalName, "sparklineGroup", StringComparison.OrdinalIgnoreCase));

    // ── Read side: the real Excel schema shape must be understood ──────────────

    [Fact]
    public void RealExcelSchemaShape_DateAxisAttributeWithBareFormula_IsReadCorrectly()
    {
        // Build an ordinary workbook + sparkline (no date axis yet) to get a well-formed package
        // with all the surrounding parts (workbook.xml, rels, styles, extLst, ...), then hand-rewrite
        // just the sparklineGroup element into the REAL Excel shape: a dateAxis="1" attribute on the
        // group plus a bare <xm:f> direct child -- exactly what real Excel emits and what the old
        // "<x14:dateAxis><xm:f>...</xm:f></x14:dateAxis>" wrapper-hunting read code could never match.
        var workbook = new Workbook("SparklineRealSchema");
        var sheet = workbook.AddSheet("Data");
        for (uint col = 1; col <= 5; col++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));
            sheet.SetCell(new CellAddress(sheet.Id, 2, col), new NumberValue(45658 + col));
        }
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 5)),
            Location  = new CellAddress(sheet.Id, 1, 6),
            Kind      = SparklineKind.Line,
        });

        using var packageStream = SaveXlsx(workbook);

        RewriteWorksheetXml(packageStream, "xl/worksheets/sheet1.xml", wsXml =>
        {
            var group = SparklineGroup(wsXml);
            group.SetAttributeValue("dateAxis", "1");
            group.Add(new XElement(XNamespace.Get(XmNs) + "f", "Data!A2:E2"));
        });

        packageStream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(packageStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(1);
        var reloadedSparkline = reloadedSheet.Sparklines[0];

        reloadedSparkline.DateAxisRange.Should().NotBeNull(
            "the real Excel dateAxis=\"1\" attribute + bare <xm:f> shape must be recognized as a date-axis range");
        reloadedSparkline.DateAxisRange!.Value.Start.Row.Should().Be(2u);
        reloadedSparkline.DateAxisRange!.Value.Start.Col.Should().Be(1u);
        reloadedSparkline.DateAxisRange!.Value.End.Row.Should().Be(2u);
        reloadedSparkline.DateAxisRange!.Value.End.Col.Should().Be(5u);
        reloadedSparkline.DateAxisRange!.Value.Start.Sheet.Should().Be(reloadedSheet.Id);
    }

    // ── No-regression sibling: no dateAxis attribute -> no false-positive date axis ────

    [Fact]
    public void NoDateAxisAttribute_DateAxisRangeStaysNull_OrdinarySparklineUnaffected()
    {
        // A sparkline group with none of the date-axis machinery must still load cleanly with
        // DateAxisRange null -- the attribute-gated read must not spuriously pick up anything.
        var workbook = new Workbook("SparklineNoDateAxis");
        var sheet = workbook.AddSheet("Data");
        for (uint col = 1; col <= 5; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 5)),
            Location  = new CellAddress(sheet.Id, 1, 6),
            Kind      = SparklineKind.Line,
        });

        using var packageStream = SaveXlsx(workbook);

        var wsXmlBeforeReload = ReadWorksheetXml(packageStream, "xl/worksheets/sheet1.xml");
        var group = SparklineGroup(wsXmlBeforeReload);
        group.Attribute("dateAxis").Should().BeNull("an ordinary sparkline must not carry the dateAxis attribute");

        packageStream.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(packageStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(1);
        reloadedSheet.Sparklines[0].DateAxisRange.Should().BeNull();
        reloadedSheet.Sparklines[0].Kind.Should().Be(SparklineKind.Line);
    }

    // ── Write side: saving a date-axis sparkline must emit the real schema shape ────────

    [Fact]
    public void Save_EmitsRealSchemaShape_AttributePlusBareFormula_NoWrapperElement()
    {
        var workbook = new Workbook("SparklineWriteSchema");
        var sheet = workbook.AddSheet("Data");
        for (uint col = 1; col <= 5; col++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));
            sheet.SetCell(new CellAddress(sheet.Id, 2, col), new NumberValue(45658 + col));
        }
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange     = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 5)),
            Location      = new CellAddress(sheet.Id, 1, 6),
            Kind          = SparklineKind.Line,
            DateAxisRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 5)),
        });

        using var packageStream = SaveXlsx(workbook);
        var wsXml = ReadWorksheetXml(packageStream, "xl/worksheets/sheet1.xml");
        var group = SparklineGroup(wsXml);

        group.Attribute("dateAxis")!.Value.Should().Be("1",
            "real Excel gates the date-axis range with a dateAxis=\"1\" attribute on the group itself");

        group.Elements().Should().NotContain(e =>
            string.Equals(e.Name.LocalName, "dateAxis", StringComparison.OrdinalIgnoreCase),
            "there is no <dateAxis> wrapper element in the real CT_SparklineGroup schema");

        var bareFormula = group.Elements()
            .Single(e => string.Equals(e.Name.LocalName, "f", StringComparison.OrdinalIgnoreCase)).Value;
        bareFormula.Should().Be("Data!A2:E2");
    }
}
