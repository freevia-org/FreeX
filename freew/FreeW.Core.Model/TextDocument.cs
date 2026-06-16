namespace FreeW.Core.Model;

/// <summary>Inline character formatting for a <see cref="Run"/>.</summary>
public sealed record RunFormatting(bool Bold = false, bool Italic = false, bool Underline = false);

/// <summary>A contiguous span of text sharing one formatting.</summary>
public sealed class Run(string text, RunFormatting? formatting = null)
{
    public string Text { get; set; } = text;
    public RunFormatting Formatting { get; set; } = formatting ?? new RunFormatting();
}

/// <summary>A paragraph: an ordered sequence of runs.</summary>
public sealed class Paragraph
{
    public List<Run> Runs { get; } = [];

    public Paragraph() { }

    public Paragraph(string text) => Runs.Add(new Run(text));

    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>
/// The FreeW text document model: an ordered list of paragraphs. Deliberately minimal —
/// the point of the scaffold is to prove the shared tier is consumable by a second app,
/// not to be a complete word processor yet.
/// </summary>
public sealed class TextDocument
{
    public List<Paragraph> Paragraphs { get; } = [];

    public static TextDocument CreateEmpty()
    {
        var doc = new TextDocument();
        doc.Paragraphs.Add(new Paragraph());
        return doc;
    }

    public string PlainText => string.Join("\n", Paragraphs.Select(p => p.PlainText));
}
