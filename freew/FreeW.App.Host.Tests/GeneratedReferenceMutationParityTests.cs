using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

public sealed class GeneratedReferenceMutationParityTests
{
    [StaFact]
    public void Index_insert_matches_Avalonia_as_one_undoable_generated_region()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Body"));
        document.IndexEntries.Add(new IndexEntry("Alpha"));
        document.IndexEntries.Add(new IndexEntry("Beta"));
        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.InsertIndex();

        editor.Model.Blocks.Count(DocumentIndex.IsIndexParagraph).Should().BeGreaterThan(1);
        editor.Undo();
        editor.Model.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().Be("Body");
    }

    [StaFact]
    public void Table_of_figures_refresh_matches_Avalonia_as_one_undoable_replacement()
    {
        var oldHeading = new Paragraph(TableOfFigures.HeadingText(CaptionLabel.Figure))
        {
            StyleId = TableOfFigures.HeadingStyleId,
        };
        var oldEntry = new Paragraph("Old Figure\t9") { StyleId = TableOfFigures.EntryStyleId };
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "Architecture");
        var document = new TextDocument();
        document.Blocks.AddRange([oldHeading, oldEntry, caption]);
        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.RefreshTableOfFigures();

        editor.Model.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Contain("Figure 1: Architecture\t1").And.NotContain("Old Figure\t9");
        editor.Undo();
        editor.Model.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal(oldHeading.PlainText, oldEntry.PlainText);
    }
}
