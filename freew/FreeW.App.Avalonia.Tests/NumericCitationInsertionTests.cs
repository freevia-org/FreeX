using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class NumericCitationInsertionTests
{
    [Fact]
    public void InsertCitation_VancouverUsesSharedSourceOrderNumber()
    {
        var first = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var second = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("See "));
        doc.BibliographyStyle = CitationStyle.Vancouver;
        doc.Sources.Add(first);
        doc.Sources.Add(second);

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.InsertCitation(second);

        var text = view.Document.Blocks.OfType<Paragraph>().Single().PlainText;
        text.Should().Contain("[2]");
        text.Should().NotContain("[Turing]");
        view.Document.Blocks.OfType<Paragraph>().Single().Runs
            .Select(run => run.ComplexField?.Keyword)
            .Should().Contain("CITATION");
    }

    [Fact]
    public void UpdateFields_VancouverCitationFieldRenumbersAfterSourceOrderChanges()
    {
        var first = new Source { Tag = "Ada1843", Author = "Ada Lovelace", Title = "Notes", Year = "1843" };
        var second = new Source { Tag = "Tur1936", Author = "Alan Turing", Title = "Computable Numbers", Year = "1936" };
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Runs = { Run.ComplexFieldRun(" CITATION Tur1936 ", "[2]") } });
        doc.BibliographyStyle = CitationStyle.Vancouver;
        doc.Sources.Add(second);
        doc.Sources.Add(first);

        var view = new DocumentView();
        view.LoadDocument(doc);

        view.UpdateFields();

        view.Document.Blocks.OfType<Paragraph>().Single().PlainText.Should().Be("[1]");
    }
}
