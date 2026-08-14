using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class TextParagraphNativeRenderDispatcherTests
{
    [Fact]
    public void Render_DispatchesEachRouteAndBulletInPlacementOrder()
    {
        var paragraphs = Enum.GetValues<TextParagraphRenderRoute>()
            .Select((route, index) => new ResolvedParagraph
            {
                Runs = [new ResolvedRun { Text = route.ToString() }]
            })
            .ToArray();
        var placements = Enum.GetValues<TextParagraphRenderRoute>()
            .Select((route, index) => new TextParagraphPlacement(
                index,
                0,
                index * 10,
                20,
                100,
                index == 0
                    ? new TextBulletPlacement("*", "Arial", 12, default, null, 0, 20)
                    : null)
            {
                RenderRoute = route
            })
            .ToArray();
        var plan = new TextMeasuredBlockLayoutPlan<string>(
            new ResolvedTextLayout { Paragraphs = paragraphs },
            default,
            new TextBlockLayoutPlan(default, placements),
            placements.ToDictionary(item => item.ParagraphIndex, item => $"artifact-{item.ParagraphIndex}"));
        var calls = new List<string>();

        TextParagraphNativeRenderDispatcher.Render(
            plan,
            new(
                bullet => calls.Add($"bullet:{bullet.Text}"),
                (paragraph, _) => calls.Add($"math:{paragraph.Runs[0].Text}"),
                (paragraph, _) => calls.Add($"effects:{paragraph.Runs[0].Text}"),
                (paragraph, _) => calls.Add($"tabs:{paragraph.Runs[0].Text}"),
                (paragraph, _) => calls.Add($"baseline:{paragraph.Runs[0].Text}"),
                (paragraph, artifact, _) => calls.Add($"plain:{paragraph.Runs[0].Text}:{artifact}")));

        calls.Should().Equal(
            "bullet:*",
            "plain:Plain:artifact-0",
            "tabs:Tabs",
            "effects:Effects",
            "baseline:Baseline",
            "math:Math");
    }

    [Fact]
    public void TryRenderTableCell_UsesNativeMeasurementAndPortablePlacement()
    {
        var text = new ResolvedTextLayout
        {
            Paragraphs =
            [
                new ResolvedParagraph
                {
                    Runs = [new ResolvedRun { Text = "cell" }]
                }
            ]
        };
        var draws = new List<(string Artifact, TextParagraphPlacement Placement)>();

        var rendered = TextParagraphNativeRenderDispatcher.TryRenderTableCell(
            text,
            new LayoutRect(0, 0, 200, 100),
            TableCellAnchor.Middle,
            (paragraph, width, wrap) => new TextNativeMeasurement<string>(paragraph.Runs[0].Text, 20, 40),
            (artifact, placement) => draws.Add((artifact, placement)));

        rendered.Should().BeTrue();
        draws.Should().ContainSingle();
        draws[0].Artifact.Should().Be("cell");
        draws[0].Placement.Y.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderStacked_SkipsStaleModelIndices()
    {
        var text = new ResolvedTextLayout
        {
            Paragraphs =
            [
                new ResolvedParagraph
                {
                    Runs = [new ResolvedRun { Text = "A" }]
                }
            ]
        };
        var plan = new TextStackedVerticalLayoutPlan(
            default,
            TextVerticalType.Vertical,
            TextVerticalRenderMode.StackedUpright,
            [],
            [
                new TextStackedGlyphPlacement(0, 0, "A", 1, 2, 3, 4),
                new TextStackedGlyphPlacement(1, 0, "stale", 0, 0, 0, 0)
            ]);
        var draws = new List<string>();

        TextParagraphNativeRenderDispatcher.RenderStacked(
            text,
            plan,
            (_, run, glyph) => draws.Add(run.Text + glyph.Text));

        draws.Should().Equal("AA");
    }
}
