using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free generation of a document Table of Contents from its heading outline (see
/// <see cref="DocumentOutline"/>). Lives in the model project so it is unit-testable without any UI.
/// <para>
/// <see cref="Build"/> produces ordinary styled <see cref="Paragraph"/>s — a "Contents" heading
/// followed by one entry per <see cref="OutlineEntry"/>, each carrying the heading's text, a tab,
/// a generated page reference, and a left indent proportional to its outline level. The paragraphs use dedicated TOC style ids
/// (<see cref="HeadingStyleId"/> and <c>TOC1</c>/<c>TOC2</c>/…) so they:
/// </para>
/// <list type="bullet">
/// <item>render with distinct TOC formatting once <see cref="EnsureStyles"/> has registered them;</item>
/// <item>round-trip through docx as normal styled paragraphs, while imported native fields retain
/// semantic ownership independently;</item>
/// <item>act as a marker so a "refresh" can locate and replace a previously inserted TOC region
/// via <see cref="IsTocParagraph"/>.</item>
/// </list>
/// </summary>
public static class TableOfContents
{
    /// <summary>Style id of the TOC's "Contents" heading paragraph.</summary>
    public const string HeadingStyleId = "TOCHeading";

    /// <summary>Display text of the TOC's heading paragraph.</summary>
    public const string HeadingText = "Contents";

    /// <summary>Prefix of a per-entry TOC style id; the outline level is appended (e.g. <c>TOC2</c>).</summary>
    public const string EntryStylePrefix = "TOC";

    /// <summary>
    /// Default right-tab position for generated entries: Word's writable default letter-page width
    /// (8.5in page with 1in left/right margins).
    /// </summary>
    public const double DefaultEntryRightTabStopPt = 468;

    /// <summary>Left indent applied per outline level (points). Level 0 has no indent.</summary>
    public const double IndentPerLevelPt = 18;

    /// <summary>
    /// Highest level for which a distinct <c>TOCn</c> style is registered/returned; deeper entries
    /// reuse this style id while still indenting by their true level.
    /// </summary>
    public const int MaxStyledLevel = 3;

    /// <summary>
    /// Builds the Table of Contents paragraphs for <paramref name="document"/>: a "Contents" heading
    /// (<see cref="HeadingStyleId"/>) followed by one paragraph per heading in the document outline, in
    /// document order. Each entry's runs are the heading text, a tab, and an explicit-break-based page
    /// reference; its <see cref="ParagraphFormatting.IndentLeftPt"/> is <c>level * </c><see cref="IndentPerLevelPt"/>
    /// and it carries a right-aligned dotted leader tab stop at the writable page width. Its style id is <c>TOC{level}</c> (clamped to
    /// <see cref="MaxStyledLevel"/>). A document with no headings yields just the heading paragraph.
    /// Deterministic and side-effect free — it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(
        TextDocument document,
        Func<int, string?>? pageTextOf = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = new List<Paragraph>
        {
            new(HeadingText) { StyleId = HeadingStyleId }
        };

        var outline = DocumentOutline.Of(document);
        var pageReferences = BuildPageReferences(document, outline);
        var entryRightTabStopPt = EntryRightTabStopPt(document.Page);

        foreach (var entry in outline)
        {
            var pageText = pageTextOf?.Invoke(entry.BlockIndex);
            if (string.IsNullOrEmpty(pageText))
                pageText = pageReferences[entry.BlockIndex].ToString(CultureInfo.InvariantCulture);

            paragraphs.Add(CreateEntryParagraph(entry, pageText, entryRightTabStopPt));
        }

        return paragraphs;
    }

    private static Paragraph CreateEntryParagraph(
        OutlineEntry entry,
        string pageText,
        double entryRightTabStopPt)
    {
        var styledLevel = Math.Clamp(entry.Level, 0, MaxStyledLevel);
        var paragraph = new Paragraph
        {
            StyleId = EntryStyleId(styledLevel),
            Formatting = ParagraphFormatting.Default with
            {
                IndentLeftPt = entry.Level * IndentPerLevelPt,
                TabStops =
                [
                    new TabStop(
                        Math.Max(0, entryRightTabStopPt),
                        TabStopAlignment.Right,
                        TabLeader.Dots)
                ]
            }
        };
        paragraph.Runs.Add(new Run(entry.Text));
        paragraph.Runs.Add(new Run("\t"));
        paragraph.Runs.Add(new Run(pageText));
        return paragraph;
    }

    private static Dictionary<int, int> BuildPageReferences(
        TextDocument document,
        IReadOnlyList<OutlineEntry> outline)
    {
        var headingBlockIndexes = outline.Select(entry => entry.BlockIndex).ToHashSet();
        var pageReferences = new Dictionary<int, int>();
        var pageNumber = 1;

        for (var i = 0; i < document.Blocks.Count; i++)
        {
            if (document.Blocks[i] is not Paragraph paragraph)
                continue;

            if (paragraph.Formatting.PageBreakBefore)
                pageNumber++;

            if (headingBlockIndexes.Contains(i))
                pageReferences[i] = pageNumber;

            foreach (var run in paragraph.Runs)
                if (run.IsPageBreak)
                    pageNumber++;

            if (paragraph.SectionBreak is { } sectionBreak)
                pageNumber = AdvanceForSectionBreak(pageNumber, sectionBreak.BreakKind);
        }

        return pageReferences;
    }

    private static int AdvanceForSectionBreak(int pageNumber, SectionBreakKind breakKind) => breakKind switch
    {
        SectionBreakKind.NextPage => pageNumber + 1,
        SectionBreakKind.EvenPage => pageNumber % 2 == 0 ? pageNumber + 2 : pageNumber + 1,
        SectionBreakKind.OddPage => pageNumber % 2 == 0 ? pageNumber + 1 : pageNumber + 2,
        _ => pageNumber
    };

    private static double EntryRightTabStopPt(PageSettings page) =>
        Math.Max(0, page.WidthPt - page.MarginLeftPt - page.MarginRightPt);

    /// <summary>The per-entry TOC style id for an outline <paramref name="level"/> (e.g. 2 → <c>TOC2</c>).</summary>
    public static string EntryStyleId(int level) =>
        EntryStylePrefix + level.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// True when <paramref name="styleId"/> is one of the TOC styles produced by <see cref="Build"/>
    /// (the heading style or a <c>TOCn</c> entry style). Used to recognise a previously inserted TOC
    /// region so a refresh can remove it.
    /// </summary>
    public static bool IsTocStyleId(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return false;
        if (string.Equals(styleId, HeadingStyleId, StringComparison.Ordinal))
            return true;
        return styleId.StartsWith(EntryStylePrefix, StringComparison.Ordinal)
            && int.TryParse(
                styleId.AsSpan(EntryStylePrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var level)
            && level >= 0;
    }

    /// <summary>
    /// True when <paramref name="block"/> is a paragraph carrying a TOC style or owned by a native
    /// multi-paragraph <c>TOC</c> field.
    /// </summary>
    public static bool IsTocParagraph(Block block) =>
        block is Paragraph paragraph
        && (paragraph.SpanningFieldOwner is { Keyword: "TOC" }
            || paragraph.Runs.Any(run => run.ComplexField is { Keyword: "TOC" })
            || IsTocStyleId(paragraph.StyleId));

    /// <summary>
    /// Registers the TOC styles (<see cref="HeadingStyleId"/> and <c>TOC1</c>..<c>TOC{MaxStyledLevel}</c>)
    /// in <paramref name="document"/>'s style catalog if they are not already present, so the inserted
    /// TOC paragraphs resolve their formatting. Idempotent — existing styles are left untouched.
    /// </summary>
    public static void EnsureStyles(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Styles.TryAdd(HeadingStyleId, new DocumentStyle
        {
            Id = HeadingStyleId,
            Name = "TOC Heading",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 6 }
        });

        for (var level = 1; level <= MaxStyledLevel; level++)
        {
            var id = EntryStyleId(level);
            document.Styles.TryAdd(id, new DocumentStyle
            {
                Id = id,
                Name = "TOC " + level.ToString(CultureInfo.InvariantCulture),
                BasedOnStyleId = "Normal",
                Paragraph = new ParagraphFormatting
                {
                    SpaceAfterPt = 2,
                    IndentLeftPt = level * IndentPerLevelPt
                }
            });
        }
    }
}
