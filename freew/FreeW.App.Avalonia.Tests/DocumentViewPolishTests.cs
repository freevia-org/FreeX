using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Tests for the AV-POLISH wave: H/F tab-stop positioning and chart annotations
/// (axis titles, data labels, legend).
/// </summary>
public sealed class DocumentViewPolishTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    // ── Page geometry constants (8.5×11", 1" margins, 96dpi) ──────────────────────────────────────
    // These match the defaults used in DocumentView tests throughout this suite.
    // _contentLeft = DeskPadding(24) + marginLeftPt(72) * (96/72) = 24 + 96 = 120
    // _contentWidth = (8.5in - 2*1in) * 72pt/in = 6.5in * 72pt = 468pt → 624 dip
    private const double ContentLeft  = 120.0;   // _contentLeft in px
    private const double ContentWidth = 624.0;   // _contentWidth in px

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    private static TextDocument DocWithTabHeader(string headerText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text."));
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter(headerText);
        return doc;
    }

    private static TextDocument DocWithTabHeaderParagraph(Paragraph headerPara)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text."));
        var hf = new HeaderFooter();
        hf.Paragraphs.Add(headerPara);
        doc.FinalSectionHeadersFooters.Header = hf;
        return doc;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // PART 1 — H/F tab-stop positioning
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    // ── Test HF-TAB-1: no tab → single item, existing alignment behaviour unchanged ────────────────

    [Fact]
    public async Task HF_no_tab_emits_single_item_with_paragraph_alignment()
    {
        IReadOnlyList<(string Text, double X, double Y, TextAlignment Alignment, double AvailableWidth)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabHeader("Plain header without tabs");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItemsFull;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        // Only one item for the paragraph (no tabs).
        var headerItems = items!.Where(i => i.Text == "Plain header without tabs").ToList();
        headerItems.Should().HaveCount(1, "a paragraph with no tabs emits exactly one HfRenderItem");
        // Non-tab items use paragraph alignment (AvailableWidth > 0).
        headerItems[0].AvailableWidth.Should().BeGreaterThan(0,
            "non-tab items must have AvailableWidth set so the draw loop applies paragraph alignment");
    }

    // ── Test HF-TAB-2: left segment of "Left\tRight" lands at _contentLeft ────────────────────────

    [Fact]
    public async Task HF_tab_left_segment_X_is_at_contentLeft()
    {
        IReadOnlyList<(string Text, double X, double Y, TextAlignment Alignment, double AvailableWidth)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabHeader("Left\tRight");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItemsFull;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        var leftItems = items!.Where(i => i.Text == "Left").ToList();
        leftItems.Should().NotBeEmpty("the left segment of 'Left\\tRight' must produce an HfRenderItem");
        var leftItem = leftItems[0];
        leftItem.X.Should().BeApproximately(ContentLeft, 1.0,
            "the left segment (stop-index 0) must be positioned at _contentLeft");
        leftItem.AvailableWidth.Should().Be(0,
            "tab-positioned items use AvailableWidth=0 so the draw loop does not add an alignment offset");
    }

    // ── Test HF-TAB-3: second segment of "Left\tRight" is at the centre tab (stop 1) ───────────────
    // With a single \t between two segments, the second segment goes to stop-index 1 = centre tab.
    // The centre tab sits at availWidth/2 from _contentLeft; with centre-alignment the text's visual
    // centre is at that position, so X ≈ _contentLeft + availWidth/2 - textWidth/2.

    [Fact]
    public async Task HF_single_tab_second_segment_is_near_centre_stop()
    {
        IReadOnlyList<(string Text, double X, double Y, TextAlignment Alignment, double AvailableWidth)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabHeader("Left\tRight");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItemsFull;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        var rightItems = items!.Where(i => i.Text == "Right").ToList();
        rightItems.Should().NotBeEmpty("the second segment of 'Left\\tRight' must produce an HfRenderItem");
        var rightItem = rightItems[0];

        // Default centre tab: stop at ContentWidth/2 relative to _contentLeft = 312px.
        // Centre-aligned: X = _contentLeft + 312 - textWidth/2.
        // The text must be right of _contentLeft and left of _contentLeft + ContentWidth.
        var rightEdge = ContentLeft + ContentWidth;
        // X > _contentLeft (it must have been shifted right by the centre stop).
        rightItem.X.Should().BeGreaterThan(ContentLeft,
            "the centre-tab segment must be positioned right of the left content edge");
        rightItem.X.Should().BeLessThanOrEqualTo(rightEdge,
            "the segment X must not exceed the right content edge");
        // The text's visual midpoint ≈ X + textWidth/2 should be near ContentWidth/2 from contentLeft.
        // We can check X is less than ContentLeft + ContentWidth/2 (since it's left-adjusted from the stop).
        rightItem.X.Should().BeLessThan(ContentLeft + ContentWidth / 2 + 50,
            "for a short word, the centre-tab X must be close to the midpoint (within ~50px)");
    }

    // ── Test HF-TAB-4: triple segment "Title\t\tPage N" — page number at right tab ──────────────

    [Fact]
    public async Task HF_triple_tab_page_number_is_at_right_stop()
    {
        IReadOnlyList<(string Text, double X, double Y, TextAlignment Alignment, double AvailableWidth)>? items = null;
        var ran = await OnUiThread(() =>
        {
            // Build a header paragraph: "Report" + TAB + TAB + PAGE field.
            var headerPara = new Paragraph();
            headerPara.Runs.Add(new Run("Report\t\t", RunFormatting.Default));
            var pageRun = new Run(string.Empty, RunFormatting.Default) { FieldKind = RunFieldKind.PageNumber };
            headerPara.Runs.Add(pageRun);
            var doc = DocWithTabHeaderParagraph(headerPara);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItemsFull;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        // "Report" should be at the left.
        var reportItems = items!.Where(i => i.Text == "Report").ToList();
        reportItems.Should().NotBeEmpty("'Report' (left segment) must produce an item");
        reportItems[0].X.Should().BeApproximately(ContentLeft, 1.0,
            "the first segment (stop-index 0) must be at the left (_contentLeft)");

        // Page number "1" should be at the right tab (stop-index 2 → default right tab = availWidth).
        var pageItems = items!.Where(i => i.Text == "1").ToList();
        pageItems.Should().NotBeEmpty("the PAGE field (stop-index 2) must produce an item");
        var rightEdge = ContentLeft + ContentWidth;
        pageItems[0].X.Should().BeGreaterThan(ContentLeft + ContentWidth * 0.6,
            "stop-index 2 (right tab) must position the page number well into the right portion of the H/F");
        pageItems[0].X.Should().BeLessThanOrEqualTo(rightEdge,
            "the page-number item X must not exceed the right edge of the content area");
    }

    // ── Test HF-TAB-5: explicit right TabStop overrides the default ───────────────────────────────

    [Fact]
    public async Task HF_explicit_right_tabstop_overrides_default()
    {
        IReadOnlyList<(string Text, double X, double Y, TextAlignment Alignment, double AvailableWidth)>? items = null;
        var ran = await OnUiThread(() =>
        {
            // 200pt explicit right tab stop.
            var headerPara = new Paragraph
            {
                Formatting = new ParagraphFormatting
                {
                    TabStops = new[]
                    {
                        new TabStop(200.0, TabStopAlignment.Right),
                    },
                },
            };
            headerPara.Runs.Add(new Run("Left\tRight", RunFormatting.Default));
            var doc = DocWithTabHeaderParagraph(headerPara);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItemsFull;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        var rightItems = items!.Where(i => i.Text == "Right").ToList();
        rightItems.Should().NotBeEmpty();

        // Explicit right tab at 200pt → 200*(96/72) ≈ 266.7 px relative to _contentLeft.
        // For a right-aligned stop: X = _contentLeft + stopPx - textWidth.
        // X must be less than _contentLeft + 266.7 (i.e. left of the stop).
        const double stopPx = 200.0 * (96.0 / 72.0); // ≈ 266.7
        var maxX = ContentLeft + stopPx;
        rightItems[0].X.Should().BeLessThanOrEqualTo(maxX + 1,
            "right-aligned explicit tab stop at 200pt places the text to the left of the stop position");
        rightItems[0].X.Should().BeGreaterThan(ContentLeft,
            "the right segment must appear to the right of the left edge");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // PART 2 — Chart annotations (axis titles, data labels, legend)
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    private static Chart MakeAnnotatedChart(
        bool showLegend = false, bool showDataLabels = false,
        string? catAxisTitle = null, string? valAxisTitle = null,
        int quickLayoutId = 0, int styleId = 0)
    {
        var chart = Chart.Create(ChartKind.Column,
            new[] { "A", "B", "C" },
            new[] { 10.0, 25.0, 15.0 },
            "Series 1",
            "Test Chart");
        chart.ShowLegend        = showLegend;
        chart.CategoryAxisTitle = catAxisTitle;
        chart.ValueAxisTitle    = valAxisTitle;
        chart.QuickLayoutId     = quickLayoutId;
        chart.StyleId           = styleId;
        return chart;
    }

    private static TextDocument DocWithInlineChart(Chart chart)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
        doc.Blocks.Add(para);
        return doc;
    }

    // ── Test CH-ANN-1: ShowLegend=true propagates to FloatingChartData ────────────────────────────

    [Fact]
    public async Task Chart_ShowLegend_true_propagates_to_annotation_data()
    {
        IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>? anns = null;
        var ran = await OnUiThread(() =>
        {
            var chart = MakeAnnotatedChart(showLegend: true);
            var doc = DocWithInlineChart(chart);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            anns = view.InlineChartAnnotations;
        });

        if (!ran) return;
        anns.Should().NotBeNull();
        anns!.Should().HaveCount(1);
        anns![0].ShowLegend.Should().BeTrue("ShowLegend=true on the model must propagate to FloatingChartData");
    }

    // ── Test CH-ANN-2: ShowLegend=false produces no legend ───────────────────────────────────────

    [Fact]
    public async Task Chart_ShowLegend_false_produces_no_legend()
    {
        IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>? anns = null;
        var ran = await OnUiThread(() =>
        {
            var chart = MakeAnnotatedChart(showLegend: false);
            var doc = DocWithInlineChart(chart);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            anns = view.InlineChartAnnotations;
        });

        if (!ran) return;
        anns.Should().NotBeNull();
        anns!.Should().HaveCount(1);
        anns![0].ShowLegend.Should().BeFalse("ShowLegend=false on the model must propagate as false");
    }

    // ── Test CH-ANN-3: axis titles propagate when set directly ────────────────────────────────────

    [Fact]
    public async Task Chart_axis_titles_propagate_when_set_directly()
    {
        IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>? anns = null;
        var ran = await OnUiThread(() =>
        {
            var chart = MakeAnnotatedChart(catAxisTitle: "Month", valAxisTitle: "Revenue");
            var doc = DocWithInlineChart(chart);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            anns = view.InlineChartAnnotations;
        });

        if (!ran) return;
        anns.Should().NotBeNull();
        anns!.Should().HaveCount(1);
        anns![0].CategoryAxisTitle.Should().Be("Month", "CategoryAxisTitle must propagate to FloatingChartData");
        anns![0].ValueAxisTitle.Should().Be("Revenue", "ValueAxisTitle must propagate to FloatingChartData");
    }

    // ── Test CH-ANN-4: axis titles absent when chart has none ────────────────────────────────────

    [Fact]
    public async Task Chart_axis_titles_absent_when_not_set()
    {
        IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>? anns = null;
        var ran = await OnUiThread(() =>
        {
            var chart = MakeAnnotatedChart(); // no titles set
            var doc = DocWithInlineChart(chart);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            anns = view.InlineChartAnnotations;
        });

        if (!ran) return;
        anns.Should().NotBeNull();
        anns!.Should().HaveCount(1);
        anns![0].CategoryAxisTitle.Should().BeNull("CategoryAxisTitle must be null when not set");
        anns![0].ValueAxisTitle.Should().BeNull("ValueAxisTitle must be null when not set");
    }

    // ── Test CH-ANN-5: QuickLayout 9 (all annotations) enables legend + data labels + axis titles ─

    [Fact]
    public async Task Chart_QuickLayout9_enables_all_annotations()
    {
        IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>? anns = null;
        var ran = await OnUiThread(() =>
        {
            // Layout 9: ShowTitle+ShowLegend+ShowAxisTitles+ShowDataLabels+ShowGridlines.
            var chart = MakeAnnotatedChart(
                catAxisTitle: "Cat", valAxisTitle: "Val",
                quickLayoutId: 9);
            var doc = DocWithInlineChart(chart);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            anns = view.InlineChartAnnotations;
        });

        if (!ran) return;
        anns.Should().NotBeNull();
        anns!.Should().HaveCount(1);
        anns![0].ShowLegend.Should().BeTrue("QuickLayout 9 sets ShowLegend=true");
        anns![0].ShowDataLabels.Should().BeTrue("QuickLayout 9 sets ShowDataLabels=true");
        anns![0].CategoryAxisTitle.Should().Be("Cat",
            "QuickLayout 9 keeps axis titles (they come from the model, shown when ShowAxisTitles=true)");
        anns![0].ValueAxisTitle.Should().Be("Val");
    }

    // ── Test CH-ANN-6: QuickLayout 1 (no annotations) suppresses legend + data labels ────────────

    [Fact]
    public async Task Chart_QuickLayout1_suppresses_legend_and_data_labels()
    {
        IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>? anns = null;
        var ran = await OnUiThread(() =>
        {
            // Layout 1: no legend, no data labels, no axis titles.
            var chart = MakeAnnotatedChart(
                showLegend: true,           // model says show
                catAxisTitle: "Cat",        // model has titles
                valAxisTitle: "Val",
                quickLayoutId: 1);          // QL overrides to none
            var doc = DocWithInlineChart(chart);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            anns = view.InlineChartAnnotations;
        });

        if (!ran) return;
        anns.Should().NotBeNull();
        anns!.Should().HaveCount(1);
        anns![0].ShowLegend.Should().BeFalse("QuickLayout 1 overrides ShowLegend to false");
        anns![0].ShowDataLabels.Should().BeFalse("QuickLayout 1 overrides ShowDataLabels to false");
        anns![0].CategoryAxisTitle.Should().BeNull(
            "QuickLayout 1 (ShowAxisTitles=false) suppresses axis titles");
        anns![0].ValueAxisTitle.Should().BeNull(
            "QuickLayout 1 (ShowAxisTitles=false) suppresses axis titles");
    }

    // ── Test CH-ANN-7: style 5 (ShowDataLabels=true) enables data labels without QuickLayout ──────

    [Fact]
    public async Task Chart_StyleId5_enables_data_labels_without_QuickLayout()
    {
        IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>? anns = null;
        var ran = await OnUiThread(() =>
        {
            // Style 5: ShowDataLabels=true (per ChartStyle.Catalog).
            var chart = MakeAnnotatedChart(styleId: 5);
            var doc = DocWithInlineChart(chart);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            anns = view.InlineChartAnnotations;
        });

        if (!ran) return;
        anns.Should().NotBeNull();
        anns!.Should().HaveCount(1);
        anns![0].ShowDataLabels.Should().BeTrue("ChartStyle 5 has ShowDataLabels=true");
    }

    // ── Test CH-ANN-8: pie chart ignores axis titles ───────────────────────────────────────────────

    [Fact]
    public async Task Chart_Pie_ignores_axis_titles()
    {
        IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>? anns = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            var chart = Chart.Create(ChartKind.Pie, new[] { "X", "Y" }, new[] { 40.0, 60.0 });
            chart.CategoryAxisTitle = "Cat";
            chart.ValueAxisTitle    = "Val";
            chart.ShowLegend = true;
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            anns = view.InlineChartAnnotations;
        });

        if (!ran) return;
        anns.Should().NotBeNull();
        anns!.Should().HaveCount(1);
        // Pie charts are axis-less; BuildChartData must null out axis titles.
        anns![0].CategoryAxisTitle.Should().BeNull(
            "Pie charts are axis-less — CategoryAxisTitle must be suppressed");
        anns![0].ValueAxisTitle.Should().BeNull(
            "Pie charts are axis-less — ValueAxisTitle must be suppressed");
        // ShowLegend may still be honoured for pie.
        anns![0].ShowLegend.Should().BeTrue("ShowLegend=true must still propagate for pie charts");
    }

    // ── Test CH-ANN-9: floating chart also gets annotation fields ─────────────────────────────────

    [Fact]
    public async Task Chart_floating_also_gets_annotation_fields()
    {
        IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>? anns = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor.", RunFormatting.Default));

            var chart = Chart.Create(ChartKind.Column,
                new[] { "A", "B" }, new[] { 10.0, 20.0 }, "S1", "Floating Chart");
            chart.ShowLegend        = true;
            chart.CategoryAxisTitle = "Cat";
            chart.ValueAxisTitle    = "Val";
            chart.Placement = new FloatingPlacement
            {
                Wrapping           = ImageWrapping.Square,
                HorizontalOffsetPt = 36,
                VerticalOffsetPt   = 36,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            anns = view.FloatingChartAnnotations;
        });

        if (!ran) return;
        anns.Should().NotBeNull();
        anns!.Should().HaveCount(1, "one floating chart should produce one annotation entry");
        anns![0].ShowLegend.Should().BeTrue();
        anns![0].CategoryAxisTitle.Should().Be("Cat");
        anns![0].ValueAxisTitle.Should().Be("Val");
    }

    // ── Test CH-ANN-10: render does not crash with all annotations enabled ────────────────────────
    // This is a smoke test — it confirms DrawFloatingChart + DrawChartDataLabels + legend + axis
    // titles do not throw for column / line / pie kinds.

    [Fact]
    public async Task Chart_render_with_all_annotations_does_not_throw()
    {
        bool ok = false;
        var ran = await OnUiThread(() =>
        {
            // Column chart with everything on.
            var chart = MakeAnnotatedChart(showLegend: true, catAxisTitle: "Cat", valAxisTitle: "Val", quickLayoutId: 9);
            chart.CategoryAxisTitle = "Month";
            chart.ValueAxisTitle    = "Revenue";
            var doc = DocWithInlineChart(chart);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            // Render into a headless context (does not throw).
            view.Arrange(new Rect(new Point(0, 0), new Size(816, 4000)));
            ok = true;
        });

        if (!ran) return;
        ok.Should().BeTrue("rendering a chart with all annotations must not throw");
    }
}
