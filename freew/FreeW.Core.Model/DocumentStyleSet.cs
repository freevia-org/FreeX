namespace FreeW.Core.Model;

/// <summary>
/// Word-style Design &gt; Document Formatting style sets. A style set rewrites the built-in paragraph
/// styles' run and paragraph formatting as a coordinated catalog change; paragraphs keep their
/// <see cref="Paragraph.StyleId"/> and pick up the new look through normal style resolution.
/// </summary>
public sealed record DocumentStyleSet(string Name, string BodyFont, string HeadingFont, string AccentColorHex)
{
    public static readonly IReadOnlyList<DocumentStyleSet> Catalog =
    [
        new("Office",        "Calibri",          "Calibri",           "#2F5496"),
        new("Simple",        "Calibri",          "Calibri Light",     "#1F4E79"),
        new("Elegant",       "Georgia",          "Cambria",           "#5B3A29"),
        new("Formal",        "Times New Roman",  "Cambria",           "#365F91"),
        new("Lines (Simple)","Calibri",          "Calibri",           "#4472C4"),
        new("Minimalist",    "Arial",            "Arial",             "#404040"),
        new("Shadow",        "Century Gothic",   "Century Gothic",    "#4BACC6"),
        new("Shaded",        "Garamond",         "Garamond",          "#C0504D"),
        new("Word 2003",     "Times New Roman",  "Times New Roman",   "#000080"),
        new("Word 2010",     "Calibri",          "Cambria",           "#17375E"),
    ];

    public static DocumentStyleSet Default => Catalog[0];

    public static DocumentStyleSet? FindByName(string name) =>
        Catalog.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Identifies the catalog set that owns the document's current body font, Heading 1 font, and
    /// Heading 1 accent. These are the three set-specific values written by <see cref="Apply"/> and are
    /// sufficient to distinguish every current catalog entry without introducing shadow package state.
    /// </summary>
    public static DocumentStyleSet? FindMatching(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.Styles.TryGetValue("Heading1", out var heading1))
            return null;

        var bodyFont = document.DefaultRun.FontFamily;
        var headingFont = heading1.Run.FontFamily ?? bodyFont;
        var accent = heading1.Run.ColorHex;
        return Catalog.FirstOrDefault(styleSet =>
            string.Equals(styleSet.BodyFont, bodyFont, StringComparison.OrdinalIgnoreCase)
            && string.Equals(styleSet.HeadingFont, headingFont, StringComparison.OrdinalIgnoreCase)
            && string.Equals(styleSet.AccentColorHex, accent, StringComparison.OrdinalIgnoreCase));
    }

    public static void Apply(TextDocument doc, DocumentStyleSet styleSet)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(styleSet);

        doc.DefaultRun = doc.DefaultRun with { FontFamily = styleSet.BodyFont, FontSizePt = 11 };

        Set(doc, "Normal",
            run => run with { FontFamily = styleSet.BodyFont, FontSizePt = 11, Bold = false, Italic = false, ColorHex = null },
            para => para with { SpaceBeforePt = 0, SpaceAfterPt = 8, LineSpacing = 1.15 });

        Set(doc, "Title",
            run => run with { FontFamily = styleSet.HeadingFont, FontSizePt = 28, Bold = true, Italic = false, ColorHex = styleSet.AccentColorHex },
            para => para with { SpaceBeforePt = 0, SpaceAfterPt = 8 });

        Set(doc, "Subtitle",
            run => run with { FontFamily = styleSet.BodyFont, FontSizePt = 15, Bold = false, Italic = true, ColorHex = "#5A5A5A" },
            para => para with { SpaceBeforePt = 0, SpaceAfterPt = 8 });

        Set(doc, "Heading1",
            run => run with { FontFamily = styleSet.HeadingFont, FontSizePt = 16, Bold = true, Italic = false, ColorHex = styleSet.AccentColorHex },
            para => para with { SpaceBeforePt = 12, SpaceAfterPt = 4 });

        Set(doc, "Heading2",
            run => run with { FontFamily = styleSet.HeadingFont, FontSizePt = 13, Bold = true, Italic = false, ColorHex = styleSet.AccentColorHex },
            para => para with { SpaceBeforePt = 10, SpaceAfterPt = 4 });

        Set(doc, "Heading3",
            run => run with { FontFamily = styleSet.HeadingFont, FontSizePt = 12, Bold = true, Italic = false, ColorHex = DarkerAccent(styleSet.AccentColorHex) },
            para => para with { SpaceBeforePt = 8, SpaceAfterPt = 4 });

        Set(doc, "Quote",
            run => run with { FontFamily = styleSet.BodyFont, FontSizePt = 11, Bold = false, Italic = true, ColorHex = "#404040" },
            para => para with { SpaceBeforePt = 10, SpaceAfterPt = 10, IndentLeftPt = 36, IndentRightPt = 36 });
    }

    private static void Set(
        TextDocument doc,
        string styleId,
        Func<RunFormatting, RunFormatting> run,
        Func<ParagraphFormatting, ParagraphFormatting> paragraph)
    {
        if (!doc.Styles.TryGetValue(styleId, out var style))
            return;

        style.Run = run(style.Run);
        style.Paragraph = paragraph(style.Paragraph);
    }

    private static string DarkerAccent(string hex) => hex.ToUpperInvariant() switch
    {
        "#2F5496" => "#1F3864",
        "#1F4E79" => "#17365D",
        "#5B3A29" => "#3F2A1F",
        "#365F91" => "#244061",
        "#4472C4" => "#2F4C8A",
        "#404040" => "#1A1A1A",
        "#4BACC6" => "#2E7A91",
        "#C0504D" => "#833533",
        "#000080" => "#000040",
        "#17375E" => "#0D2240",
        _ => hex,
    };
}
