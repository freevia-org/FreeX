using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 25 finding R25-io-validation-cf-extlst-1: a sparkline's data range (and optional date-axis
/// range) may legitimately live on a DIFFERENT sheet than the sparkline itself -- Excel's Sparkline
/// "Edit Data" dialog allows picking a cross-sheet source range. XlsxSparklineMapper.Save previously
/// (a) required <c>DataRange.Start.Sheet == sheet.Id</c> to even include the sparkline, silently
/// dropping the whole group when that didn't hold, and (b) always qualified the written <c>&lt;xm:f&gt;</c>
/// formula with the HOST sheet's name regardless of which sheet the range actually pointed at. These
/// tests pin the fixed behavior (cross-sheet ranges survive and are correctly qualified) alongside the
/// pre-existing same-sheet case (to prove no regression).
/// </summary>
public sealed class XlsxSparklineCrossSheetTests
{
    private static MemoryStream SaveXlsx(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static XDocument ReadWorksheetXml(Stream xlsxStream, string worksheetPath)
    {
        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(worksheetPath)!;
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private static IEnumerable<XElement> SparklineGroups(XDocument wsXml) =>
        wsXml.Descendants().Where(e =>
            string.Equals(e.Name.LocalName, "sparklineGroup", StringComparison.OrdinalIgnoreCase));

    private static string? FormulaOf(XElement element) =>
        element.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "f", StringComparison.OrdinalIgnoreCase))?.Value;

    // ── Bug case: cross-sheet data range ───────────────────────────────────────

