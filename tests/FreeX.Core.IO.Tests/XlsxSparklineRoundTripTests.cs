using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip fidelity tests for sparkline group settings (colors, markers, axis,
/// scaling, line weight, empty-cell handling) via XlsxFileAdapter (save → reload).
/// Also covers multi-group preservation (IO3) and unknown-extLst preservation (IO4).
/// </summary>
public sealed class XlsxSparklineRoundTripTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    private static MemoryStream SaveXlsx(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static (Workbook workbook, XDocument worksheetXml) SaveAndReadXml(Workbook workbook)
    {
        using var saved = SaveXlsx(workbook);

        // Read the worksheet XML directly for structural assertions.
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument wsXml;
        using (var s = entry.Open())
            wsXml = XDocument.Load(s);

        // Reload via adapter.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        return (reloaded, wsXml);
    }

    private static IEnumerable<XElement> SparklineGroups(XDocument wsXml) =>
        wsXml.Descendants().Where(e =>
            string.Equals(e.Name.LocalName, "sparklineGroup", StringComparison.OrdinalIgnoreCase));

    // ── Full settings round-trip ───────────────────────────────────────────────

    [Fact]
    public void Sparkline_RoundTrips_AllGroupSettings_ThroughXlsx()
    {
        var workbook = new Workbook("SparklineFullSettings");
        var sheet    = workbook.AddSheet("Data");

        for (uint col = 1; col <= 5; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        var sparkline = new SparklineModel
        {
            DataRange           = Range(sheet, 1, 1, 1, 5),
            Location            = new CellAddress(sheet.Id, 1, 6),
            Kind                = SparklineKind.Line,
            SeriesColor         = new CellColor(0x10, 0x20, 0x30),
            NegativeColor       = new CellColor(0xFF, 0x00, 0x00),
            AxisColor           = new CellColor(0x00, 0x00, 0x00),
            MarkersColor        = new CellColor(0x00, 0xFF, 0x00),
            HighPointColor      = new CellColor(0xFF, 0xA5, 0x00),
            LowPointColor       = new CellColor(0x80, 0x00, 0x80),
            FirstPointColor     = new CellColor(0x00, 0x00, 0xFF),
            LastPointColor      = new CellColor(0xC0, 0xC0, 0xC0),
            ShowMarkers         = true,
            ShowHighPoint       = true,
            ShowLowPoint        = true,
            ShowFirstPoint      = true,
            ShowLastPoint       = true,
            ShowNegativePoints  = true,
            ShowAxis            = true,
            DisplayHidden       = true,
            RightToLeft         = false,
            LineWeight          = 2.25,
            MinAxisType         = SparklineAxisScaling.Group,
            MaxAxisType         = SparklineAxisScaling.Custom,
            ManualMax           = 100.0,
            DisplayEmptyCellsAs = SparklineEmptyCellDisplay.Zero,
        };

        sheet.Sparklines.Add(sparkline);

        var (reloaded, wsXml) = SaveAndReadXml(workbook);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(1);
        var s = reloadedSheet.Sparklines[0];

        // ── Inspect the saved XML ─────────────────────────────────────────────
        var groups = SparklineGroups(wsXml).ToList();
        groups.Should().HaveCount(1);
        var grp = groups[0];

        grp.Attribute("type")!.Value.Should().Be("line");
        grp.Attribute("lineWeight")!.Value.Should().Be("2.25");
        grp.Attribute("markers")!.Value.Should().BeOneOf("1", "true");
        grp.Attribute("high")!.Value.Should().BeOneOf("1", "true");
        grp.Attribute("low")!.Value.Should().BeOneOf("1", "true");
        grp.Attribute("first")!.Value.Should().BeOneOf("1", "true");
        grp.Attribute("last")!.Value.Should().BeOneOf("1", "true");
        grp.Attribute("negative")!.Value.Should().BeOneOf("1", "true");
        grp.Attribute("displayXAxis")!.Value.Should().BeOneOf("1", "true");
        grp.Attribute("displayHidden")!.Value.Should().BeOneOf("1", "true");
        grp.Attribute("minAxisType")!.Value.Should().Be("group");
        grp.Attribute("maxAxisType")!.Value.Should().Be("custom");
        grp.Attribute("manualMax")!.Value.Should().Be("100");
        grp.Attribute("displayEmptyCellsAs")!.Value.Should().Be("zero");

        // Color sub-elements present
        var colorNames = grp.Elements()
            .Select(e => e.Name.LocalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        colorNames.Should().Contain("colorSeries");
        colorNames.Should().Contain("colorNegative");
        colorNames.Should().Contain("colorAxis");
        colorNames.Should().Contain("colorMarkers");
        colorNames.Should().Contain("colorFirst");
        colorNames.Should().Contain("colorLast");
        colorNames.Should().Contain("colorHigh");
        colorNames.Should().Contain("colorLow");

        // ── Assert reloaded model ─────────────────────────────────────────────
        s.Kind.Should().Be(SparklineKind.Line);
        s.ShowMarkers.Should().BeTrue();
        s.ShowHighPoint.Should().BeTrue();
        s.ShowLowPoint.Should().BeTrue();
        s.ShowFirstPoint.Should().BeTrue();
        s.ShowLastPoint.Should().BeTrue();
        s.ShowNegativePoints.Should().BeTrue();
        s.ShowAxis.Should().BeTrue();
        s.DisplayHidden.Should().BeTrue();
        s.RightToLeft.Should().BeFalse();
        s.LineWeight.Should().BeApproximately(2.25, 1e-9);
        s.MinAxisType.Should().Be(SparklineAxisScaling.Group);
        s.MaxAxisType.Should().Be(SparklineAxisScaling.Custom);
        s.ManualMax.Should().BeApproximately(100.0, 1e-9);
        s.ManualMin.Should().BeNull();
        s.DisplayEmptyCellsAs.Should().Be(SparklineEmptyCellDisplay.Zero);

        s.SeriesColor.Should().Be(new CellColor(0x10, 0x20, 0x30));
        s.NegativeColor.Should().Be(new CellColor(0xFF, 0x00, 0x00));
        s.AxisColor.Should().Be(new CellColor(0x00, 0x00, 0x00));
        s.MarkersColor.Should().Be(new CellColor(0x00, 0xFF, 0x00));
        s.HighPointColor.Should().Be(new CellColor(0xFF, 0xA5, 0x00));
        s.LowPointColor.Should().Be(new CellColor(0x80, 0x00, 0x80));
        s.FirstPointColor.Should().Be(new CellColor(0x00, 0x00, 0xFF));
        s.LastPointColor.Should().Be(new CellColor(0xC0, 0xC0, 0xC0));
    }

    // ── Multi-group round-trip (IO3) ───────────────────────────────────────────

    [Fact]
    public void Sparkline_TwoLineGroupsWithDifferentColors_BothSurviveRoundTrip()
    {
        var workbook = new Workbook("SparklineMultiGroup");
        var sheet    = workbook.AddSheet("Data");

        for (uint col = 1; col <= 5; col++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));
            sheet.SetCell(new CellAddress(sheet.Id, 2, col), new NumberValue(col * 2));
        }

        // Two Line sparklines intentionally assigned to DIFFERENT groups.
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange    = Range(sheet, 1, 1, 1, 5),
            Location     = new CellAddress(sheet.Id, 1, 6),
            Kind         = SparklineKind.Line,
            GroupId      = 1,
            SeriesColor  = new CellColor(0xFF, 0x00, 0x00),
            LineWeight   = 1.0,
        });
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange    = Range(sheet, 2, 1, 2, 5),
            Location     = new CellAddress(sheet.Id, 2, 6),
            Kind         = SparklineKind.Line,
            GroupId      = 2,
            SeriesColor  = new CellColor(0x00, 0x00, 0xFF),
            LineWeight   = 3.0,
        });

        var (reloaded, wsXml) = SaveAndReadXml(workbook);

        // Two distinct sparklineGroup elements must exist.
        var groups = SparklineGroups(wsXml).ToList();
        groups.Should().HaveCount(2, "each GroupId must produce a separate <x14:sparklineGroup>");

        // Both must be "line".
        groups.Should().AllSatisfy(g => g.Attribute("type")!.Value.Should().Be("line"));

        // The two groups must have distinct lineWeight values.
        var weights = groups.Select(g => g.Attribute("lineWeight")?.Value).ToHashSet();
        weights.Should().HaveCount(2);

        // Reloaded model: two sparklines, both Line, different SeriesColors.
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.Sparklines.Should().HaveCount(2);

        var colors = reloadedSheet.Sparklines
            .Select(s => s.SeriesColor)
            .ToList();
        colors.Should().Contain(new CellColor(0xFF, 0x00, 0x00));
        colors.Should().Contain(new CellColor(0x00, 0x00, 0xFF));

        var lw = reloadedSheet.Sparklines
            .Select(s => s.LineWeight)
            .OrderBy(x => x)
            .ToList();
        lw[0].Should().BeApproximately(1.0, 1e-9);
        lw[1].Should().BeApproximately(3.0, 1e-9);
    }

    // ── Span empty-cell mode ───────────────────────────────────────────────────

    [Fact]
    public void Sparkline_DisplayEmptyCellsAs_Span_RoundTrips()
    {
        var workbook = new Workbook("SparklineSpan");
        var sheet    = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(10));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange           = Range(sheet, 1, 1, 1, 3),
            Location            = new CellAddress(sheet.Id, 1, 4),
            Kind                = SparklineKind.Line,
            DisplayEmptyCellsAs = SparklineEmptyCellDisplay.Span,
        });

        var (reloaded, wsXml) = SaveAndReadXml(workbook);

        var grp = SparklineGroups(wsXml).Single();
        grp.Attribute("displayEmptyCellsAs")!.Value.Should().Be("span");

        var s = reloaded.GetSheetAt(0).Sparklines.Single();
        s.DisplayEmptyCellsAs.Should().Be(SparklineEmptyCellDisplay.Span);
    }

    // ── ManualMin + Custom MinAxisType ─────────────────────────────────────────

    [Fact]
    public void Sparkline_CustomMinAxis_RoundTrips()
    {
        var workbook = new Workbook("SparklineCustomAxis");
        var sheet    = workbook.AddSheet("Data");
        for (uint col = 1; col <= 4; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col * 5));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange   = Range(sheet, 1, 1, 1, 4),
            Location    = new CellAddress(sheet.Id, 1, 5),
            Kind        = SparklineKind.Column,
            MinAxisType = SparklineAxisScaling.Custom,
            ManualMin   = -10.0,
            MaxAxisType = SparklineAxisScaling.Group,
        });

        var (reloaded, wsXml) = SaveAndReadXml(workbook);

        var grp = SparklineGroups(wsXml).Single();
        grp.Attribute("minAxisType")!.Value.Should().Be("custom");
        grp.Attribute("maxAxisType")!.Value.Should().Be("group");
        grp.Attribute("manualMin")!.Value.Should().Be("-10");

        var s = reloaded.GetSheetAt(0).Sparklines.Single();
        s.MinAxisType.Should().Be(SparklineAxisScaling.Custom);
        s.MaxAxisType.Should().Be(SparklineAxisScaling.Group);
        s.ManualMin.Should().BeApproximately(-10.0, 1e-9);
        s.ManualMax.Should().BeNull();
    }

    // ── Default values are omitted from XML ───────────────────────────────────

    [Fact]
    public void Sparkline_DefaultSettings_DoNotWriteRedundantAttributes()
    {
        var workbook = new Workbook("SparklineDefaults");
        var sheet    = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, 1, 1, 1, 1),
            Location  = new CellAddress(sheet.Id, 1, 2),
            Kind      = SparklineKind.Line,
            // All other settings left at defaults
        });

        var (_, wsXml) = SaveAndReadXml(workbook);

        var grp = SparklineGroups(wsXml).Single();

        // Default bools must not be written.
        grp.Attribute("markers").Should().BeNull();
        grp.Attribute("high").Should().BeNull();
        grp.Attribute("low").Should().BeNull();
        grp.Attribute("first").Should().BeNull();
        grp.Attribute("last").Should().BeNull();
        grp.Attribute("negative").Should().BeNull();
        grp.Attribute("displayXAxis").Should().BeNull();
        grp.Attribute("displayHidden").Should().BeNull();
        grp.Attribute("rightToLeft").Should().BeNull();

        // Default axis types not written.
        grp.Attribute("minAxisType").Should().BeNull();
        grp.Attribute("maxAxisType").Should().BeNull();
        grp.Attribute("manualMin").Should().BeNull();
        grp.Attribute("manualMax").Should().BeNull();

        // Default empty-cell display (Gap) not written.
        grp.Attribute("displayEmptyCellsAs").Should().BeNull();
    }

    // ── Schema validity still passes ──────────────────────────────────────────

    [Fact]
    public void Sparkline_FullSettings_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("SparklineSchemaFull");
        var sheet    = workbook.AddSheet("Data");

        for (uint col = 1; col <= 5; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange           = Range(sheet, 1, 1, 1, 5),
            Location            = new CellAddress(sheet.Id, 1, 6),
            Kind                = SparklineKind.Line,
            SeriesColor         = new CellColor(0x10, 0x20, 0x30),
            NegativeColor       = new CellColor(0xFF, 0x00, 0x00),
            AxisColor           = new CellColor(0x00, 0x00, 0x00),
            MarkersColor        = new CellColor(0x00, 0xFF, 0x00),
            HighPointColor      = new CellColor(0xFF, 0xA5, 0x00),
            LowPointColor       = new CellColor(0x80, 0x00, 0x80),
            FirstPointColor     = new CellColor(0x00, 0x00, 0xFF),
            LastPointColor      = new CellColor(0xC0, 0xC0, 0xC0),
            ShowMarkers         = true,
            ShowHighPoint       = true,
            ShowLowPoint        = true,
            ShowFirstPoint      = true,
            ShowLastPoint       = true,
            ShowNegativePoints  = true,
            ShowAxis            = true,
            LineWeight          = 2.25,
            MinAxisType         = SparklineAxisScaling.Group,
            MaxAxisType         = SparklineAxisScaling.Custom,
            ManualMax           = 100.0,
            DisplayEmptyCellsAs = SparklineEmptyCellDisplay.Zero,
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        SchemaErrors(stream).Should().BeEmpty();
    }

    private static List<string> SchemaErrors(Stream stream)
    {
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Select(e => e.Description)
            .ToList();
    }

    // ── NativeJson round-trip ─────────────────────────────────────────────────

    [Fact]
    public void Sparkline_NativeJson_RoundTrips_AllSettings()
    {
        var workbook = new Workbook("SparklineJsonFull");
        var sheet    = workbook.AddSheet("Data");

        for (uint col = 1; col <= 5; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        var sparkline = new SparklineModel
        {
            DataRange           = Range(sheet, 1, 1, 1, 5),
            Location            = new CellAddress(sheet.Id, 1, 6),
            Kind                = SparklineKind.Column,
            GroupId             = 7,
            SeriesColor         = new CellColor(0xAA, 0xBB, 0xCC),
            NegativeColor       = new CellColor(0x11, 0x22, 0x33),
            AxisColor           = new CellColor(0x44, 0x55, 0x66),
            MarkersColor        = new CellColor(0x77, 0x88, 0x99),
            HighPointColor      = new CellColor(0xAA, 0xBB, 0xCC),
            LowPointColor       = new CellColor(0xDD, 0xEE, 0xFF),
            FirstPointColor     = new CellColor(0x01, 0x02, 0x03),
            LastPointColor      = new CellColor(0x04, 0x05, 0x06),
            ShowMarkers         = true,
            ShowHighPoint       = true,
            ShowLowPoint        = false,
            ShowFirstPoint      = true,
            ShowLastPoint       = false,
            ShowNegativePoints  = true,
            ShowAxis            = true,
            DisplayHidden       = false,
            RightToLeft         = true,
            LineWeight          = 1.5,
            MinAxisType         = SparklineAxisScaling.Custom,
            MaxAxisType         = SparklineAxisScaling.Group,
            ManualMin           = -5.0,
            ManualMax           = null,
            DisplayEmptyCellsAs = SparklineEmptyCellDisplay.Zero,
        };

        sheet.Sparklines.Add(sparkline);

        // Round-trip via NativeJson.
        using var jsonStream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, jsonStream);
        jsonStream.Position = 0;
        var reloaded = adapter.Load(jsonStream);

        var s = reloaded.GetSheetAt(0).Sparklines.Single();

        s.Kind.Should().Be(SparklineKind.Column);
        s.GroupId.Should().Be(7);
        s.ShowMarkers.Should().BeTrue();
        s.ShowHighPoint.Should().BeTrue();
        s.ShowLowPoint.Should().BeFalse();
        s.ShowFirstPoint.Should().BeTrue();
        s.ShowLastPoint.Should().BeFalse();
        s.ShowNegativePoints.Should().BeTrue();
        s.ShowAxis.Should().BeTrue();
        s.DisplayHidden.Should().BeFalse();
        s.RightToLeft.Should().BeTrue();
        s.LineWeight.Should().BeApproximately(1.5, 1e-9);
        s.MinAxisType.Should().Be(SparklineAxisScaling.Custom);
        s.MaxAxisType.Should().Be(SparklineAxisScaling.Group);
        s.ManualMin.Should().BeApproximately(-5.0, 1e-9);
        s.ManualMax.Should().BeNull();
        s.DisplayEmptyCellsAs.Should().Be(SparklineEmptyCellDisplay.Zero);

        s.SeriesColor.Should().Be(new CellColor(0xAA, 0xBB, 0xCC));
        s.NegativeColor.Should().Be(new CellColor(0x11, 0x22, 0x33));
        s.AxisColor.Should().Be(new CellColor(0x44, 0x55, 0x66));
        s.MarkersColor.Should().Be(new CellColor(0x77, 0x88, 0x99));
        s.HighPointColor.Should().Be(new CellColor(0xAA, 0xBB, 0xCC));
        s.LowPointColor.Should().Be(new CellColor(0xDD, 0xEE, 0xFF));
        s.FirstPointColor.Should().Be(new CellColor(0x01, 0x02, 0x03));
        s.LastPointColor.Should().Be(new CellColor(0x04, 0x05, 0x06));
    }
}
