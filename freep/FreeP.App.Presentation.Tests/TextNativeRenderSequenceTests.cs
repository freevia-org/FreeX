using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class TextNativeRenderSequenceTests
{
    [Fact]
    public void RenderTabs_SequencesLeaderAndNativeArtifacts()
    {
        var paragraph = new ResolvedParagraph
        {
            Runs = [new ResolvedRun { Text = "A\tB" }]
        };
        var draws = new List<(string Text, double X, double Y)>();

        TextNativeRenderSequence.RenderTabs(
            paragraph,
            startX: 0,
            startY: 12,
            [new ResolvedTabStop
            {
                PositionDip = 50,
                Alignment = TabStopAlignment.Right,
                Leader = TabStopLeader.Dots
            }],
            (run, text, rightToLeft) => Artifact(text),
            (artifact, x, y) => draws.Add((artifact, x, y)));

        draws.Select(draw => draw.Text).Should().Equal("A", "...", "B");
        draws.Select(draw => draw.Y).Should().OnlyContain(y => y == 12);
        draws[0].X.Should().Be(0);
        draws[2].X.Should().Be(40);
    }

    [Fact]
    public void RenderBaseline_UsesNativeMetricsAndBaselineOffset()
    {
        var paragraph = new ResolvedParagraph
        {
            Runs =
            [
                new ResolvedRun { Text = "A", FontSizePt = 12 },
                new ResolvedRun { Text = "B", FontSizePt = 12, BaselineOffset = 25 }
            ]
        };
        var draws = new List<(string Text, double X, double Y)>();

        TextNativeRenderSequence.RenderBaseline(
            paragraph,
            startX: 5,
            startY: 20,
            maxWidth: 100,
            (run, text, fontScale, rightToLeft) => Artifact(text),
            (artifact, x, y) => draws.Add((artifact, x, y)));

        draws.Select(draw => draw.Text).Should().Equal("A", "B");
        draws[0].X.Should().Be(5);
        draws[1].X.Should().Be(15);
        draws[1].Y.Should().BeLessThan(draws[0].Y);
    }

    [Fact]
    public void RenderBaseline_UsesWrappedSequenceWhenNativeWidthExceedsLine()
    {
        var paragraph = new ResolvedParagraph
        {
            Runs = [new ResolvedRun { Text = "AB CD", FontSizePt = 12 }]
        };
        var draws = new List<(string Text, double X, double Y)>();

        TextNativeRenderSequence.RenderBaseline(
            paragraph,
            startX: 0,
            startY: 0,
            maxWidth: 25,
            (run, text, fontScale, rightToLeft) => Artifact(text),
            (artifact, x, y) => draws.Add((artifact, x, y)));

        draws.Should().HaveCountGreaterThan(1);
        draws.Select(draw => draw.Text).Should().Equal("AB", "CD");
        draws.Select(draw => draw.Y).Distinct().Should().HaveCountGreaterThan(1);
    }

    private static TextNativeArtifact<string> Artifact(string text) =>
        new(text, text.Length * 10, BaselineDip: 8, HeightDip: 10);
}
