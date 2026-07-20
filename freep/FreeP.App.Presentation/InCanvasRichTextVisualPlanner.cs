using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record InCanvasRichTextVisualRun(
    int Start,
    int Length,
    string Text,
    string? FontFamily,
    double? FontSizePt,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikethrough,
    int? BaselineOffset,
    ThemeAwareColor? Color);

public sealed record InCanvasRichTextVisualParagraph(
    int ParagraphIndex,
    int GlobalStart,
    string Text,
    TextAlign Alignment,
    double SpaceBeforeDip,
    double SpaceAfterDip,
    IReadOnlyList<InCanvasRichTextVisualRun> Runs)
{
    public int GlobalEnd => GlobalStart + Text.Length;
}

public sealed record InCanvasRichTextVisualPlan(
    string PlainText,
    IReadOnlyList<InCanvasRichTextVisualParagraph> Paragraphs);

/// <summary>
/// Framework-neutral visual contract for in-canvas rich editors. It keeps paragraph alignment,
/// spacing, run styling, and model text offsets identical across hosts without changing WPF policy.
/// </summary>
public static class InCanvasRichTextVisualPlanner
{
    private const double PtToDip = 96.0 / 72.0;

    public static InCanvasRichTextVisualPlan Create(TextBody? body)
    {
        string plainText = InCanvasTextEditPlanner.ExtractPlainText(body);
        if (body is null || body.Paragraphs.Count == 0)
            return new InCanvasRichTextVisualPlan(plainText, [EmptyParagraph()]);

        var paragraphs = new List<InCanvasRichTextVisualParagraph>(body.Paragraphs.Count);
        int globalStart = 0;

        for (int paragraphIndex = 0; paragraphIndex < body.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = body.Paragraphs[paragraphIndex];
            string text = string.Concat(paragraph.Runs.Select(run => run.Text));
            var runs = new List<InCanvasRichTextVisualRun>(paragraph.Runs.Count);
            int runStart = 0;
            foreach (var run in paragraph.Runs)
            {
                runs.Add(new InCanvasRichTextVisualRun(
                    runStart,
                    run.Text.Length,
                    run.Text,
                    run.FontFamily,
                    run.FontSizePt,
                    run.Bold,
                    run.Italic,
                    run.Underline,
                    run.Strikethrough,
                    run.BaselineOffset,
                    run.Color));
                runStart += run.Text.Length;
            }

            paragraphs.Add(new InCanvasRichTextVisualParagraph(
                paragraphIndex,
                globalStart,
                text,
                paragraph.Align ?? body.DefaultParaAlign ?? TextAlign.Left,
                Math.Max(0, paragraph.SpaceBeforePt ?? 0) * PtToDip,
                Math.Max(0, paragraph.SpaceAfterPt ?? 0) * PtToDip,
                runs));

            globalStart += text.Length + (paragraphIndex + 1 < body.Paragraphs.Count ? 1 : 0);
        }

        return new InCanvasRichTextVisualPlan(plainText, paragraphs);
    }

    private static InCanvasRichTextVisualParagraph EmptyParagraph() => new(
        0,
        0,
        string.Empty,
        TextAlign.Left,
        0,
        0,
        []);
}
