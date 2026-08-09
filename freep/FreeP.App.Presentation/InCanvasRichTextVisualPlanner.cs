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

    /// <summary>Resolved paragraph left margin, including list-style inheritance.</summary>
    public double MarginLeftDip { get; init; }

    /// <summary>Resolved first-line indent, including list-style inheritance.</summary>
    public double TextIndentDip { get; init; }
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
            var marker = ResolveListMarker(paragraph, inheritedStyle, seedRun, markerState);
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
                Math.Max(0, paragraph.SpaceBeforePt ?? 0) * PtToDip,
                Math.Max(0, paragraph.SpaceAfterPt ?? 0) * PtToDip,
                runs,
                marker.Kind,
                marker.Text,
                marker.Image,
                marker.FontFamily,
                marker.FontSizePt,
                marker.Color,
                indentDip,
                hangingDip,
                paragraph.RightToLeft ?? inheritedStyle?.RightToLeft
                    ?? body.DefaultParaRightToLeft
                    ?? false,
                tabStops)
            {
                MarginLeftDip = marginLeftDip,
                TextIndentDip = textIndentDip,
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

    private const double EmuPerDip = 9525.0;

    private static ResolvedListMarker ResolveListMarker(
        Paragraph paragraph,
        TextStyleLevel? inheritedStyle,
        Run? seedRun,
        PresentationListMarkerContinuationState markerState)
    {
        if (paragraph.BulletSuppressed)
        {
            markerState.Break();
            return ResolvedListMarker.None;
        }

        bool inheritsStyleBullet = paragraph.BulletKind == BulletKind.None
            && inheritedStyle?.BulletKind is { };
        BulletKind kind = inheritsStyleBullet
            ? inheritedStyle!.BulletKind!.Value
            : paragraph.BulletKind;
        string? markerChar = inheritsStyleBullet
            ? inheritedStyle!.BulletChar
            : paragraph.BulletChar;
        AutoNumType autoNumType = inheritsStyleBullet
            ? inheritedStyle!.AutoNumType
            : paragraph.AutoNumType;

        ThemeAwareColor? color = paragraph.BulletColorFollowsText
            ? seedRun?.Color
            : paragraph.BulletColor
                ?? (inheritedStyle?.BulletColorFollowsText == true
                    ? seedRun?.Color
                    : inheritedStyle?.BulletColor ?? seedRun?.Color);
        string? fontFamily = paragraph.BulletFontFollowsText
            ? seedRun?.FontFamily
            : paragraph.BulletFontFamily
                ?? (inheritedStyle?.BulletFontFollowsText == true
                    ? seedRun?.FontFamily
                    : inheritedStyle?.BulletFontFamily ?? seedRun?.FontFamily);

        double? sizePt;
        int? sizePct;
        if (paragraph.BulletSizeFollowsText)
        {
            sizePt = null;
            sizePct = null;
        }
        else if (paragraph.BulletSizePt.HasValue)
        {
            sizePt = paragraph.BulletSizePt;
            sizePct = null;
        }
        else if (paragraph.BulletSizePct.HasValue)
        {
            sizePt = null;
            sizePct = paragraph.BulletSizePct;
        }
        else if (inheritedStyle?.BulletSizeFollowsText == true)
        {
            sizePt = null;
            sizePct = null;
        }
        else
        {
            sizePt = inheritedStyle?.BulletSizePt;
            sizePct = sizePt.HasValue ? null : inheritedStyle?.BulletSizePct;
        }

        double? fontSizePt = sizePt
            ?? (sizePct is > 0 && seedRun?.FontSizePt is > 0
                ? seedRun.FontSizePt.Value * sizePct.Value / 100000.0
                : seedRun?.FontSizePt);
        string text = string.Empty;
        switch (kind)
        {
            case BulletKind.Char:
                text = markerChar ?? "•";
                markerState.Break();
                break;
            case BulletKind.Auto:
            {
                int value = markerState.Next(
                    paragraph.Level,
                    autoNumType,
                    paragraph.AutoNumStartAt,
                    paragraph.AutoNumStartAtSpecified);
                text = markerState.FormatTemplate(
                    paragraph.Level,
                    autoNumType,
                    value,
                    paragraph.AutoNumTextTemplate);
                break;
            }
            default:
                markerState.Break();
                break;
        }

        return new ResolvedListMarker(
            kind,
            text,
            kind == BulletKind.Image ? paragraph.BulletImage : null,
            fontFamily,
            fontSizePt,
            color);
    }

    private readonly record struct ResolvedListMarker(
        BulletKind Kind,
        string Text,
        ImagePart? Image,
        string? FontFamily,
        double? FontSizePt,
        ThemeAwareColor? Color)
    {
        public static ResolvedListMarker None { get; } = new(
            BulletKind.None,
            string.Empty,
            null,
            null,
            null,
            null);
    }
}
