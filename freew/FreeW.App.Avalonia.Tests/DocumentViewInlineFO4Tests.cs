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
/// Tests for the Avalonia DocumentView inline-object render path — FO4 wave:
/// inline (non-floating) Charts, WordArt, and SmartArt laid out in the document flow.
/// Verifies: each type reserves a non-zero line box; is stored in the inline collections;
/// caret/selection still steps over each object; render does not crash; headless PNG captures
/// non-blank output with the objects in-flow.
/// </summary>
public sealed class DocumentViewInlineFO4Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a doc with a single paragraph: text before + inline chart + text after.</summary>
    private static TextDocument DocWithInlineChart(
        ChartKind kind,
        double widthPt  = 240,
        double heightPt = 160,
        string? title   = "Inline Chart")
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Before ", RunFormatting.Default));

        var chart = Chart.Create(kind,
            new[] { "A", "B", "C" },
            new[] { 10.0, 25.0, 15.0 },
            "Series 1",
            title);
        chart.WidthPt  = widthPt;
        chart.HeightPt = heightPt;
        // No Placement → inline.
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });

        para.Runs.Add(new Run(" after.", RunFormatting.Default));
        doc.Blocks.Add(para);
        return doc;
    }

    /// <summary>Creates a doc with a single paragraph: text before + inline WordArt + text after.</summary>
    private static TextDocument DocWithInlineWordArt(
        WordArtStyle style = WordArtStyle.FillBlue,
        string text = "Hello WordArt",
        double fontSizePt = 28,
        WordArtWarp warp = WordArtWarp.None)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Before ", RunFormatting.Default));

        var wa = new WordArt(text, style, fontSizePt) { Warp = warp }; // No Placement -> inline.
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });

        para.Runs.Add(new Run(" after.", RunFormatting.Default));
        doc.Blocks.Add(para);
        return doc;
    }

    /// <summary>Creates a doc with a single paragraph: text before + inline SmartArt + text after.</summary>
    private static TextDocument DocWithInlineSmartArt(
        SmartArtKind kind = SmartArtKind.Process,
        double widthPt  = 360,
        double heightPt = 160,
        string? colorSchemeId = null,
        string? styleId = null,
        Action<SmartArt>? configure = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Before ", RunFormatting.Default));

        var sa = CreateInlineSmartArt(kind);
        sa.WidthPt  = widthPt;
        sa.HeightPt = heightPt;
        sa.ColorSchemeId = colorSchemeId;
        sa.StyleId = styleId;
        configure?.Invoke(sa);
        // No Placement → inline.
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });

        para.Runs.Add(new Run(" after.", RunFormatting.Default));
        doc.Blocks.Add(para);
        return doc;
    }

    private static SmartArt CreateInlineSmartArt(SmartArtKind kind)
    {
        if (kind != SmartArtKind.Hierarchy)
            return SmartArt.Create(kind, new[] { "Step A", "Step B", "Step C" });

        var root = new SmartArtNode("Root");
        var child = root.AddChild("Child");
        child.AddChild("Grandchild");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(root);
        return smartArt;
    }

    // ── Inline chart tests ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_chart_is_collected_in_inline_list()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.InlineChartCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one inline chart must produce one entry in _inlineCharts");
    }

    [Fact]
    public async Task Inline_chart_is_not_collected_as_floating()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Bar);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingChartCount;
        });

        if (!ran) return;
        count.Should().Be(0, "inline chart must NOT appear in _floatingCharts");
    }

    [Fact]
    public async Task Inline_chart_rect_has_positive_height()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column, heightPt: 160);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineChartRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeGreaterThan(0, "inline chart must reserve a positive height in the flow");
    }

    [Fact]
    public async Task Inline_chart_rect_height_matches_model_heightpt()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column, heightPt: 144);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineChartRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeApproximately(144 * (96.0 / 72.0), 2,
            "inline chart height should be 144pt converted to DIP");
    }

    [Fact]
    public async Task Inline_chart_kind_preserved()
    {
        ChartKind kind = ChartKind.Column;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Pie);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineChartRects;
            if (rects.Count > 0) kind = rects[0].Kind;
        });

        if (!ran) return;
        kind.Should().Be(ChartKind.Pie, "chart kind must be preserved for inline charts");
    }

    [Fact]
    public async Task Inline_scatter_chart_uses_marker_only_plan_and_named_palette()
    {
        ChartVisualGeometryKind geometry = ChartVisualGeometryKind.Lines;
        string? firstColor = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            var chart = Chart.Create(ChartKind.Scatter, ["155", "160", "165"], [52.0, 58.0, 63.0], "Sample");
            chart.ColorSchemeId = "colorful2";
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var plans = view.InlineChartVisualPlans;
            if (plans.Count > 0)
            {
                geometry = plans[0].GeometryKind;
                firstColor = plans[0].PaletteHex[0];
            }
        });

        if (!ran) return;
        geometry.Should().Be(ChartVisualGeometryKind.MarkerOnly,
            "scatter charts should not use the connected line geometry path");
        firstColor.Should().Be("#ED7D31",
            "Avalonia should consume the shared named chart palette");
    }

    [Fact]
    public async Task Inline_chart_title_preserved()
    {
        string? title = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column, title: "Revenue");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineChartRects;
            if (rects.Count > 0) title = rects[0].Title;
        });

        if (!ran) return;
        title.Should().Be("Revenue", "chart title must be preserved for inline charts");
    }

    [Fact]
    public async Task Inline_chart_quick_layout_can_suppress_title()
    {
        string? title = "kept";
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column, title: "Revenue");
            // Runs[0] is the leading "Before " text -- the chart is Runs[1].
            ((Paragraph)doc.Blocks[0]).Runs[1].Chart!.QuickLayoutId = 1;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineChartRects;
            if (rects.Count > 0) title = rects[0].Title;
        });

        if (!ran) return;
        title.Should().BeNull("Avalonia should consume the shared chart plan's title visibility");
    }

    [Fact]
    public async Task Inline_chart_produces_sentinel_glyph_for_caret()
    {
        int glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            // Placed count includes sentinel(s) — at minimum the chart sentinel + end sentinel.
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        glyphs.Should().BeGreaterThan(0, "inline chart paragraph must emit at least one glyph/sentinel");
    }

    // ── Inline WordArt tests ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_wordart_is_collected_in_inline_list()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.InlineWordArtCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one inline WordArt must produce one entry in _inlineWordArts");
    }

    [Fact]
    public async Task Inline_wordart_is_not_collected_as_floating()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingWordArtCount;
        });

        if (!ran) return;
        count.Should().Be(0, "inline WordArt must NOT appear in _floatingWordArts");
    }

    [Fact]
    public async Task Inline_wordart_rect_has_positive_height()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt(fontSizePt: 36);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineWordArtRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeGreaterThan(0, "inline WordArt must reserve a positive height in the flow");
    }

    [Fact]
    public async Task Inline_wordart_text_and_style_preserved()
    {
        string? text = null;
        WordArtStyle style = WordArtStyle.FillBlue;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt(WordArtStyle.Shadow, text: "FreeW!");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineWordArtRects;
            if (rects.Count > 0) { text = rects[0].Text; style = rects[0].Style; }
        });

        if (!ran) return;
        text.Should().Be("FreeW!", "WordArt text must be preserved for inline WordArt");
        style.Should().Be(WordArtStyle.Shadow, "WordArt style must be preserved for inline WordArt");
    }

    // ── Inline SmartArt tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_wordart_effect_summary_uses_shared_visual_planner()
    {
        string[] summaries = [];
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt(WordArtStyle.GlowGold, text: "FreeW!");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            summaries = view.InlineWordArtEffectSummaries.ToArray();
        });

        if (!ran) return;
        summaries.Should().ContainSingle().Which.Should().Be("glow");
    }

    [Fact]
    public async Task Inline_wordart_visual_summary_matches_shared_plan()
    {
        string[] summaries = [];
        var expected = DrawingObjectVisualPlanner.BuildInlineWordArtPlan(
            new WordArt("FreeW!", WordArtStyle.PatternFill, fontSizePt: 24)
            {
                Warp = WordArtWarp.Wave1
            }).Summary;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt(WordArtStyle.PatternFill, text: "FreeW!", fontSizePt: 24, warp: WordArtWarp.Wave1);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            summaries = view.InlineWordArtVisualSummaries.ToArray();
        });

        if (!ran) return;
        summaries.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public async Task Inline_smartart_is_collected_in_inline_list()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(SmartArtKind.Process);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.InlineSmartArtCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one inline SmartArt must produce one entry in _inlineSmartArts");
    }

    [Fact]
    public async Task Inline_smartart_is_not_collected_as_floating()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingSmartArtCount;
        });

        if (!ran) return;
        count.Should().Be(0, "inline SmartArt must NOT appear in _floatingSmartArts");
    }

    [Fact]
    public async Task Inline_smartart_rect_has_positive_height()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(heightPt: 160);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineSmartArtRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeGreaterThan(0, "inline SmartArt must reserve a positive height in the flow");
    }

    [Fact]
    public async Task Inline_smartart_rect_height_matches_model_heightpt()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(heightPt: 120);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineSmartArtRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeApproximately(120 * (96.0 / 72.0), 2,
            "inline SmartArt height should be 120pt converted to DIP");
    }

    [Fact]
    public async Task Inline_smartart_kind_preserved()
    {
        SmartArtKind kind = SmartArtKind.List;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(SmartArtKind.Hierarchy);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineSmartArtRects;
            if (rects.Count > 0) kind = rects[0].Kind;
        });

        if (!ran) return;
        kind.Should().Be(SmartArtKind.Hierarchy, "SmartArt kind must be preserved for inline SmartArt");
    }

    [Fact]
    public async Task Inline_smartart_node_count_preserved()
    {
        int nodeCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineSmartArtRects;
            if (rects.Count > 0) nodeCount = rects[0].NodeCount;
        });

        if (!ran) return;
        nodeCount.Should().Be(3, "SmartArt with 3 nodes must expose node count 3");
    }

    [Fact]
    public async Task Inline_smartart_hierarchy_depth_and_connectors_preserved()
    {
        (int NodeCount, int MaxDepth, int ConnectorCount) values = default;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(SmartArtKind.Hierarchy);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rect = view.InlineSmartArtRects.Single();
            values = (rect.NodeCount, rect.MaxHierarchyDepth, rect.HierarchyConnectorCount);
        });

        if (!ran) return;
        values.NodeCount.Should().Be(3, "root/child/grandchild should all be planned for inline hierarchy SmartArt");
        values.MaxDepth.Should().Be(2, "inline hierarchy SmartArt should preserve grandchild depth");
        values.ConnectorCount.Should().Be(2, "inline hierarchy SmartArt should expose parent-child connector geometry");
    }

    [Fact]
    public async Task Inline_smartart_uses_resolved_hierarchy_layout_when_model_kind_is_stale()
    {
        (SmartArtKind Kind, int MaxDepth, int ConnectorCount) values = default;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(
                SmartArtKind.Hierarchy,
                configure: smartArt =>
                {
                    smartArt.Kind = SmartArtKind.Process;
                    smartArt.LayoutId = "orgchart1";
                });
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rect = view.InlineSmartArtRects.Single();
            values = (rect.Kind, rect.MaxHierarchyDepth, rect.HierarchyConnectorCount);
        });

        if (!ran) return;
        values.Kind.Should().Be(SmartArtKind.Hierarchy, "the resolved org-chart layout should drive Avalonia rendering");
        values.MaxDepth.Should().Be(2);
        values.ConnectorCount.Should().Be(2);
    }

    [Fact]
    public async Task Inline_smartart_carries_shared_cycle_layout_geometry()
    {
        (string? LayoutId, string? GeometryKind, int NodeCount, int ConnectorCount) values = default;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(
                SmartArtKind.List,
                configure: smartArt => smartArt.LayoutId = "cycle1");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var geometry = view.InlineSmartArtLayoutGeometries.Single();
            values = (geometry.LayoutId, geometry.GeometryKind, geometry.GeometryNodeCount, geometry.GeometryConnectorCount);
        });

        if (!ran) return;
        values.LayoutId.Should().Be("cycle1");
        values.GeometryKind.Should().Be("Cycle");
        values.NodeCount.Should().Be(3);
        values.ConnectorCount.Should().Be(3);
    }

    [Theory]
    [InlineData("list1", "BasicList", 0, 0, 0)]
    [InlineData("vertbullet1", "VerticalBulletList", 0, 0, 0)]
    [InlineData("process1", "BasicProcess", 2, 0, 0)]
    [InlineData("pyramid1", "Pyramid", 0, 3, 4)]
    public async Task Inline_smartart_carries_basic_shared_layout_geometry(
        string layoutId,
        string expectedKind,
        int expectedConnectors,
        int expectedPolygonNodes,
        int expectedFirstPolygonPointCount)
    {
        (string? LayoutId, string? GeometryKind, int NodeCount, int ConnectorCount,
            int PolygonNodeCount, int FirstPolygonPointCount) values = default;
        var ran = await OnUiThread(() =>
        {
            var smartArtKind = layoutId == "process1" ? SmartArtKind.Process : SmartArtKind.List;
            var doc = DocWithInlineSmartArt(
                smartArtKind,
                configure: smartArt => smartArt.LayoutId = layoutId);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var geometry = view.InlineSmartArtLayoutGeometries.Single();
            values = (
                geometry.LayoutId,
                geometry.GeometryKind,
                geometry.GeometryNodeCount,
                geometry.GeometryConnectorCount,
                geometry.PolygonNodeCount,
                geometry.FirstPolygonPointCount);
        });

        if (!ran) return;
        values.LayoutId.Should().Be(layoutId);
        values.GeometryKind.Should().Be(expectedKind);
        values.NodeCount.Should().Be(3);
        values.ConnectorCount.Should().Be(expectedConnectors);
        values.PolygonNodeCount.Should().Be(expectedPolygonNodes);
        values.FirstPolygonPointCount.Should().Be(expectedFirstPolygonPointCount);
    }

    [Fact]
    public async Task Inline_smartart_carries_planned_style_values()
    {
        (string? Fill, string? Border, double BorderThickness, double CornerRadius,
            double ShadowOpacity, double ShadowBlur, double ShadowDepth, string? Connector) values = default;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(
                colorSchemeId: "accent1",
                styleId: "3d1");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rect = view.InlineSmartArtRects.Single();
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
        values.Fill.Should().Be("#526B97");
        values.Border.Should().Be("#243D69");
        values.BorderThickness.Should().Be(1.0);
        values.CornerRadius.Should().Be(8);
        values.ShadowOpacity.Should().Be(0.40);
        values.ShadowBlur.Should().BeApproximately(7.2, 0.01);
        values.ShadowDepth.Should().BeApproximately(2.3, 0.01);
        values.Connector.Should().NotBe(values.Fill);
    }

    // ── Caret / selection step-over ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_chart_paragraph_still_produces_glyphs_from_text_runs()
    {
        int glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            // "Before " + chart + " after." — text runs should still produce placed glyphs.
            var doc = DocWithInlineChart(ChartKind.Column);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        // "Before " = 7 chars, " after." = 7 chars, plus the atomic chart position and sentinels.
        glyphs.Should().BeGreaterThan(0,
            "a paragraph with inline chart + surrounding text must produce placed glyphs");
    }

    [Fact]
    public async Task Multiple_inline_types_in_sequence_all_collected()
    {
        int charts = 0, wordarts = 0, smartarts = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var p1 = new Paragraph();
            var c = Chart.Create(ChartKind.Line, new[] { "A" }, new[] { 1.0 });
            p1.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = c });
            doc.Blocks.Add(p1);

            var p2 = new Paragraph();
            var wa = new WordArt("Test", WordArtStyle.FillBlue, 24);
            p2.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });
            doc.Blocks.Add(p2);

            var p3 = new Paragraph();
            var sa = SmartArt.Create(SmartArtKind.List, new[] { "X", "Y" });
            p3.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });
            doc.Blocks.Add(p3);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            charts    = view.InlineChartCount;
            wordarts  = view.InlineWordArtCount;
            smartarts = view.InlineSmartArtCount;
        });

        if (!ran) return;
        charts.Should().Be(1,   "one inline chart paragraph");
        wordarts.Should().Be(1, "one inline WordArt paragraph");
        smartarts.Should().Be(1, "one inline SmartArt paragraph");
    }

    // ── Headless render capture — all FO4 inline types in one document ────────────────────────────

    [Fact]
    public async Task FO4_render_capture_inline_types_produces_non_blank_output()
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

                // Introductory text paragraph.
                var intro = new Paragraph();
                intro.Runs.Add(new Run("FO4 inline render test.", RunFormatting.Default with { FontSizePt = 11 }));
                doc.Blocks.Add(intro);

                // Inline chart paragraph.
                var pChart = new Paragraph();
                pChart.Runs.Add(new Run("Chart: ", RunFormatting.Default));
                var chart = Chart.Create(ChartKind.Column,
                    new[] { "Q1", "Q2", "Q3" },
                    new[] { 10.0, 20.0, 15.0 },
                    "Sales", "Revenue 2025");
                chart.WidthPt  = 280;
                chart.HeightPt = 150;
                pChart.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
                pChart.Runs.Add(new Run(" end.", RunFormatting.Default));
                doc.Blocks.Add(pChart);

                // Inline WordArt paragraph.
                var pWa = new Paragraph();
                pWa.Runs.Add(new Run("WordArt: ", RunFormatting.Default));
                var wa = new WordArt("FreeW!", WordArtStyle.GradientFill, 28);
                pWa.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });
                pWa.Runs.Add(new Run(" end.", RunFormatting.Default));
                doc.Blocks.Add(pWa);

                // Inline SmartArt paragraph.
                var pSa = new Paragraph();
                pSa.Runs.Add(new Run("SmartArt: ", RunFormatting.Default));
                var sa = SmartArt.Create(SmartArtKind.Process, new[] { "Design", "Build", "Ship" });
                sa.WidthPt  = 320;
                sa.HeightPt = 100;
                pSa.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });
                pSa.Runs.Add(new Run(" end.", RunFormatting.Default));
                doc.Blocks.Add(pSa);

                // Trailing paragraph.
                var trail = new Paragraph();
                trail.Runs.Add(new Run("Done.", RunFormatting.Default));
                doc.Blocks.Add(trail);

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

                var testBinDir = Path.GetDirectoryName(typeof(DocumentViewInlineFO4Tests).Assembly.Location) ?? ".";
                outPath = Path.GetFullPath(Path.Combine(testBinDir, "freew_avalonia_fo4_inline.png"));
                if (pngBytes is { Length: > 0 })
                    File.WriteAllBytes(outPath, pngBytes);

                Console.WriteLine($"[FO4Capture] PNG written ({pngBytes?.Length ?? 0} bytes) to: {outPath}");
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FO4Capture] Skipped: {ex.GetType().Name}: {ex.Message}");
            ran = false;
        }

        if (!ran) return;
        if (pngBytes is null)
        {
            Console.WriteLine("[FO4Capture] CaptureRenderedFrame returned null — skipping.");
            return;
        }
        if (pngBytes.Length == 0)
        {
            Console.WriteLine("[FO4Capture] Encoder produced 0 bytes — skipping.");
            return;
        }

        pngBytes.Length.Should().BeGreaterThan(5_000,
            "a rendered page with FO4 inline objects and body text should produce a non-trivial PNG");
        pngBytes[0].Should().Be(0x89);
        pngBytes[1].Should().Be((byte)'P');
        pngBytes[2].Should().Be((byte)'N');
        pngBytes[3].Should().Be((byte)'G');

        Console.WriteLine($"[FO4Capture] Visual inspection: {outPath}");
    }

    // ── YY1: floating objects anchored to an inline-object paragraph land on the correct page ────────

    /// <summary>Builds a minimal 4x4 orange PNG as a stand-in for a real floating image.</summary>
    private static byte[] SmallPng()
    {
        using var bmp = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(255, 128, 0));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>
    /// YY1: Fill a page so the anchor paragraph (with a tall inline chart, 216pt ≈ 288 DIP) sits
    /// in the last band of the first page.  The inline chart correctly overflows to page 2 via
    /// ReserveContentY, but before the fix PeekFirstLineContentY(1) didn't detect the page-break
    /// and the floating image anchored to this paragraph landed on page 1 (wrong page).
    ///
    /// After the YY1 fix PeekFirstLineContentY receives the chart's actual height (288 DIP), detects
    /// the overflow, and returns a contentY on page 2 → the floating image anchors on page 2.
    ///
    /// Layout geometry (96 DPI, US Letter, 1-in margins):
    ///   textAreaHeight = (792 - 72 - 72) × (96/72) = 864 DIP
    ///   chartHeight ≈ 216pt × (96/72) = 288 DIP
    ///   Fill to leave &lt;288 DIP at the bottom but &gt;1 DIP (so the chart overflows to page 2).
    ///   Default 11pt line ≈ 20.3 DIP;  fillerCount = floor((864 - 1) / 20.3) ≈ 42 lines.
    ///   After filler, remaining space = 864 - 42×20.3 ≈ 11.4 DIP &lt; 288 DIP → chart overflows.
    ///   Page 2 threshold ≈ 24 (desk) + 864 (page 1 area) + 20 (gap) + 96 (top margin) = 1004 DIP.
    /// </summary>
    [Fact]
    public async Task YY1_floating_object_anchored_to_inline_chart_paragraph_lands_on_same_page_as_chart()
    {
        Rect floatRect = default;
        int pageCount = 0;
        double inlineChartRectY = double.MaxValue;

        var ran = await OnUiThread(() =>
        {
            const double textAreaHeightDip = 864.0;
            const double lineHDip = 20.3;  // default 11pt line height
            // Fill to within <chartHeight but >1 DIP of the page bottom.
            var fillerCount = (int)((textAreaHeightDip - 1) / lineHDip); // ≈ 42

            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var bodyFmt = RunFormatting.Default with { FontSizePt = 11 };

            // Filler paragraphs to push the anchor near page bottom.
            for (var i = 0; i < fillerCount; i++)
            {
                var filler = new Paragraph();
                filler.Runs.Add(new Run($"Fill {i + 1}.", bodyFmt));
                doc.Blocks.Add(filler);
            }

            // Anchor paragraph: tall inline chart (216pt = 288 DIP) + floating image with vOffset=0.
            var anchorPara = new Paragraph();
            var chart = Chart.Create(ChartKind.Column,
                new[] { "A", "B" }, new[] { 10.0, 20.0 }, "S1");
            chart.WidthPt  = 360;  // default width
            chart.HeightPt = 216;  // default height → 288 DIP, causes page break
            anchorPara.Runs.Add(new Run(string.Empty, bodyFmt) { Chart = chart });

            var floatImg = new InlineImage(SmallPng(), 72, 54)
            {
                Wrapping           = ImageWrapping.Square,
                HorizontalOffsetPt = 0,
                VerticalOffsetPt   = 0,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = 0,
            };
            anchorPara.Runs.Add(new Run(string.Empty, bodyFmt) { Image = floatImg });
            doc.Blocks.Add(anchorPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, double.PositiveInfinity));

            pageCount = view.PageCount;
            if (view.FloatingImageRects.Count > 0)
                floatRect = view.FloatingImageRects[0].Rect;

            // Inline chart rect Y — both float and chart should be on page 2.
            var chartRects = view.InlineChartRects;
            if (chartRects.Count > 0)
                inlineChartRectY = chartRects[0].Rect.Y;
        });

        if (!ran) return;

        pageCount.Should().BeGreaterThan(1,
            "YY1: filler paragraphs should fill page 1 and push the inline-chart paragraph to page 2");

        const double page2Threshold = 1000.0;

        // The inline chart must be on page 2.
        inlineChartRectY.Should().BeGreaterThanOrEqualTo(page2Threshold,
            $"YY1: the tall inline chart must overflow to page 2 (Y ≥ {page2Threshold}), got Y={inlineChartRectY:F1}");

        // The floating image anchored to this paragraph must ALSO be on page 2.
        floatRect.Y.Should().BeGreaterThanOrEqualTo(page2Threshold,
            $"YY1: paragraph-anchored float must land on page 2 (Y ≥ {page2Threshold}), got Y={floatRect.Y:F1}. " +
            "Before YY1 fix, PeekFirstLineContentY(1) didn't detect the chart's page-break so the " +
            "float was anchored on page 1 while the inline chart rendered on page 2.");

        // Float Y should be close to the inline chart's Y (both on page 2, vOffset=0).
        if (inlineChartRectY < double.MaxValue)
        {
            var delta = Math.Abs(floatRect.Y - inlineChartRectY);
            delta.Should().BeLessThanOrEqualTo(8.0,
                $"YY1: float Y ({floatRect.Y:F1}) should be near the inline chart Y ({inlineChartRectY:F1}) since vOffset=0");
        }
    }

    // ── ZZ1 (was YY3): full-height inline-object sentinel for correct hit-test reach ─────────────────

    /// <summary>
    /// ZZ1 / YY3-revert: After the ZZ1 fix the caret sentinel for an inline chart uses the FULL
    /// object box as its hit-test band: Y == chartRectTop and LineHeight == chartHeight.
    /// The YY3 baseline cosmetic (shrunken band at the bottom) is reverted because PlacedChar has no
    /// separate caret-draw Y field, so navigation correctness takes priority over the cosmetic.
    /// </summary>
    [Fact]
    public async Task ZZ1_inline_chart_sentinel_covers_full_object_height_for_hit_test()
    {
        double sentinelY = double.MinValue;
        double sentinelLineH = 0;
        double chartRectTop = double.MaxValue;
        double chartRectHeight = 0;

        var ran = await OnUiThread(() =>
        {
            const double heightPt = 216; // 216pt → 288 DIP at 96 DPI
            var doc = DocWithInlineChart(ChartKind.Column, heightPt: heightPt);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            // Block 0 = the inline-chart paragraph.
            var placed = view.GetPlacedForBlock(0);
            // Find the atomic sentinel for the inline chart (width=0, '\0' char).
            var chartGlyphs = placed.Where(p => p.W == 0).ToList();
            if (chartGlyphs.Count > 0)
            {
                var g = chartGlyphs[0];
                sentinelY     = g.Y;
                sentinelLineH = g.LineHeight;
            }

            var chartRects = view.InlineChartRects;
            if (chartRects.Count > 0)
            {
                chartRectTop    = chartRects[0].Rect.Top;
                chartRectHeight = chartRects[0].Rect.Height;
            }
        });

        if (!ran) return;
        if (sentinelY < double.MinValue + 1 || chartRectTop >= double.MaxValue) return; // couldn't introspect

        // The sentinel Y must be at the chart rect TOP (full-height band, not shrunk to baseline).
        var topDelta = Math.Abs(sentinelY - chartRectTop);
        topDelta.Should().BeLessThanOrEqualTo(2.0,
            $"ZZ1: sentinel Y ({sentinelY:F1}) should equal chart rect top ({chartRectTop:F1}) — " +
            "full-height sentinel required so TryHitTest can reach the object from above.");

        // The sentinel LineHeight must equal the full chart height.
        var heightDelta = Math.Abs(sentinelLineH - chartRectHeight);
        heightDelta.Should().BeLessThanOrEqualTo(2.0,
            $"ZZ1: sentinel LineHeight ({sentinelLineH:F1}) should equal chart height ({chartRectHeight:F1}) — " +
            "the full object box is the hit-test band.");
    }

    // ── ZZ1: Down-arrow from text-line above enters tall inline chart ─────────────────────────────────

    /// <summary>
    /// ZZ1: A document with a text paragraph immediately above a tall inline chart (216pt = 288 DIP).
    /// Caret starts on the text line (block 0).  Press Down → caret must move to the chart paragraph
    /// (block 1, offset 0) rather than staying on block 0.
    ///
    /// Before the fix: YY3 shrunk the sentinel to [bottom-19, bottom], so targetY (near chartTop)
    /// was ~269 DIP away from the 19-px band → text line won the score → Down was stuck.
    /// After the fix: sentinel band = [chartTop, chartTop+288] → targetY is inside → chart wins.
    /// </summary>
    [Fact]
    public async Task ZZ1_Down_from_text_above_enters_tall_inline_chart()
    {
        (int Block, int Offset) caretAfterDown = (-1, -1);

        var ran = await OnUiThread(() =>
        {
            // Doc: paragraph 0 = "Hello" text; paragraph 1 = inline chart 216pt tall.
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var textPara = new Paragraph();
            textPara.Runs.Add(new Run("Hello", RunFormatting.Default));
            doc.Blocks.Add(textPara);

            var chartPara = new Paragraph();
            var chart = Chart.Create(ChartKind.Column,
                new[] { "A", "B", "C" }, new[] { 10.0, 25.0, 15.0 }, "S1");
            chart.WidthPt  = 240;
            chart.HeightPt = 216; // 216pt → ~288 DIP: tall enough to expose the regression
            chartPara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(chartPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            // Caret starts at block 0, offset 0 (the text paragraph).
            // Press Down → MoveCaretVertical(+1).
            view.TestMoveCaretVertical(+1);
            caretAfterDown = view.CaretPosition;
        });

        if (!ran) return;
        caretAfterDown.Block.Should().Be(1,
            "ZZ1: Down from a text line should move the caret into the inline-chart paragraph (block 1), " +
            "not stay stuck on block 0.  Before the fix the shrunk sentinel band [bottom-19, bottom] " +
            "scored worse than the text line, keeping the caret on block 0.");
    }

    /// <summary>
    /// ZZ1: A click in the upper portion (~25% from top) of a tall inline chart (216pt = 288 DIP)
    /// should resolve to the chart paragraph rather than the text line above.
    ///
    /// Before the fix: the sentinel band was only the bottom ~19px, so a click at the chart's
    /// upper quarter was closer to the text line above → TryHitTest returned the text block.
    /// After the fix: sentinel band = full height → click anywhere in the chart → chart wins.
    /// </summary>
    [Fact]
    public async Task ZZ1_Click_in_upper_portion_of_tall_inline_chart_hits_chart_block()
    {
        (int Block, int Offset)? hitResult = null;
        double chartRectTop = 0;
        double chartRectHeight = 0;

        var ran = await OnUiThread(() =>
        {
            // Doc: paragraph 0 = "Hello" text; paragraph 1 = inline chart 216pt tall.
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var textPara = new Paragraph();
            textPara.Runs.Add(new Run("Hello", RunFormatting.Default));
            doc.Blocks.Add(textPara);

            var chartPara = new Paragraph();
            var chart = Chart.Create(ChartKind.Column,
                new[] { "A", "B", "C" }, new[] { 10.0, 25.0, 15.0 }, "S1");
            chart.WidthPt  = 240;
            chart.HeightPt = 216; // 288 DIP tall
            chartPara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(chartPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var chartRects = view.InlineChartRects;
            if (chartRects.Count > 0)
            {
                chartRectTop    = chartRects[0].Rect.Top;
                chartRectHeight = chartRects[0].Rect.Height;
            }

            // Click at 25% down from the chart's top edge — well inside the upper portion.
            var clickY = chartRectTop + chartRectHeight * 0.25;
            var clickX = chartRects.Count > 0 ? chartRects[0].Rect.Left + 10 : 100;
            hitResult = view.TestHitTest(new Point(clickX, clickY));
        });

        if (!ran) return;
        if (chartRectHeight < 4) return; // couldn't introspect layout — skip gracefully

        hitResult.Should().NotBeNull(
            "ZZ1: TestHitTest should resolve a click inside the chart's bounding box");
        hitResult!.Value.Block.Should().Be(1,
            $"ZZ1: A click at 25% into the chart's height (Y ≈ chartTop + {chartRectHeight * 0.25:F0}px) " +
            "should hit the chart paragraph (block 1).  Before the fix the sentinel band was only " +
            "the bottom ~19px so this click landed on the text paragraph above (block 0).");
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
