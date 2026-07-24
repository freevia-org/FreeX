using FluentAssertions;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintPreviewParityFixtureTests
{
    [Fact]
    public void Fixture_UsesTheAuthoritativeWpfPageGeometryAndPageCount()
    {
        PrintPreviewParityFixture.PageWidth.Should().Be(696);
        PrintPreviewParityFixture.PageHeight.Should().Be(768);
        PrintPreviewParityFixture.DocumentWidth.Should().BeApproximately(694.4, 0.001);
        PrintPreviewParityFixture.DocumentHeight.Should().BeApproximately(770, 0.001);
        PrintPreviewParityFixture.Pages.Should().HaveCount(2);
    }

    [Fact]
    public void Fixture_UsesTheSameTextCoordinatesForEveryShell()
    {
        var page = PrintPreviewParityFixture.Pages[0];

        page.TextRuns.Should().Contain(run =>
            run.Text == "Parity Demo"
            && run.Left == 48
            && run.Top == 44
            && run.FontSize == 22
            && run.Bold);
        page.TextRuns.Should().Contain(run =>
            run.Text == "Revenue by region"
            && run.Left == 48
            && run.Top == 78
            && run.FontSize == 14
            && !run.Bold);
        page.TextRuns.Should().Contain(run =>
            run.Text == "Page 1"
            && run.Left == 48
            && run.Top == 704);
    }
}
