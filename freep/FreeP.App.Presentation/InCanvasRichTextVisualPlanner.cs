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
    bool? RightToLeft,
    int? BaselineOffset,
    ThemeAwareColor? Color,
    ImagePart? InlineImage = null,
    long? InlineImageWidthEmu = null,
    long? InlineImageHeightEmu = null,
    InlineOleObjectInfo? InlineOleObject = null,
    InlineTableInfo? InlineTable = null);

public sealed record InCanvasRichTextVisualParagraph(
    int ParagraphIndex,
    int GlobalStart,
    string Text,
    TextAlign Alignment,
    double SpaceBeforeDip,
    double SpaceAfterDip,
    IReadOnlyList<InCanvasRichTextVisualRun> Runs,
    BulletKind BulletKind = BulletKind.None,
    string BulletText = "",
    ImagePart? BulletImage = null,
    string? BulletFontFamily = null,
    double? BulletFontSizePt = null,
    ThemeAwareColor? BulletColor = null,
    double IndentDip = 0,
    double HangingDip = 0,
    bool RightToLeft = false,
    IReadOnlyList<ResolvedTabStop>? TabStops = null)
{
    public int GlobalEnd => GlobalStart + Text.Length;
}

public sealed record InCanvasRichTextVisualPlan(
    string PlainText,
    IReadOnlyList<InCanvasRichTextVisualParagraph> Paragraphs,
    bool Wrap);

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
            return new InCanvasRichTextVisualPlan(
                plainText,
                [EmptyParagraph()],
                body?.Wrap ?? true);

        var paragraphs = new List<InCanvasRichTextVisualParagraph>(body.Paragraphs.Count);
        int globalStart = 0;
        // The editable overlay must show the same marker sequence as the slide renderer and
        // WPF FlowDocument conversion. Keep the continuation state shared rather than allowing
        // a second, simplified counter to drift after restarts and level transitions.
        var markerState = new PresentationListMarkerContinuationState();

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
                    run.RightToLeft,
                    run.BaselineOffset,
                    run.Color,
                    run.InlineImage,
                    run.InlineImageWidthEmu,
                    run.InlineImageHeightEmu,
                    run.InlineOleObject,
                    run.InlineTable));
                runStart += run.Text.Length;
            }

            var seedRun = paragraph.Runs.FirstOrDefault(run => run.Text.Length > 0)
                ?? paragraph.Runs.FirstOrDefault();
            string bulletText = string.Empty;
            if (!paragraph.BulletSuppressed)
            {
                switch (paragraph.BulletKind)
                {
                    case BulletKind.Char:
                        bulletText = paragraph.BulletChar ?? "•";
                        break;
                    case BulletKind.Auto:
                    {
                        int value = markerState.Next(
                            paragraph.Level,
                            paragraph.AutoNumType,
                            paragraph.AutoNumStartAt,
                            paragraph.AutoNumStartAtSpecified);
                        bulletText = markerState.FormatTemplate(
                            paragraph.Level,
                            paragraph.AutoNumType,
                            value,
                            paragraph.AutoNumTextTemplate);
                        break;
                    }
                    default:
                        markerState.Break();
                        break;
                }
            }

            if (paragraph.BulletKind is BulletKind.Char or BulletKind.Image
                || paragraph.BulletSuppressed)
                markerState.Break();

            double indentDip = paragraph.MarginLeftEmu is { } marginLeft
                ? Math.Max(0, marginLeft / EmuPerDip)
                : 0;
            double hangingDip = paragraph.IndentEmu is { } indent && indent < 0
                ? -indent / EmuPerDip
                : 0;
            var tabStops = paragraph.TabStops
                .Where(tabStop => tabStop.PositionEmu > 0)
                .OrderBy(tabStop => tabStop.PositionEmu)
                .Select(tabStop => new ResolvedTabStop
                {
                    PositionDip = tabStop.PositionEmu / EmuPerDip,
                    Alignment = tabStop.Alignment,
                    Leader = tabStop.Leader,
                })
                .ToArray();

            paragraphs.Add(new InCanvasRichTextVisualParagraph(
                paragraphIndex,
                globalStart,
                text,
                paragraph.Align ?? body.DefaultParaAlign ?? TextAlign.Left,
                Math.Max(0, paragraph.SpaceBeforePt ?? 0) * PtToDip,
                Math.Max(0, paragraph.SpaceAfterPt ?? 0) * PtToDip,
                runs,
                paragraph.BulletKind,
                bulletText,
                paragraph.BulletImage,
                paragraph.BulletFontFamily ?? seedRun?.FontFamily,
                paragraph.BulletSizePt ?? ResolveBulletSize(seedRun, paragraph.BulletSizePct),
                paragraph.BulletColor ?? seedRun?.Color,
                indentDip,
                hangingDip,
                paragraph.RightToLeft ?? body.LstStyle?.Resolve(paragraph.Level)?.RightToLeft
                    ?? body.DefaultParaRightToLeft
                    ?? false,
                tabStops));

            globalStart += text.Length + (paragraphIndex + 1 < body.Paragraphs.Count ? 1 : 0);
        }

        return new InCanvasRichTextVisualPlan(plainText, paragraphs, body.Wrap);
    }

    private static InCanvasRichTextVisualParagraph EmptyParagraph() => new(
        0,
        0,
        string.Empty,
        TextAlign.Left,
        0,
        0,
        []);

    private const double EmuPerDip = 9525.0;

    private static double? ResolveBulletSize(Run? seedRun, int? sizePct)
    {
        if (sizePct is > 0 && seedRun?.FontSizePt is > 0)
            return seedRun.FontSizePt.Value * sizePct.Value / 100000.0;
        return seedRun?.FontSizePt;
    }
}
