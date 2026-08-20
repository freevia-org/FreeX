using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-TAB: Validates body-paragraph tab-stop rendering (glyph X positions), leader spans,
/// default-interval tabs, caret offset correctness, and regression for non-tab text.
///
/// Page geometry (8.5×11", 1" margins, 96 dpi):
///   contentLeft  = DeskPadding(24) + 72pt × (96/72) = 24 + 96 = 120 px
///   PxPerPoint   = 96 / 72 ≈ 1.3333
///   defaultTab   = 36 pt × 1.3333 ≈ 48 px
///   stop@144pt   = 144 × 1.3333 ≈ 192 px from contentLeft → absolute 120+192 = 312 px
/// </summary>
public sealed class DocumentViewTabStopTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private const double PxPerPoint   = 96.0 / 72.0;
    private const double ContentLeft  = 120.0;   // DeskPadding(24) + 72pt margin in px
    private const double DefaultTabPx = 36 * PxPerPoint; // 48 px

    // Tolerance: tab stop position matching (glyph X is floating-point).
    private const double Tol = 2.0;

    // ── Helper: one-paragraph doc with explicit tab stops ─────────────────────────────────────────

    private static TextDocument DocWithTabStop(string text, TabStop? stop = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var pf = stop is not null
            ? new ParagraphFormatting { TabStops = new[] { stop } }
            : ParagraphFormatting.Default;
        var para = new Paragraph { Formatting = pf };
        para.Runs.Add(new Run(text, RunFormatting.Default));
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument DocWithTabStops(string text, TabStop[] stops)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var pf = new ParagraphFormatting { TabStops = stops };
        var para = new Paragraph { Formatting = pf };
        para.Runs.Add(new Run(text, RunFormatting.Default));
        doc.Blocks.Add(para);
        return doc;
    }

    // ── TAB-1: plain text (no tabs) — no regression ──────────────────────────────────────────────

    [Fact]
    public async Task TAB_1_plain_text_no_regression()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Hello");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;

        placed.Should().NotBeNull();
        // All characters should be placed; none should be a tab.
        placed!.Should().HaveCount(5, "plain text 'Hello' has 5 chars");
        placed.All(p => p.Ch != '\t').Should().BeTrue("no tab chars in non-tab paragraph");
        // First char lands at contentLeft (left-aligned, no tabs).
        placed[0].X.Should().BeApproximately(ContentLeft, Tol, "first glyph at content left");
        // Each subsequent glyph should be to the right of the previous.
        for (var i = 1; i < placed.Count; i++)
            placed[i].X.Should().BeGreaterThan(placed[i - 1].X, "glyphs advance left→right");
    }

    // ── TAB-2: default tab interval (no explicit stops) ──────────────────────────────────────────
    // "A\tB": A lands at contentLeft; tab should advance to the next 36pt (48px) multiple.
    // A is narrow (say ~8px); pen after A ≈ 8px. Next 48px multiple = 48. So B starts at
    // contentLeft + 48.

    [Fact]
    public async Task TAB_2_default_tab_advances_to_next_interval_multiple()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("A\tB"); // no explicit stop
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        // Three placed items: 'A', '\t', 'B'.
        placed!.Should().HaveCount(3, "'A\\tB' has 3 chars: A, tab, B");
        placed[0].Ch.Should().Be('A');
        placed[1].Ch.Should().Be('\t');
        placed[2].Ch.Should().Be('B');

        // 'A' starts at contentLeft.
        placed[0].X.Should().BeApproximately(ContentLeft, Tol, "'A' starts at contentLeft");

        // Tab glyph's X = where the tab char starts (= end of 'A').
        var aEnd = placed[0].X + placed[0].W;
        placed[1].X.Should().BeApproximately(aEnd, Tol, "tab starts right after 'A'");

        // 'B' starts at the next 48px (36pt) multiple past A's end.
        // A's end (relative to content origin) is aEnd - ContentLeft.
        var penInLine = aEnd - ContentLeft;
        var expectedStop = (Math.Floor(penInLine / DefaultTabPx) + 1) * DefaultTabPx;
        var expectedBX   = ContentLeft + expectedStop;
        placed[2].X.Should().BeApproximately(expectedBX, Tol,
            "'B' starts at the next default-tab-interval multiple");
    }

    // ── TAB-3: explicit left tab stop at 144pt ───────────────────────────────────────────────────
    // "Name\tValue" with a Left tab at 144pt → 'V' starts at contentLeft + 144*PxPerPoint.

    [Fact]
    public async Task TAB_3_explicit_left_tab_places_text_at_stop()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var stopPositionPt = 144.0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Name\tValue", new TabStop(stopPositionPt, TabStopAlignment.Left));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        // "Name\tValue" → 9 chars (N,a,m,e,\t,V,a,l,u,e).
        placed!.Should().HaveCount(10, "'Name\\tValue' has 10 chars");

        // 'V' (index 5) should start at contentLeft + 144pt in DIP.
        var expectedVX = ContentLeft + stopPositionPt * PxPerPoint;
        var vGlyph = placed.FirstOrDefault(p => p.Ch == 'V');
        vGlyph.Ch.Should().Be('V', "should find the 'V' glyph");
        vGlyph.X.Should().BeApproximately(expectedVX, Tol,
            "'V' starts at the 144pt left tab stop");
    }

    // ── TAB-4: explicit right tab stop at 288pt ──────────────────────────────────────────────────
    // "Name\tValue" with Right tab at 288pt → the last char of "Value" ENDS at the stop.

    [Fact]
    public async Task TAB_4_right_tab_ends_segment_at_stop()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var stopPositionPt = 288.0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Name\tValue", new TabStop(stopPositionPt, TabStopAlignment.Right));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        // Find the last 'e' in "Value" (the segment after the tab).
        var afterTab = placed!.SkipWhile(p => p.Ch != '\t').Skip(1).ToList();
        afterTab.Should().NotBeEmpty("there are chars after the tab");

        // The last char's right edge should be at or near the stop.
        var last = afterTab.Last();
        var rightEdge = last.X + last.W;
        var stopX = ContentLeft + stopPositionPt * PxPerPoint;
        rightEdge.Should().BeApproximately(stopX, Tol,
            "right-tab: last char's right edge aligns to the stop");
    }

    // ── TAB-5: center tab stop at 216pt ──────────────────────────────────────────────────────────
    // "Name\tValue" with Center tab at 216pt → segment "Value" is centred on the stop.

    [Fact]
    public async Task TAB_4b_right_tab_uses_shared_tab_stop_planner()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var stop = new TabStop(288.0, TabStopAlignment.Right, TabLeader.Dashes);
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Name\tValue", stop);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        var glyphs = placed!;
        var tab = glyphs.Single(g => g.Ch == '\t');
        var afterTab = glyphs.SkipWhile(g => g.Ch != '\t').Skip(1).ToList();
        var followingWidth = afterTab.Sum(g => g.W);
        var plan = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
            penPositionDip: tab.X - ContentLeft,
            followingSegmentWidthDip: followingWidth,
            tabStops: [stop],
            defaultTabStopPt: 36,
            dipPerPoint: PxPerPoint);

        tab.W.Should().BeApproximately(plan.AdvanceDip, Tol);
        afterTab.First().X.Should().BeApproximately(ContentLeft + plan.SegmentStartDip, Tol);
        plan.Leader.Should().Be(TabLeader.Dashes);
    }

    [Fact]
    public async Task TAB_5_center_tab_centres_segment_at_stop()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var stopPositionPt = 216.0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Name\tValue", new TabStop(stopPositionPt, TabStopAlignment.Center));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        var afterTab = placed!.SkipWhile(p => p.Ch != '\t').Skip(1).ToList();
        afterTab.Should().NotBeEmpty();

        // Segment spans from first-char-X to last-char-right-edge.
        var segStart = afterTab.First().X;
        var segEnd   = afterTab.Last().X + afterTab.Last().W;
        var segCentre = (segStart + segEnd) / 2;
        var stopX = ContentLeft + stopPositionPt * PxPerPoint;

        segCentre.Should().BeApproximately(stopX, Tol,
            "center-tab: segment is centred on the stop position");
    }

    // ── TAB-6: dot-leader produces a tab leader span ─────────────────────────────────────────────

    [Fact]
    public async Task TAB_5b_decimal_tab_aligns_separator_at_stop()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var stopPositionPt = 216.0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Total\t123.45", new TabStop(stopPositionPt, TabStopAlignment.Decimal));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        var afterTab = placed!.SkipWhile(p => p.Ch != '\t').Skip(1).ToList();
        var separator = afterTab.Single(p => p.Ch == '.');
        var stopX = ContentLeft + stopPositionPt * PxPerPoint;
        separator.X.Should().BeApproximately(stopX, Tol,
            "decimal-tab: separator starts at the tab stop");
    }

    [Fact]
    public async Task TAB_6_dot_leader_tab_produces_leader_span()
    {
        IReadOnlyList<(double X1, double X2, double Y, double LineHeight, TabLeader Leader)>? leaders = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Name\tValue",
                new TabStop(144.0, TabStopAlignment.Left, TabLeader.Dots));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            leaders = view.TabLeaderSpans;
        });
        if (!ran) return;
        leaders.Should().NotBeNull();

        // Exactly one leader span should be produced for the single tab.
        leaders!.Should().HaveCount(1, "one tab with a dot leader → one leader span");
        var span = leaders![0];
        span.Leader.Should().Be(TabLeader.Dots, "leader kind is Dots");
        span.X2.Should().BeGreaterThan(span.X1, "leader span has positive width");
    }

    // ── TAB-7: dash-leader tab ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TAB_7_dash_leader_tab_produces_leader_span()
    {
        IReadOnlyList<(double X1, double X2, double Y, double LineHeight, TabLeader Leader)>? leaders = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Abc\tDef",
                new TabStop(144.0, TabStopAlignment.Left, TabLeader.Dashes));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            leaders = view.TabLeaderSpans;
        });
        if (!ran) return;
        leaders.Should().NotBeNull();
        leaders!.Should().HaveCount(1, "one tab with a dash leader");
        leaders![0].Leader.Should().Be(TabLeader.Dashes);
    }

    // ── TAB-8: underline-leader tab ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TAB_8_underline_leader_tab_produces_leader_span()
    {
        IReadOnlyList<(double X1, double X2, double Y, double LineHeight, TabLeader Leader)>? leaders = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Abc\tDef",
                new TabStop(144.0, TabStopAlignment.Left, TabLeader.Underline));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            leaders = view.TabLeaderSpans;
        });
        if (!ran) return;
        leaders.Should().NotBeNull();
        leaders!.Should().HaveCount(1);
        leaders![0].Leader.Should().Be(TabLeader.Underline);
    }

    // ── TAB-9: no leader → no leader span ────────────────────────────────────────────────────────

    [Fact]
    public async Task TAB_9_no_leader_produces_no_leader_span()
    {
        IReadOnlyList<(double X1, double X2, double Y, double LineHeight, TabLeader Leader)>? leaders = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("Abc\tDef",
                new TabStop(144.0, TabStopAlignment.Left, TabLeader.None));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            leaders = view.TabLeaderSpans;
        });
        if (!ran) return;
        leaders.Should().NotBeNull();
        leaders!.Should().BeEmpty("leader=None produces no leader span");
    }

    // ── TAB-10: caret index maps correctly across a tab ──────────────────────────────────────────
    // In "A\tB" (block 0), the caret offsets should be 0=A, 1=\t, 2=B, 3=sentinel.
    // Check that GetBodyTabPlaced returns the chars in the correct order.

    [Fact]
    public async Task TAB_10_glyph_offset_sequence_includes_tab_character()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("A\tB");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        // Offsets in order: A(0), \t(1), B(2)
        placed!.Should().HaveCount(3);
        placed[0].Ch.Should().Be('A');
        placed[1].Ch.Should().Be('\t');
        placed[2].Ch.Should().Be('B');

        // Tab glyph must have positive width (so caret can land on it).
        placed[1].W.Should().BeGreaterThan(0, "tab char has positive advance width");

        // B's X must equal tab's X + tab's W (they are adjacent, no gap in _placed).
        placed[2].X.Should().BeApproximately(placed[1].X + placed[1].W, Tol,
            "'B' starts exactly where the tab char ended");
    }

    // ── TAB-11: multiple tabs on one line ────────────────────────────────────────────────────────

    [Fact]
    public async Task TAB_11_multiple_explicit_tabs_use_successive_stops()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var stop1Pt = 72.0;
        var stop2Pt = 216.0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStops("A\tB\tC", new[]
            {
                new TabStop(stop1Pt, TabStopAlignment.Left),
                new TabStop(stop2Pt, TabStopAlignment.Left),
            });
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        // A, \t, B, \t, C  →  5 chars.
        placed!.Should().HaveCount(5, "'A\\tB\\tC' has 5 chars");

        // 'B' should start at contentLeft + 72pt.
        var bGlyph = placed[2];
        bGlyph.Ch.Should().Be('B');
        bGlyph.X.Should().BeApproximately(ContentLeft + stop1Pt * PxPerPoint, Tol,
            "'B' starts at the first tab stop (72pt)");

        // 'C' should start at contentLeft + 216pt.
        var cGlyph = placed[4];
        cGlyph.Ch.Should().Be('C');
        cGlyph.X.Should().BeApproximately(ContentLeft + stop2Pt * PxPerPoint, Tol,
            "'C' starts at the second tab stop (216pt)");
    }

    // ── TAB-12: custom default tab interval ───────────────────────────────────────────────────────
    // Set DefaultTabStopPt = 72 → next interval multiple for a tab after ~8px should be 72pt.

    [Fact]
    public async Task TAB_12_custom_default_tab_interval_is_respected()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        var customTabPt = 72.0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStop("A\tB"); // no explicit stop
            doc.Page.DefaultTabStopPt = customTabPt;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();
        placed!.Should().HaveCount(3);

        // 'B' should align to the next 72pt multiple past 'A'.
        var bGlyph = placed[2];
        bGlyph.Ch.Should().Be('B');
        var aEnd = placed[0].X + placed[0].W;
        var penInLine = aEnd - ContentLeft;
        var intervalPx = customTabPt * PxPerPoint;
        var expectedBX = ContentLeft + (Math.Floor(penInLine / intervalPx) + 1) * intervalPx;
        bGlyph.X.Should().BeApproximately(expectedBX, Tol,
            "'B' aligns to the 72pt custom default tab interval");
    }

    // ── BP1 regression tests: tab stops are MARGIN-relative, not indent-relative ─────────────────

    // Helper: one-paragraph doc with an explicit tab stop and a paragraph indent.
    private static TextDocument DocWithTabStopAndIndent(
        string text,
        double indentLeftPt,
        double stopPositionPt,
        TabStopAlignment alignment = TabStopAlignment.Left)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var pf = new ParagraphFormatting
        {
            IndentLeftPt = indentLeftPt,
            TabStops = new[] { new TabStop(stopPositionPt, alignment) },
        };
        var para = new Paragraph { Formatting = pf };
        para.Runs.Add(new Run(text, RunFormatting.Default));
        doc.Blocks.Add(para);
        return doc;
    }

    // ── TAB-13 (BP1): indented paragraph — tab stop is MARGIN-relative ───────────────────────────
    // Paragraph with IndentLeftPt=36 (0.5") and a Left tab stop at 144pt (2").
    // Text = "a\tb".  After the fix:
    //   'b' must land at ContentLeft + 144pt*PxPerPoint  (margin-relative, 2" from margin)
    // NOT at ContentLeft + 36pt*PxPerPoint + 144pt*PxPerPoint (which would be 2.5" from margin).
    // This matches Word/OOXML semantics and the WPF Ruler (Ruler.cs:609).

    [Fact]
    public async Task TAB_13_BP1_indented_paragraph_tab_stop_is_margin_relative()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        const double indentPt   = 36.0;  // 0.5"
        const double stopPt     = 144.0; // 2.0"
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStopAndIndent("a\tb", indentPt, stopPt);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        // 3 placed items: 'a', '\t', 'b'.
        placed!.Should().HaveCount(3, "'a\\tb' has 3 chars");
        placed[0].Ch.Should().Be('a');
        placed[1].Ch.Should().Be('\t');
        placed[2].Ch.Should().Be('b');

        // 'a' starts at the INDENTED text origin: ContentLeft + indent.
        var indentPx = indentPt * PxPerPoint;
        placed[0].X.Should().BeApproximately(ContentLeft + indentPx, Tol,
            "'a' starts at the indented text origin (margin + 0.5in)");

        // 'b' must land at the tab stop measured from the MARGIN (not from the indent).
        // Expected: ContentLeft + 144pt*PxPerPoint  (2" from margin).
        // Wrong (pre-fix): ContentLeft + 36pt*PxPerPoint + 144pt*PxPerPoint (2.5" from margin).
        var expectedBX     = ContentLeft + stopPt * PxPerPoint;
        var wrongPreFixBX  = ContentLeft + indentPx + stopPt * PxPerPoint;
        placed[2].X.Should().BeApproximately(expectedBX, Tol,
            "'b' must land at 2\" from the margin (OOXML/Word/Ruler semantics), not 2.5\"");
        placed[2].X.Should().BeLessThan(wrongPreFixBX - 1,
            "if 'b' is at wrongPreFixBX the indent was incorrectly added to the tab position");
    }

    // ── TAB-14 (BP1 no-regression): non-indented paragraph still places tab at margin-relative pos ─
    // IndentLeftPt=0 and a Left tab stop at 144pt — same as TAB-3 but with the explicit assertion
    // that the indent=0 case is unaffected by the BP1 fix.

    [Fact]
    public async Task TAB_14_BP1_no_indent_tab_stop_unaffected()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        const double stopPt = 144.0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithTabStopAndIndent("Name\tValue", indentLeftPt: 0, stopPositionPt: stopPt);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        // "Name\tValue" → 10 chars.
        placed!.Should().HaveCount(10, "'Name\\tValue' has 10 chars");

        // 'V' (the first char of "Value") must start at ContentLeft + 144pt*PxPerPoint.
        var vGlyph = placed.FirstOrDefault(p => p.Ch == 'V');
        vGlyph.Ch.Should().Be('V');
        vGlyph.X.Should().BeApproximately(ContentLeft + stopPt * PxPerPoint, Tol,
            "non-indented paragraph: tab stop at 144pt still lands at 2\" from margin");
    }

    // ── TAB-15 (BP1): default tab interval is margin-relative for indented paragraphs ─────────────
    // Paragraph with IndentLeftPt=36 and no explicit tab stops (default 36pt interval).
    // The pen after 'a' is at ~(ContentLeft + 36pt + ~8px).
    // Margin-relative pen = indent + glyph width ≈ 36pt*PxPerPt + ~8px.
    // The next 36pt multiple from that pen (margin-relative) determines the stop.
    // Before the fix, the default tab would double-count the indent.

    [Fact]
    public async Task TAB_15_BP1_indented_paragraph_default_tab_is_margin_relative()
    {
        IReadOnlyList<(char Ch, double X, double W)>? placed = null;
        const double indentPt   = 36.0;   // 0.5" indent
        const double defaultPt  = 36.0;   // default tab = 0.5"
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Page.DefaultTabStopPt = defaultPt;
            var pf = ParagraphFormatting.Default with { IndentLeftPt = indentPt };
            var para = new Paragraph { Formatting = pf };
            para.Runs.Add(new Run("a\tb", RunFormatting.Default));
            doc.Blocks.Add(para);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            placed = view.GetBodyTabPlaced(0);
        });
        if (!ran) return;
        placed.Should().NotBeNull();

        placed!.Should().HaveCount(3, "'a\\tb' has 3 chars");
        placed[0].Ch.Should().Be('a');
        placed[2].Ch.Should().Be('b');

        // 'a' starts at ContentLeft + indent.
        var indentPx    = indentPt * PxPerPoint;
        var intervalPx  = defaultPt * PxPerPoint;
        placed[0].X.Should().BeApproximately(ContentLeft + indentPx, Tol,
            "'a' starts at the indented origin");

        // Margin-relative pen after 'a': indentPx + aWidth.
        var aWidth           = placed[0].W;
        var penFromMargin    = indentPx + aWidth;

        // Next default tab stop (multiple of intervalPx strictly past penFromMargin).
        var expectedStop = (Math.Floor(penFromMargin / intervalPx) + 1) * intervalPx;
        var expectedBX   = ContentLeft + expectedStop;

        placed[2].X.Should().BeApproximately(expectedBX, Tol,
            "'b' lands at the next default-tab multiple from the MARGIN (not from the text start)");
    }
}
