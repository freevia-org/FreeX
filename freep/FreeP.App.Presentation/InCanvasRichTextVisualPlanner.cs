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

public sealed record InCanvasInheritedRunStylePlan(
    bool IsPresent,
    double? FontSizePt,
    bool? Bold,
    bool? Italic,
    string? FontFamily,
    ThemeAwareColor? Color)
{
    public static InCanvasInheritedRunStylePlan Empty { get; } =
        new(false, null, null, null, null, null);
}

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

    /// <summary>Resolved paragraph left margin, including list-style inheritance.</summary>
    public double MarginLeftDip { get; init; }

    /// <summary>Resolved first-line indent, including list-style inheritance.</summary>
    public double TextIndentDip { get; init; }

    public InCanvasInheritedRunStylePlan InheritedRunStyle { get; init; } =
        InCanvasInheritedRunStylePlan.Empty;
}

public sealed record InCanvasRichTextVisualPlan(
    string PlainText,
    IReadOnlyList<InCanvasRichTextVisualParagraph> Paragraphs,
    bool Wrap);

/// <summary>
/// Framework-neutral visual contract for rich editors. It keeps paragraph and marker resolution,
/// run styling, and model text offsets identical across WPF and Avalonia hosts.
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
            var inheritedStyle = body.LstStyle?.Resolve(paragraph.Level);
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
            var marker = PresentationListMarkerPlanner.Resolve(
                paragraph,
                inheritedStyle,
                markerState);
            long effectiveMarginLeftEmu = paragraph.MarginLeftEmu
                ?? inheritedStyle?.MarginLeftEmu
                ?? 0;
            long effectiveIndentEmu = paragraph.IndentEmu
                ?? inheritedStyle?.IndentEmu
                ?? 0;
            double marginLeftDip = effectiveMarginLeftEmu / EmuPerDip;
            double textIndentDip = effectiveIndentEmu / EmuPerDip;
            double indentDip = effectiveMarginLeftEmu > 0
                ? marginLeftDip
                : 0;
            double hangingDip = effectiveIndentEmu < 0
                ? -textIndentDip
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
                paragraph.Align ?? inheritedStyle?.Align ?? body.DefaultParaAlign ?? TextAlign.Left,
                (paragraph.SpaceBeforePt ?? 0) * PtToDip,
                (paragraph.SpaceAfterPt ?? 0) * PtToDip,
                runs,
                marker.Kind,
                marker.Text,
                marker.Image,
                marker.FontFamily ?? seedRun?.FontFamily,
                marker.ResolveFontSizePt(seedRun?.FontSizePt),
                marker.Color ?? seedRun?.Color,
                indentDip,
                hangingDip,
                paragraph.RightToLeft ?? inheritedStyle?.RightToLeft
                    ?? body.DefaultParaRightToLeft
                    ?? false,
                tabStops)
            {
                MarginLeftDip = marginLeftDip,
                TextIndentDip = textIndentDip,
                InheritedRunStyle = BuildInheritedRunStyle(inheritedStyle),
            });

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

    private static InCanvasInheritedRunStylePlan BuildInheritedRunStyle(TextStyleLevel? style)
    {
        if (style is null)
            return InCanvasInheritedRunStylePlan.Empty;

        var fontFamily = !string.IsNullOrWhiteSpace(style.LatinFont)
            && !style.LatinFont.StartsWith("+", StringComparison.Ordinal)
                ? style.LatinFont
                : null;
        return new InCanvasInheritedRunStylePlan(
            true,
            style.FontSizePt,
            style.Bold,
            style.Italic,
            fontFamily,
            style.Color);
    }

    private const double EmuPerDip = 9525.0;
}
