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

        doc.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            Run = RunFormatting.Default with { Bold = true, FontSizePt = 16, ColorHex = "#2B5797" },
            Paragraph = ParagraphFormatting.Default with { SpaceBeforePt = 12, SpaceAfterPt = 6 },
        };
        var heading = new Paragraph { StyleId = "Heading1" };
        heading.Runs.Add(new Run("This heading is styled by the document style (resolved at render)."));
        doc.Blocks.Add(heading);

        AddListItem(doc, "Bullet and numbered lists now render with markers.", ListKind.Bullet);
        AddListItem(doc, "Open and save Word .docx files.", ListKind.Bullet);

        AddListItem(doc, "Pick a font size from the ribbon.", ListKind.Number);
        AddListItem(doc, "Toggle bold, italic, underline.", ListKind.Number);
        AddListItem(doc, "Undo and redo every edit.", ListKind.Number);

        var table = Table.Create(3, 3);
        table.Formatting = TableFormatting.Default with { HeaderRow = true, BandedRows = true, Borders = true };
        SetCell(table, 0, 0, "Capability"); SetCell(table, 0, 1, "Windows"); SetCell(table, 0, 2, "Linux");
        SetCell(table, 1, 0, "Edit + DOCX"); SetCell(table, 1, 1, "Yes"); SetCell(table, 1, 2, "Yes");
        SetCell(table, 2, 0, "Ribbon"); SetCell(table, 2, 1, "Yes"); SetCell(table, 2, 2, "Yes");
        doc.Blocks.Add(table);

        var imagePara = new Paragraph { Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center } };
        imagePara.Runs.Add(Run.FromImage(new InlineImage(SamplePngBytes(), widthPt: 180, heightPt: 54) { AltText = "FreeW sample image" }));
        doc.Blocks.Add(imagePara);

        return doc;
    }

    // A 1x1 PNG (stretched to the run's point size when drawn). Embedded as bytes so the headless
    // packaging-smoke path needs no rendering platform; DocumentView decoding is crash-proof and
    // falls back to a placeholder box if the bytes ever fail to decode.
    private static byte[] SamplePngBytes() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static void SetCell(Table table, int row, int column, string text) =>
        table.Rows[row].Cells[column] = new TableCell(text);

    private static void AddListItem(TextDocument doc, string text, ListKind kind)
    {
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { ListKind = kind, SpaceAfterPt = 2 },
        };
        paragraph.Runs.Add(new Run(text, RunFormatting.Default with { FontSizePt = 12 }));
        doc.Blocks.Add(paragraph);
    }
}
