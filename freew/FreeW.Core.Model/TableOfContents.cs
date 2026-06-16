using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free generation of a document Table of Contents from its heading outline (see
/// <see cref="DocumentOutline"/>). Lives in the model project so it is unit-testable without any UI.
/// <para>
/// <see cref="Build"/> produces ordinary styled <see cref="Paragraph"/>s — a "Contents" heading
/// followed by one entry per <see cref="OutlineEntry"/>, each carrying the heading's text and a
/// left indent proportional to its outline level. The paragraphs use dedicated TOC style ids
/// (<see cref="HeadingStyleId"/> and <c>TOC1</c>/<c>TOC2</c>/…) so they:
/// </para>
/// <list type="bullet">
/// <item>render with distinct TOC formatting once <see cref="EnsureStyles"/> has registered them;</item>
/// <item>round-trip through docx as normal styled paragraphs (no I/O changes needed); and</item>
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
    /// document order. Each entry's text is the heading's text and its <see cref="ParagraphFormatting.IndentLeftPt"/>
    /// is <c>level * </c><see cref="IndentPerLevelPt"/>; its style id is <c>TOC{level}</c> (clamped to
    /// <see cref="MaxStyledLevel"/>). A document with no headings yields just the heading paragraph.
    /// Deterministic and side-effect free — it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = new List<Paragraph>
        {
            new(HeadingText) { StyleId = HeadingStyleId }
        };

        foreach (var entry in DocumentOutline.Of(document))
        {
            var styledLevel = Math.Clamp(entry.Level, 0, MaxStyledLevel);
            paragraphs.Add(new Paragraph(entry.Text)
            {
                StyleId = EntryStyleId(styledLevel),
                Formatting = ParagraphFormatting.Default with
                {
                    IndentLeftPt = entry.Level * IndentPerLevelPt
                }
            });
        }

        return paragraphs;
    }

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

    /// <summary>True when <paramref name="block"/> is a paragraph carrying a TOC style (see <see cref="IsTocStyleId"/>).</summary>
    public static bool IsTocParagraph(Block block) =>
        block is Paragraph paragraph && IsTocStyleId(paragraph.StyleId);

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
