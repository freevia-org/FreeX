using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for review-group F-fxl (native .fxl round-trip data loss): named ranges
/// and named formulas (workbook- and sheet-scoped), sheet form controls, workbook slicers and
/// timelines, legacy comment authors / pinned-visible state, and rich-text run theme/indexed/auto
/// color kinds. Each finding previously silently dropped its state on a Save/Load round trip
/// through <see cref="NativeJsonAdapter"/>.
/// </summary>
public sealed class NativeJsonAdapterFxlGroupFRoundTripTests
{
    // ── H8: sheet-scoped named ranges + workbook/sheet-scoped named formulas ───────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_WorkbookScopedNamedFormula()
    {
        var workbook = new Workbook("NamedFormulaWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(CellAddress.Parse("B1", sheet.Id), Cell.FromValue(new NumberValue(100)));
        workbook.NamedFormulas["MyRate"] = "0.08*Sheet1!$B$1";

        var loaded = RoundTrip(workbook);

        loaded.NamedFormulas.Should().ContainKey("MyRate")
            .WhoseValue.Should().Be("0.08*Sheet1!$B$1");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_SheetScopedNamedFormula()
    {
        var workbook = new Workbook("ScopedNamedFormulaWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedFormula("LocalRate", "0.05+1", sheet.Id);

        var loaded = RoundTrip(workbook);
        var loadedSheet = loaded.GetSheet("Sheet1")!;

        loaded.ScopedNamedFormulas.Should().ContainKey(("LocalRate", loadedSheet.Id))
            .WhoseValue.Should().Be("0.05+1");
        loaded.TryGetNamedFormulaText("LocalRate", loadedSheet.Id).Should().Be("0.05+1");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_SheetScopedNamedRange()
    {
        var workbook = new Workbook("ScopedNamedRangeWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            CellAddress.Parse("A1", sheet.Id),
            CellAddress.Parse("A5", sheet.Id));
        workbook.DefineNamedRange("LocalRange", range, new NamedRangeMetadata("Sheet1", "a local range"), sheet.Id);

        var loaded = RoundTrip(workbook);
        var loadedSheet = loaded.GetSheet("Sheet1")!;

        loaded.ScopedNamedRanges.Should().ContainKey(("LocalRange", loadedSheet.Id));
        loaded.TryGetNamedRange("LocalRange", loadedSheet.Id, out var loadedRange).Should().BeTrue();
        loadedRange.ToString().Should().Be("A1:A5");

        // Workbook-global lookup (no sheet context) must NOT see the sheet-scoped name.
        loaded.TryGetNamedRange("LocalRange", out _).Should().BeFalse();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_WorkbookScopedNamedRange_StillWorks()
    {
        var workbook = new Workbook("PlainNamedRangeWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            CellAddress.Parse("A1", sheet.Id),
            CellAddress.Parse("B2", sheet.Id));
        workbook.DefineNamedRange("GlobalRange", range);

        var loaded = RoundTrip(workbook);

        loaded.TryGetNamedRange("GlobalRange", out var loadedRange).Should().BeTrue();
        loadedRange.Start.Row.Should().Be(1);
        loadedRange.End.Row.Should().Be(2);
    }

    // ── H36: Sheet.FormControls ──────────────────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_FormControl_CheckBox()
    {
        var workbook = new Workbook("FormControlWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        var control = new FormControlModel
        {
            Kind = FormControlKind.CheckBox,
            Name = "Check Box 1",
            Caption = "Enable feature",
            ShapeId = 5,
            Anchor = new GridRange(CellAddress.Parse("B2", sheet.Id), CellAddress.Parse("C3", sheet.Id)),
            AnchorOffsets = new DrawingAnchorRange(
                new DrawingAnchorPoint(1, 1000, 1, 2000),
                new DrawingAnchorPoint(2, 3000, 2, 4000)),
            LinkedCell = "B2",
            IsChecked = true
        };
        sheet.FormControls.Add(control);

        var loaded = RoundTrip(workbook);
        var loadedSheet = loaded.GetSheet("Sheet1")!;

        var loadedControl = loadedSheet.FormControls.Should().ContainSingle().Subject;
        loadedControl.Kind.Should().Be(FormControlKind.CheckBox);
        loadedControl.Caption.Should().Be("Enable feature");
        loadedControl.ShapeId.Should().Be(5u);
        loadedControl.LinkedCell.Should().Be("B2");
        loadedControl.IsChecked.Should().BeTrue();
        loadedControl.Anchor.Should().NotBeNull();
        loadedControl.AnchorOffsets.Should().NotBeNull();
        loadedControl.AnchorOffsets!.From.ColumnOffsetEmu.Should().Be(1000);
        loadedControl.AnchorOffsets!.To.RowOffsetEmu.Should().Be(4000);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_FormControl_Spinner()
    {
        var workbook = new Workbook("SpinnerWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.FormControls.Add(new FormControlModel
        {
            Kind = FormControlKind.Spinner,
            LinkedCell = "D4",
            Value = 5,
            Min = 0,
            Max = 10,
            Increment = 1
        });

        var loaded = RoundTrip(workbook);
        var loadedSheet = loaded.GetSheet("Sheet1")!;

        var loadedControl = loadedSheet.FormControls.Should().ContainSingle().Subject;
        loadedControl.Kind.Should().Be(FormControlKind.Spinner);
        loadedControl.Value.Should().Be(5);
        loadedControl.Min.Should().Be(0);
        loadedControl.Max.Should().Be(10);
        loadedControl.Increment.Should().Be(1);
    }

    // ── H37: Workbook.Slicers ────────────────────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_Slicer_WithSelectedItems()
    {
        var workbook = new Workbook("SlicerWorkbook");
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            Caption = "Region",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(0, 0, 0, 0),
                new DrawingAnchorPoint(3, 0, 8, 0)),
            ColumnCount = 2,
            ShowCaption = false,
            SourceSheetName = "Sheet1"
        };
        slicer.SelectedItems.Add("East");
        slicer.SelectedItems.Add("West");
        workbook.Slicers.Add(slicer);

        var loaded = RoundTrip(workbook);

        var loadedSlicer = loaded.Slicers.Should().ContainSingle().Subject;
        loadedSlicer.Name.Should().Be("Region Slicer");
        loadedSlicer.SelectedItems.Should().Equal("East", "West");
        loadedSlicer.StyleName.Should().Be("SlicerStyleLight2");
        loadedSlicer.ColumnCount.Should().Be(2);
        loadedSlicer.ShowCaption.Should().BeFalse();
        loadedSlicer.SourceSheetName.Should().Be("Sheet1");
        loadedSlicer.DrawingAnchor.Should().NotBeNull();
        loadedSlicer.DrawingAnchor!.To.Column.Should().Be(3u);
    }

    // ── H38: Workbook.Timelines ──────────────────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_Timeline_WithSelectedDateRange()
    {
        var workbook = new Workbook("TimelineWorkbook");
        var timeline = new TimelineModel
        {
            Name = "Date Timeline",
            Caption = "Order Date",
            CacheName = "Timeline_OrderDate",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "OrderDate",
            StyleName = "TimeSlicerStyleLight1",
            StartDate = "2026-01-01T00:00:00",
            EndDate = "2026-12-31T00:00:00",
            SelectedStartDate = "2026-03-01T00:00:00",
            SelectedEndDate = "2026-06-30T00:00:00",
            Level = 2,
            SourceSheetName = "Sheet1"
        };
        workbook.Timelines.Add(timeline);

        var loaded = RoundTrip(workbook);

        var loadedTimeline = loaded.Timelines.Should().ContainSingle().Subject;
        loadedTimeline.Name.Should().Be("Date Timeline");
        loadedTimeline.SelectedStartDate.Should().Be("2026-03-01T00:00:00");
        loadedTimeline.SelectedEndDate.Should().Be("2026-06-30T00:00:00");
        loadedTimeline.Level.Should().Be(2);
        loadedTimeline.SourceSheetName.Should().Be("Sheet1");
    }

    // ── H56: Sheet.CommentAuthors + Sheet.ShownComments ──────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_CommentAuthorAndShownState()
    {
        var workbook = new Workbook("CommentAuthorWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        var address = CellAddress.Parse("B2", sheet.Id);
        sheet.Comments[address] = "Please review this figure.";
        sheet.CommentAuthors[address] = "Jane Doe";
        sheet.ShownComments.Add(address);

        var loaded = RoundTrip(workbook);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var loadedAddress = CellAddress.Parse("B2", loadedSheet.Id);

        loadedSheet.Comments[loadedAddress].Should().Be("Please review this figure.");
        loadedSheet.CommentAuthors.Should().ContainKey(loadedAddress)
            .WhoseValue.Should().Be("Jane Doe");
        loadedSheet.ShownComments.Should().Contain(loadedAddress);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_Comment_WithoutAuthorOrShownState()
    {
        var workbook = new Workbook("PlainCommentWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        var address = CellAddress.Parse("A1", sheet.Id);
        sheet.Comments[address] = "No author here.";

        var loaded = RoundTrip(workbook);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var loadedAddress = CellAddress.Parse("A1", loadedSheet.Id);

        loadedSheet.Comments[loadedAddress].Should().Be("No author here.");
        loadedSheet.CommentAuthors.Should().NotContainKey(loadedAddress);
        loadedSheet.ShownComments.Should().NotContain(loadedAddress);
    }

    // ── H57: rich-text run color KIND (theme/indexed/auto) ──────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_RichTextRun_ThemeColor()
    {
        var workbook = new Workbook("RichRunThemeWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        var address = CellAddress.Parse("A1", sheet.Id);
        sheet.SetCell(address, Cell.FromValue(new TextValue("Hello world")));
        sheet.RichTextRuns[address] =
        [
            new CellTextRun("Hello ", null, null, null, null, null, null, CellRunColor.FromTheme(4, 0.2)),
            new CellTextRun("world", null, null, null, null, null, null, CellRunColor.FromRgb(new CellColor(0, 0, 0)))
        ];

        var loaded = RoundTrip(workbook);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var loadedAddress = CellAddress.Parse("A1", loadedSheet.Id);

        var runs = loadedSheet.RichTextRuns[loadedAddress];
        runs.Should().HaveCount(2);
        runs[0].FontColor.Should().NotBeNull();
        runs[0].FontColor!.Value.Kind.Should().Be(CellRunColorKind.Theme);
        runs[0].FontColor!.Value.ThemeIndex.Should().Be(4);
        runs[0].FontColor!.Value.Tint.Should().BeApproximately(0.2, 1e-9);
        runs[1].FontColor.Should().NotBeNull();
        runs[1].FontColor!.Value.Kind.Should().Be(CellRunColorKind.Rgb);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_RichTextRun_IndexedAndAutoColor()
    {
        var workbook = new Workbook("RichRunIndexedAutoWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        var address = CellAddress.Parse("A1", sheet.Id);
        sheet.SetCell(address, Cell.FromValue(new TextValue("AB")));
        sheet.RichTextRuns[address] =
        [
            new CellTextRun("A", null, null, null, null, null, null, CellRunColor.FromIndexed(9)),
            new CellTextRun("B", null, null, null, null, null, null, CellRunColor.Auto())
        ];

        var loaded = RoundTrip(workbook);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var loadedAddress = CellAddress.Parse("A1", loadedSheet.Id);

        var runs = loadedSheet.RichTextRuns[loadedAddress];
        runs[0].FontColor!.Value.Kind.Should().Be(CellRunColorKind.Indexed);
        runs[0].FontColor!.Value.IndexedIndex.Should().Be(9);
        runs[1].FontColor!.Value.Kind.Should().Be(CellRunColorKind.Auto);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private static Workbook RoundTrip(Workbook source)
    {
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(source, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }
}
