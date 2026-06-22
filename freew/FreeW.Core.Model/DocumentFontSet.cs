namespace FreeW.Core.Model;

/// <summary>
/// Word-style Design &gt; Document Formatting font sets. A font set changes only the document-wide
/// heading/body font pairing carried by the theme and built-in style catalog; colours, sizes, paragraph
/// formatting, direct run formatting, and custom styles are left alone.
/// </summary>
public sealed record DocumentFontSet(string Name, string HeadingFont, string BodyFont)
{
    public static readonly IReadOnlyList<DocumentFontSet> Catalog =
    [
        new("Office", "Calibri", "Calibri"),
        new("Cambria", "Cambria", "Calibri"),
        new("Georgia", "Georgia", "Georgia"),
        new("Trebuchet", "Trebuchet MS", "Trebuchet MS"),
    ];

    public static DocumentFontSet Default => Catalog[0];

    public static DocumentFontSet? FindByName(string name) =>
        Catalog.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static void Apply(TextDocument doc, DocumentFontSet fontSet)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(fontSet);

        doc.Theme = doc.Theme with
        {
            HeadingFont = fontSet.HeadingFont,
            BodyFont = fontSet.BodyFont,
        };
        doc.DefaultRun = doc.DefaultRun with { FontFamily = fontSet.BodyFont };

        SetRun(doc, "Normal", run => run with { FontFamily = fontSet.BodyFont });
        SetRun(doc, "Title", run => run with { FontFamily = fontSet.HeadingFont });
        SetRun(doc, "Subtitle", run => run with { FontFamily = fontSet.BodyFont });
        SetRun(doc, "Heading1", run => run with { FontFamily = fontSet.HeadingFont });
        SetRun(doc, "Heading2", run => run with { FontFamily = fontSet.HeadingFont });
        SetRun(doc, "Heading3", run => run with { FontFamily = fontSet.HeadingFont });
        SetRun(doc, "Quote", run => run with { FontFamily = fontSet.BodyFont });
    }

    private static void SetRun(TextDocument doc, string styleId, Func<RunFormatting, RunFormatting> transform)
    {
        if (doc.Styles.TryGetValue(styleId, out var style))
            style.Run = transform(style.Run);
    }
}
