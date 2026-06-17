using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Builds the starter document shown on launch, exercising mixed run formatting.</summary>
internal static class SampleDocument
{
    public static TextDocument Create()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var title = new Paragraph { Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center } };
        title.Runs.Add(new Run("Welcome to FreeW", RunFormatting.Default with { Bold = true, FontSizePt = 22 }));
        doc.Blocks.Add(title);

        var intro = new Paragraph();
        intro.Runs.Add(new Run("FreeW is the word processor in the ", RunFormatting.Default with { FontSizePt = 12 }));
        intro.Runs.Add(new Run("Free", RunFormatting.Default with { FontSizePt = 12, Bold = true }));
        intro.Runs.Add(new Run(" suite, now running natively on ", RunFormatting.Default with { FontSizePt = 12 }));
        intro.Runs.Add(new Run("Linux", RunFormatting.Default with { FontSizePt = 12, Italic = true, ColorHex = "#1A6E2E" }));
        intro.Runs.Add(new Run(" through Avalonia.", RunFormatting.Default with { FontSizePt = 12 }));
        doc.Blocks.Add(intro);

        var tip = new Paragraph();
        tip.Runs.Add(new Run("Type to edit. Use the toolbar for ", RunFormatting.Default with { FontSizePt = 12 }));
        tip.Runs.Add(new Run("bold", RunFormatting.Default with { FontSizePt = 12, Bold = true }));
        tip.Runs.Add(new Run(", ", RunFormatting.Default with { FontSizePt = 12 }));
        tip.Runs.Add(new Run("italic", RunFormatting.Default with { FontSizePt = 12, Italic = true }));
        tip.Runs.Add(new Run(", and ", RunFormatting.Default with { FontSizePt = 12 }));
        tip.Runs.Add(new Run("underline", RunFormatting.Default with { FontSizePt = 12, Underline = true }));
        tip.Runs.Add(new Run(". Undo and redo are wired through the shared command bus.", RunFormatting.Default with { FontSizePt = 12 }));
        doc.Blocks.Add(tip);

        return doc;
    }
}
