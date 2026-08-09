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

    private static RibbonCommandRegistry MakeSmartArtRegistry(
        EditingSession editor,
        Action<SmartArtLayoutPreset> onSmartArtLayoutPreset)
        => FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            onSmartArtLayoutPreset: onSmartArtLayoutPreset);

    private static RibbonCommandRegistry MakeSmartArtQuickStyleRegistry(
        EditingSession editor,
        Action<SmartArtQuickStylePreset> onSmartArtQuickStylePreset)
        => FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            onSmartArtQuickStylePreset: onSmartArtQuickStylePreset);

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
    public void AnimationEmphasisCommands_AreDefinedAndRouted()
    {
        var definition = FreePRibbon.Build();
        var effects = definition.Tabs
            .Single(tab => tab.Id == "animations")
            .Groups.Single(group => group.Id == "animation-effects");
        var expected = new[]
        {
            "freep.anim.emphasis.teeter",
            "freep.anim.emphasis.blink",
            "freep.anim.emphasis.color-pulse",
            "freep.anim.emphasis.change-color",
            "freep.anim.emphasis.grow-with-color",
            "freep.anim.emphasis.wave",
            "freep.anim.emphasis.shimmer",
            "freep.anim.emphasis.bold",
            "freep.anim.emphasis.underline",
        };

        foreach (var commandId in expected)
        {
            Assert.Contains(effects.Controls, control => control.CommandId.Value == commandId);
            Assert.Contains(
                PresentationAnimationCommandPlanner.BuiltInPlans,
                plan => plan.CommandId == commandId
                    && plan.Intent == PresentationAnimationCommandIntentKind.AddEffect);
        }
    }

    [Fact]
    public void SmartArtContinuousBlockProcess_IsDefinedAndRoutedByHost()
    {
        var definition = FreePRibbon.Build();
        var layouts = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .Single(group => group.Id == "smartart-layouts");
        Assert.Contains(layouts.Controls,
            control => control.CommandId.Value == SmartArtAuthoringPlanner.ContinuousBlockProcessLayoutCommandId);

        var (editor, _) = MakeSession();
        SmartArtLayoutPreset? applied = null;
        Exec(
            MakeSmartArtRegistry(editor, preset => applied = preset),
            SmartArtAuthoringPlanner.ContinuousBlockProcessLayoutCommandId);

        Assert.Equal(SmartArtLayoutPreset.ContinuousBlockProcess, applied);
    }

    [Fact]
    public void SmartArtQuickStyleGallery_IsDefinedAndAllEntriesRouteThroughHost()
    {
        var definition = FreePRibbon.Build();
        var styles = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .Single(group => group.Id == "smartart-styles");
        var expected = new Dictionary<string, SmartArtQuickStylePreset>
        {
            [SmartArtAuthoringPlanner.SimpleQuickStyleCommandId] = SmartArtQuickStylePreset.SimpleFill,
            [SmartArtAuthoringPlanner.SoftEdgeQuickStyleCommandId] = SmartArtQuickStylePreset.WhiteOutline,
            [SmartArtAuthoringPlanner.SubtleQuickStyleCommandId] = SmartArtQuickStylePreset.SubtleEffect,
            [SmartArtAuthoringPlanner.ModerateQuickStyleCommandId] = SmartArtQuickStylePreset.ModerateEffect,
            [SmartArtAuthoringPlanner.IntenseQuickStyleCommandId] = SmartArtQuickStylePreset.IntenseEffect,
            [SmartArtAuthoringPlanner.PolishedQuickStyleCommandId] = SmartArtQuickStylePreset.Polished,
            [SmartArtAuthoringPlanner.InsertQuickStyleCommandId] = SmartArtQuickStylePreset.Inset,
            [SmartArtAuthoringPlanner.CartoonQuickStyleCommandId] = SmartArtQuickStylePreset.Cartoon,
            [SmartArtAuthoringPlanner.PowderQuickStyleCommandId] = SmartArtQuickStylePreset.Powder,
            [SmartArtAuthoringPlanner.BrickSceneQuickStyleCommandId] = SmartArtQuickStylePreset.BrickScene,
            [SmartArtAuthoringPlanner.FlatSceneQuickStyleCommandId] = SmartArtQuickStylePreset.FlatScene,
            [SmartArtAuthoringPlanner.MetallicSceneQuickStyleCommandId] = SmartArtQuickStylePreset.MetallicScene,
            [SmartArtAuthoringPlanner.SunsetSceneQuickStyleCommandId] = SmartArtQuickStylePreset.SunsetScene,
            [SmartArtAuthoringPlanner.BirdsEyeSceneQuickStyleCommandId] = SmartArtQuickStylePreset.BirdsEyeScene,
        };

        foreach (var (commandId, preset) in expected)
        {
            Assert.Contains(styles.Controls, control => control.CommandId.Value == commandId);
            var (editor, _) = MakeSession();
            SmartArtQuickStylePreset? applied = null;
            Exec(MakeSmartArtQuickStyleRegistry(editor, value => applied = value), commandId);
            Assert.Equal(preset, applied);
        }
    }

    [Fact]
    public void PictureCropCommands_AreDefinedAndRouteThroughSharedSession()
    {
        var definition = FreePRibbon.Build();
        var illustrationIds = definition.Tabs
            .Single(tab => tab.Id == "insert")
            .Groups.Single(group => group.Id == "illustrations")
            .Controls.Select(control => control.CommandId.Value)
            .ToArray();
        Assert.Contains(PictureCropAuthoringPlanner.InsetCommandId, illustrationIds);
        Assert.Contains(PictureCropAuthoringPlanner.ResetCommandId, illustrationIds);
        Assert.Contains(PictureColorEffectAuthoringPlanner.GrayscaleCommandId, illustrationIds);
        Assert.Contains(PictureColorEffectAuthoringPlanner.ResetCommandId, illustrationIds);

        var (editor, presentation) = MakeSession();
        presentation.Slides[0].Shapes.Clear();
        var picture = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1, 2, 3] }
        };
        presentation.Slides[0].Shapes.Add(picture);
        editor.SelectSlide(0);
        editor.Select(1);
        var registry = MakeRegistry(editor);

        Exec(registry, PictureCropAuthoringPlanner.InsetCommandId);
        Assert.Equal(0.1, picture.PictureFormat!.CropLeft);
        Assert.Equal(0.1, picture.PictureFormat.CropBottom);

        Exec(registry, PictureCropAuthoringPlanner.ResetCommandId);
        Assert.Null(picture.PictureFormat);

        Exec(registry, PictureColorEffectAuthoringPlanner.GrayscaleCommandId);
        Assert.True(picture.PictureFormat!.Grayscale);

        Exec(registry, PictureColorEffectAuthoringPlanner.ResetCommandId);
        Assert.Null(picture.PictureFormat);
    }

    [Fact]
    public void SmartArtExtendedLayouts_AreDefinedAndRoutedByHost()
    {
        var definition = FreePRibbon.Build();
        var layouts = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .Single(group => group.Id == "smartart-layouts");
        var expected = new Dictionary<string, SmartArtLayoutPreset>
        {
            [SmartArtAuthoringPlanner.AccentProcessLayoutCommandId] = SmartArtLayoutPreset.AccentProcess,
            [SmartArtAuthoringPlanner.AscendingProcessLayoutCommandId] = SmartArtLayoutPreset.AscendingProcess,
            [SmartArtAuthoringPlanner.DescendingProcessLayoutCommandId] = SmartArtLayoutPreset.DescendingProcess,
            [SmartArtAuthoringPlanner.SegmentedProcessLayoutCommandId] = SmartArtLayoutPreset.SegmentedProcess,
            [SmartArtAuthoringPlanner.CircleAccentTimelineLayoutCommandId] = SmartArtLayoutPreset.CircleAccentTimeline,
            [SmartArtAuthoringPlanner.ChevronProcessLayoutCommandId] = SmartArtLayoutPreset.ChevronProcess,
            [SmartArtAuthoringPlanner.BasicChevronProcessLayoutCommandId] = SmartArtLayoutPreset.BasicChevronProcess,
            [SmartArtAuthoringPlanner.ClosedChevronProcessLayoutCommandId] = SmartArtLayoutPreset.ClosedChevronProcess,
            [SmartArtAuthoringPlanner.BendingProcessLayoutCommandId] = SmartArtLayoutPreset.BendingProcess,
            [SmartArtAuthoringPlanner.VerticalBulletListLayoutCommandId] = SmartArtLayoutPreset.VerticalBulletList,
            [SmartArtAuthoringPlanner.VerticalBlockListLayoutCommandId] = SmartArtLayoutPreset.VerticalBlockList,
            [SmartArtAuthoringPlanner.HorizontalBulletListLayoutCommandId] = SmartArtLayoutPreset.HorizontalBulletList,
            [SmartArtAuthoringPlanner.HorizontalBlockListLayoutCommandId] = SmartArtLayoutPreset.HorizontalBlockList,
            [SmartArtAuthoringPlanner.TitledMatrixLayoutCommandId] = SmartArtLayoutPreset.TitledMatrix,
            [SmartArtAuthoringPlanner.GridMatrixLayoutCommandId] = SmartArtLayoutPreset.GridMatrix,
            [SmartArtAuthoringPlanner.BasicRelationshipLayoutCommandId] = SmartArtLayoutPreset.BasicRelationship,
            [SmartArtAuthoringPlanner.InterlockingRingsLayoutCommandId] = SmartArtLayoutPreset.InterlockingRings,
            [SmartArtAuthoringPlanner.OpposingIdeasLayoutCommandId] = SmartArtLayoutPreset.OpposingIdeas,
            [SmartArtAuthoringPlanner.ConvergingRadialLayoutCommandId] = SmartArtLayoutPreset.ConvergingRadial,
            [SmartArtAuthoringPlanner.DivergingRadialLayoutCommandId] = SmartArtLayoutPreset.DivergingRadial,
            [SmartArtAuthoringPlanner.Hierarchy3LayoutCommandId] = SmartArtLayoutPreset.Hierarchy3,
            [SmartArtAuthoringPlanner.GearCycleLayoutCommandId] = SmartArtLayoutPreset.GearCycle,
            [SmartArtAuthoringPlanner.Cycle2LayoutCommandId] = SmartArtLayoutPreset.Cycle2,
            [SmartArtAuthoringPlanner.MultidirectionalCycleLayoutCommandId] = SmartArtLayoutPreset.MultidirectionalCycle,
            [SmartArtAuthoringPlanner.ContinuousCycleLayoutCommandId] = SmartArtLayoutPreset.ContinuousCycle,
            [SmartArtAuthoringPlanner.TextCycleLayoutCommandId] = SmartArtLayoutPreset.TextCycle,
            [SmartArtAuthoringPlanner.BlockCycleLayoutCommandId] = SmartArtLayoutPreset.BlockCycle,
            [SmartArtAuthoringPlanner.NonDirectionalCycleLayoutCommandId] = SmartArtLayoutPreset.NonDirectionalCycle,
            [SmartArtAuthoringPlanner.BasicListLayoutCommandId] = SmartArtLayoutPreset.BasicList,
            [SmartArtAuthoringPlanner.List2LayoutCommandId] = SmartArtLayoutPreset.List2,
            [SmartArtAuthoringPlanner.BasicRadialLayoutCommandId] = SmartArtLayoutPreset.BasicRadial,
            [SmartArtAuthoringPlanner.InvertedPyramidLayoutCommandId] = SmartArtLayoutPreset.InvertedPyramid,
            [SmartArtAuthoringPlanner.RadialClusterLayoutCommandId] = SmartArtLayoutPreset.RadialCluster,
            [SmartArtAuthoringPlanner.RadialListLayoutCommandId] = SmartArtLayoutPreset.RadialList,
            [SmartArtAuthoringPlanner.IncreasingCircleProcessLayoutCommandId] = SmartArtLayoutPreset.IncreasingCircleProcess,
            [SmartArtAuthoringPlanner.PictureAccentProcessLayoutCommandId] = SmartArtLayoutPreset.PictureAccentProcess,
        };

        foreach (var (commandId, preset) in expected)
        {
            Assert.Contains(layouts.Controls, control => control.CommandId.Value == commandId);
            var (editor, _) = MakeSession();
            SmartArtLayoutPreset? applied = null;
            Exec(MakeSmartArtRegistry(editor, selected => applied = selected), commandId);
            Assert.Equal(preset, applied);
        }
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
    public void IllustrationsGroup_ContainsCommonShapeIds()
    {
        var def = FreePRibbon.Build();
        var tab = def.Tabs.Single(t => t.Id == "insert");
        var group = tab.Groups.Single(g => g.Id == "illustrations");
        var ids = group.Controls.Select(c => c.CommandId.Value).ToHashSet();
        Assert.Contains(SlideObjectInsertionPlanner.TriangleCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.RoundedRectangleCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.DiamondCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.HexagonCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.ParallelogramCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.TrapezoidCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.LeftArrowCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.RightArrowCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.UpArrowCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.DownArrowCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.Star5CommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.CrossCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.PlusSignCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.PentagonCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.OctagonCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.LeftRightArrowCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.UpDownArrowCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.Star8CommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.ChevronCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.HomePlateCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.RightTriangleCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.MinusSignCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.MultiplySignCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.DivideSignCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.EqualSignCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.NotEqualSignCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.WaveCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.RectangularCalloutCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.RoundedRectangularCalloutCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.OvalCalloutCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.ExplosionCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.RibbonCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.FlowchartProcessCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.FlowchartDecisionCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.FlowchartDataCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.FlowchartPredefinedProcessCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.FlowchartDocumentCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.FlowchartTerminatorCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.LineCalloutCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.CylinderCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.ChordCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.HeartCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.ConnectorCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.ElbowConnectorCommandId, ids);
        Assert.Contains(SlideObjectInsertionPlanner.CurvedConnectorCommandId, ids);
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
        Assert.Contains("freep.insert-chart-of-pie", ids);
        Assert.Contains("freep.insert-chart-column-stacked", ids);
        Assert.Contains("freep.insert-chart-column-stacked-100", ids);
        Assert.Contains("freep.insert-chart-bar-stacked", ids);
        Assert.Contains("freep.insert-chart-bar-stacked-100", ids);
        Assert.Contains("freep.insert-chart-line-markers", ids);
        Assert.Contains("freep.insert-chart-area", ids);
        Assert.Contains("freep.insert-chart-area-stacked", ids);
        Assert.Contains("freep.insert-chart-scatter", ids);
        Assert.Contains("freep.insert-chart-doughnut", ids);
        Assert.Contains("freep.insert-chart-radar", ids);
        Assert.Contains("freep.insert-chart-bubble", ids);
        Assert.Contains("freep.insert-chart-stock", ids);
        Assert.Contains("freep.insert-chart-surface", ids);
        Assert.Contains("freep.insert-chart-surface-3d", ids);
        Assert.Contains("freep.insert-chart-funnel", ids);
        Assert.Contains("freep.insert-chart-waterfall", ids);
        Assert.Contains("freep.insert-chart-combo", ids);
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
    public void Cmd_UndoRedo_RoutesThroughWpfRibbonRegistry()
    {
        var (ed, _) = MakeSession();
        var reg = MakeRegistry(ed);

        ed.InsertDefaultTextBox();
        Assert.True(ed.CanUndo);

        Exec(reg, "freep.undo");
        Assert.False(ed.CanUndo);
        Assert.True(ed.CanRedo);

        Exec(reg, "freep.redo");
        Assert.True(ed.CanUndo);
        Assert.False(ed.CanRedo);
    }

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

    [Theory]
    [InlineData("freep.arrange.change-shape.rectangle", DrawingShapeKind.Rectangle)]
    [InlineData("freep.arrange.change-shape.rounded-rectangle", DrawingShapeKind.RoundedRectangle)]
    [InlineData("freep.arrange.change-shape.ellipse", DrawingShapeKind.Ellipse)]
    [InlineData("freep.arrange.change-shape.triangle", DrawingShapeKind.Triangle)]
    [InlineData("freep.arrange.change-shape.diamond", DrawingShapeKind.Diamond)]
    [InlineData("freep.arrange.change-shape.right-arrow", DrawingShapeKind.RightArrow)]
    [InlineData("freep.arrange.change-shape.hexagon", DrawingShapeKind.Hexagon)]
    [InlineData("freep.arrange.change-shape.parallelogram", DrawingShapeKind.Parallelogram)]
    [InlineData("freep.arrange.change-shape.trapezoid", DrawingShapeKind.Trapezoid)]
    [InlineData("freep.arrange.change-shape.left-arrow", DrawingShapeKind.LeftArrow)]
    [InlineData("freep.arrange.change-shape.star5", DrawingShapeKind.Star5)]
    [InlineData("freep.arrange.change-shape.up-arrow", DrawingShapeKind.UpArrow)]
    [InlineData("freep.arrange.change-shape.down-arrow", DrawingShapeKind.DownArrow)]
    [InlineData("freep.arrange.change-shape.cross", DrawingShapeKind.Cross)]
    [InlineData("freep.arrange.change-shape.plus-sign", DrawingShapeKind.PlusSign)]
    [InlineData("freep.arrange.change-shape.right-triangle", DrawingShapeKind.RightTriangle)]
    [InlineData("freep.arrange.change-shape.minus-sign", DrawingShapeKind.MinusSign)]
    [InlineData("freep.arrange.change-shape.multiply-sign", DrawingShapeKind.MultiplySign)]
    [InlineData("freep.arrange.change-shape.divide-sign", DrawingShapeKind.DivideSign)]
    [InlineData("freep.arrange.change-shape.equal-sign", DrawingShapeKind.EqualSign)]
    [InlineData("freep.arrange.change-shape.not-equal-sign", DrawingShapeKind.NotEqualSign)]
    [InlineData("freep.arrange.change-shape.wave", DrawingShapeKind.Wave)]
    [InlineData("freep.arrange.change-shape.rectangular-callout", DrawingShapeKind.RectangularCallout)]
    [InlineData("freep.arrange.change-shape.rounded-rectangular-callout", DrawingShapeKind.RoundedRectangularCallout)]
    [InlineData("freep.arrange.change-shape.oval-callout", DrawingShapeKind.OvalCallout)]
    [InlineData("freep.arrange.change-shape.explosion", DrawingShapeKind.Explosion)]
    [InlineData("freep.arrange.change-shape.ribbon", DrawingShapeKind.Ribbon)]
    [InlineData("freep.arrange.change-shape.flowchart-process", DrawingShapeKind.FlowchartProcess)]
    [InlineData("freep.arrange.change-shape.flowchart-decision", DrawingShapeKind.FlowchartDecision)]
    [InlineData("freep.arrange.change-shape.flowchart-data", DrawingShapeKind.FlowchartData)]
    [InlineData("freep.arrange.change-shape.flowchart-predefined-process", DrawingShapeKind.FlowchartPredefinedProcess)]
    [InlineData("freep.arrange.change-shape.flowchart-document", DrawingShapeKind.FlowchartDocument)]
    [InlineData("freep.arrange.change-shape.flowchart-terminator", DrawingShapeKind.FlowchartTerminator)]
    [InlineData("freep.arrange.change-shape.line-callout", DrawingShapeKind.LineCallout)]
    [InlineData("freep.arrange.change-shape.cylinder", DrawingShapeKind.Cylinder)]
    [InlineData("freep.arrange.change-shape.chord", DrawingShapeKind.Chord)]
    [InlineData("freep.arrange.change-shape.heart", DrawingShapeKind.Heart)]
    public void Cmd_ChangeShape_RoutesThroughSharedEditingSession(
        string commandId,
        DrawingShapeKind expectedKind)
    {
        var (ed, pres) = MakeSession();
        var shape = new SlideShape
        {
            Id = 501,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 2 * DrawingMlCoordinateUnits.EmuPerInch,
            ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerInch,
        };
        pres.Slides[0].Shapes.Add(shape);
        ed.Select(shape.Id);

        Exec(MakeRegistry(ed), commandId);

        Assert.Equal(expectedKind, shape.AutoShapeKind);
        ed.Undo();
        Assert.Equal(DrawingShapeKind.Rectangle, shape.AutoShapeKind);
    }

    [Fact]
    public void Cmd_OpenEmbeddedObject_ProvidesSelectedOlePayload()
    {
        var (ed, pres) = MakeSession();
        var expected = new OleObjectInfo
        {
            EmbeddedBytes = [1, 2, 3],
            EmbeddedExtension = "xlsx",
            ProgId = "Excel.Sheet.12",
        };
        var shape = new SlideShape
        {
            Id = 502,
            Name = "Embedded",
            Kind = SlideShapeKind.Ole,
            OleObject = expected,
            ExtentCxEmu = DrawingMlCoordinateUnits.EmuPerInch,
            ExtentCyEmu = DrawingMlCoordinateUnits.EmuPerInch,
        };
        pres.Slides[0].Shapes.Add(shape);
        ed.Select(shape.Id);

        OleObjectInfo? opened = null;
        var registry = FreePRibbonCommands.Build(
            new RibbonStateStore(), ed,
            onOpenEmbeddedObject: ole => opened = ole);

        Exec(registry, OleActivationPlanner.OpenEmbeddedObjectCommandId);

        Assert.Same(expected, opened);
    }

    [Fact]
    public void Cmd_OpenEmbeddedObject_PrefersActiveInlineObject()
    {
        var (ed, _) = MakeSession();
        bool inlineOpened = false;
        bool slideOpened = false;
        var registry = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            ed,
            tryOpenInlineEmbeddedObject: () =>
            {
                inlineOpened = true;
                return true;
            },
            onOpenEmbeddedObject: _ => slideOpened = true);

        Exec(registry, OleActivationPlanner.OpenEmbeddedObjectCommandId);

        Assert.True(inlineOpened);
        Assert.False(slideOpened);
    }

    [Fact]
    public void Cmd_InsertEmbeddedObject_RoutesToHostPickerCallback()
    {
        var (ed, _) = MakeSession();
        var invoked = false;
        var registry = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            ed,
            onInsertEmbeddedObject: () => invoked = true);

        Exec(registry, OleInsertionPlanner.InsertEmbeddedObjectCommandId);

        Assert.True(invoked);
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
    public void Cmd_ZoomResetPreview_InvokesHostCallbackWhenProvided()
    {
        var (ed, _) = MakeSession();
        var invoked = false;
        var reg = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            ed,
            onResetZoomCoverImage: () => invoked = true);

        Exec(reg, FreeP.App.Compositor.ZoomCoverImagePlanner.ResetCommandId);

        Assert.True(invoked);
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
    public void Cmd_SlideNumber_WithoutHostCallback_HonorsEnabledTitleSlidePlaceholders()
    {
        var (ed, pres) = MakeSession();
        pres.ShowSpecialPlaceholdersOnTitleSlide = true;
        var reg = MakeRegistry(ed);

        Exec(reg, HeaderFooterCommandPlanner.SlideNumberCommandId);

        Assert.True(pres.Slides[0].HfVisibility!.ShowSlideNum);
        var slideNumber = Assert.Single(
            pres.Slides[0].Shapes,
            shape => shape.Placeholder?.Type == PlaceholderType.SlideNumber);
        Assert.Equal("slidenum", slideNumber.TextBody?.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Single().Field?.FieldType);
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

    [Fact]
    public void Cmd_InsertChartOfPie_AddsOfPieChart()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.insert-chart-of-pie");
        var added = pres.Slides[0].Shapes.Last();
        Assert.Equal(ChartType.OfPie, added.Chart!.ChartType);
    }

    [Fact]
    public void Cmd_ChangeChartType_RegistersAllOptionsAndRoutesSelectedChart()
    {
        var (ed, pres) = MakeSession();
        var reg = MakeRegistry(ed);
        Exec(reg, "freep.insert-chart-column");
        var chart = pres.Slides[0].Shapes.Last();
        ed.Select(chart.Id);

        Assert.True(reg.TryGet(ChartDataDialogPlanner.ChangeChartTypeCommandId, out _));
        foreach (var option in ChartDataDialogPlanner.ChartTypeOptions)
        {
            var commandId = ChartDataDialogPlanner.ChangeChartTypeOptionCommandId(option.Value);
            Assert.True(reg.TryGet(commandId, out _), $"Command '{commandId}' was not registered.");
        }

        Exec(reg, ChartDataDialogPlanner.ChangeChartTypeOptionCommandId(ChartType.Scatter));
        Assert.Equal(ChartType.Scatter, chart.Chart!.ChartType);
        Assert.True(ed.CanUndo);
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
    public void Cmd_TextAutoFit_WithSelectedValue_RoutesToEditor()
    {
        var (ed, pres) = MakeSession();
        ed.InsertDefaultTextBox();
        var shape = pres.Slides[0].Shapes.Last();
        ed.Select(shape.Id);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.text-autofit", RibbonCommandContext.ForSelectedValue("Shrink text on overflow"));

        Assert.Equal(TextAutoFitKind.Normal, shape.TextBody!.AutoFitKind);
        ed.Undo();
        Assert.Equal(TextAutoFitKind.None, shape.TextBody.AutoFitKind);
    }

    [Fact]
    public void Cmd_TextDirection_WithSelectedValue_RoutesToShapeAndUndo()
    {
        var (ed, pres) = MakeSession();
        ed.InsertDefaultTextBox();
        var shape = pres.Slides[0].Shapes.Last();
        ed.Select(shape.Id);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.text-direction", RibbonCommandContext.ForSelectedValue("Rotate 90 degrees"));

        Assert.Equal(TextVerticalType.Vertical, shape.TextBody!.VerticalType);
        ed.Undo();
        Assert.Equal(TextVerticalType.Horizontal, shape.TextBody.VerticalType);
    }

    [Fact]
    public void Cmd_TextColumns_WithSelectedValue_RoutesToShapeAndUndo()
    {
        var (ed, pres) = MakeSession();
        ed.InsertDefaultTextBox();
        var shape = pres.Slides[0].Shapes.Last();
        ed.Select(shape.Id);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.text-columns", RibbonCommandContext.ForSelectedValue("3"));

        Assert.Equal(3, shape.TextBody!.ColumnCount);
        ed.Undo();
        Assert.Equal(1, shape.TextBody.ColumnCount);
    }

    [Fact]
    public void Cmd_TextColumnSpacing_WithSelectedValue_RoutesToShapeAndUndo()
    {
        var (ed, pres) = MakeSession();
        ed.InsertDefaultTextBox();
        var shape = pres.Slides[0].Shapes.Last();
        ed.Select(shape.Id);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.text-column-spacing", RibbonCommandContext.ForSelectedValue("12 pt"));

        Assert.Equal(152_400, shape.TextBody!.ColumnSpacingEmu);
        ed.Undo();
        Assert.Equal(0, shape.TextBody.ColumnSpacingEmu);
    }

    [Fact]
    public void Cmd_TextDirection_WithActiveTableCell_RoutesToCell()
    {
        var (ed, pres) = MakeSession();
        var table = AddSingleCellTable(pres, 10, MakeTextBody("cell"));
        ed.Select(table.Id);
        ed.SetActiveTableCell(0, 0);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.text-direction", RibbonCommandContext.ForSelectedValue("East Asian vertical"));

        Assert.Equal(TextVerticalType.EastAsianVertical, table.Table!.Rows[0].Cells[0].TextBody!.VerticalType);
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

    [Fact]
    public void Cmd_TableCellFill_WithActiveTableCell_UsesSharedCommand()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 801, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.table-cell-fill", RibbonCommandContext.ForSelectedValue("#336699"));

        var solid = shape.Table!.Rows[0].Cells[0].Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        Assert.Equal(SrgbColor.FromRgb(0x336699), solid.Color.Resolved);
        ed.Undo();
        Assert.Null(shape.Table.Rows[0].Cells[0].Fill);
    }

    [Fact]
    public void Cmd_TableCellAnchor_WithActiveTableCell_UsesSharedCommand()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 802, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.table-cell-anchor", RibbonCommandContext.ForSelectedValue("Bottom"));

        Assert.Equal(TableCellAnchor.Bottom, shape.Table!.Rows[0].Cells[0].Anchor);
        ed.Undo();
        Assert.Null(shape.Table.Rows[0].Cells[0].Anchor);
    }

    [Fact]
    public void Cmd_TableCellBorder_WithActiveTableCell_UsesSharedCommand()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 803, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.table-cell-border", RibbonCommandContext.ForSelectedValue("Bottom:Black 1pt"));

        var outline = shape.Table!.Rows[0].Cells[0].Borders!.Bottom
            .Should().BeOfType<ShapeOutline.Visible>().Subject;
        Assert.Equal(1, outline.WidthPt);
        Assert.Equal(ThemeAwareColor.Black, outline.Color);
        ed.Undo();
        Assert.Null(shape.Table.Rows[0].Cells[0].Borders);
    }

    [Fact]
    public void Cmd_TableCellInset_WithActiveTableCell_UsesSharedCommand()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 804, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.table-cell-inset", RibbonCommandContext.ForSelectedValue("All:4pt"));

        var cell = shape.Table!.Rows[0].Cells[0];
        Assert.Equal(4, cell.InsetLeftPt);
        Assert.Equal(4, cell.InsetBottomPt);
        ed.Undo();
        Assert.Null(cell.InsetLeftPt);
        Assert.Null(cell.InsetBottomPt);
    }

    [Fact]
    public void Cmd_TableRowHeight_WithActiveTableCell_UsesSharedCommand()
    {
        var (ed, pres) = MakeSession();
        var shape = AddSingleCellTable(pres, 805, MakeTextBody("Cell"));
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);
        var reg = MakeRegistry(ed);

        Exec(reg, "freep.table-row-height", RibbonCommandContext.ForSelectedValue("0.75in"));

        Assert.Equal(685800, shape.Table!.Rows[0].HeightEmu);
        ed.Undo();
        Assert.NotEqual(685800, shape.Table.Rows[0].HeightEmu);
    }

    [Fact]
    public void Cmd_TableDistributeRows_WithActiveTableCell_PreservesTotalAndUndoes()
    {
        var (ed, _) = MakeSession();
        var shape = ed.InsertTable(3, 2);
        shape.Table!.Rows[0].HeightEmu = 300000;
        shape.Table.Rows[1].HeightEmu = 500000;
        shape.Table.Rows[2].HeightEmu = 700000;
        ed.Select(shape.Id);
        ed.SetActiveTableCell(1, 0);
        long total = shape.Table.Rows.Sum(row => row.HeightEmu);

        Exec(MakeRegistry(ed), TableCellEditPlanner.DistributeRowsCommandId);

        Assert.Equal(new[] { 500000L, 500000L, 500000L }, shape.Table.Rows.Select(row => row.HeightEmu));
        Assert.Equal(total, shape.Table.Rows.Sum(row => row.HeightEmu));
        ed.Undo();
        Assert.Equal(new[] { 300000L, 500000L, 700000L }, shape.Table.Rows.Select(row => row.HeightEmu));
    }

    [Fact]
    public void Cmd_TableDistributeColumns_WithActiveTableCell_PreservesTotalAndUndoes()
    {
        var (ed, _) = MakeSession();
        var shape = ed.InsertTable(2, 3);
        shape.Table!.ColumnWidthsEmu[0] = 300000;
        shape.Table.ColumnWidthsEmu[1] = 500000;
        shape.Table.ColumnWidthsEmu[2] = 700000;
        ed.Select(shape.Id);
        ed.SetActiveTableCell(1, 1);
        long total = shape.Table.ColumnWidthsEmu.Sum();

        Exec(MakeRegistry(ed), TableCellEditPlanner.DistributeColumnsCommandId);

        Assert.Equal(new[] { 500000L, 500000L, 500000L }, shape.Table.ColumnWidthsEmu);
        Assert.Equal(total, shape.Table.ColumnWidthsEmu.Sum());
        ed.Undo();
        Assert.Equal(new[] { 300000L, 500000L, 700000L }, shape.Table.ColumnWidthsEmu);
    }

    [Fact]
    public void Cmd_TableMergeCells_WithActiveTableCell_UsesSharedCommand()
    {
        var (ed, _) = MakeSession();
        var shape = ed.InsertTable(1, 2);
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);

        Exec(MakeRegistry(ed), TableCellEditPlanner.MergeCellsCommandId);

        Assert.Equal(2, shape.Table!.Rows[0].Cells[0].GridSpan);
        ed.Undo();
        Assert.Equal(1, shape.Table.Rows[0].Cells[0].GridSpan);
    }

    [Fact]
    public void Cmd_TableSplitCell_WithActiveTableCell_UsesSharedCommand()
    {
        var (ed, _) = MakeSession();
        var shape = ed.InsertTable(1, 2);
        ed.Select(shape.Id);
        ed.SetActiveTableCell(0, 0);
        Assert.True(ed.TryMergeActiveTableCell());

        Exec(MakeRegistry(ed), TableCellEditPlanner.SplitCellCommandId);

        Assert.Equal(1, shape.Table!.Rows[0].Cells[0].GridSpan);
    }

    [Theory]
    [InlineData(TableCellEditPlanner.TableFirstRowCommandId, TableStyleFlagKind.FirstRow)]
    [InlineData(TableCellEditPlanner.TableLastRowCommandId, TableStyleFlagKind.LastRow)]
    [InlineData(TableCellEditPlanner.TableFirstColCommandId, TableStyleFlagKind.FirstCol)]
    [InlineData(TableCellEditPlanner.TableLastColCommandId, TableStyleFlagKind.LastCol)]
    [InlineData(TableCellEditPlanner.TableBandRowCommandId, TableStyleFlagKind.BandRow)]
    [InlineData(TableCellEditPlanner.TableBandColCommandId, TableStyleFlagKind.BandCol)]
    public void Cmd_TableStyleFlag_WithSelectedTable_TogglesAndUndoes(
        string commandId,
        TableStyleFlagKind kind)
    {
        var (ed, _) = MakeSession();
        var shape = ed.InsertTable(2, 2);
        ed.Select(shape.Id);
        var before = GetTableStyleFlag(shape.Table!.Flags, kind);

        Exec(MakeRegistry(ed), commandId);

        Assert.Equal(!before, GetTableStyleFlag(shape.Table.Flags, kind));
        ed.Undo();
        Assert.Equal(before, GetTableStyleFlag(shape.Table.Flags, kind));
    }

    private static bool GetTableStyleFlag(TableStyleFlags flags, TableStyleFlagKind kind) => kind switch
    {
        TableStyleFlagKind.FirstRow => flags.FirstRow,
        TableStyleFlagKind.LastRow => flags.LastRow,
        TableStyleFlagKind.FirstCol => flags.FirstCol,
        TableStyleFlagKind.LastCol => flags.LastCol,
        TableStyleFlagKind.BandRow => flags.BandRow,
        TableStyleFlagKind.BandCol => flags.BandCol,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [Theory]
    [InlineData("freep.bold", TableCellTextFormatKind.Bold)]
    [InlineData("freep.italic", TableCellTextFormatKind.Italic)]
    [InlineData("freep.underline", TableCellTextFormatKind.Underline)]
    [InlineData("freep.superscript", TableCellTextFormatKind.Superscript)]
    [InlineData("freep.subscript", TableCellTextFormatKind.Subscript)]
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
            TableCellTextFormatKind.Superscript => run.BaselineOffset > 0,
            TableCellTextFormatKind.Subscript => run.BaselineOffset < 0,
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
    [InlineData("freep.text-autofit")]
    [InlineData("freep.text-columns")]
    [InlineData("freep.table-cell-fill")]
    [InlineData("freep.table-cell-anchor")]
    [InlineData("freep.table-cell-border")]
    [InlineData("freep.table-cell-inset")]
    [InlineData("freep.table-row-height")]
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
    [InlineData("freep.insert-chart-of-pie")]
    [InlineData("freep.shape-triangle")]
    [InlineData("freep.shape-diamond")]
    [InlineData("freep.shape-hexagon")]
    [InlineData("freep.shape-right-arrow")]
    [InlineData("freep.shape-star5")]
    [InlineData("freep.shape-pentagon")]
    [InlineData("freep.shape-octagon")]
    [InlineData("freep.shape-left-right-arrow")]
    [InlineData("freep.shape-up-down-arrow")]
    [InlineData("freep.shape-star8")]
    [InlineData("freep.shape-chevron")]
    [InlineData("freep.shape-home-plate")]
    [InlineData("freep.shape-right-triangle")]
    [InlineData("freep.shape-minus-sign")]
    [InlineData("freep.shape-multiply-sign")]
    [InlineData("freep.shape-divide-sign")]
    [InlineData("freep.shape-equal-sign")]
    [InlineData("freep.shape-not-equal-sign")]
    [InlineData("freep.shape-wave")]
    [InlineData("freep.shape-rectangular-callout")]
    [InlineData("freep.shape-rounded-rectangular-callout")]
    [InlineData("freep.shape-oval-callout")]
    [InlineData("freep.shape-explosion")]
    [InlineData("freep.shape-ribbon")]
    [InlineData("freep.shape-flowchart-process")]
    [InlineData("freep.shape-flowchart-decision")]
    [InlineData("freep.shape-flowchart-data")]
    [InlineData("freep.shape-flowchart-predefined-process")]
    [InlineData("freep.shape-flowchart-document")]
    [InlineData("freep.shape-flowchart-terminator")]
    [InlineData("freep.shape-line-callout")]
    [InlineData("freep.shape-cylinder")]
    [InlineData("freep.shape-chord")]
    [InlineData("freep.shape-heart")]
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
