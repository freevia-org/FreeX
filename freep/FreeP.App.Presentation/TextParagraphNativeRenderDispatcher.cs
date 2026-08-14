using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record TextParagraphNativeRenderCallbacks<TArtifact>(
    Action<TextBulletPlacement> DrawBullet,
    Action<ResolvedParagraph, TextParagraphPlacement> DrawMath,
    Action<ResolvedParagraph, TextParagraphPlacement> DrawEffects,
    Action<ResolvedParagraph, TextParagraphPlacement> DrawTabs,
    Action<ResolvedParagraph, TextParagraphPlacement> DrawBaseline,
    Action<ResolvedParagraph, TArtifact, TextParagraphPlacement> DrawPlain);

/// <summary>
/// Routes measured paragraph placements to renderer-owned native drawing callbacks.
/// </summary>
public static class TextParagraphNativeRenderDispatcher
{
    public static void Render<TArtifact>(
        TextMeasuredBlockLayoutPlan<TArtifact> plan,
        TextParagraphNativeRenderCallbacks<TArtifact> callbacks)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(callbacks);

        foreach (var placement in plan.Layout.Paragraphs)
        {
            var paragraph = plan.RenderText.Paragraphs[placement.ParagraphIndex];
            var artifact = plan.Artifacts[placement.ParagraphIndex];
            if (placement.Bullet is { } bullet)
                callbacks.DrawBullet(bullet);

            switch (placement.RenderRoute)
            {
                case TextParagraphRenderRoute.Math:
                    callbacks.DrawMath(paragraph, placement);
                    break;
                case TextParagraphRenderRoute.Effects:
                    callbacks.DrawEffects(paragraph, placement);
                    break;
                case TextParagraphRenderRoute.Tabs:
                    callbacks.DrawTabs(paragraph, placement);
                    break;
                case TextParagraphRenderRoute.Baseline:
                    callbacks.DrawBaseline(paragraph, placement);
                    break;
                default:
                    callbacks.DrawPlain(paragraph, artifact, placement);
                    break;
            }
        }
    }

    public static void RenderStacked(
        ResolvedTextLayout renderText,
        TextStackedVerticalLayoutPlan plan,
        Action<ResolvedTextLayout, ResolvedRun, TextStackedGlyphPlacement> drawGlyph)
    {
        ArgumentNullException.ThrowIfNull(renderText);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(drawGlyph);

        foreach (var glyph in plan.Glyphs)
        {
            if ((uint)glyph.ParagraphIndex >= (uint)renderText.Paragraphs.Count)
                continue;

            var paragraph = renderText.Paragraphs[glyph.ParagraphIndex];
            if ((uint)glyph.RunIndex >= (uint)paragraph.Runs.Count)
                continue;

            drawGlyph(renderText, paragraph.Runs[glyph.RunIndex], glyph);
        }
    }

    public static bool TryRenderTableCell<TArtifact>(
        ResolvedTextLayout text,
        LayoutRect bounds,
        TableCellAnchor anchor,
        Func<ResolvedParagraph, double, bool, TextNativeMeasurement<TArtifact>> measure,
        Action<TArtifact, TextParagraphPlacement> draw)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(draw);

        if (text.VerticalType != TextVerticalType.Horizontal)
            return false;

        var area = TextLayoutPlanner.GetTextArea(text, bounds);
        var artifacts = new Dictionary<int, TArtifact>();
        var measures = new List<TextParagraphMeasure>();
        for (int index = 0; index < text.Paragraphs.Count; index++)
        {
            var paragraph = text.Paragraphs[index];
            if (paragraph.Runs.Count == 0)
                continue;

            var measurement = measure(paragraph, area.Width, text.Wrap);
            artifacts[index] = measurement.Artifact;
            measures.Add(TextLayoutPlanner.CreateParagraphMeasure(
                index,
                measurement.HeightDip,
                paragraph.SpaceBeforePt,
                paragraph.SpaceAfterPt));
        }

        var plan = TextLayoutPlanner.PlanTableCellText(text, bounds, anchor, measures);
        foreach (var placement in plan.Paragraphs)
            draw(artifacts[placement.ParagraphIndex], placement);

        return true;
    }
}
