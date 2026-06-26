using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Unit tests for <c>GridView.ApplyRichRunFormatting</c> — the WPF per-character-range
/// FormattedText decorator for per-run rich text cells.
/// </summary>
public sealed class GridViewRichRunFormattingTests
{
    private static FormattedText MakeFormattedText(string text, double fontSize = 11)
    {
        var typeface = new Typeface("Calibri");
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip: 1.0);
    }

    // ── No-op guard ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyRichRunFormatting_EmptyRunList_DoesNotThrow()
    {
        WpfTestThread.Run(() =>
        {
            var ft = MakeFormattedText("Hello World");
            var runs = new List<ResolvedCellTextRun>();

            var act = () => GridView.ApplyRichRunFormatting(ft, runs, null);
            act.Should().NotThrow();
        });
    }

    // ── Character range offsets ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyRichRunFormatting_TwoRuns_ComputesCorrectCharacterRanges()
    {
        WpfTestThread.Run(() =>
        {
            // "Hello" = 5 chars, " World" = 6 chars
            var ft = MakeFormattedText("Hello World");
            var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
            var runs = CellRichRunLayoutPlanner.Resolve(
                [
                    new CellTextRun("Hello", Bold: true,  Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
                    new CellTextRun(" World", Bold: false, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: CellRunColor.FromRgb(new CellColor(255, 0, 0))),
                ],
                style);

            // Must not throw and must apply without error.
            var act = () => GridView.ApplyRichRunFormatting(ft, runs, brushCache: null);
            act.Should().NotThrow();
            // Verify the planner gave us two runs with correct lengths.
            runs.Should().HaveCount(2);
            runs[0].Text.Should().Be("Hello");
            runs[1].Text.Should().Be(" World");
        });
    }

    // ── Super/subscript font size ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyRichRunFormatting_SubscriptRun_AppliesReducedFontSize()
    {
        WpfTestThread.Run(() =>
        {
            // H₂O: "H", "2" (subscript), "O"
            var ft = MakeFormattedText("H2O", fontSize: 12);
            var style = new CellStyle { FontName = "Calibri", FontSize = 12 };
            var runs = CellRichRunLayoutPlanner.Resolve(
                [
                    new CellTextRun("H",  Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
                    new CellTextRun("2",  Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null, VertAlign: CellTextRunVertAlign.Subscript),
                    new CellTextRun("O",  Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
                ],
                style);

            GridView.ApplyRichRunFormatting(ft, runs, brushCache: null);

            // The planner's SuperSubSizeFactor (0.67) should be reflected in RenderedFontSize.
            runs[1].VertAlign.Should().Be(CellTextRunVertAlign.Subscript);
            runs[1].RenderedFontSize.Should().BeApproximately(12 * 0.67, precision: 0.01);
        });
    }

    [Fact]
    public void ApplyRichRunFormatting_SuperscriptRun_AppliesReducedFontSize()
    {
        WpfTestThread.Run(() =>
        {
            // X²: "X", "2" (superscript)
            var ft = MakeFormattedText("X2", fontSize: 12);
            var style = new CellStyle { FontName = "Calibri", FontSize = 12 };
            var runs = CellRichRunLayoutPlanner.Resolve(
                [
                    new CellTextRun("X",  Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
                    new CellTextRun("2",  Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null, VertAlign: CellTextRunVertAlign.Superscript),
                ],
                style);

            GridView.ApplyRichRunFormatting(ft, runs, brushCache: null);

            runs[1].VertAlign.Should().Be(CellTextRunVertAlign.Superscript);
            runs[1].RenderedFontSize.Should().BeApproximately(12 * 0.67, precision: 0.01);
        });
    }

    // ── Brush cache ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyRichRunFormatting_PopulatesBrushCacheForRunColors()
    {
        WpfTestThread.Run(() =>
        {
            var ft = MakeFormattedText("Hello World");
            var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
            var redRunColor = CellRunColor.FromRgb(new CellColor(255, 0, 0));
            var runs = CellRichRunLayoutPlanner.Resolve(
                [
                    new CellTextRun("Hello", Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
                    new CellTextRun(" World", Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: redRunColor),
                ],
                style);

            var brushCache = new Dictionary<CellColor, System.Windows.Media.SolidColorBrush>();
            GridView.ApplyRichRunFormatting(ft, runs, brushCache);

            // Red color should be in the cache after applying.
            brushCache.Should().ContainKey(new CellColor(255, 0, 0));
        });
    }

    // ── Null style defaults ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_InheritsFromCellStyleWhenRunPropertiesNull()
    {
        var style = new CellStyle
        {
            FontName = "Arial",
            FontSize = 14,
            Bold = true,
        };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("test", Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)],
            style);

        runs.Should().ContainSingle();
        runs[0].Bold.Should().BeTrue();
        runs[0].FontName.Should().Be("Arial");
        runs[0].BaseFontSize.Should().Be(14);
        runs[0].RenderedFontSize.Should().Be(14); // no super/sub
    }
}
