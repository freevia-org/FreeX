using FreeP.Core.Model;
using FreeP.App.Compositor.MathLayout;

namespace FreeP.App.Compositor;

public readonly record struct TextNativeArtifact<TArtifact>(
    TArtifact Artifact,
    double WidthDip,
    double BaselineDip,
    double HeightDip)
    where TArtifact : class;

/// <summary>
/// Owns renderer-neutral sequencing around native text artifacts. Renderers retain native
/// artifact creation, measurement, and drawing.
/// </summary>
public static class TextNativeRenderSequence
{
    public static void RenderTabs<TArtifact>(
        ResolvedParagraph paragraph,
        double startX,
        double startY,
        IReadOnlyList<ResolvedTabStop> tabStops,
        Func<ResolvedRun, string, bool, TextNativeArtifact<TArtifact>> format,
        Action<TArtifact, double, double> draw)
        where TArtifact : class
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(tabStops);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(draw);

        bool rightToLeft = paragraph.RightToLeft;
        var plan = TextLayoutPlanner.PlanTabStops(
            paragraph,
            startX,
            tabStops,
            (run, text) => format(run, text, rightToLeft).WidthDip);

        double previousEndX = startX;
        foreach (var segment in plan.Segments)
        {
            var run = paragraph.Runs[segment.RunIndex];
            var artifact = format(run, segment.Text, rightToLeft);
            var leader = TextLayoutPlanner.PlanTabLeaderFill(
                segment.Leader,
                previousEndX,
                segment.X,
                glyph => format(run, glyph.ToString(), rightToLeft).WidthDip);
            if (leader.ShouldDraw)
                draw(format(run, leader.Text, rightToLeft).Artifact, leader.StartX, startY);

            draw(artifact.Artifact, segment.X, startY);
            previousEndX = segment.X + artifact.WidthDip;
        }
    }

    public static void RenderBaseline<TArtifact>(
        ResolvedParagraph paragraph,
        double startX,
        double startY,
        double maxWidth,
        Func<ResolvedRun, string, double, bool, TextNativeArtifact<TArtifact>> format,
        Action<TArtifact, double, double> draw)
        where TArtifact : class
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(draw);

        var artifacts = paragraph.Runs
            .Select(run => format(
                run,
                run.Text,
                BaselineFontScale(run),
                TextLayoutPlanner.ResolveRunRightToLeft(paragraph.RightToLeft, run.Text)))
            .ToArray();
        if (maxWidth > 0 && artifacts.Sum(item => item.WidthDip) > maxWidth)
        {
            RenderWrappedBaseline(paragraph, startX, startY, maxWidth, format, draw);
            return;
        }

        var line = TextLayoutPlanner.PlanInlineBaselineLine(
            paragraph,
            startX,
            startY,
            maxWidth,
            (runIndex, run, rightToLeft) =>
            {
                var measured = format(run, run.Text, BaselineFontScale(run), rightToLeft);
                return new TextInlineRunMeasure(
                    measured.WidthDip,
                    artifacts[runIndex].BaselineDip,
                    artifacts[runIndex].HeightDip);
            });
        foreach (var placement in line.Runs)
        {
            var run = paragraph.Runs[placement.RunIndex];
            double offsetDip = TextLayoutPlanner.BaselineOffsetToDip(
                run.BaselineOffset,
                run.FontSizePt);
            draw(
                artifacts[placement.RunIndex].Artifact,
                placement.X,
                placement.Y - offsetDip);
        }
    }

    public static void RenderMath<TArtifact>(
        ResolvedParagraph paragraph,
        double startX,
        double startY,
        Func<ResolvedRun, string, bool, TextNativeArtifact<TArtifact>> format,
        Action<TArtifact, double, double> drawText,
        Action<MathDrawOp> drawMath)
        where TArtifact : class
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(drawText);
        ArgumentNullException.ThrowIfNull(drawMath);

        var artifacts = new TextNativeArtifact<TArtifact>?[paragraph.Runs.Count];
        for (int index = 0; index < paragraph.Runs.Count; index++)
        {
            var run = paragraph.Runs[index];
            if (!run.IsMathRun && !string.IsNullOrEmpty(run.Text))
            {
                artifacts[index] = format(
                    run,
                    run.Text,
                    TextLayoutPlanner.ResolveRunRightToLeft(paragraph.RightToLeft, run.Text));
            }
        }

        var line = TextLayoutPlanner.PlanInlineBaselineLine(
            paragraph,
            startX,
            startY,
            0,
            (runIndex, run, rightToLeft) =>
            {
                if (run.IsMathRun && run.MathLayout is not null)
                {
                    var metrics = run.MathLayout.Metrics;
                    return new TextInlineRunMeasure(metrics.Width, metrics.Ascent, metrics.Height);
                }

                var measured = format(run, run.Text, rightToLeft);
                var artifact = artifacts[runIndex];
                return new TextInlineRunMeasure(
                    measured.WidthDip,
                    artifact?.BaselineDip ?? 0,
                    artifact?.HeightDip ?? 0);
            });

        foreach (var placement in line.Runs)
        {
            var run = paragraph.Runs[placement.RunIndex];
            if (run.IsMathRun && run.MathLayout is not null)
            {
                foreach (var operation in MathBoxRenderPlanner.Plan(
                             run.MathLayout,
                             placement.X,
                             placement.Y,
                             run.Color,
                             run.FontFamily))
                {
                    drawMath(operation);
                }
            }
            else if (!string.IsNullOrEmpty(run.Text) &&
                     artifacts[placement.RunIndex] is { } artifact)
            {
                drawText(artifact.Artifact, placement.X, placement.Y);
            }
        }
    }

    private static void RenderWrappedBaseline<TArtifact>(
        ResolvedParagraph paragraph,
        double startX,
        double startY,
        double maxWidth,
        Func<ResolvedRun, string, double, bool, TextNativeArtifact<TArtifact>> format,
        Action<TArtifact, double, double> draw)
        where TArtifact : class
    {
        var lines = TextLayoutPlanner.PlanBaselineLines(
            paragraph,
            startX,
            startY,
            maxWidth,
            (run, text, rightToLeft) =>
            {
                var artifact = format(run, text, BaselineFontScale(run), rightToLeft);
                return new TextBaselineFragmentMeasure(
                    artifact.WidthDip,
                    artifact.BaselineDip,
                    artifact.HeightDip);
            });
        foreach (var line in lines)
        {
            foreach (var fragment in line.Fragments)
            {
                var artifact = format(
                    paragraph.Runs[fragment.RunIndex],
                    fragment.Text,
                    BaselineFontScale(paragraph.Runs[fragment.RunIndex]),
                    fragment.RightToLeft);
                draw(artifact.Artifact, fragment.X, fragment.Y);
            }
        }
    }

    private static double BaselineFontScale(ResolvedRun run) =>
        run.BaselineOffset.HasValue ? TextLayoutPlanner.BaselineRunFontScale : 1.0;
}
