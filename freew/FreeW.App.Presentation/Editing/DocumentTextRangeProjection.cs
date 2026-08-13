using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>Projects renderer-native body selections into safe model text ranges.</summary>
public static class DocumentTextRangeProjection
{
    public static bool TryProject(
        TextDocument document,
        int blockIndex,
        int startOffset,
        int endOffset,
        out DocumentTextRange range,
        Func<Paragraph, bool>? isEligible = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        range = default;
        if (blockIndex < 0
            || blockIndex >= document.Blocks.Count
            || document.Blocks[blockIndex] is not Paragraph paragraph
            || isEligible is not null && !isEligible(paragraph))
        {
            return false;
        }

        var textLength = paragraph.PlainText.Length;
        range = new DocumentTextRange(
            new DocumentTextPosition(blockIndex, Math.Clamp(startOffset, 0, textLength)),
            new DocumentTextPosition(blockIndex, Math.Clamp(endOffset, 0, textLength)));
        return true;
    }
}
