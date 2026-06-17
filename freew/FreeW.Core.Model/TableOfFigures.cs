namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free generation of a document Table of Figures (or Table of Tables) from its captions
/// (see <see cref="Captions"/>). Lives in the model project so it is unit-testable without any UI,
/// mirroring <see cref="TableOfContents"/> and <see cref="DocumentIndex"/>.
/// <para>
/// <see cref="Build"/> produces ordinary styled <see cref="Paragraph"/>s — a "Table of Figures"
/// (or "Table of Tables") heading followed by one paragraph per caption of the requested
/// <see cref="CaptionLabel"/>, in document order, each carrying the caption's text. The paragraphs use
/// dedicated style ids (<see cref="HeadingStyleId"/> and <see cref="EntryStyleId"/>) so they:
/// </para>
/// <list type="bullet">
/// <item>render with distinct formatting once <see cref="EnsureStyles"/> has registered them;</item>
/// <item>round-trip through docx as normal styled paragraphs (no I/O changes needed); and</item>
/// <item>act as a marker so a "refresh" can locate and replace a previously inserted region via
/// <see cref="IsTableOfFiguresParagraph"/>.</item>
/// </list>
/// </summary>
public static class TableOfFigures
{
    /// <summary>Style id of the table-of-figures heading paragraph.</summary>
    public const string HeadingStyleId = "TableOfFiguresHeading";

    /// <summary>Style id carried by each generated table-of-figures entry paragraph.</summary>
    public const string EntryStyleId = "TableOfFiguresEntry";

    /// <summary>The heading text for a table of the given <paramref name="label"/>'s captions.</summary>
    public static string HeadingText(CaptionLabel label) => label switch
    {
        CaptionLabel.Figure => "Table of Figures",
        CaptionLabel.Table => "Table of Tables",
        _ => "Table of " + Captions.LabelText(label) + "s"
    };

    /// <summary>
    /// Builds the table-of-figures paragraphs for <paramref name="document"/>: a heading
    /// (<see cref="HeadingStyleId"/>, text from <see cref="HeadingText"/>) followed by one paragraph per
    /// caption of <paramref name="label"/> found in document order, each carrying the caption's text and
    /// the <see cref="EntryStyleId"/> style. A document with no matching captions yields just the heading
    /// paragraph. Deterministic and side-effect free — it never mutates <paramref name="document"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> Build(TextDocument document, CaptionLabel label = CaptionLabel.Figure)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphs = new List<Paragraph>
        {
            new(HeadingText(label)) { StyleId = HeadingStyleId }
        };

        var prefix = Captions.LabelText(label) + " ";
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph
                && Captions.IsCaptionParagraph(paragraph)
                && paragraph.PlainText.StartsWith(prefix, StringComparison.Ordinal))
            {
                paragraphs.Add(new Paragraph(paragraph.PlainText) { StyleId = EntryStyleId });
            }
        }

        return paragraphs;
    }

    /// <summary>
    /// True when <paramref name="styleId"/> is one of the table-of-figures styles produced by
    /// <see cref="Build"/> (the heading style or the entry style). Used to recognise a previously inserted
    /// region so a refresh can remove it.
    /// </summary>
    public static bool IsTableOfFiguresStyleId(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return false;
        return string.Equals(styleId, HeadingStyleId, StringComparison.Ordinal)
            || string.Equals(styleId, EntryStyleId, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="block"/> is a paragraph carrying a table-of-figures style (see
    /// <see cref="IsTableOfFiguresStyleId"/>).
    /// </summary>
    public static bool IsTableOfFiguresParagraph(Block block) =>
        block is Paragraph paragraph && IsTableOfFiguresStyleId(paragraph.StyleId);

    /// <summary>
    /// Registers the table-of-figures styles (<see cref="HeadingStyleId"/> and <see cref="EntryStyleId"/>)
    /// in <paramref name="document"/>'s style catalog if they are not already present, so the inserted
    /// paragraphs resolve their formatting. Idempotent — existing styles are left untouched.
    /// </summary>
    public static void EnsureStyles(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Styles.TryAdd(HeadingStyleId, new DocumentStyle
        {
            Id = HeadingStyleId,
            Name = "Table of Figures Heading",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 6 }
        });

        document.Styles.TryAdd(EntryStyleId, new DocumentStyle
        {
            Id = EntryStyleId,
            Name = "Table of Figures Entry",
            BasedOnStyleId = "Normal",
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 2 }
        });
    }
}
