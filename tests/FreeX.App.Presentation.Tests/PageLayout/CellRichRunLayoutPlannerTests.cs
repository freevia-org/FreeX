using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Unit tests for <see cref="CellRichRunLayoutPlanner.Resolve"/> — the shared planner that
/// coalesces per-run nullable properties against a base <see cref="CellStyle"/>, used by both
/// WPF (<c>ApplyRichRunFormatting</c>) and Avalonia (<c>CellRichTextInlinesBuilder</c>).
/// </summary>
public sealed class CellRichRunLayoutPlannerTests
{
    private static readonly CellStyle BaseStyle = new()
    {
        FontName      = "Calibri",
        FontSize      = 11,
        Bold          = false,
        Italic        = false,
        Underline     = false,
        Strikethrough = false,
        FontColor     = CellColor.Black,
    };

    // ── Null / empty inputs ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NullRuns_ReturnsEmptyList()
    {
        CellRichRunLayoutPlanner.Resolve(null, BaseStyle).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EmptyRuns_ReturnsEmptyList()
    {
        CellRichRunLayoutPlanner.Resolve([], BaseStyle).Should().BeEmpty();
    }

    // ── Inheritance: null props fall back to cell style ───────────────────────────────────────────

    [Fact]
    public void Resolve_NullBold_InheritsCellStyleBold()
    {
        var style = new CellStyle { FontName = "Calibri", FontSize = 11, Bold = true };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("hi", Bold: null, null, null, null, null, null, null)],
            style);
        runs.Single().Bold.Should().BeTrue();
    }

    [Fact]
    public void Resolve_NullFontName_InheritsCellStyleFontName()
    {
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("hi", null, null, null, null, FontName: null, null, null)],
            BaseStyle);
        runs.Single().FontName.Should().Be("Calibri");
    }

    [Fact]
    public void Resolve_NullFontSize_InheritsCellStyleFontSize()
    {
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("hi", null, null, null, null, null, FontSize: null, null)],
            BaseStyle);
        runs.Single().BaseFontSize.Should().Be(11);
    }

    // ── Explicit run properties override cell style ────────────────────────────────────────────────

    [Fact]
    public void Resolve_ExplicitBoldTrue_OverridesCellStyleBoldFalse()
    {
        var styleNotBold = new CellStyle { FontName = "Calibri", FontSize = 11, Bold = false };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("hi", Bold: true, null, null, null, null, null, null)],
            styleNotBold);
        runs.Single().Bold.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ExplicitFontSize18_SetsBaseFontSizeAndRenderedFontSize()
    {
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("Big", null, null, null, null, null, FontSize: 18, null)],
            BaseStyle);
        runs.Single().BaseFontSize.Should().Be(18);
        runs.Single().RenderedFontSize.Should().Be(18); // no super/sub
    }

    // ── Super/subscript size scaling ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_Superscript_RenderedFontSizeIs67Percent()
    {
        var style12 = new CellStyle { FontName = "Calibri", FontSize = 12 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("2", null, null, null, null, null, null, null, CellTextRunVertAlign.Superscript)],
            style12);
        runs.Single().BaseFontSize.Should().Be(12);
        runs.Single().RenderedFontSize.Should().BeApproximately(12 * 0.67, precision: 0.01);
    }

    [Fact]
    public void Resolve_Subscript_RenderedFontSizeIs67Percent()
    {
        var style12 = new CellStyle { FontName = "Calibri", FontSize = 12 };
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("2", null, null, null, null, null, null, null, CellTextRunVertAlign.Subscript)],
            style12);
        runs.Single().BaseFontSize.Should().Be(12);
        runs.Single().RenderedFontSize.Should().BeApproximately(12 * 0.67, precision: 0.01);
    }

    [Fact]
    public void Resolve_NormalVertAlign_RenderedFontSizeEqualBaseFontSize()
    {
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("A", null, null, null, null, null, null, null, CellTextRunVertAlign.None)],
            BaseStyle);
        runs.Single().RenderedFontSize.Should().Be(runs.Single().BaseFontSize);
    }

    // ── Text content ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_TextIsPreservedVerbatim()
    {
        var runs = CellRichRunLayoutPlanner.Resolve(
            [new CellTextRun("H₂O", null, null, null, null, null, null, null)],
            BaseStyle);
        runs.Single().Text.Should().Be("H₂O");
    }

    // ── Multiple runs ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_TwoRuns_RunCountAndOrderPreserved()
    {
        var runs = CellRichRunLayoutPlanner.Resolve(
            [
                new CellTextRun("Hello", Bold: true,  null, null, null, null, null, null),
                new CellTextRun(" World", Bold: false, null, null, null, null, null, FontColor: new CellColor(255, 0, 0)),
            ],
            BaseStyle);

        runs.Should().HaveCount(2);
        runs[0].Text.Should().Be("Hello");
        runs[0].Bold.Should().BeTrue();
        runs[1].Text.Should().Be(" World");
        runs[1].FontColor.Should().Be(new CellColor(255, 0, 0));
    }
}
