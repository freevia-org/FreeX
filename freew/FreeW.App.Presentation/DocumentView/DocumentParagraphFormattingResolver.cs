using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Resolves effective paragraph formatting through the complete based-on style chain and document
/// defaults. Renderers retain only native projection; Word cascade policy lives here.
/// </summary>
public static class DocumentParagraphFormattingResolver
{
    public static ParagraphFormatting Resolve(TextDocument document, Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paragraph);

        var direct = paragraph.Formatting;
        var style = ResolveStyleChain(document, paragraph.StyleId);
        if (style is null)
            return ApplyDocumentDefaults(direct, document.DefaultParagraph);
        if (direct == ParagraphFormatting.Default)
            return style;

        var defaults = ParagraphFormatting.Default;
        var lineFrom = direct.LineSpacingIsSet
            ? direct
            : style.LineSpacingIsSet
                ? style
                : document.DefaultParagraph.LineSpacingIsSet
                    ? document.DefaultParagraph
                    : direct;
        var shadingFrom = direct.ShadingColorHex is not null ? direct : style;

        return direct with
        {
            ContextualSpacing = direct.ContextualSpacing
                ?? style.ContextualSpacing
                ?? document.DefaultParagraph.ContextualSpacing,
            SuppressAutoHyphens = (direct.SuppressAutoHyphensIsSet || direct.SuppressAutoHyphens)
                ? direct.SuppressAutoHyphens
                : style.SuppressAutoHyphens,
            SuppressAutoHyphensIsSet = direct.SuppressAutoHyphensIsSet
                || direct.SuppressAutoHyphens
                || style.SuppressAutoHyphensIsSet
                || style.SuppressAutoHyphens,
            SuppressLineNumbers = direct.SuppressLineNumbersIsSet
                ? direct.SuppressLineNumbers
                : style.SuppressLineNumbersIsSet && style.SuppressLineNumbers,
            SuppressLineNumbersIsSet = direct.SuppressLineNumbersIsSet || style.SuppressLineNumbersIsSet,
            Alignment = direct.Alignment != defaults.Alignment ? direct.Alignment : style.Alignment,
            SpaceBeforePt = direct.SpaceBeforeIsSet
                ? direct.SpaceBeforePt
                : style.SpaceBeforeIsSet
                    ? style.SpaceBeforePt
                    : direct.SpaceBeforePt,
            SpaceAfterPt = direct.SpaceAfterIsSet
                ? direct.SpaceAfterPt
                : style.SpaceAfterIsSet
                    ? style.SpaceAfterPt
                    : direct.SpaceAfterPt,
            SpaceBeforeIsSet = direct.SpaceBeforeIsSet || style.SpaceBeforeIsSet,
            SpaceAfterIsSet = direct.SpaceAfterIsSet || style.SpaceAfterIsSet,
            LineSpacing = lineFrom.LineSpacing,
            LineRule = lineFrom.LineRule,
            LineHeightPt = lineFrom.LineHeightPt,
            LineSpacingIsSet = direct.LineSpacingIsSet
                || style.LineSpacingIsSet
                || document.DefaultParagraph.LineSpacingIsSet,
            IndentLeftPt = direct.IndentLeftPt != defaults.IndentLeftPt
                ? direct.IndentLeftPt
                : style.IndentLeftPt,
            IndentRightPt = direct.IndentRightPt != defaults.IndentRightPt
                ? direct.IndentRightPt
                : style.IndentRightPt,
            FirstLineIndentPt = direct.FirstLineIndentPt != defaults.FirstLineIndentPt
                ? direct.FirstLineIndentPt
                : style.FirstLineIndentPt,
            Border = direct.Border ?? style.Border,
            ShadingColorHex = shadingFrom.ShadingColorHex,
            ShadingPattern = shadingFrom.ShadingPattern,
            TabStops = MergeTabStops(style.TabStops, direct.TabStops),
        };
    }

    private static ParagraphFormatting ApplyDocumentDefaults(
        ParagraphFormatting direct,
        ParagraphFormatting documentDefaults)
    {
        if (direct.LineSpacingIsSet || !documentDefaults.LineSpacingIsSet)
        {
            return direct with
            {
                ContextualSpacing = direct.ContextualSpacing ?? documentDefaults.ContextualSpacing,
            };
        }

        return direct with
        {
            ContextualSpacing = direct.ContextualSpacing ?? documentDefaults.ContextualSpacing,
            LineSpacing = documentDefaults.LineSpacing,
            LineRule = documentDefaults.LineRule,
            LineHeightPt = documentDefaults.LineHeightPt,
            LineSpacingIsSet = true,
        };
    }

    private static ParagraphFormatting? ResolveStyleChain(TextDocument document, string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
            return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<DocumentStyle>();
        var current = styleId;
        while (!string.IsNullOrWhiteSpace(current)
            && seen.Add(current)
            && document.Styles.TryGetValue(current, out var style))
        {
            chain.Add(style);
            current = style.BasedOnStyleId;
        }

        ParagraphFormatting? resolved = null;
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            resolved = resolved is null
                ? chain[index].Paragraph
                : OverlayStyle(resolved, chain[index].Paragraph);
        }
        return resolved;
    }

    private static ParagraphFormatting OverlayStyle(
        ParagraphFormatting inherited,
        ParagraphFormatting over)
    {
        if (over == ParagraphFormatting.Default)
            return inherited;

        var defaults = ParagraphFormatting.Default;
        var lineFrom = over.LineSpacingIsSet ? over : inherited;
        var shadingFrom = over.ShadingColorHex is not null ? over : inherited;
        return over with
        {
            Alignment = over.Alignment != defaults.Alignment ? over.Alignment : inherited.Alignment,
            Rtl = inherited.Rtl || over.Rtl,
            SpaceBeforePt = over.SpaceBeforeIsSet ? over.SpaceBeforePt : inherited.SpaceBeforePt,
            SpaceAfterPt = over.SpaceAfterIsSet ? over.SpaceAfterPt : inherited.SpaceAfterPt,
            SpaceBeforeIsSet = inherited.SpaceBeforeIsSet || over.SpaceBeforeIsSet,
            SpaceAfterIsSet = inherited.SpaceAfterIsSet || over.SpaceAfterIsSet,
            BeforeAutoSpacing = over.SpaceBeforeIsSet ? over.BeforeAutoSpacing : inherited.BeforeAutoSpacing,
            AfterAutoSpacing = over.SpaceAfterIsSet ? over.AfterAutoSpacing : inherited.AfterAutoSpacing,
            ContextualSpacing = over.ContextualSpacing ?? inherited.ContextualSpacing,
            LineSpacing = lineFrom.LineSpacing,
            LineRule = lineFrom.LineRule,
            LineHeightPt = lineFrom.LineHeightPt,
            LineSpacingIsSet = inherited.LineSpacingIsSet || over.LineSpacingIsSet,
            IndentLeftPt = over.IndentLeftPt != defaults.IndentLeftPt
                ? over.IndentLeftPt
                : inherited.IndentLeftPt,
            IndentRightPt = over.IndentRightPt != defaults.IndentRightPt
                ? over.IndentRightPt
                : inherited.IndentRightPt,
            FirstLineIndentPt = over.FirstLineIndentPt != defaults.FirstLineIndentPt
                ? over.FirstLineIndentPt
                : inherited.FirstLineIndentPt,
            ListKind = over.ListKind != defaults.ListKind ? over.ListKind : inherited.ListKind,
            ListLevel = over.ListLevel != defaults.ListLevel ? over.ListLevel : inherited.ListLevel,
            ListStartOverride = over.ListStartOverride ?? inherited.ListStartOverride,
            Border = over.Border ?? inherited.Border,
            PageBreakBefore = inherited.PageBreakBefore || over.PageBreakBefore,
            KeepWithNext = inherited.KeepWithNext || over.KeepWithNext,
            KeepLinesTogether = inherited.KeepLinesTogether || over.KeepLinesTogether,
            WidowControl = over.WidowControlIsSet ? over.WidowControl : inherited.WidowControl,
            WidowControlIsSet = inherited.WidowControlIsSet || over.WidowControlIsSet,
            SuppressAutoHyphens = (over.SuppressAutoHyphensIsSet || over.SuppressAutoHyphens)
                ? over.SuppressAutoHyphens
                : inherited.SuppressAutoHyphens,
            SuppressAutoHyphensIsSet = inherited.SuppressAutoHyphensIsSet
                || inherited.SuppressAutoHyphens
                || over.SuppressAutoHyphensIsSet
                || over.SuppressAutoHyphens,
            SuppressLineNumbers = over.SuppressLineNumbersIsSet
                ? over.SuppressLineNumbers
                : inherited.SuppressLineNumbers,
            SuppressLineNumbersIsSet = inherited.SuppressLineNumbersIsSet || over.SuppressLineNumbersIsSet,
            ShadingColorHex = shadingFrom.ShadingColorHex,
            ShadingPattern = shadingFrom.ShadingPattern,
            TabStops = MergeTabStops(inherited.TabStops, over.TabStops),
        };
    }

    private static IReadOnlyList<TabStop> MergeTabStops(
        IReadOnlyList<TabStop> inherited,
        IReadOnlyList<TabStop> operations)
    {
        var effective = inherited.Where(stop => !stop.IsClear).ToList();
        foreach (var operation in operations)
        {
            effective.RemoveAll(stop => Math.Abs(stop.PositionPt - operation.PositionPt) <= 0.01);
            if (!operation.IsClear)
                effective.Add(operation);
        }

        effective.Sort((left, right) => left.PositionPt.CompareTo(right.PositionPt));
        return effective;
    }
}

public sealed record DocumentEffectiveFormatting(
    RunFormatting Run,
    ParagraphFormatting Paragraph);

/// <summary>Builds the effective formatting state at a renderer-supplied model paragraph/offset.</summary>
public static class DocumentFormattingProbePlanner
{
    public static DocumentEffectiveFormatting Resolve(
        TextDocument document,
        Paragraph paragraph,
        int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paragraph);

        var run = RevisionEditPlanner.RunAtOffset(paragraph, offset);
        var rawRun = run?.Formatting ?? RunFormatting.Default;
        return new DocumentEffectiveFormatting(
            DocumentRunFormattingResolver.Resolve(document, paragraph, rawRun, run?.StyleId),
            DocumentParagraphFormattingResolver.Resolve(document, paragraph));
    }
}
