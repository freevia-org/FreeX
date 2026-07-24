using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Shared paragraph-metadata contract for in-canvas rich-text paragraph edits.
/// A WPF Enter operation can create more FlowDocument paragraphs than existed in
/// the source model; PowerPoint carries the split paragraph's list metadata forward.
/// A join keeps the leading paragraph's metadata.
/// </summary>
public static class InCanvasRichTextParagraphEditPlanner
{
    public static Paragraph CloneParagraphMetadata(Paragraph source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var copy = new Paragraph
        {
            Align = source.Align,
            Level = source.Level,
            BulletKind = source.BulletKind,
            BulletSuppressed = source.BulletSuppressed,
            BulletChar = source.BulletChar,
            BulletImage = source.BulletImage,
            AutoNumType = source.AutoNumType,
            AutoNumStartAt = source.AutoNumStartAt,
            MarginLeftEmu = source.MarginLeftEmu,
            IndentEmu = source.IndentEmu,
            BulletColor = source.BulletColor,
            BulletColorFollowsText = source.BulletColorFollowsText,
            BulletSizePct = source.BulletSizePct,
            BulletSizePt = source.BulletSizePt,
            BulletSizeFollowsText = source.BulletSizeFollowsText,
            BulletFontFamily = source.BulletFontFamily,
            BulletFontFollowsText = source.BulletFontFollowsText,
            SpaceBeforePt = source.SpaceBeforePt,
            SpaceAfterPt = source.SpaceAfterPt,
        };

        foreach (var tabStop in source.TabStops)
            copy.TabStops.Add(new TabStop
            {
                PositionEmu = tabStop.PositionEmu,
                Alignment = tabStop.Alignment,
            });

        return copy;
    }

    public static int ResolveSourceParagraphIndex(
        int sourceParagraphCount,
        int editedParagraphCount,
        int editedParagraphIndex)
    {
        if (sourceParagraphCount <= 0 || editedParagraphIndex < 0)
            return -1;

        if (sourceParagraphCount == 1 || editedParagraphCount <= 1)
            return 0;

        return Math.Min(editedParagraphIndex, sourceParagraphCount - 1);
    }

    public static void ApplySourceParagraphMetadata(
        IReadOnlyList<Paragraph> sourceParagraphs,
        IList<Paragraph> editedParagraphs)
    {
        ArgumentNullException.ThrowIfNull(sourceParagraphs);
        ArgumentNullException.ThrowIfNull(editedParagraphs);

        for (int index = 0; index < editedParagraphs.Count; index++)
        {
            int sourceIndex = ResolveSourceParagraphIndex(
                sourceParagraphs.Count,
                editedParagraphs.Count,
                index);
            if (sourceIndex < 0)
                continue;

            var source = sourceParagraphs[sourceIndex];
            var edited = editedParagraphs[index];
            var metadata = CloneParagraphMetadata(source);
            var runs = edited.Runs
                .Select(TextBodyModelCloner.CloneRun)
                .ToList();

            metadata.Runs.Clear();
            foreach (var run in runs)
                metadata.Runs.Add(run);

            editedParagraphs[index] = metadata;
        }
    }
}
