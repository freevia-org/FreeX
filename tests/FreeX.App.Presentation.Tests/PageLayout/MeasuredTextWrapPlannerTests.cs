using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class MeasuredTextWrapPlannerTests
{
    [Fact]
    public void WrapWithCharacterEllipsis_GreedilyWrapsAndEllipsizesAtLineBudget()
    {
        var lines = MeasuredTextWrapPlanner.WrapWithCharacterEllipsis(
            "alpha beta gamma delta hidden-tail-token",
            maxWidth: 10,
            measureWidth: static text => text.Length,
            maxLines: 2);

        lines.Should().Equal("alpha beta", "gamma" + MeasuredTextWrapPlanner.Ellipsis);
    }

    [Theory]
    [InlineData("line one\r\nline two\rline three\nline four")]
    [InlineData("line one\nline two\nline three\nline four")]
    public void WrapWithCharacterEllipsis_NormalizesHardLines(string text)
    {
        var lines = MeasuredTextWrapPlanner.WrapWithCharacterEllipsis(
            text,
            maxWidth: 50,
            measureWidth: static value => value.Length,
            maxLines: 3);

        lines.Should().Equal("line one", "line two", "line three" + MeasuredTextWrapPlanner.Ellipsis);
    }

    [Fact]
    public void WrapWithCharacterEllipsis_TrimsOversizedUnbrokenToken()
    {
        var lines = MeasuredTextWrapPlanner.WrapWithCharacterEllipsis(
            new string('x', 12) + " hidden-tail-token",
            maxWidth: 6,
            measureWidth: static text => text.Length,
            maxLines: 3);

        lines.Should().ContainSingle().Which.Should().Be("xxxxx" + MeasuredTextWrapPlanner.Ellipsis);
    }

    [Fact]
    public void WrapWithCharacterEllipsis_PreservesEmptyHardLineWithinBudget()
    {
        var lines = MeasuredTextWrapPlanner.WrapWithCharacterEllipsis(
            "alpha\n\nbeta",
            maxWidth: 20,
            measureWidth: static text => text.Length,
            maxLines: 3);

        lines.Should().Equal("alpha", "", "beta");
    }

    [Fact]
    public void WrapWithCharacterEllipsis_NonPositiveLineBudgetReturnsNoLines()
    {
        MeasuredTextWrapPlanner.WrapWithCharacterEllipsis(
                "alpha",
                maxWidth: 20,
                measureWidth: static text => text.Length,
                maxLines: 0)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void PrintOverlayConsumers_DelegateMeasuredWrappingToSharedPresentationPolicy()
    {
        var hostDirectory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");
        var presentationDirectory = RepositoryFileLocator.FindDirectory(
            "src",
            "FreeX.App.Presentation",
            "PageLayout");
        var rendererSource = File.ReadAllText(Path.Combine(hostDirectory, "PrintRenderer.DrawingObjects.cs"));
        var commentPlannerSource = File.ReadAllText(Path.Combine(
            presentationDirectory,
            "PrintCommentSummaryPlanner.cs"));

        rendererSource.Should().Contain("MeasuredTextWrapPlanner.WrapWithCharacterEllipsis(");
        rendererSource.Should().NotContain("WrapPrintedTextBoxOverlayText(");
        rendererSource.Should().NotContain("AddWrappedPrintedTextBoxHardLine(");
        rendererSource.Should().NotContain("TrimPrintedTextBoxOverlayText(");
        commentPlannerSource.Should().Contain("MeasuredTextWrapPlanner.WrapWithCharacterEllipsis(");
        commentPlannerSource.Should().NotContain("private static bool AddWrappedHardLine(");
        commentPlannerSource.Should().NotContain("private static string TrimToWidth(");
    }
}
