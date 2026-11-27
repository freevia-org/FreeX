using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>Display-only list geometry and contextual gaps between body paragraphs.</summary>
public static class DocumentBodyParagraphLayoutPlanner
{
    public const double NativeListIndentStepPt = 18;

    public static double AdditionalListInsetPt(ParagraphFormatting formatting, int level)
    {
        // Imported numbering and authored paragraph/style indents already describe an absolute
        // text position. Only FreeW's unpositioned native lists need the application default inset.
        return HasNumberingGeometry(formatting) || formatting.IndentLeftIsSet
            || formatting.IndentLeftPt != 0 || formatting.FirstLineIndentIsSet
            ? 0
            : NativeListIndentStepPt * (Math.Clamp(level, 0, 8) + 1);
    }

    public static double TextFirstLineIndentPt(ParagraphFormatting formatting) =>
        HasNumberingGeometry(formatting) && formatting.FirstLineIndentPt < 0
            ? 0 // Numbering's hanging area contains the marker; every text line starts at left indent.
            : formatting.FirstLineIndentPt;

    public static double MarkerOffsetFromTextPt(
        ParagraphFormatting formatting, double markerWidthPt, double nativeGapPt) =>
        HasNumberingGeometry(formatting) && formatting.FirstLineIndentPt < 0
            ? formatting.FirstLineIndentPt
            : -markerWidthPt - nativeGapPt;

    public static DocumentBodyParagraphSpacingPlan Spacing(
        TextDocument document, int blockIndex, ParagraphFormatting formatting)
    {
        ArgumentNullException.ThrowIfNull(document);
        if ((uint)blockIndex >= (uint)document.Blocks.Count
            || document.Blocks[blockIndex] is not Paragraph paragraph)
            throw new ArgumentOutOfRangeException(nameof(blockIndex));

        var before = formatting.SpaceBeforePt;
        var after = formatting.SpaceAfterPt;
        if (blockIndex > 0 && document.Blocks[blockIndex - 1] is Paragraph previous
            && SameStyle(document, previous, paragraph))
        {
            var previousFormatting = DocumentParagraphFormattingResolver.Resolve(document, previous);
            if (previousFormatting.ContextualSpacing is true || formatting.ContextualSpacing is true)
                before = ContextualGap(previousFormatting, formatting);
        }
        if (blockIndex + 1 < document.Blocks.Count && document.Blocks[blockIndex + 1] is Paragraph next
            && SameStyle(document, paragraph, next))
        {
            var nextFormatting = DocumentParagraphFormattingResolver.Resolve(document, next);
            if (formatting.ContextualSpacing is true || nextFormatting.ContextualSpacing is true)
                after = 0; // The next paragraph owns the complete contextual boundary gap.
        }
        return new(before, after);
    }

    private static bool HasNumberingGeometry(ParagraphFormatting formatting) =>
        formatting.ListLevelIndentLeftPt is not null || formatting.ListLevelFirstLineIndentPt is not null;

    private static bool SameStyle(TextDocument document, Paragraph left, Paragraph right)
    {
        var defaultStyle = DocumentStyle.FindDefaultParagraphStyleId(document);
        var leftStyle = string.IsNullOrWhiteSpace(left.StyleId) ? defaultStyle : left.StyleId;
        var rightStyle = string.IsNullOrWhiteSpace(right.StyleId) ? defaultStyle : right.StyleId;
        return string.Equals(leftStyle, rightStyle, StringComparison.Ordinal);
    }

    private static double ContextualGap(ParagraphFormatting previous, ParagraphFormatting current)
    {
        // ISO 29500 contextualSpacing subtracts each enabled side from the normal maximum gap.
        // Merely dropping enabled sides before taking max is wrong when only one side is enabled:
        // after10(contextual) + before12(noncontextual) produces2pt, not12pt.
        var after = Math.Max(0, previous.SpaceAfterPt);
        var before = Math.Max(0, current.SpaceBeforePt);
        return Math.Max(0, Math.Max(after, before)
            - (previous.ContextualSpacing is true ? after : 0)
            - (current.ContextualSpacing is true ? before : 0));
    }
}

public readonly record struct DocumentBodyParagraphSpacingPlan(double BeforePt, double AfterPt);
