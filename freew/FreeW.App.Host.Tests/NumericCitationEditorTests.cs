using System.Linq;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class NumericCitationEditorTests
{
    private static Paragraph Heading(string text, int level = 1) =>
        new(text) { StyleId = "Heading" + level.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    [StaFact]
    public void InsertCitation_IeeeUsesSharedSourceOrderNumber()
    {
        var first = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var second = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("See "));
        model.BibliographyStyle = CitationStyle.Ieee;
        model.Sources.Add(first);
        model.Sources.Add(second);

        var view = new DocumentView();
        view.LoadModel(model);
        view.InsertCitation(second);
        view.CommitToModel();

        var text = view.Model.Blocks.OfType<Paragraph>().Single().PlainText;
        text.Should().Contain("[2]");
        text.Should().NotContain("[Turing]");
        view.Model.Blocks.OfType<Paragraph>().Single().Runs
            .Select(run => run.ComplexField?.Keyword)
            .Should().Contain("CITATION");
    }

    [StaFact]
    public void UpdateFields_IeeeCitationFieldRenumbersAfterSourceOrderChanges()
    {
        var first = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var second = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph { Runs = { Run.ComplexFieldRun(" CITATION Tur1936 ", "[2]") } });
        model.BibliographyStyle = CitationStyle.Ieee;
        model.Sources.Add(second);
        model.Sources.Add(first);

        var view = new DocumentView();
        view.LoadModel(model);

        view.UpdateFields();
        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>().Single().PlainText.Should().Be("[1]");
    }

    [StaFact]
    public void UpdateFields_RefreshesTocAndBibliographyInSamePass()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph(TableOfContents.HeadingText) { StyleId = TableOfContents.HeadingStyleId });
        model.Blocks.Add(new Paragraph("Old Heading") { StyleId = TableOfContents.EntryStyleId(1) });
        model.Blocks.Add(Heading("New Heading"));
        model.Blocks.Add(new Paragraph(Citations.HeadingText) { StyleId = Citations.HeadingStyleId });
        model.Blocks.Add(new Paragraph("Old. (1999). Entry.") { StyleId = Citations.EntryStyleId });
        model.Sources.Add(new Source { Tag = "New2024", Author = "New Author", Title = "Fresh Entry", Year = "2024" });

        var view = new DocumentView();
        view.LoadModel(model);

        view.UpdateFields();
        view.CommitToModel();

        var tocText = view.Model.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .ToList();
        tocText.Should().Contain("New Heading\t1");
        tocText.Should().NotContain("Old Heading");

        var bibliographyText = view.Model.Blocks.Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .ToList();
        bibliographyText.Should().Contain("New Author. (2024). Fresh Entry.");
        bibliographyText.Should().NotContain("Old. (1999). Entry.");
    }

    [StaFact]
    public void UpdateFields_TocUsesLogicalPageLabelOfPlacedHeading()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph(TableOfContents.HeadingText) { StyleId = TableOfContents.HeadingStyleId });
        model.Blocks.Add(new Paragraph("Old Heading\t9") { StyleId = TableOfContents.EntryStyleId(1) });
        model.Blocks.Add(DocumentOps.CreatePageBreak());
        model.Blocks.Add(Heading("Chapter Two"));
        model.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        model.Page.PageNumberStartAt = 4;

        var view = new DocumentView();
        view.LoadModel(model);

        view.UpdateFields();
        view.CommitToModel();

        view.Model.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Chapter Two\tV");
    }

    [StaFact]
    public void RefreshTableOfContents_ReplacesNativeOwnedResultWithoutDeletingSourceHeading()
    {
        var field = new ComplexField(" TOC \\o \"1-3\" ");
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph("Old Heading\t9")
        {
            StyleId = "Normal",
            SpanningFieldStart = field,
            SpanningFieldOwner = field,
            EndsSpanningField = true
        });
        model.Blocks.Add(Heading("Source Heading"));

        var view = new DocumentView();
        view.LoadModel(model);

        view.RefreshTableOfContents();
        view.CommitToModel();

        view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Contain("Source Heading");
        view.Model.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Source Heading\t1").And.NotContain("Old Heading\t9");
    }

    [StaFact]
    public void UpdateFields_CitationFieldAndBibliographyRefresh_DoNotOverwriteCitationFromStaleView()
    {
        var first = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var second = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph { Runs = { Run.ComplexFieldRun(" CITATION Tur1936 ", "[2]") } });
        model.Blocks.Add(new Paragraph(Citations.HeadingText) { StyleId = Citations.HeadingStyleId });
        model.Blocks.Add(new Paragraph("[2] Old bibliography") { StyleId = Citations.EntryStyleId });
        model.BibliographyStyle = CitationStyle.Ieee;
        model.Sources.Add(second);
        model.Sources.Add(first);

        var view = new DocumentView();
        view.LoadModel(model);

        view.UpdateFields();
        view.CommitToModel();

        var citationRun = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.ComplexField?.Keyword == "CITATION");
        citationRun.Text.Should().Be("[1]");

        view.Model.Blocks.Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("[1] Alan Turing, \"Computable Numbers,\" 1936.");
    }
}
