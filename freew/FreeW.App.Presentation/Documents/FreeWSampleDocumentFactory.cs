using FreeW.Core.Model;

namespace FreeW.App.Presentation.Documents;

public enum FreeWSampleDocumentProfile
{
    ClassicEditor,
    FeatureShowcase,
}

/// <summary>Builds the portable model documents used by the desktop host startup experiences.</summary>
public static class FreeWSampleDocumentFactory
{
    public static TextDocument Create(FreeWSampleDocumentProfile profile) => profile switch
    {
        FreeWSampleDocumentProfile.ClassicEditor => CreateClassicEditor(),
        FreeWSampleDocumentProfile.FeatureShowcase => CreateFeatureShowcase(),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
    };

    private static TextDocument CreateClassicEditor()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        document.Blocks.Add(new Paragraph("Welcome to FreeW") { StyleId = "Title" });
        document.Blocks.Add(new Paragraph("A free word processor") { StyleId = "Heading1" });

        var intro = new Paragraph();
        intro.Runs.Add(new Run("This document is rendered from the FreeW model. Formatting like "));
        intro.Runs.Add(new Run("bold", new RunFormatting { Bold = true }));
        intro.Runs.Add(new Run(", "));
        intro.Runs.Add(new Run("italic", new RunFormatting { Italic = true }));
        intro.Runs.Add(new Run(", "));
        intro.Runs.Add(new Run("underline", new RunFormatting { Underline = true }));
        intro.Runs.Add(new Run(" and "));
        intro.Runs.Add(new Run("colour", new RunFormatting { ColorHex = "#C0504D", Bold = true }));
        intro.Runs.Add(new Run(
            " resolves through styles and document defaults. Edit freely \u2014 the surface is a live RichTextBox; CommitToModel() maps your edits back."));
        document.Blocks.Add(intro);

        document.Blocks.Add(new Paragraph("Centered paragraph.")
        {
            Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center },
        });

        return document;
    }

    private static TextDocument CreateFeatureShowcase()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var title = new Paragraph { Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center } };
        title.Runs.Add(new Run("Welcome to FreeW", RunFormatting.Default with { Bold = true, FontSizePt = 22 }));
        document.Blocks.Add(title);

        var intro = new Paragraph();
        intro.Runs.Add(new Run("FreeW is the word processor in the ", RunFormatting.Default with { FontSizePt = 12 }));
        intro.Runs.Add(new Run("Free", RunFormatting.Default with { FontSizePt = 12, Bold = true }));
        intro.Runs.Add(new Run(" suite, now running natively on ", RunFormatting.Default with { FontSizePt = 12 }));
        intro.Runs.Add(new Run("Linux", RunFormatting.Default with { FontSizePt = 12, Italic = true, ColorHex = "#1A6E2E" }));
        intro.Runs.Add(new Run(" through Avalonia.", RunFormatting.Default with { FontSizePt = 12 }));
        document.Blocks.Add(intro);

        var tip = new Paragraph();
        tip.Runs.Add(new Run("Type to edit. Use the toolbar for ", RunFormatting.Default with { FontSizePt = 12 }));
        tip.Runs.Add(new Run("bold", RunFormatting.Default with { FontSizePt = 12, Bold = true }));
        tip.Runs.Add(new Run(", ", RunFormatting.Default with { FontSizePt = 12 }));
        tip.Runs.Add(new Run("italic", RunFormatting.Default with { FontSizePt = 12, Italic = true }));
        tip.Runs.Add(new Run(", and ", RunFormatting.Default with { FontSizePt = 12 }));
        tip.Runs.Add(new Run("underline", RunFormatting.Default with { FontSizePt = 12, Underline = true }));
        tip.Runs.Add(new Run(". Undo and redo are wired through the shared command bus.", RunFormatting.Default with { FontSizePt = 12 }));
        document.Blocks.Add(tip);

        document.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            Run = RunFormatting.Default with { Bold = true, FontSizePt = 16, ColorHex = "#2B5797" },
            Paragraph = ParagraphFormatting.Default with { SpaceBeforePt = 12, SpaceAfterPt = 6 },
        };
        var heading = new Paragraph { StyleId = "Heading1" };
        heading.Runs.Add(new Run("This heading is styled by the document style (resolved at render)."));
        document.Blocks.Add(heading);

        AddListItem(document, "Bullet and numbered lists now render with markers.", ListKind.Bullet);
        AddListItem(document, "Open and save Word .docx files.", ListKind.Bullet);
        AddListItem(document, "Pick a font size from the ribbon.", ListKind.Number);
        AddListItem(document, "Toggle bold, italic, underline.", ListKind.Number);
        AddListItem(document, "Undo and redo every edit.", ListKind.Number);

        var table = Table.Create(3, 3);
        table.Formatting = TableFormatting.Default with { HeaderRow = true, BandedRows = true, Borders = true };
        SetCell(table, 0, 0, "Capability");
        SetCell(table, 0, 1, "Windows");
        SetCell(table, 0, 2, "Linux");
        SetCell(table, 1, 0, "Edit + DOCX");
        SetCell(table, 1, 1, "Yes");
        SetCell(table, 1, 2, "Yes");
        SetCell(table, 2, 0, "Ribbon");
        SetCell(table, 2, 1, "Yes");
        SetCell(table, 2, 2, "Yes");
        document.Blocks.Add(table);

        var imageParagraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { Alignment = TextAlignment.Center },
        };
        imageParagraph.Runs.Add(Run.FromImage(new InlineImage(
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="),
            widthPt: 180,
            heightPt: 54)
        {
            AltText = "FreeW sample image",
        }));
        document.Blocks.Add(imageParagraph);

        return document;
    }

    private static void SetCell(Table table, int row, int column, string text) =>
        table.Rows[row].Cells[column] = new TableCell(text);

    private static void AddListItem(TextDocument document, string text, ListKind kind)
    {
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { ListKind = kind, SpaceAfterPt = 2 },
        };
        paragraph.Runs.Add(new Run(text, RunFormatting.Default with { FontSizePt = 12 }));
        document.Blocks.Add(paragraph);
    }
}