    [Fact]
    public void CrossSheetDataRange_SurvivesSave_QualifiedWithSourceSheetName_NotHostSheet()
    {
        var workbook = new Workbook("SparklineCrossSheet");
        var data = workbook.AddSheet("Data");     // host sheet: hosts the sparkline itself
        var source = workbook.AddSheet("Source"); // data-source sheet: different from the host

        for (uint col = 1; col <= 5; col++)
            source.SetCell(new CellAddress(source.Id, 1, col), new NumberValue(col));

        // Sparkline sits on "Data" but its data range is on "Source" -- exactly what real Excel's
        // Sparkline "Edit Data" dialog allows when picking a cross-sheet source range.
        var sparkline = new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 1, 5)),
            Location = new CellAddress(data.Id, 2, 2),
            Kind = SparklineKind.Line,
        };
        data.Sparklines.Add(sparkline);

        using var saved = SaveXlsx(workbook);
        var wsXml = ReadWorksheetXml(saved, "xl/worksheets/sheet1.xml");

        var groups = SparklineGroups(wsXml).ToList();
        groups.Should().ContainSingle(
            "the sparkline group must not be silently dropped just because its data range is on a different sheet");

        var sparklineElement = groups[0].Descendants()
            .Single(e => string.Equals(e.Name.LocalName, "sparkline", StringComparison.OrdinalIgnoreCase));
        var formula = FormulaOf(sparklineElement);

        formula.Should().Be("Source!A1:E1",
            "the written formula must qualify with the range's ACTUAL sheet (Source), not the host sheet (Data)");
    }

    // ── Bug case: cross-sheet date-axis range ──────────────────────────────────

    [Fact]
    public void CrossSheetDateAxisRange_SurvivesSave_QualifiedWithSourceSheetName_NotHostSheet()
    {
        var workbook = new Workbook("SparklineCrossSheetDateAxis");
        var data = workbook.AddSheet("Data");
        var source = workbook.AddSheet("Source");

        for (uint col = 1; col <= 3; col++)
        {
            source.SetCell(new CellAddress(source.Id, 1, col), new NumberValue(col));
            // Date-axis cells hold Excel serial-date numbers; a NumberValue is sufficient here since
            // this test only exercises the sheet-qualifier resolution, not date formatting.
            source.SetCell(new CellAddress(source.Id, 2, col), new NumberValue(46023 + col));
        }

        var sparkline = new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 1, 3)),
            Location = new CellAddress(data.Id, 1, 5),
            Kind = SparklineKind.Line,
            DateAxisRange = new GridRange(new CellAddress(source.Id, 2, 1), new CellAddress(source.Id, 2, 3)),
        };
        data.Sparklines.Add(sparkline);

        using var saved = SaveXlsx(workbook);
        var wsXml = ReadWorksheetXml(saved, "xl/worksheets/sheet1.xml");

        var group = SparklineGroups(wsXml).Single();

        // Per the real CT_SparklineGroup schema there is no wrapper element -- the date-axis range
        // is a bare <xm:f> direct child of the group, gated by the group's own dateAxis="1" attribute.
        group.Attribute("dateAxis")!.Value.Should().Be("1");
        var dateAxisFormula = group.Elements()
            .Single(e => string.Equals(e.Name.LocalName, "f", StringComparison.OrdinalIgnoreCase)).Value;

        dateAxisFormula.Should().Be("Source!A2:C2",
            "the date-axis formula must qualify with its own referenced sheet (Source), not the host sheet (Data)");
    }

    // ── Bug case (read side): cross-sheet range resolves back to the source sheet on load ──

    [Fact]
    public void CrossSheetRanges_RoundTripThroughLoad_AnchoredToSourceSheet_NotHost()
    {
        // The READ-side half of R25-io-validation-cf-extlst-1: before the fix, XlsxSparklineMapper.Read
        // discarded the "Source!" qualifier and the loader force-anchored the reloaded DataRange (and
        // DateAxisRange) onto the HOST sheet, so a genuine cross-sheet sparkline came back pointing at
        // the wrong sheet's cells. This full Save→Load round trip pins that both ranges now resolve to
        // the sheet the <xm:f> qualifier actually named.
        var workbook = new Workbook("SparklineCrossSheetRoundTrip");
        var data = workbook.AddSheet("Data");     // host sheet: hosts the sparkline itself
        var source = workbook.AddSheet("Source"); // data-source sheet: different from the host

        for (uint col = 1; col <= 5; col++)
        {
            source.SetCell(new CellAddress(source.Id, 1, col), new NumberValue(col));
            source.SetCell(new CellAddress(source.Id, 2, col), new NumberValue(46023 + col));
        }

        data.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 1, 5)),
            Location = new CellAddress(data.Id, 2, 2),
            Kind = SparklineKind.Line,
            DateAxisRange = new GridRange(new CellAddress(source.Id, 2, 1), new CellAddress(source.Id, 2, 5)),
        });

        using var saved = SaveXlsx(workbook);

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedData = reloaded.GetSheet("Data")!;
        var reloadedSource = reloaded.GetSheet("Source")!;

        reloadedData.Sparklines.Should().HaveCount(1);
        var reloadedSparkline = reloadedData.Sparklines[0];

        // Location stays on the host sheet (a sparkline always occupies a cell on its own sheet).
        reloadedSparkline.Location.Sheet.Should().Be(reloadedData.Id);

        // The data range must resolve to the SOURCE sheet's id -- the whole point of the read-side fix.
        reloadedSparkline.DataRange.Start.Sheet.Should().Be(reloadedSource.Id,
            "the reloaded data range must point at the Source sheet the <xm:f> qualifier named, not the host (Data) sheet");
        reloadedSparkline.DataRange.End.Sheet.Should().Be(reloadedSource.Id);
        reloadedSparkline.DataRange.Start.Row.Should().Be(1);
        reloadedSparkline.DataRange.Start.Col.Should().Be(1);
        reloadedSparkline.DataRange.End.Col.Should().Be(5);

        // The cross-sheet date-axis range must likewise resolve back to Source, not the host sheet.
        reloadedSparkline.DateAxisRange.Should().NotBeNull();
        reloadedSparkline.DateAxisRange!.Value.Start.Sheet.Should().Be(reloadedSource.Id);
        reloadedSparkline.DateAxisRange!.Value.End.Sheet.Should().Be(reloadedSource.Id);
    }

    // ── Representative already-working case: same-sheet data range (no regression) ─

    [Fact]
    public void SameSheetDataRange_StillQualifiedWithHostSheetName_AndRoundTrips()
    {
        var workbook = new Workbook("SparklineSameSheet");
        var sheet = workbook.AddSheet("Data");

        for (uint col = 1; col <= 5; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        var sparkline = new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 5)),
            Location = new CellAddress(sheet.Id, 1, 6),
            Kind = SparklineKind.Line,
        };
        sheet.Sparklines.Add(sparkline);

        using var saved = SaveXlsx(workbook);
        var wsXml = ReadWorksheetXml(saved, "xl/worksheets/sheet1.xml");

        var group = SparklineGroups(wsXml).Single();
        var sparklineElement = group.Descendants()
            .Single(e => string.Equals(e.Name.LocalName, "sparkline", StringComparison.OrdinalIgnoreCase));

        FormulaOf(sparklineElement).Should().Be("Data!A1:E1",
            "an ordinary same-sheet sparkline must still be qualified with its own (host) sheet name");

        // Full round trip via the adapter must still succeed for the ordinary, same-sheet case.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(1);
        var reloadedSparkline = reloadedSheet.Sparklines[0];
        reloadedSparkline.Kind.Should().Be(SparklineKind.Line);
        reloadedSparkline.DataRange.Start.Row.Should().Be(1);
        reloadedSparkline.DataRange.Start.Col.Should().Be(1);
        reloadedSparkline.DataRange.End.Row.Should().Be(1);
        reloadedSparkline.DataRange.End.Col.Should().Be(5);
    }

    // ── Deleted source sheet: no dangling reference is written ─────────────────

    [Fact]
    public void DataRangeSheetNoLongerInWorkbook_IsExcludedFromSave_NoException()
    {
        // A defensive case for ResolveSheetName's null path: if a sparkline's DataRange somehow
        // points at a SheetId that isn't in the workbook (e.g. the source sheet was removed without
        // updating the sparkline), saving must not throw and must not write a dangling reference.
        var workbook = new Workbook("SparklineOrphanedSheet");
        var data = workbook.AddSheet("Data");
        var missingSheetId = SheetId.New();

        var sparkline = new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(missingSheetId, 1, 1), new CellAddress(missingSheetId, 1, 5)),
            Location = new CellAddress(data.Id, 2, 2),
            Kind = SparklineKind.Line,
        };
        data.Sparklines.Add(sparkline);

        using var saved = SaveXlsx(workbook);
        var wsXml = ReadWorksheetXml(saved, "xl/worksheets/sheet1.xml");

        SparklineGroups(wsXml).Should().BeEmpty(
            "a sparkline whose data range references a sheet no longer in the workbook must not be written");
    }
}
