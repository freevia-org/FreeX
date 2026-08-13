namespace FreeW.Core.Model;

/// <summary>
/// Word-style Design &gt; Document Formatting paragraph-spacing presets. A preset rewrites the document
/// default and built-in paragraph style spacing while leaving fonts, colours, style identity, custom
/// styles, and direct paragraph formatting alone.
/// </summary>
public sealed record DocumentParagraphSpacingSet(
    string Name,
    double SpaceBeforePt,
    double SpaceAfterPt,
    double LineSpacing)
{
    public static readonly IReadOnlyList<DocumentParagraphSpacingSet> Catalog =
    [
        new("No Paragraph Space", 0, 0, 1.0),
        new("Compact", 0, 4, 1.0),
        new("Tight", 0, 6, 1.15),
        new("Open", 0, 10, 1.15),
        new("Relaxed", 0, 6, 1.5),
        new("Double", 0, 8, 2.0),
    ];

    public static DocumentParagraphSpacingSet Default => Catalog[2];

    public static DocumentParagraphSpacingSet? FindByName(string name) =>
        Catalog.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static void Apply(TextDocument doc, DocumentParagraphSpacingSet spacingSet)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(spacingSet);

        doc.DefaultParagraph = ApplySpacing(doc.DefaultParagraph, spacingSet);

        foreach (var descriptor in BuiltInStyles.RoleCatalog)
        {
            if (doc.Styles.TryGetValue(descriptor.Id, out var style))
                style.Paragraph = ApplySpacing(style.Paragraph, spacingSet);
        }
    }

    private static ParagraphFormatting ApplySpacing(ParagraphFormatting paragraph, DocumentParagraphSpacingSet spacingSet) =>
        paragraph with
        {
            SpaceBeforePt = spacingSet.SpaceBeforePt,
            SpaceAfterPt = spacingSet.SpaceAfterPt,
            SpaceBeforeIsSet = true,
            SpaceAfterIsSet = true,
            LineSpacing = spacingSet.LineSpacing,
            LineRule = LineSpacingRule.Multiple,
            LineHeightPt = 0,
            LineSpacingIsSet = true,
        };
}
