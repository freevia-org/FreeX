using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// drawing-zorder-share-part (residual-gap closure): a worksheet can reference exactly ONE drawing
/// part, but <c>XlsxWorksheetChartWriter</c> and <c>XlsxWorksheetDrawingObjectWriter</c> each build one
/// independently during the same save (chart writer first, drawing-object writer second). When the
/// sheet already owned a source drawing part, both writers resolved that same path and the chart
/// writer's shadow copy + <c>XlsxWorksheetDrawingPartMerger</c> reunited the two halves afterwards.
/// <para>
/// For a sheet with NO source drawing part -- every sheet of a brand-new, never-saved workbook, and
/// any freshly added/duplicated sheet -- that route does not exist: the merger only runs when the
/// workbook has a source package with drawings, so no shadow was written, the drawing-object writer
/// allocated a SECOND drawing part, and its worksheet <c>&lt;drawing r:id&gt;</c> rewrite silently
/// orphaned the chart writer's part. Result: every chart on a sheet that also carried a picture, shape
/// or text box was lost on save -- and the saved package additionally kept a dangling worksheet
/// relationship to the orphaned part. The fix makes the drawing-object writer write INTO the chart
/// writer's freshly allocated part, carrying its chart anchors and relationships forward.
/// </para>
/// </summary>
public sealed class ChartAndDrawingObjectSharedDrawingPartTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string DrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // ── Primary finding: chart + shape on one from-scratch sheet. Fail-before/pass-after. ──

    [Fact]
    public void ChartAndShapeOnSameFreshSheet_SurviveSaveReload()
    {
        var workbook = NewWorkbook(out var sheet);
        AddChart(sheet, "Chart 1", hyperlink: null);
        AddShape(sheet, "Rectangle 1", hyperlink: null);

        var reloaded = SaveAndReload(workbook, out var saved).GetSheetAt(0);

        reloaded.Charts.Should().ContainSingle("the chart must not be orphaned by the drawing-object writer");
        reloaded.DrawingShapes.Should().ContainSingle();
        AssertSingleDrawingPart(saved);
    }

    [Fact]
    public void ChartAndTextBoxOnSameFreshSheet_SurviveSaveReload()
    {
        var workbook = NewWorkbook(out var sheet);
        AddChart(sheet, "Chart 1", hyperlink: null);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "TextBox 1",
            Anchor = new CellAddress(sheet.Id, 8, 2),
            Text = "hello",
        });

        var reloaded = SaveAndReload(workbook, out var saved).GetSheetAt(0);

        reloaded.Charts.Should().ContainSingle();
        reloaded.TextBoxes.Should().ContainSingle();
        AssertSingleDrawingPart(saved);
    }

    [Fact]
    public void ChartAndPictureOnSameFreshSheet_SurviveSaveReload()
    {
        var workbook = NewWorkbook(out var sheet);
        AddChart(sheet, "Chart 1", hyperlink: null);
        AddPicture(sheet, "Picture 1", hyperlink: null);

        var reloaded = SaveAndReload(workbook, out var saved).GetSheetAt(0);

        reloaded.Charts.Should().ContainSingle();
        reloaded.Pictures.Should().ContainSingle();
        AssertSingleDrawingPart(saved);
    }

    [Fact]
    public void ChartAndDrawingObjectsOnSeveralFreshSheets_EachSheetKeepsBoth()
    {
        var workbook = new Workbook("Book");
        for (var i = 0; i < 3; i++)
        {
            var sheet = workbook.AddSheet("S" + i);
            SeedData(sheet);
            AddChart(sheet, "Chart " + i, hyperlink: null);
            AddShape(sheet, "Rectangle " + i, hyperlink: null);
        }

        var reloaded = SaveAndReload(workbook, out var saved);

        for (var i = 0; i < 3; i++)
        {
            reloaded.GetSheetAt(i).Charts.Should().ContainSingle("sheet {0} must keep its chart", i);
            reloaded.GetSheetAt(i).DrawingShapes.Should().ContainSingle("sheet {0} must keep its shape", i);
        }

        AssertNoDanglingDrawingRelationships(saved);
    }

    [Fact]
    public void ChartOnlySheetAndObjectOnlySheet_StillGetTheirOwnDrawingParts()
    {
        var workbook = new Workbook("Book");
        var chartSheet = workbook.AddSheet("Charts");
        SeedData(chartSheet);
        AddChart(chartSheet, "Chart 1", hyperlink: null);
        var shapeSheet = workbook.AddSheet("Shapes");
        SeedData(shapeSheet);
        AddShape(shapeSheet, "Rectangle 1", hyperlink: null);

        var reloaded = SaveAndReload(workbook, out var saved);

        reloaded.GetSheetAt(0).Charts.Should().ContainSingle();
        reloaded.GetSheetAt(1).DrawingShapes.Should().ContainSingle();
        AssertNoDanglingDrawingRelationships(saved);
    }

    [Fact]
    public void ChartAndShapeOnSameFreshSheet_ProduceASchemaValidPackage()
    {
        var workbook = NewWorkbook(out var sheet);
        AddChart(sheet, "Chart 1", hyperlink: null);
        AddShape(sheet, "Rectangle 1", hyperlink: null);
        AddPicture(sheet, "Picture 1", hyperlink: null);

        SaveAndReload(workbook, out var saved);

        saved.Position = 0;
        using var document = SpreadsheetDocument.Open(saved, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Path?.XPath}: {error.Description}")
            .ToList();
        errors.Should().BeEmpty();
    }

    // ── Object hyperlinks on all four kinds must survive a forced FULL-REBUILD round trip together. ──

    [Fact]
    public void AllFourDrawingObjectKinds_KeepTheirHyperlinksAcrossAForcedFullRebuild()
    {
        var workbook = new Workbook("Book");
        var data = workbook.AddSheet("Data");
        SeedData(data);
        var dashboard = workbook.AddSheet("Dashboard");
        AddChart(dashboard, "Chart 1", new DrawingObjectHyperlink("https://example.com/chart", "External"), data);
        AddShape(dashboard, "Rectangle 1", new DrawingObjectHyperlink("Data!A1"));
        dashboard.TextBoxes.Add(new TextBoxModel
        {
            Name = "TextBox 1",
            Anchor = new CellAddress(dashboard.Id, 12, 2),
            Text = "hello",
            Hyperlink = new DrawingObjectHyperlink("https://example.com/textbox", "External"),
        });
        AddPicture(dashboard, "Picture 1", new DrawingObjectHyperlink("Data!B2"));

        var adapter = new XlsxFileAdapter();
        using var first = new MemoryStream();
        adapter.Save(workbook, first);
        first.Position = 0;
        var loaded = adapter.Load(first);

        AssertDashboardHyperlinks(loaded, "after the first round trip");

        // Force the FULL (ClosedXML rebuild) save path with an edit that has nothing to do with any
        // drawing object: a structurally-neutral sheet add+remove plus an unrelated cell edit.
        var temp = loaded.AddSheet("__Temp__");
        loaded.RemoveSheet(temp.Id);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 9, 9), new TextValue("unrelated"));

        using var second = new MemoryStream();
        adapter.Save(loaded, second);
        adapter.LastSaveDiagnostics!.Path.Should().Be(
            XlsxSavePath.FullSave, "the repro only means anything if the full rebuild really ran");

        second.Position = 0;
        AssertDashboardHyperlinks(new XlsxFileAdapter().Load(second), "after the forced full rebuild");
    }

    private static void AssertDashboardHyperlinks(Workbook workbook, string because)
    {
        var sheet = workbook.GetSheetAt(1);
        sheet.Charts.Should().ContainSingle(because).Which
            .Hyperlink!.Target.Should().Be("https://example.com/chart", because);
        sheet.DrawingShapes.Should().ContainSingle(because).Which
            .Hyperlink!.Target.Should().Be("Data!A1", because);
        sheet.TextBoxes.Should().ContainSingle(because).Which
            .Hyperlink!.Target.Should().Be("https://example.com/textbox", because);
        sheet.Pictures.Should().ContainSingle(because).Which
            .Hyperlink!.Target.Should().Be("Data!B2", because);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Workbook NewWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("Book");
        sheet = workbook.AddSheet("Sheet1");
        SeedData(sheet);
        return workbook;
    }

    private static void SeedData(Sheet sheet)
    {
        for (uint row = 1; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue("r" + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
        }
    }

    private static void AddChart(Sheet sheet, string name, DrawingObjectHyperlink? hyperlink, Sheet? dataSheet = null)
    {
        var source = dataSheet ?? sheet;
        sheet.Charts.Add(new ChartModel
        {
            Name = name,
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(source.Id, 1, 1), new CellAddress(source.Id, 4, 2)),
            Hyperlink = hyperlink,
        });
    }

    private static void AddShape(Sheet sheet, string name, DrawingObjectHyperlink? hyperlink)
    {
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = name,
            Anchor = new CellAddress(sheet.Id, 8, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 200,
            Height = 100,
            Hyperlink = hyperlink,
        });
    }

    private static void AddPicture(Sheet sheet, string name, DrawingObjectHyperlink? hyperlink)
    {
        sheet.Pictures.Add(new PictureModel
        {
            Name = name,
            Anchor = new CellAddress(sheet.Id, 16, 2),
            Kind = PictureKind.Image,
            ImageBytes = PngBytes,
            ContentType = "image/png",
            Hyperlink = hyperlink,
        });
    }

    private static Workbook SaveAndReload(Workbook workbook, out MemoryStream saved)
    {
        var adapter = new XlsxFileAdapter();
        saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        return adapter.Load(saved);
    }

    /// <summary>The sheet must end up with exactly one drawing part, referenced by the worksheet.</summary>
    private static void AssertSingleDrawingPart(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith("xl/drawings/drawing", StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle("one worksheet can reference only one drawing part");
        AssertNoDanglingDrawingRelationships(saved);
    }

    /// <summary>Every worksheet drawing relationship must be the one the worksheet actually points at.</summary>
    private static void AssertNoDanglingDrawingRelationships(MemoryStream saved)
    {
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries
                     .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                                 e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var relsEntry = archive.GetEntry(
                $"xl/worksheets/_rels/{entry.FullName["xl/worksheets/".Length..]}.rels");
            if (relsEntry is null)
                continue;

            List<string> drawingRelIds;
            using (var relsStream = relsEntry.Open())
            {
                drawingRelIds = XDocument.Load(relsStream).Root!
                    .Elements(PackageRelNs + "Relationship")
                    .Where(r => r.Attribute("Type")?.Value == DrawingRelationshipType)
                    .Select(r => r.Attribute("Id")!.Value)
                    .ToList();
            }

            string? referencedRelId;
            using (var worksheetStream = entry.Open())
            {
                referencedRelId = XDocument.Load(worksheetStream).Root!
                    .Element(WorksheetNs + "drawing")?
                    .Attribute(RelNs + "id")?.Value;
            }

            drawingRelIds.Should().BeSubsetOf(
                referencedRelId is null ? [] : new[] { referencedRelId },
                "{0} must not keep a drawing relationship to a part the worksheet no longer references",
                entry.FullName);
        }
    }
}
