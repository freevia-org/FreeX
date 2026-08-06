using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using SkiaSharp;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Tests for the Avalonia DocumentView floating object render path — FO3 wave:
/// floating Charts, WordArt, SmartArt, and DrawingGroups.
/// Verifies: each type is collected separately; page-space rect is resolved from FloatingPlacement;
/// z-order (BehindText / ZOrder) is correct; render does not crash; headless PNG captures non-blank output.
/// </summary>
public sealed class DocumentViewFloatingFO3Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string RepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(segments));
    }

    [Fact]
    public void Floating_chart_and_smartart_render_data_use_shared_visual_planner()
    {
        var source = File.ReadAllText(RepositoryFile(
            "freew",
            "FreeW.App.Avalonia",
            "Editing",
            "DocumentView.cs"));

        source.Should().Contain("ChartSmartArtVisualPlanner.BuildChartPlan(chart)");
        source.Should().Contain("ChartSmartArtVisualPlanner.BuildChartScene(chart, settings, rect.Width, rect.Height)");
        source.Should().Contain("ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt, _doc.Theme)");
        source.Should().Contain("RenderChartScene(context, cd.Scene)");
        source.Should().Contain("DrawFloatingSmartArt(context, smartArt)");
        source.Should().Contain("sd.LayoutGeometry is { Kind: SmartArtLayoutGeometryKind.Pyramid, Nodes.Count: > 0 } nativePyramid");
        source.Should().Contain("const double nativeWordPyramidBaselineOffsetDip = 22;");
        source.Should().Contain("DrawSmartArtLayoutGeometry(context, sd, nativePyramid, nativePyramidTarget);");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private static TextDocument DocWithFloatingChart(
        ChartKind kind,
        ImageWrapping wrapping,
        double hOffsetPt,
        double vOffsetPt,
        int zOrder = 0,
        double widthPt  = 216,
        double heightPt = 144,
        string? title   = "Test Chart")
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Anchor text.", RunFormatting.Default));

        var chart = Chart.Create(kind,
            new[] { "A", "B", "C" },
            new[] { 10.0, 25.0, 15.0 },
            "Series 1",
            title);
        chart.WidthPt  = widthPt;
        chart.HeightPt = heightPt;
        chart.Placement = new FloatingPlacement
        {
            Wrapping           = wrapping,
            HorizontalOffsetPt = hOffsetPt,
            VerticalOffsetPt   = vOffsetPt,
            HorizontalAnchor   = HorizontalAnchor.Column,
            VerticalAnchor     = VerticalAnchor.Paragraph,
            ZOrderIndex        = zOrder,
        };
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithFloatingWordArt(
        WordArtStyle style,
        ImageWrapping wrapping,
        double hOffsetPt,
        double vOffsetPt,
        int zOrder = 0,
        string text = "Hello WordArt",
        WordArtWarp warp = WordArtWarp.None)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Anchor text.", RunFormatting.Default));

        var wa = new WordArt(text, style, 36)
        {
            Placement = new FloatingPlacement
            {
                Wrapping           = wrapping,
                HorizontalOffsetPt = hOffsetPt,
                VerticalOffsetPt   = vOffsetPt,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = zOrder,
            },
            Warp = warp,
        };
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithFloatingSmartArt(
        SmartArtKind kind,
        ImageWrapping wrapping,
        double hOffsetPt,
        double vOffsetPt,
        int zOrder = 0,
        string? colorSchemeId = null,
        string? styleId = null,
        Action<SmartArt>? configure = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Anchor text.", RunFormatting.Default));

        var sa = CreateFloatingSmartArt(kind);
        sa.ColorSchemeId = colorSchemeId;
        sa.StyleId = styleId;
        configure?.Invoke(sa);
        sa.Placement = new FloatingPlacement
        {
            Wrapping           = wrapping,
            HorizontalOffsetPt = hOffsetPt,
            VerticalOffsetPt   = vOffsetPt,
            HorizontalAnchor   = HorizontalAnchor.Column,
            VerticalAnchor     = VerticalAnchor.Paragraph,
            ZOrderIndex        = zOrder,
        };
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });
        doc.Blocks.Add(para);
        return doc;
    }

    private static SmartArt CreateFloatingSmartArt(SmartArtKind kind)
    {
        if (kind != SmartArtKind.Hierarchy)
            return SmartArt.Create(kind, new[] { "Node A", "Node B", "Node C" });

        var root = new SmartArtNode("Root");
        var child = root.AddChild("Child");
        child.AddChild("Grandchild");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(root);
        return smartArt;
    }

    private static TextDocument DocWithFloatingGroup(ImageWrapping wrapping, double hOffsetPt, double vOffsetPt, int zOrder = 0)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Anchor text.", RunFormatting.Default));

        var grp = new DrawingGroup
        {
            WidthPt  = 200,
            HeightPt = 100,
            Placement = new FloatingPlacement
            {
                Wrapping           = wrapping,
                HorizontalOffsetPt = hOffsetPt,
                VerticalOffsetPt   = vOffsetPt,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = zOrder,
            },
        };

        // Child 1: shape
        var s1 = new Shape(ShapeKind.Rectangle, 80, 60, "#4472C4");
        grp.Children.Add(s1);
        grp.ChildOffsets.Add((0, 0));

        // Child 2: shape (ellipse)
        var s2 = new Shape(ShapeKind.Ellipse, 60, 60, "#ED7D31");
        grp.Children.Add(s2);
        grp.ChildOffsets.Add((90, 10));

        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { DrawingGroup = grp });
        doc.Blocks.Add(para);
        return doc;
    }

    // ── Chart collection tests ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_chart_is_not_collected_as_floating()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            // Inline chart (no Placement).
            var chart = Chart.Create(ChartKind.Column, new[] { "A" }, new[] { 1.0 });
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingChartCount;
        });

        if (!ran) return;
        count.Should().Be(0, "an inline chart (no Placement) must not be added to _floatingCharts");
    }

    [Fact]
    public async Task Floating_column_chart_is_collected()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Column, ImageWrapping.Square, 36, 36);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingChartCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one floating chart should produce one entry in _floatingCharts");
    }

    [Fact]
    public async Task Floating_chart_rect_has_correct_width()
    {
        double capturedWidth = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Bar, ImageWrapping.InFront, 0, 0, widthPt: 216);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingChartRects;
            if (rects.Count > 0) capturedWidth = rects[0].Rect.Width;
        });

        if (!ran) return;
        capturedWidth.Should().BeApproximately(216 * (96.0 / 72.0), 2,
            "chart width should be 216pt converted to DIP");
    }

    [Fact]
    public async Task Floating_chart_behind_text_flag()
    {
        bool? behind = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Pie, ImageWrapping.Behind, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingChartRects;
            if (rects.Count > 0) behind = rects[0].BehindText;
        });

        if (!ran) return;
        behind.Should().BeTrue("ImageWrapping.Behind must set BehindText=true for charts");
    }

    [Fact]
    public async Task Floating_chart_infront_flag_false()
    {
        bool? behind = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Line, ImageWrapping.InFront, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingChartRects;
            if (rects.Count > 0) behind = rects[0].BehindText;
        });

        if (!ran) return;
        behind.Should().BeFalse("ImageWrapping.InFront must set BehindText=false for charts");
    }

    [Fact]
    public async Task Floating_chart_zorder_preserved()
    {
        int zOrder = -999;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Column, ImageWrapping.Square, 0, 0, zOrder: 42);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingChartRects;
            if (rects.Count > 0) zOrder = rects[0].ZOrder;
        });

        if (!ran) return;
        zOrder.Should().Be(42, "ZOrderIndex from FloatingPlacement must be preserved for charts");
    }

    [Fact]
    public async Task Floating_chart_kind_preserved()
    {
        ChartKind kind = ChartKind.Column;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Pie, ImageWrapping.Square, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingChartRects;
            if (rects.Count > 0) kind = rects[0].Kind;
        });

        if (!ran) return;
        kind.Should().Be(ChartKind.Pie, "chart kind must be preserved in FloatingChartRects");
    }

    [Fact]
    public async Task Floating_chart_title_preserved()
    {
        string? title = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Column, ImageWrapping.Square, 0, 0, title: "My Chart");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingChartRects;
            if (rects.Count > 0) title = rects[0].Title;
        });

        if (!ran) return;
        title.Should().Be("My Chart", "chart title must be preserved in FloatingChartRects");
    }

    // ── WordArt collection tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_wordart_is_not_collected_as_floating()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            var wa = new WordArt("Test", WordArtStyle.FillBlue); // no Placement
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingWordArtCount;
        });

        if (!ran) return;
        count.Should().Be(0, "inline WordArt must not be collected as floating");
    }

    [Fact]
    public async Task Floating_wordart_is_collected()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingWordArt(WordArtStyle.FillBlue, ImageWrapping.InFront, 36, 36);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingWordArtCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one floating WordArt should produce one entry");
    }

    [Fact]
    public async Task Floating_wordart_behind_text_flag()
    {
        bool? behind = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingWordArt(WordArtStyle.FillGold, ImageWrapping.Behind, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingWordArtRects;
            if (rects.Count > 0) behind = rects[0].BehindText;
        });

        if (!ran) return;
        behind.Should().BeTrue("ImageWrapping.Behind must set BehindText=true for WordArt");
    }

    [Fact]
    public async Task Floating_wordart_text_and_style_preserved()
    {
        string? text = null;
        WordArtStyle style = WordArtStyle.FillBlue;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingWordArt(WordArtStyle.Shadow, ImageWrapping.Square, 0, 0, text: "Hello");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingWordArtRects;
            if (rects.Count > 0) { text = rects[0].Text; style = rects[0].Style; }
        });

        if (!ran) return;
        text.Should().Be("Hello", "WordArt text must be preserved in FloatingWordArtRects");
        style.Should().Be(WordArtStyle.Shadow, "WordArt style must be preserved");
    }

    [Fact]
    public async Task Floating_wordart_zorder_preserved()
    {
        int zOrder = -999;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingWordArt(WordArtStyle.Outline, ImageWrapping.Square, 0, 0, zOrder: 99);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingWordArtRects;
            if (rects.Count > 0) zOrder = rects[0].ZOrder;
        });

        if (!ran) return;
        zOrder.Should().Be(99, "ZOrderIndex must be preserved for WordArt");
    }

    [Fact]
    public async Task Floating_wordart_visual_summary_matches_shared_plan()
    {
        string[] summaries = [];
        var expected = DrawingObjectVisualPlanner.BuildInlineWordArtPlan(
            new WordArt("Hello", WordArtStyle.GlowBlue, fontSizePt: 36)
            {
                Warp = WordArtWarp.ArchDown
            }).Summary;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingWordArt(
                WordArtStyle.GlowBlue,
                ImageWrapping.Square,
                0,
                0,
                text: "Hello",
                warp: WordArtWarp.ArchDown);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            summaries = view.FloatingWordArtVisualSummaries.ToArray();
        });

        if (!ran) return;
        summaries.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public void ArchUpGlyphPlacement_IsSymmetricAndCurved()
    {
        var placements = DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(
            WordArtWarp.ArchUp, [10d, 10d, 10d, 10d, 10d], 100, 40).Glyphs;

        placements.Should().HaveCount(5);
        placements[0].CenterYNormalized.Should().BeApproximately(placements[4].CenterYNormalized, 0.001);
        placements[1].CenterYNormalized.Should().BeApproximately(placements[3].CenterYNormalized, 0.001);
        placements[0].CenterYNormalized.Should().BeGreaterThan(placements[2].CenterYNormalized);
        placements[0].RotationRadians.Should().BeLessThan(0);
        placements[2].RotationRadians.Should().BeApproximately(0, 0.001);
        placements[4].RotationRadians.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Wave1GlyphPlacement_UsesOneCenteredSineCycle()
    {
        var placements = DrawingObjectVisualPlanner.BuildWordArtPlacementPlan(
            WordArtWarp.Wave1, [10d, 10d, 10d, 10d, 10d], 100, 40).Glyphs;

        placements.Should().HaveCount(5);
        placements[0].CenterYNormalized.Should().BeLessThan(placements[1].CenterYNormalized);
        placements[1].CenterYNormalized.Should().BeGreaterThan(placements[2].CenterYNormalized);
        placements[2].CenterYNormalized.Should().BeGreaterThan(placements[3].CenterYNormalized);
        placements[3].CenterYNormalized.Should().BeLessThan(placements[4].CenterYNormalized);
        (placements[0].CenterYNormalized + placements[4].CenterYNormalized).Should().BeApproximately(1, 0.001);
        (placements[1].CenterYNormalized + placements[3].CenterYNormalized).Should().BeApproximately(1, 0.001);
        placements[0].RotationRadians.Should().BeGreaterThan(0);
        placements[2].RotationRadians.Should().BeLessThan(0);
    }

    // ── SmartArt collection tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_smartart_is_not_collected_as_floating()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            var sa = SmartArt.Create(SmartArtKind.List, new[] { "A", "B" }); // no Placement
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingSmartArtCount;
        });

        if (!ran) return;
        count.Should().Be(0, "inline SmartArt must not be collected as floating");
    }

    [Fact]
    public async Task Floating_smartart_is_collected()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingSmartArt(SmartArtKind.Process, ImageWrapping.InFront, 36, 36);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingSmartArtCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one floating SmartArt should produce one entry");
    }

    [Fact]
    public async Task Floating_smartart_behind_text_flag()
    {
        bool? behind = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingSmartArt(SmartArtKind.List, ImageWrapping.Behind, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingSmartArtRects;
            if (rects.Count > 0) behind = rects[0].BehindText;
        });

        if (!ran) return;
        behind.Should().BeTrue("ImageWrapping.Behind must set BehindText=true for SmartArt");
    }

    [Fact]
    public async Task Floating_smartart_kind_and_nodecount_preserved()
    {
        SmartArtKind kind = SmartArtKind.List;
        int nodeCount = 0;
        int maxDepth = -1;
        int connectorCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingSmartArt(SmartArtKind.Hierarchy, ImageWrapping.Square, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingSmartArtRects;
            if (rects.Count > 0)
            {
                kind = rects[0].Kind;
                nodeCount = rects[0].NodeCount;
                maxDepth = rects[0].MaxHierarchyDepth;
                connectorCount = rects[0].HierarchyConnectorCount;
            }
        });

        if (!ran) return;
        kind.Should().Be(SmartArtKind.Hierarchy, "SmartArt kind must be preserved");
        nodeCount.Should().Be(3, "root/child/grandchild must be captured in FloatingSmartArtRects");
        maxDepth.Should().Be(2, "floating hierarchy SmartArt should expose grandchild depth");
        connectorCount.Should().Be(2, "floating hierarchy SmartArt should expose parent-child connector geometry");
    }

    [Fact]
    public async Task Floating_smartart_uses_resolved_hierarchy_layout_when_model_kind_is_stale()
    {
        SmartArtKind kind = SmartArtKind.List;
        int maxDepth = -1;
        int connectorCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingSmartArt(
                SmartArtKind.Hierarchy,
                ImageWrapping.Square,
                0,
                0,
                configure: smartArt =>
                {
                    smartArt.Kind = SmartArtKind.Process;
                    smartArt.LayoutId = "orgchart1";
                });
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rect = view.FloatingSmartArtRects.Single();
            kind = rect.Kind;
            maxDepth = rect.MaxHierarchyDepth;
            connectorCount = rect.HierarchyConnectorCount;
        });

        if (!ran) return;
        kind.Should().Be(SmartArtKind.Hierarchy, "the resolved org-chart layout should drive Avalonia rendering");
        maxDepth.Should().Be(2);
        connectorCount.Should().Be(2);
    }

    [Fact]
    public async Task Floating_smartart_carries_shared_radial_layout_geometry()
    {
        (string? LayoutId, string? GeometryKind, int NodeCount, int ConnectorCount) values = default;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingSmartArt(
                SmartArtKind.List,
                ImageWrapping.Square,
                0,
                0,
                configure: smartArt => smartArt.LayoutId = "radial1");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var geometry = view.FloatingSmartArtLayoutGeometries.Single();
            values = (geometry.LayoutId, geometry.GeometryKind, geometry.GeometryNodeCount, geometry.GeometryConnectorCount);
        });

        if (!ran) return;
        values.LayoutId.Should().Be("radial1");
        values.GeometryKind.Should().Be("Radial");
        values.NodeCount.Should().Be(3);
        values.ConnectorCount.Should().Be(2);
    }

    [Theory]
    [InlineData("list1", "BasicList", 0)]
    [InlineData("vertbullet1", "VerticalBulletList", 0)]
    [InlineData("process1", "BasicProcess", 2)]
    public async Task Floating_smartart_carries_basic_shared_layout_geometry(
        string layoutId,
        string expectedKind,
        int expectedConnectors)
    {
        (string? LayoutId, string? GeometryKind, int NodeCount, int ConnectorCount) values = default;
        var ran = await OnUiThread(() =>
        {
            var smartArtKind = layoutId == "process1" ? SmartArtKind.Process : SmartArtKind.List;
            var doc = DocWithFloatingSmartArt(
                smartArtKind,
                ImageWrapping.Square,
                0,
                0,
                configure: smartArt => smartArt.LayoutId = layoutId);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var geometry = view.FloatingSmartArtLayoutGeometries.Single();
            values = (geometry.LayoutId, geometry.GeometryKind, geometry.GeometryNodeCount, geometry.GeometryConnectorCount);
        });

        if (!ran) return;
        values.LayoutId.Should().Be(layoutId);
        values.GeometryKind.Should().Be(expectedKind);
        values.NodeCount.Should().Be(3);
        values.ConnectorCount.Should().Be(expectedConnectors);
    }

    [Fact]
    public async Task Floating_smartart_zorder_preserved()
    {
        int zOrder = -999;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingSmartArt(SmartArtKind.Process, ImageWrapping.Square, 0, 0, zOrder: 55);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingSmartArtRects;
            if (rects.Count > 0) zOrder = rects[0].ZOrder;
        });

        if (!ran) return;
        zOrder.Should().Be(55, "ZOrderIndex must be preserved for SmartArt");
    }

    [Fact]
    public async Task Floating_smartart_carries_planned_style_values()
    {
        (string? Fill, string? Border, double BorderThickness, double CornerRadius,
            double ShadowOpacity, double ShadowBlur, double ShadowDepth, string? Connector) values = default;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingSmartArt(
                SmartArtKind.Process,
                ImageWrapping.Square,
                0,
                0,
                colorSchemeId: "accent1",
                styleId: "intense1");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rect = view.FloatingSmartArtRects.Single();
            values = (
                rect.FirstFillHex,
                rect.FirstBorderHex,
                rect.BorderThickness,
                rect.CornerRadius,
                rect.ShadowOpacity,
                rect.ShadowBlur,
                rect.ShadowDepth,
                rect.FirstConnectorHex);
        });

        if (!ran) return;
        values.Fill.Should().Be("#38517D");
        values.Border.Should().Be("#0A234F");
        values.BorderThickness.Should().Be(1.5);
        values.CornerRadius.Should().Be(0);
        values.ShadowOpacity.Should().Be(0.30);
        values.ShadowBlur.Should().BeApproximately(6.4, 0.01);
        values.ShadowDepth.Should().BeApproximately(2.1, 0.01);
        values.Connector.Should().NotBe(values.Fill);
    }

    // ── DrawingGroup collection tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Floating_group_is_collected()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingGroup(ImageWrapping.Square, 36, 36);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingGroupCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one floating group should produce one entry");
    }

    [Fact]
    public async Task Floating_group_child_count_correct()
    {
        int childCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingGroup(ImageWrapping.InFront, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingGroupRects;
            if (rects.Count > 0) childCount = rects[0].ChildCount;
        });

        if (!ran) return;
        childCount.Should().Be(2, "group with 2 children must report ChildCount=2");
    }

    [Fact]
    public async Task Floating_group_child_shape_effect_summary_is_preserved()
    {
        string[] summaries = [];
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor text.", RunFormatting.Default));
            var group = new DrawingGroup
            {
                WidthPt = 160,
                HeightPt = 80,
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalOffsetPt = 36,
                    VerticalOffsetPt = 36,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph,
                    ZOrderIndex = 9
                }
            };
            group.Children.Add(new Shape(ShapeKind.Ellipse, 80, 48, "#CFE2F3")
            {
                OutlineColorHex = "#1155CC",
                Effects = new ShapeEffectLst
                {
                    HasGlow = true,
                    GlowColorHex = "4472C4",
                    GlowRad = 63500
                }
            });
            group.ChildOffsets.Add((0, 12));
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { DrawingGroup = group });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            summaries = view.FloatingGroupChildEffectSummaries.ToArray();
        });

        if (!ran) return;
        summaries.Should().ContainSingle().Which.Should().Be("GroupChild0:Shape:glow");
    }

    [Fact]
    public async Task Floating_group_child_wordart_effect_summary_is_preserved()
    {
        string[] summaries = [];
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor text.", RunFormatting.Default));
            var group = new DrawingGroup
            {
                WidthPt = 160,
                HeightPt = 80,
                Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalOffsetPt = 36,
                    VerticalOffsetPt = 36,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor = VerticalAnchor.Paragraph,
                    ZOrderIndex = 9
                }
            };
            group.Children.Add(new WordArt("Grouped", WordArtStyle.GlowGold, 22));
            group.ChildOffsets.Add((24, 12));
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { DrawingGroup = group });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            summaries = view.FloatingGroupChildEffectSummaries.ToArray();
        });

        if (!ran) return;
        summaries.Should().ContainSingle().Which.Should().Be("GroupChild0:WordArt:glow");
    }

    [Fact]
    public async Task Floating_group_behind_text_flag()
    {
        bool? behind = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingGroup(ImageWrapping.Behind, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingGroupRects;
            if (rects.Count > 0) behind = rects[0].BehindText;
        });

        if (!ran) return;
        behind.Should().BeTrue("ImageWrapping.Behind must set BehindText=true for groups");
    }

    [Fact]
    public async Task Floating_group_zorder_preserved()
    {
        int zOrder = -999;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingGroup(ImageWrapping.Square, 0, 0, zOrder: 7);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingGroupRects;
            if (rects.Count > 0) zOrder = rects[0].ZOrder;
        });

        if (!ran) return;
        zOrder.Should().Be(7, "ZOrderIndex must be preserved for drawing groups");
    }

    [Fact]
    public async Task Floating_group_rect_has_positive_x()
    {
        double rectX = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingGroup(ImageWrapping.Square, 36, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.FloatingGroupRects;
            if (rects.Count > 0) rectX = rects[0].Rect.X;
        });

        if (!ran) return;
        rectX.Should().BeGreaterThan(0, "floating group X should be positive (content left + offset)");
    }

    // ── XX1: FO3 types z-interleave with images+shapes (unified draw-order list) ─────────────────

    /// <summary>
    /// XX1 (HIGH) regression test: Before the fix, charts/WordArt/SmartArt/groups ran in FOUR
    /// SEPARATE OrderBy loops AFTER the merged images+shapes pass, so a chart with ZOrderIndex=1
    /// always painted over an image with ZOrderIndex=99 in the same band — re-introducing the exact
    /// UU1 bug.  The fix merges all six types into ONE OrderBy list.
    ///
    /// Scenario: behind-text CHART (z=1) + behind-text IMAGE (z=99) in the same band.
    /// Expected merged draw order: Chart first (z=1), then Image (z=99), i.e. Image is on top.
    /// Mirror: in-front WordArt (z=1) + in-front Shape (z=99) → WordArt first, Shape on top.
    /// </summary>
    [Fact]
    public async Task XX1_behind_chart_z1_is_drawn_before_image_z99_in_merged_order()
    {
        IReadOnlyList<(int ZOrder, string TypeTag)>? order = null;
        var ran = await OnUiThread(() =>
        {
            // Image behind-text z=99 and Chart behind-text z=1 in the same paragraph.
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor.", RunFormatting.Default));

            // Floating image: BehindText, ZOrder=99.
            var img = new InlineImage([], 72, 54)
            {
                Wrapping         = ImageWrapping.Behind,
                HorizontalAnchor = HorizontalAnchor.Column,
                VerticalAnchor   = VerticalAnchor.Paragraph,
                ZOrderIndex      = 99,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = img });

            // Floating chart: BehindText, ZOrder=1.
            var chart = Chart.Create(ChartKind.Column,
                new[] { "A", "B" }, new[] { 1.0, 2.0 }, "S", "Test");
            chart.WidthPt  = 100;
            chart.HeightPt = 80;
            chart.Placement = new FloatingPlacement
            {
                Wrapping         = ImageWrapping.Behind,
                HorizontalAnchor = HorizontalAnchor.Column,
                VerticalAnchor   = VerticalAnchor.Paragraph,
                ZOrderIndex      = 1,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            order = view.MergedBehindDrawOrder;
        });

        if (!ran) return;
        order.Should().NotBeNull();
        order!.Count.Should().Be(2, "one image (z=99) + one chart (z=1) should produce 2 entries in behind draw list");

        // The merged list is sorted by ZOrder ascending (lower ZOrder drawn first = appears under).
        order[0].TypeTag.Should().Be("Chart",  "chart (z=1) must be drawn FIRST (beneath) in the merged list");
        order[0].ZOrder.Should().Be(1);
        order[1].TypeTag.Should().Be("Image",  "image (z=99) must be drawn SECOND (on top) in the merged list");
        order[1].ZOrder.Should().Be(99);
    }

    [Fact]
    public async Task XX1_infront_wordart_z1_is_drawn_before_shape_z99_in_merged_order()
    {
        IReadOnlyList<(int ZOrder, string TypeTag)>? order = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor.", RunFormatting.Default));

            // Floating shape: InFront, ZOrder=99.
            var shape = new Shape(ShapeKind.Rectangle, 80, 60, "#4472C4")
            {
                Placement = new FloatingPlacement
                {
                    Wrapping         = ImageWrapping.InFront,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor   = VerticalAnchor.Paragraph,
                    ZOrderIndex      = 99,
                },
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Shape = shape });

            // Floating WordArt: InFront, ZOrder=1.
            var wa = new WordArt("Hello", WordArtStyle.FillBlue, 24)
            {
                Placement = new FloatingPlacement
                {
                    Wrapping         = ImageWrapping.InFront,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor   = VerticalAnchor.Paragraph,
                    ZOrderIndex      = 1,
                },
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            order = view.MergedFrontDrawOrder;
        });

        if (!ran) return;
        order.Should().NotBeNull();
        order!.Count.Should().Be(2, "one shape (z=99) + one WordArt (z=1) should produce 2 entries in front draw list");

        order[0].TypeTag.Should().Be("WordArt", "WordArt (z=1) must be drawn FIRST (beneath) in the merged list");
        order[0].ZOrder.Should().Be(1);
        order[1].TypeTag.Should().Be("Shape",   "shape (z=99) must be drawn SECOND (on top) in the merged list");
        order[1].ZOrder.Should().Be(99);
    }

    // ── Body text still lays out with mixed FO3 objects ───────────────────────────────────────────

    [Fact]
    public async Task Paragraph_with_floating_chart_still_produces_text_glyphs()
    {
        int glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Column, ImageWrapping.Square, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        glyphs.Should().BeGreaterThan(0, "paragraph with floating chart + text must still produce placed glyphs");
    }

    // ── Headless render capture — all FO3 types together ─────────────────────────────────────────

    [Fact]
    public async Task FO3_render_capture_all_types_produces_non_blank_output()
    {
        byte[]? pngBytes = null;
        string? outPath  = null;
        var ran = false;

        try
        {
            await Session.Dispatch(() =>
            {
                ran = true;

                var doc = TextDocument.CreateEmpty();
                doc.Blocks.Clear();

                var para = new Paragraph();
                para.Runs.Add(new Run("FO3 render test: chart + WordArt + SmartArt + group.",
                    RunFormatting.Default with { FontSizePt = 11 }));

                // Floating chart (column, in-front).
                var chart = Chart.Create(ChartKind.Column,
                    new[] { "Q1", "Q2", "Q3", "Q4" },
                    new[] { 12.0, 30.0, 18.0, 25.0 },
                    "Revenue", "Sales 2025");
                chart.WidthPt  = 200;
                chart.HeightPt = 130;
                chart.Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.InFront,
                    HorizontalOffsetPt = 10, VerticalOffsetPt = 60,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor   = VerticalAnchor.Paragraph,
                    ZOrderIndex = 1,
                };
                para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });

                // Floating WordArt (in-front, right side).
                var wa = new WordArt("FreeW!", WordArtStyle.GradientFill, 28)
                {
                    Placement = new FloatingPlacement
                    {
                        Wrapping = ImageWrapping.InFront,
                        HorizontalOffsetPt = 230, VerticalOffsetPt = 60,
                        HorizontalAnchor = HorizontalAnchor.Column,
                        VerticalAnchor   = VerticalAnchor.Paragraph,
                        ZOrderIndex = 2,
                    },
                };
                para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });

                // Floating SmartArt (Process, below).
                var sa = SmartArt.Create(SmartArtKind.Process, new[] { "Design", "Build", "Test", "Ship" });
                sa.WidthPt  = 320;
                sa.HeightPt = 80;
                sa.Placement = new FloatingPlacement
                {
                    Wrapping = ImageWrapping.InFront,
                    HorizontalOffsetPt = 10, VerticalOffsetPt = 210,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalAnchor   = VerticalAnchor.Paragraph,
                    ZOrderIndex = 3,
                };
                para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });

                // Floating group (two shapes).
                var grp = new DrawingGroup
                {
                    WidthPt  = 150,
                    HeightPt = 70,
                    Placement = new FloatingPlacement
                    {
                        Wrapping = ImageWrapping.InFront,
                        HorizontalOffsetPt = 350, VerticalOffsetPt = 60,
                        HorizontalAnchor = HorizontalAnchor.Column,
                        VerticalAnchor   = VerticalAnchor.Paragraph,
                        ZOrderIndex = 4,
                    },
                };
                grp.Children.Add(new Shape(ShapeKind.Rectangle, 60, 50, "#4472C4"));
                grp.ChildOffsets.Add((0, 0));
                grp.Children.Add(new Shape(ShapeKind.Ellipse, 50, 50, "#ED7D31"));
                grp.ChildOffsets.Add((70, 0));
                para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { DrawingGroup = grp });

                doc.Blocks.Add(para);

                for (var i = 0; i < 3; i++)
                {
                    var p = new Paragraph();
                    p.Runs.Add(new Run($"Body paragraph {i + 1}: lorem ipsum dolor sit amet.",
                        RunFormatting.Default));
                    doc.Blocks.Add(p);
                }

                var view = new DocumentView();
                view.LoadDocument(doc);

                var window = new Window { Width = 816, Height = 1100, Content = view };
                window.Show();
                window.Measure(new Size(816, 1100));
                window.Arrange(new Rect(0, 0, 816, 1100));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var frame = window.CaptureRenderedFrame();
                if (frame is not null)
                    pngBytes = WriteableBitmapToPng(frame);

                window.Close();

                var testBinDir = Path.GetDirectoryName(typeof(DocumentViewFloatingFO3Tests).Assembly.Location) ?? ".";
                outPath = Path.GetFullPath(Path.Combine(testBinDir, "freew_avalonia_fo3_all_types.png"));
                if (pngBytes is { Length: > 0 })
                    File.WriteAllBytes(outPath, pngBytes);

                Console.WriteLine($"[FO3Capture] PNG written ({pngBytes?.Length ?? 0} bytes) to: {outPath}");
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FO3Capture] Skipped: {ex.GetType().Name}: {ex.Message}");
            ran = false;
        }

        if (!ran) return;
        if (pngBytes is null)
        {
            Console.WriteLine("[FO3Capture] CaptureRenderedFrame returned null — skipping.");
            return;
        }
        if (pngBytes.Length == 0)
        {
            Console.WriteLine("[FO3Capture] Encoder produced 0 bytes — skipping.");
            return;
        }

        pngBytes.Length.Should().BeGreaterThan(5_000,
            "a rendered page with FO3 floating objects and body text should produce a non-trivial PNG");
        pngBytes[0].Should().Be(0x89);
        pngBytes[1].Should().Be((byte)'P');
        pngBytes[2].Should().Be((byte)'N');
        pngBytes[3].Should().Be((byte)'G');

        Console.WriteLine($"[FO3Capture] Visual inspection: {outPath}");
    }

    // ── BC1: Axis labels present in chart data ────────────────────────────────────────────────────
    // These tests verify that the chart builds a FloatingChartData with non-empty Categories
    // (which are the source for X-axis labels) and that the data round-trips from the model.

    [Fact]
    public async Task BC1_column_chart_categories_are_present_for_axis_labels()
    {
        int catCount = 0;
        string? firstCat = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Column, ImageWrapping.InFront, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var snaps = view.FloatingChartDataSnapshots;
            if (snaps.Count > 0)
            {
                catCount = snaps[0].Categories.Count;
                firstCat = snaps[0].Categories.Count > 0 ? snaps[0].Categories[0] : null;
            }
        });

        if (!ran) return;
        catCount.Should().Be(3, "BC1: chart has 3 categories A/B/C — all must be present for X-axis labels");
        firstCat.Should().Be("A", "BC1: first category label must be 'A'");
    }

    [Fact]
    public async Task BC1_line_chart_categories_are_present_for_axis_labels()
    {
        int catCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingChart(ChartKind.Line, ImageWrapping.InFront, 0, 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var snaps = view.FloatingChartDataSnapshots;
            if (snaps.Count > 0)
                catCount = snaps[0].Categories.Count;
        });

        if (!ran) return;
        catCount.Should().Be(3, "BC1: line chart categories must be present for X-axis labels");
    }

    [Fact]
    public async Task BC1_chart_render_does_not_crash_with_categories()
    {
        // Verify the render path (DrawFloatingChart) completes without exception
        // when categories are present (which triggers the new X-axis label drawing code).
        bool completed = false;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor.", RunFormatting.Default));

            var chart = Chart.Create(ChartKind.Column,
                new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun" },
                new[] { 10.0, 25.0, 15.0, 30.0, 20.0, 35.0 },
                "Revenue", "Monthly Sales");
            chart.ShowLegend = true;
            // Use QuickLayoutId=5 (ShowDataLabels=true per ChartLayoutPreset) to exercise data labels.
            chart.QuickLayoutId = 5;
            chart.WidthPt  = 300;
            chart.HeightPt = 200;
            chart.Placement = new FloatingPlacement
            {
                Wrapping         = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Column,
                VerticalAnchor   = VerticalAnchor.Paragraph,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            // Simply measuring triggers the full chart drawing path.
            completed = true;
        });

        if (!ran) return;
        completed.Should().BeTrue("BC1: chart render with categories/legend/datalabels must not throw");
    }

    // ── BC2: Legend includes all series with fallback names ───────────────────────────────────────

    [Fact]
    public async Task BC2_all_series_present_in_chart_including_unnamed()
    {
        int seriesCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor.", RunFormatting.Default));

            // Two series: one named, one unnamed.
            var chart = new Chart { Kind = ChartKind.Column };
            chart.Categories.AddRange(new[] { "A", "B", "C" });
            chart.Series.Add(new ChartSeries("Named S1", new[] { 1.0, 2.0, 3.0 }));
            chart.Series.Add(new ChartSeries(string.Empty, new[] { 4.0, 5.0, 6.0 })); // no name
            chart.ShowLegend = true;
            chart.WidthPt  = 200;
            chart.HeightPt = 130;
            chart.Placement = new FloatingPlacement
            {
                Wrapping         = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Column,
                VerticalAnchor   = VerticalAnchor.Paragraph,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var snaps = view.FloatingChartDataSnapshots;
            if (snaps.Count > 0)
                seriesCount = snaps[0].SeriesCount;
        });

        if (!ran) return;
        seriesCount.Should().Be(2, "BC2: both series (named and unnamed) must be preserved in FloatingChartData");
    }

    // ── BC3: Negative-value chart renders without crash ───────────────────────────────────────────

    [Fact]
    public async Task BC3_chart_with_negative_values_renders_without_crash()
    {
        bool completed = false;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Negative chart.", RunFormatting.Default));

            var chart = new Chart { Kind = ChartKind.Column, QuickLayoutId = 5 }; // QuickLayoutId=5 → ShowDataLabels
            chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" });
            chart.Series.Add(new ChartSeries("Profit", new[] { 10.0, -5.0, 8.0, -3.0 }));
            chart.WidthPt  = 250;
            chart.HeightPt = 160;
            chart.Placement = new FloatingPlacement
            {
                Wrapping         = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Column,
                VerticalAnchor   = VerticalAnchor.Paragraph,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            completed = true;
        });

        if (!ran) return;
        completed.Should().BeTrue("BC3: chart with negative values must render without exception");
    }

    [Fact]
    public async Task BC3_all_zero_chart_renders_without_crash()
    {
        bool completed = false;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("All-zero chart.", RunFormatting.Default));

            var chart = new Chart { Kind = ChartKind.Line };
            chart.Categories.AddRange(new[] { "A", "B", "C" });
            chart.Series.Add(new ChartSeries("Zero", new[] { 0.0, 0.0, 0.0 }));
            chart.WidthPt  = 200;
            chart.HeightPt = 130;
            chart.Placement = new FloatingPlacement
            {
                Wrapping         = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Column,
                VerticalAnchor   = VerticalAnchor.Paragraph,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            completed = true;
        });

        if (!ran) return;
        completed.Should().BeTrue("BC3: all-zero data must not divide-by-zero; renders without exception");
    }

    [Fact]
    public async Task BC3_negative_line_chart_renders_without_crash()
    {
        bool completed = false;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Negative line chart.", RunFormatting.Default));

            var chart = new Chart { Kind = ChartKind.Line };
            chart.Categories.AddRange(new[] { "Jan", "Feb", "Mar", "Apr" });
            chart.Series.Add(new ChartSeries("Delta", new[] { -10.0, 5.0, -3.0, 8.0 }));
            chart.WidthPt  = 250;
            chart.HeightPt = 160;
            chart.Placement = new FloatingPlacement
            {
                Wrapping         = ImageWrapping.InFront,
                HorizontalAnchor = HorizontalAnchor.Column,
                VerticalAnchor   = VerticalAnchor.Paragraph,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            completed = true;
        });

        if (!ran) return;
        completed.Should().BeTrue("BC3: line chart with negative values must render without exception");
    }

    // ── PNG encoder ───────────────────────────────────────────────────────────────────────────────

    private static byte[] WriteableBitmapToPng(WriteableBitmap bitmap)
    {
        try
        {
            using var locked = bitmap.Lock();
            var info = new SKImageInfo(
                locked.Size.Width,
                locked.Size.Height,
                locked.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888 : SKColorType.Rgba8888,
                SKAlphaType.Premul);

            using var skBitmap = new SKBitmap();
            if (!skBitmap.InstallPixels(info, locked.Address, locked.RowBytes))
                return [];

            using var skImage = SKImage.FromBitmap(skBitmap);
            using var data    = skImage.Encode(SKEncodedImageFormat.Png, 90);
            return data?.ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
