namespace FreeW.Core.Model;

/// <summary>
/// An inline raster image carried by a <see cref="Run"/>. Modelled at the run level (rather than as
/// a block) so it round-trips through docx as an inline w:drawing without touching paragraph storage.
/// PNG bytes only; size is in points to match the rest of the FreeW unit model.
/// </summary>
public sealed class InlineImage(byte[] pngBytes, double widthPt, double heightPt)
{
    /// <summary>The raw PNG image bytes (the only supported format).</summary>
    public byte[] PngBytes { get; } = pngBytes;
    public double WidthPt { get; set; } = widthPt;
    public double HeightPt { get; set; } = heightPt;
}

/// <summary>
/// A contiguous span of text sharing one run formatting, or — when <see cref="Image"/> is set — an
/// inline image anchored in the run flow. An image run carries no text (<see cref="Text"/> is empty).
/// </summary>
public sealed class Run(string text, RunFormatting? formatting = null)
{
    public string Text { get; set; } = text;
    public RunFormatting Formatting { get; set; } = formatting ?? RunFormatting.Default;

    /// <summary>Optional inline image. When non-null this run renders/serialises as a picture.</summary>
    public InlineImage? Image { get; set; }

    /// <summary>Creates a run that carries an inline image instead of text.</summary>
    public static Run FromImage(InlineImage image) => new(string.Empty) { Image = image };
}

/// <summary>A paragraph: an ordered sequence of runs plus paragraph formatting and an optional style.</summary>
public sealed class Paragraph
{
    public List<Run> Runs { get; } = [];
    public ParagraphFormatting Formatting { get; set; } = ParagraphFormatting.Default;
    public string? StyleId { get; set; }

    public Paragraph() { }

    public Paragraph(string text)
    {
        if (text.Length > 0)
            Runs.Add(new Run(text));
    }

    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>Page geometry for a section (points; US Letter with 1in margins by default).</summary>
public sealed class PageSettings
{
    public double WidthPt { get; set; } = 612;
    public double HeightPt { get; set; } = 792;
    public double MarginLeftPt { get; set; } = 72;
    public double MarginRightPt { get; set; } = 72;
    public double MarginTopPt { get; set; } = 72;
    public double MarginBottomPt { get; set; } = 72;
    public bool Landscape { get; set; }
}

/// <summary>
/// The FreeW text document: ordered paragraphs, a style catalog, document-level defaults, and
/// page settings. Still intentionally lean, but now rich enough to carry real formatting and to
/// map onto WordprocessingML (document.xml / styles.xml) in a later milestone.
/// </summary>
public sealed class TextDocument
{
    public List<Paragraph> Paragraphs { get; } = [];
    public Dictionary<string, DocumentStyle> Styles { get; } = [];
    public RunFormatting DefaultRun { get; set; } = new() { FontFamily = "Calibri", FontSizePt = 11 };
    public ParagraphFormatting DefaultParagraph { get; set; } = ParagraphFormatting.Default;
    public PageSettings Page { get; } = new();

    public static TextDocument CreateEmpty()
    {
        var doc = new TextDocument();
        doc.AddBuiltInStyles();
        doc.Paragraphs.Add(new Paragraph());
        return doc;
    }

    public string PlainText => string.Join("\n", Paragraphs.Select(p => p.PlainText));

    private void AddBuiltInStyles()
    {
        Styles["Normal"] = new DocumentStyle { Id = "Normal", Name = "Normal" };
        Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 16, ColorHex = "#2F5496" },
            Paragraph = new ParagraphFormatting { SpaceBeforePt = 12, SpaceAfterPt = 4 }
        };
        Styles["Title"] = new DocumentStyle
        {
            Id = "Title",
            Name = "Title",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 28 },
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 8 }
        };
    }
}
