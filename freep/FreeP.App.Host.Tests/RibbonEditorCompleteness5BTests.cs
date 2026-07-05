using System;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 5B: Tests for the Design tab, Insert galleries (tables + charts), clipboard commands,
/// font-family ComboBox wiring, and Format Painter.
///
/// These tests run plain (no STA required) because they exercise the command layer directly
/// without constructing WPF controls.
/// </summary>
public class RibbonEditorCompleteness5BTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>Creates a fresh EditingSession with one slide containing one autoshape (id=1).</summary>
    private static (EditingSession editor, Presentation pres) MakeSession()
    {
        var pres = Presentation.CreateEmpty();
        var bus  = new PresentationCommandBus(pres);
        var ed   = new EditingSession(pres, bus);
        return (ed, pres);
    }

    private static RibbonCommandRegistry MakeRegistry(EditingSession editor)
        => FreePRibbonCommands.Build(new RibbonStateStore(), editor);

    private static RibbonCommandRegistry MakeRegistry(
        EditingSession editor,
        Func<PresentationPictureBulletPayload?> pickPictureBulletPayload)
        => FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            pickPictureBulletPayload: pickPictureBulletPayload);

    private static void Exec(RibbonCommandRegistry registry, string id,
        RibbonCommandContext? context = null)
    {
        bool found = registry.TryGet(id, out var cmd);
        Assert.True(found, $"Command '{id}' was not registered.");
        cmd!.Execute(context ?? RibbonCommandContext.Empty);
    }

    // ── Ribbon definition: Design tab structure ──────────────────────────────────

    [Fact]
    public void RibbonBuild_ContainsDesignTab()
    {
        var def = FreePRibbon.Build();
        Assert.Contains(def.Tabs, t => t.Id == "design");
    }

    [Fact]
    public void DesignTab_ContainsThemesGroup()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "design");
        Assert.Contains(tab.Groups, g => g.Id == "themes");
    }

    [Fact]
    public void DesignTab_ContainsCustomizeGroup()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "design");
        Assert.Contains(tab.Groups, g => g.Id == "customize");
    }

    [Fact]
    public void ThemesGroup_ContainsAllFiveBuiltInThemeIds()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "design");
        var group = tab.Groups.Single(g => g.Id == "themes");
        var ids = group.Controls.Select(c => c.CommandId.Value).ToHashSet();
        Assert.Contains("freep.theme.office", ids);
        Assert.Contains("freep.theme.berlin", ids);
        Assert.Contains("freep.theme.facet",  ids);
        Assert.Contains("freep.theme.ion",    ids);
        Assert.Contains("freep.theme.slice",  ids);
    }

    [Fact]
    public void CustomizeGroup_ContainsSlideSizeIds()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "design");
        var group = tab.Groups.Single(g => g.Id == "customize");
        var ids = group.Controls.Select(c => c.CommandId.Value).ToHashSet();
        Assert.Contains("freep.slide-size-16x9", ids);
        Assert.Contains("freep.slide-size-4x3",  ids);
    }

    // ── Ribbon definition: Insert tab additions ──────────────────────────────────

    [Fact]
    public void InsertTab_ContainsTablesGroup()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "insert");
        Assert.Contains(tab.Groups, g => g.Id == "tables");
    }

    [Fact]
    public void InsertTab_ContainsChartsGroup()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "insert");
        Assert.Contains(tab.Groups, g => g.Id == "charts");
    }

    [Fact]
    public void TablesGroup_ContainsExpectedTableIds()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "insert");
        var group = tab.Groups.Single(g => g.Id == "tables");
        var ids = group.Controls.Select(c => c.CommandId.Value).ToHashSet();
        Assert.Contains("freep.insert-table-3x3", ids);
        Assert.Contains("freep.insert-table-2x2", ids);
        Assert.Contains("freep.insert-table-4x4", ids);
    }

    [Fact]
    public void ChartsGroup_ContainsExpectedChartIds()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "insert");
        var group = tab.Groups.Single(g => g.Id == "charts");
        var ids = group.Controls.Select(c => c.CommandId.Value).ToHashSet();
        Assert.Contains("freep.insert-chart-column", ids);
        Assert.Contains("freep.insert-chart-bar",    ids);
        Assert.Contains("freep.insert-chart-line",   ids);
        Assert.Contains("freep.insert-chart-pie",    ids);
    }

    [Fact]
    public void TextGroup_ContainsHeaderFooterCommandIds()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "insert");
        var group = tab.Groups.Single(g => g.Id == "text");
        var ids = group.Controls.Select(c => c.CommandId.Value).ToHashSet();
        Assert.Contains(HeaderFooterCommandPlanner.HeaderFooterCommandId, ids);
        Assert.Contains(HeaderFooterCommandPlanner.DateTimeCommandId, ids);
        Assert.Contains(HeaderFooterCommandPlanner.SlideNumberCommandId, ids);
    }

    // ── Ribbon definition: Home tab clipboard additions ──────────────────────────

    [Fact]
    public void HomeTab_ClipboardGroup_ContainsFormatPainterId()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "home");
        var group = tab.Groups.Single(g => g.Id == "clipboard");
        var ids = group.Controls.Select(c => c.CommandId.Value).ToHashSet();
        Assert.Contains("freep.format-painter", ids);
    }

    // ── Command: clipboard (copy → CanPaste → paste) ─────────────────────────────

    [Fact]
    public void Cmd_Copy_ThenPaste_AddsShapeToSlide()
    {
        var (ed, pres) = MakeSession();
        var shapeId = pres.Slides[0].Shapes[0].Id;
        ed.Select(shapeId);
        var reg = MakeRegistry(ed);

        Assert.False(ed.CanPaste, "CanPaste should be false before copy.");
        Exec(reg, "freep.copy");
        Assert.True(ed.CanPaste, "CanPaste should be true after copy.");

        int countBefore = pres.Slides[0].Shapes.Count;
        Exec(reg, "freep.paste");
        Assert.Equal(countBefore + 1, pres.Slides[0].Shapes.Count);
    }

    [Fact]
    public void Cmd_Cut_RemovesShapeFromSlide()
    {
        var (ed, pres) = MakeSession();
        var shapeId = pres.Slides[0].Shapes[0].Id;
        ed.Select(shapeId);
        var reg = MakeRegistry(ed);

        int countBefore = pres.Slides[0].Shapes.Count;
        Exec(reg, "freep.cut");
        Assert.Equal(countBefore - 1, pres.Slides[0].Shapes.Count);
        Assert.True(ed.CanPaste, "CanPaste should be true after cut.");
    }

    [Fact]
    public void Cmd_Paste_WithNoClipboard_IsNoOp()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        int countBefore = pres.Slides[0].Shapes.Count;
        // No prior copy — paste should be silent no-op.
        Exec(reg, "freep.paste");
        Assert.Equal(countBefore, pres.Slides[0].Shapes.Count);
    }

    // ── Command: theme buttons ────────────────────────────────────────────────────

    [Fact]
    public void Cmd_ThemeOffice_SetsOfficeTheme()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.theme.office");
        // Verify it is the Office theme by checking the major font (Calibri Light).
        Assert.Equal("Calibri Light", pres.Theme.FontScheme.MajorLatinFont);
    }

    [Fact]
    public void Cmd_ThemeBerlin_SetsThemeNameBerlin()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.theme.berlin");
        Assert.Equal("Berlin", pres.Theme.Name);
    }

    [Fact]
    public void Cmd_ThemeFacet_SetsThemeNameFacet()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.theme.facet");
        Assert.Equal("Facet", pres.Theme.Name);
    }

    [Fact]
    public void Cmd_ThemeIon_SetsThemeNameIon()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.theme.ion");
        Assert.Equal("Ion", pres.Theme.Name);
    }

    [Fact]
    public void Cmd_ThemeSlice_SetsThemeNameSlice()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.theme.slice");
        Assert.Equal("Slice", pres.Theme.Name);
    }

    // ── Command: slide size ───────────────────────────────────────────────────────

    [Fact]
    public void Cmd_SlideSize16x9_SetsCxEmuTo12192000()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.slide-size-16x9");
        Assert.Equal(12192000L, pres.SlideSizeCxEmu);
    }

    [Fact]
    public void Cmd_SlideSize4x3_SetsCxEmuTo9144000()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.slide-size-4x3");
        Assert.Equal(9144000L, pres.SlideSizeCxEmu);
    }

    [Fact]
    public void Cmd_CustomSlideSize_InvokesSharedPlannerCallback()
    {
        var (ed, _) = MakeSession();
        var invoked = false;
        var reg = FreePRibbonCommands.Build(new RibbonStateStore(), ed, onCustomSlideSize: () => invoked = true);

        Exec(reg, "freep.slide-size-custom");

        Assert.True(invoked);
    }

    [Fact]
    public void Cmd_Layout_InvokesSharedPlannerCallback()
    {
        var (ed, _) = MakeSession();
        var invoked = false;
        var reg = FreePRibbonCommands.Build(new RibbonStateStore(), ed, onLayoutPicker: () => invoked = true);

        Exec(reg, PresentationDesignCommandPlanner.LayoutCommandId);

        Assert.True(invoked);
    }

    [Fact]
    public void Cmd_InsertTable3x3_InvokesPickerCallbackWhenHostProvidesWorkflow()
    {
        var (ed, _) = MakeSession();
        var invoked = false;
        var reg = FreePRibbonCommands.Build(new RibbonStateStore(), ed, onTablePicker: () => invoked = true);

        Exec(reg, SlideObjectInsertionPlanner.Table3x3CommandId);

        Assert.True(invoked);
    }

    [Fact]
    public void Cmd_HeaderFooter_InvokesHostCallbackWhenProvided()
    {
        var (ed, _) = MakeSession();
        HeaderFooterCommandFocus? focus = null;
        var reg = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            ed,
            onHeaderFooter: value => focus = value);

        Exec(reg, HeaderFooterCommandPlanner.DateTimeCommandId);

        Assert.Equal(HeaderFooterCommandFocus.DateTime, focus);
    }

    [Fact]
    public void Cmd_ViewShow_TogglesSharedViewStateThroughHostCallback()
    {
        var (ed, _) = MakeSession();
        var stateStore = new RibbonStateStore();
        var state = PresentationViewShowState.Default;
        var reg = FreePRibbonCommands.Build(
            stateStore,
            ed,
            getViewShowState: () => state,
            applyViewShowState: next => state = next);

        Exec(reg, PresentationViewShowPlanner.GridlinesCommandId);

        Assert.False(state.ShowGridlines);
        Assert.True(state.ShowGuides);
        Assert.False(stateStore.GetState(PresentationViewShowPlanner.GridlinesCommandId).IsChecked);

        Exec(reg, PresentationViewShowPlanner.GuidesCommandId);

        Assert.False(state.ShowGridlines);
        Assert.False(state.ShowGuides);
        Assert.False(stateStore.GetState(PresentationViewShowPlanner.GuidesCommandId).IsChecked);
    }

    [Fact]
    public void Cmd_ViewZoom_AppliesSharedZoomStateThroughHostCallback()
    {
        var (ed, _) = MakeSession();
        var state = PresentationViewZoomState.FitToWindow;
        var reg = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            ed,
            getViewZoomState: () => state,
            applyViewZoomState: next => state = next);

        Exec(
            reg,
            PresentationViewZoomPlanner.ZoomCommandId,
            RibbonCommandContext.ForSelectedValue("150%"));

        Assert.Equal(PresentationViewZoomMode.Percent, state.Mode);
        Assert.Equal(150, state.ZoomPercent);

        Exec(reg, PresentationViewZoomPlanner.FitToWindowCommandId);

        Assert.Equal(PresentationViewZoomMode.FitToWindow, state.Mode);
        Assert.Equal(150, state.ZoomPercent);
    }

    [Fact]
    public void Cmd_SlideNumber_WithoutHostCallback_AppliesSharedPlannerDefault()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);

        Exec(reg, HeaderFooterCommandPlanner.SlideNumberCommandId);

        Assert.True(pres.Slides[0].HfVisibility!.ShowSlideNum);
    }

    // ── Command: insert table ─────────────────────────────────────────────────────

    [Fact]
    public void Cmd_InsertTable3x3_AddsTableShapeToSlide()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        int before = pres.Slides[0].Shapes.Count;
        Exec(reg, "freep.insert-table-3x3");
        Assert.Equal(before + 1, pres.Slides[0].Shapes.Count);
        var added = pres.Slides[0].Shapes.Last();
        Assert.Equal(SlideShapeKind.Table, added.Kind);
        Assert.NotNull(added.Table);
        Assert.Equal(3, added.Table!.Rows.Count);
        Assert.Equal(3, added.Table.ColumnWidthsEmu.Count);
    }

    [Fact]
    public void Cmd_InsertTable2x2_AddsTableWith2Rows2Cols()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.insert-table-2x2");
        var added = pres.Slides[0].Shapes.Last();
        Assert.Equal(SlideShapeKind.Table, added.Kind);
        Assert.Equal(2, added.Table!.Rows.Count);
        Assert.Equal(2, added.Table.ColumnWidthsEmu.Count);
    }

    [Fact]
    public void Cmd_InsertTable4x4_AddsTableWith4Rows4Cols()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.insert-table-4x4");
        var added = pres.Slides[0].Shapes.Last();
        Assert.Equal(SlideShapeKind.Table, added.Kind);
        Assert.Equal(4, added.Table!.Rows.Count);
        Assert.Equal(4, added.Table.ColumnWidthsEmu.Count);
    }

    // ── Command: insert chart ─────────────────────────────────────────────────────

    [Fact]
    public void Cmd_InsertChartColumn_AddsChartShapeColumnClustered()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        int before = pres.Slides[0].Shapes.Count;
        Exec(reg, "freep.insert-chart-column");
        Assert.Equal(before + 1, pres.Slides[0].Shapes.Count);
        var added = pres.Slides[0].Shapes.Last();
        Assert.Equal(SlideShapeKind.Chart, added.Kind);
        Assert.Equal(ChartType.ColumnClustered, added.Chart!.ChartType);
    }

    [Fact]
    public void Cmd_InsertChartBar_AddsBarClusteredChart()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.insert-chart-bar");
        var added = pres.Slides[0].Shapes.Last();
        Assert.Equal(SlideShapeKind.Chart, added.Kind);
        Assert.Equal(ChartType.BarClustered, added.Chart!.ChartType);
    }

    [Fact]
    public void Cmd_InsertChartLine_AddsLineChart()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.insert-chart-line");
        var added = pres.Slides[0].Shapes.Last();
        Assert.Equal(ChartType.Line, added.Chart!.ChartType);
    }

    [Fact]
    public void Cmd_InsertChartPie_AddsPieChart()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.insert-chart-pie");
        var added = pres.Slides[0].Shapes.Last();
        Assert.Equal(ChartType.Pie, added.Chart!.ChartType);
    }

    // ── Command: font-family ComboBox ────────────────────────────────────────────

    [Fact]
    public void Cmd_FontFamily_WithSelectedValue_SetsRunFont()
    {
        var (ed, pres) = MakeSession();
        // The default text-box has text runs; insert one to be sure.
        ed.InsertDefaultTextBox();
        var shape = pres.Slides[0].Shapes.Last();
        ed.Select(shape.Id);
        var reg = MakeRegistry(ed);

        var ctx = RibbonCommandContext.ForSelectedValue("Arial");
        Exec(reg, "freep.font-family", ctx);

        // All runs in the shape should now be Arial.
        var firstRun = shape.TextBody!.Paragraphs[0].Runs[0];
        Assert.Equal("Arial", firstRun.FontFamily);
    }

    [Fact]
    public void Cmd_FontFamily_WithEmptyValue_IsNoOp()
    {
        var (ed, pres) = MakeSession();
        ed.InsertDefaultTextBox();
        var shape = pres.Slides[0].Shapes.Last();
        ed.Select(shape.Id);
        var reg = MakeRegistry(ed);

        // Execute with no SelectedValue — must not throw.
        var ex = Record.Exception(() => Exec(reg, "freep.font-family", RibbonCommandContext.Empty));
        Assert.Null(ex);
    }

    [Fact]
    public void Cmd_FontSizeAndColor_WithSelectedTextShape_RoutesToEditor()
    {
        var (ed, pres) = MakeSession();
        ed.InsertDefaultTextBox();
        var shape = pres.Slides[0].Shapes.Last();
        ed.Select(shape.Id);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.font-size", RibbonCommandContext.ForSelectedValue("26pt"));
        Exec(reg, "freep.font-color", RibbonCommandContext.ForSelectedValue("#336699"));

        var run = shape.TextBody!.Paragraphs[0].Runs[0];
        Assert.Equal(26, run.FontSizePt);
        Assert.NotNull(run.Color);
        Assert.Equal(SrgbColor.FromRgb(0x336699), run.Color!.Resolved);
    }

    [Fact]
    public void Cmd_FontSizeAndColor_WithActiveTableCell_RoutesToSharedTableCellPlan()
    {
        var (ed, pres) = MakeSession();
        var body = new TextBody { Wrap = true };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "Cell", FontSizePt = 10 });
        paragraph.Runs.Add(new Run { Text = " text", FontSizePt = 14, Bold = true });
        body.Paragraphs.Add(paragraph);
        var shape = AddSingleCellTable(pres, 800, body);
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.font-size", RibbonCommandContext.ForSelectedValue("22"));
        Exec(reg, "freep.font-color", RibbonCommandContext.ForSelectedValue("#8844CC"));

        var runs = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs;
        Assert.All(runs, run => Assert.Equal(22, run.FontSizePt));
        Assert.All(runs, run => Assert.Equal(SrgbColor.FromRgb(0x8844CC), run.Color!.Resolved));
        Assert.True(runs[1].Bold);
    }

    [Theory]
    [InlineData("freep.bold", TableCellTextFormatKind.Bold)]
    [InlineData("freep.italic", TableCellTextFormatKind.Italic)]
    [InlineData("freep.underline", TableCellTextFormatKind.Underline)]
    public void Cmd_FontToggle_WithActiveTableCell_UsesSharedTableCellPlan(
        string commandId,
        TableCellTextFormatKind kind)
    {
        var (ed, pres) = MakeSession();
        var body = new TextBody { Wrap = true };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = "Cell" });
        body.Paragraphs.Add(paragraph);
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerInch);
        var row = new TableRow { HeightEmu = DrawingMlCoordinateUnits.EmuPerInch / 2 };
        row.Cells.Add(new TableCell { TextBody = body });
        table.Rows.Add(row);
        var shape = new SlideShape
        {
            Id = 400,
            Kind = SlideShapeKind.Table,
            Table = table,
            ExtentCxEmu = DrawingMlCoordinateUnits.EmuPerInch,
            ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerInch / 2,
        };
        pres.Slides[0].Shapes.Add(shape);
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);

        Exec(MakeRegistry(ed), commandId);

        var run = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0];
        Assert.True(kind switch
        {
            TableCellTextFormatKind.Bold => run.Bold,
            TableCellTextFormatKind.Italic => run.Italic,
            TableCellTextFormatKind.Underline => run.Underline,
            _ => false,
        });
    }

    // ── Command: Format Painter ───────────────────────────────────────────────────

    [Theory]
    [InlineData("freep.paragraph.align-left", TextAlign.Left)]
    [InlineData("freep.paragraph.align-center", TextAlign.Center)]
    [InlineData("freep.paragraph.align-right", TextAlign.Right)]
    [InlineData("freep.paragraph.align-justify", TextAlign.Justify)]
    public void Cmd_ParagraphAlign_WithActiveTableCell_UsesSharedTableCellPlan(
        string commandId,
        TextAlign alignment)
    {
        var (ed, pres) = MakeSession();
        var body = new TextBody { Wrap = true };
        var paragraph = new Paragraph { Align = TextAlign.Left };
        paragraph.Runs.Add(new Run { Text = "Cell" });
        body.Paragraphs.Add(paragraph);
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerInch);
        var row = new TableRow { HeightEmu = DrawingMlCoordinateUnits.EmuPerInch / 2 };
        row.Cells.Add(new TableCell { TextBody = body });
        table.Rows.Add(row);
        var shape = new SlideShape
        {
            Id = 401,
            Kind = SlideShapeKind.Table,
            Table = table,
            ExtentCxEmu = DrawingMlCoordinateUnits.EmuPerInch,
            ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerInch / 2,
        };
        pres.Slides[0].Shapes.Add(shape);
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);

        Exec(MakeRegistry(ed), commandId);

        shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0].Align.Should().Be(alignment);
    }

    [Fact]
    public void Cmd_Bullets_WithActiveTableCell_UsesSharedTableCellPlan()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 402, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);

        Exec(MakeRegistry(ed), "freep.bullets");

        var paragraph = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Char);
        paragraph.BulletChar.Should().Be("\u2022");
        paragraph.BulletSuppressed.Should().BeFalse();
    }

    [Fact]
    public void Cmd_Numbering_WithActiveTableCell_UsesSharedTableCellPlan()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 404, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);

        Exec(MakeRegistry(ed), "freep.numbering");

        var paragraph = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Auto);
        paragraph.AutoNumType.Should().Be(AutoNumType.ArabicPeriod);
        paragraph.AutoNumStartAt.Should().Be(1);
        paragraph.BulletChar.Should().BeNull();
        paragraph.BulletSuppressed.Should().BeFalse();
    }

    [Fact]
    public void Cmd_Numbering_WithPresetContext_AppliesSharedTableCellListPreset()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 405, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);

        Exec(
            MakeRegistry(ed),
            "freep.numbering",
            RibbonCommandContext.ForSelectedValue(TableCellListPresetCatalog.NumberRomanUpperPeriodId));

        var paragraph = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Auto);
        paragraph.AutoNumType.Should().Be(AutoNumType.RomanUcPeriod);
        paragraph.AutoNumStartAt.Should().Be(1);
        paragraph.BulletChar.Should().BeNull();
        paragraph.BulletSuppressed.Should().BeFalse();
    }

    [Fact]
    public void Cmd_Bullets_VisibleGalleryPresetCommand_AppliesSharedTableCellPreset()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 406, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);
        var commandId = PresentationListGalleryPlanner.BuildBulletGalleryPlan()
            .Items.Single(item => item.ListPreset?.Id == TableCellListPresetCatalog.BulletSquareId)
            .CommandId;

        Exec(MakeRegistry(ed), commandId);

        var paragraph = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Char);
        paragraph.BulletChar.Should().Be("\u25AA");
        paragraph.BulletSuppressed.Should().BeFalse();
    }

    [Fact]
    public void Cmd_PictureBullet_WithInjectedPicker_AppliesSharedTableCellImageBullet()
    {
        var (ed, pres) = MakeSession();
        var body = MakeTextBody("Cell");
        var table = AddSingleCellTable(pres, 2, body);
        ed.Select(table.Id);
        ed.SetActiveTableCell(0, 0);
        var reg = MakeRegistry(ed, () =>
            PresentationPictureBulletAuthoringPlanner.CreatePayload(
                [0x89, 0x50, 0x4E, 0x47],
                "image/png",
                "bullet.png"));

        Exec(reg, PresentationListGalleryPlanner.ImageBulletCommandId);

        var paragraph = table.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Image);
        paragraph.BulletImage.Should().NotBeNull();
        paragraph.BulletImage!.ContentType.Should().Be("image/png");
        paragraph.BulletImage.Bytes.Should().Equal(0x89, 0x50, 0x4E, 0x47);
        paragraph.BulletChar.Should().BeNull();
        paragraph.BulletSuppressed.Should().BeFalse();
    }

    [Fact]
    public void Cmd_IndentIncreaseDecrease_WithActiveTableCell_UsesSharedTableCellPlan()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 403, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);

        Exec(MakeRegistry(ed), "freep.indent-increase");

        var paragraph = shape.Table!.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.Level.Should().Be(1);
        paragraph.MarginLeftEmu.Should().Be(457200);

        Exec(MakeRegistry(ed), "freep.indent-decrease");

        paragraph = shape.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.Level.Should().Be(0);
        paragraph.MarginLeftEmu.Should().BeNull();
    }

    [Fact]
    public void Cmd_FormatPainter_CopiesFillFromFirstSelectedToOthers()
    {
        var (ed, pres) = MakeSession();

        // Insert two rectangles.
        var r1 = ed.InsertDefaultRectangle();
        var r2 = ed.InsertDefaultRectangle();

        // Give r1 a distinct solid fill.
        var redFill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0xFF0000)));
        ed.Select(r1.Id);
        ed.SetSelectedFill(redFill);

        // Select both (r1 first as the source, r2 as the target).
        ed.Select(r1.Id);
        ed.Select(r2.Id, addToSelection: true);

        var reg = MakeRegistry(ed);
        Exec(reg, "freep.format-painter");

        // r2 should now have the same fill as r1.
        var r2Shape = pres.Slides[0].Shapes.First(s => s.Id == r2.Id);
        Assert.IsType<ShapeFill.Solid>(r2Shape.Fill);
    }

    [Fact]
    public void Cmd_FormatPainter_WithNoSelection_IsNoOp()
    {
        var (ed, pres) = MakeSession();
        ed.ClearSelection();
        var reg = MakeRegistry(ed);

        var ex = Record.Exception(() => Exec(reg, "freep.format-painter"));
        Assert.Null(ex);
    }

    // ── All Wave 5B command ids are registered ────────────────────────────────────

    [Theory]
    [InlineData("freep.copy")]
    [InlineData("freep.cut")]
    [InlineData("freep.paste")]
    [InlineData("freep.font-family")]
    [InlineData("freep.font-size")]
    [InlineData("freep.font-color")]
    [InlineData("freep.format-painter")]
    [InlineData("freep.theme.office")]
    [InlineData("freep.theme.berlin")]
    [InlineData("freep.theme.facet")]
    [InlineData("freep.theme.ion")]
    [InlineData("freep.theme.slice")]
    [InlineData("freep.slide-size-16x9")]
    [InlineData("freep.slide-size-4x3")]
    [InlineData("freep.slide-size-custom")]
    [InlineData("freep.layout")]
    [InlineData("freep.insert-table-3x3")]
    [InlineData("freep.insert-table-2x2")]
    [InlineData("freep.insert-table-4x4")]
    [InlineData("freep.insert-chart-column")]
    [InlineData("freep.insert-chart-bar")]
    [InlineData("freep.insert-chart-line")]
    [InlineData("freep.insert-chart-pie")]
    [InlineData("freep.header-footer")]
    [InlineData("freep.date-time")]
    [InlineData("freep.slide-number")]
    [InlineData("freep.paragraph.align-left")]
    [InlineData("freep.paragraph.align-center")]
    [InlineData("freep.paragraph.align-right")]
    [InlineData("freep.paragraph.align-justify")]
    [InlineData("freep.bullets")]
    [InlineData("freep.numbering")]
    [InlineData("freep.indent-increase")]
    [InlineData("freep.indent-decrease")]
    [InlineData("freep.bullets.bullet.square")]
    [InlineData("freep.numbering.number.roman-upper-period")]
    [InlineData("freep.bullets.picture")]
    [InlineData("freep.increase-indent")]
    [InlineData("freep.decrease-indent")]
    [InlineData("freep.view.zoom")]
    [InlineData("freep.view.fit-to-window")]
    public void AllWave5BCommandIds_AreRegistered(string commandId)
    {
        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);
        bool found = reg.TryGet(commandId, out _);
        Assert.True(found, $"Command '{commandId}' was not registered.");
    }

    private static TextBody MakeTextBody(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static SlideShape AddSingleCellTable(Presentation presentation, uint id, TextBody body)
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerInch);
        var row = new TableRow { HeightEmu = DrawingMlCoordinateUnits.EmuPerInch / 2 };
        row.Cells.Add(new TableCell { TextBody = body });
        table.Rows.Add(row);
        var shape = new SlideShape
        {
            Id = id,
            Kind = SlideShapeKind.Table,
            Table = table,
            ExtentCxEmu = DrawingMlCoordinateUnits.EmuPerInch,
            ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerInch / 2,
        };
        presentation.Slides[0].Shapes.Add(shape);
        return shape;
    }
}
