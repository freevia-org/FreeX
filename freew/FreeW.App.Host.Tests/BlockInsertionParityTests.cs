using FreeW.App.Host.Editing;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

public sealed class BlockInsertionParityTests
{
    [StaFact]
    public void Cover_page_matches_Avalonia_as_one_undoable_prepend_mutation()
    {
        var document = new TextDocument();
        document.Properties.Title = "Parity Plan";
        document.Properties.Author = "FreeW";
        document.Blocks.Add(new Paragraph("Body"));
        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.InsertCoverPage(CoverPagePreset.Banded);

        editor.Model.Blocks.Should().HaveCountGreaterThan(1);
        editor.Model.Blocks[0].Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Contain("Parity Plan");

        editor.Undo();

        editor.Model.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be("Body");
    }

    [StaFact]
    public void Blank_page_matches_Avalonia_as_one_undoable_two_break_mutation()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Body"));
        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CaretPosition = editor.Document.Blocks.OfType<WpfParagraph>().Single().ContentEnd;

        editor.InsertBlankPage();

        editor.Model.Blocks.Should().HaveCount(3);
        editor.Model.Blocks.Skip(1).Cast<Paragraph>()
            .Should().OnlyContain(paragraph => paragraph.Formatting.PageBreakBefore);

        editor.Undo();

        editor.Model.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be("Body");
    }
}
