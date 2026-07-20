using Avalonia.Controls.Documents;
using Avalonia.Media;
using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for <see cref="CellRichTextInlinesBuilder"/> — the Avalonia per-run Inlines builder
/// that populates a <see cref="TextBlock"/>'s Inlines collection with one <see cref="Run"/> per
/// resolved cell text run.  Avalonia 12 data types are constructable without a running app.
/// </summary>
public sealed class CellRichTextInlinesBuilderTests
{
    private static IBrush BrushFactory(CellColor color) =>
        new SolidColorBrush(new Color(255, color.R, color.G, color.B));

    // ── HasRuns gate ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HasRuns_NullList_ReturnsFalse()
    {
        CellRichTextInlinesBuilder.HasRuns(null).Should().BeFalse();
    }

    [Fact]
    public void HasRuns_EmptyList_ReturnsFalse()
    {
        CellRichTextInlinesBuilder.HasRuns([]).Should().BeFalse();
    }

    [Fact]
    public void HasRuns_OneRun_ReturnsTrue()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("hi", null, null, null, null, null, null, null)],
            style);
        CellRichTextInlinesBuilder.HasRuns(runs).Should().BeTrue();
    }

    // ── Run count ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_TwoRuns_ProducesTwoInlines()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [
                new CellTextRun("Hello", Bold: true,  Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null),
                new CellTextRun(" World", Bold: null, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: CellRunColor.FromRgb(new CellColor(255, 0, 0))),
            ],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        inlines.Should().HaveCount(2);
    }

    // ── Text content ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_RunTextIsPreservedInOrder()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [
                new CellTextRun("Hello", null, null, null, null, null, null, null),
                new CellTextRun(" World", null, null, null, null, null, null, null),
            ],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        inlines.OfType<Run>().Select(r => r.Text).Should().Equal("Hello", " World");
    }

    // ── Bold / italic ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_BoldRun_HasFontWeightBold()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("Bold", Bold: true, Italic: null, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        inlines.OfType<Run>().Single().FontWeight.Should().Be(FontWeight.Bold);
    }

    [Fact]
    public void Build_ItalicRun_HasFontStyleItalic()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("Italic", Bold: null, Italic: true, Underline: null, Strikethrough: null, FontName: null, FontSize: null, FontColor: null)],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        inlines.OfType<Run>().Single().FontStyle.Should().Be(FontStyle.Italic);
    }

    // ── Superscript / subscript BaselineAlignment ────────────────────────────────────────────────

    [Fact]
    public void Build_SuperscriptRun_HasBaselineAlignmentSuperscript()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 12 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [
                new CellTextRun("X",  null, null, null, null, null, null, null),
                new CellTextRun("2",  null, null, null, null, null, null, null, CellTextRunVertAlign.Superscript),
            ],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        var runList = inlines.OfType<Run>().ToList();
        runList[0].BaselineAlignment.Should().Be(BaselineAlignment.Baseline);
        runList[1].BaselineAlignment.Should().Be(BaselineAlignment.Superscript);
    }

    [Fact]
    public void Build_SubscriptRun_HasBaselineAlignmentSubscript()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 12 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [
                new CellTextRun("H",  null, null, null, null, null, null, null),
                new CellTextRun("2",  null, null, null, null, null, null, null, CellTextRunVertAlign.Subscript),
                new CellTextRun("O",  null, null, null, null, null, null, null),
            ],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        var runList = inlines.OfType<Run>().ToList();
        runList[1].BaselineAlignment.Should().Be(BaselineAlignment.Subscript);
    }

    // ── Super/subscript font size reduction ───────────────────────────────────────────────────────

    [Fact]
    public void Build_SubscriptRun_FontSizeReducedTo67Percent()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 12 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [
                new CellTextRun("H", null, null, null, null, null, null, null),
                new CellTextRun("2", null, null, null, null, null, null, null, CellTextRunVertAlign.Subscript),
            ],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        var runList = inlines.OfType<Run>().ToList();
        runList[0].FontSize.Should().BeApproximately(12, precision: 0.01);
        runList[1].FontSize.Should().BeApproximately(12 * 0.67, precision: 0.01);
    }

    // ── Mixed font sizes ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_MixedFontSizeRuns_EachRunHasCorrectFontSize()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [
                new CellTextRun("Big",   null, null, null, null, null, FontSize: 18, null),
                new CellTextRun("Small", null, null, null, null, null, FontSize: 8,  null),
            ],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        var runList = inlines.OfType<Run>().ToList();
        runList[0].FontSize.Should().BeApproximately(18, precision: 0.01);
        runList[1].FontSize.Should().BeApproximately(8, precision: 0.01);
    }

    // ── Underline / strikethrough decorations ─────────────────────────────────────────────────────

    [Fact]
    public void Build_UnderlineRun_HasUnderlineTextDecoration()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("underlined", null, null, Underline: true, null, null, null, null)],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        var r = inlines.OfType<Run>().Single();
        r.TextDecorations.Should().NotBeNull();
        r.TextDecorations!.Should().Contain(td => td.Location == TextDecorationLocation.Underline);
        r.TextDecorations[0].Should().NotBeSameAs(TextDecorations.Underline[0]);
    }

    [Fact]
    public void Build_PlainRun_HasNullTextDecorations()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 11 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("plain", null, null, null, null, null, null, null)],
            style);

        var inlines = new InlineCollection();
        CellRichTextInlinesBuilder.Build(runs, inlines, BrushFactory);

        inlines.OfType<Run>().Single().TextDecorations.Should().BeNull();
    }
}
