namespace FreeW.Core.Model.Tests;

public class DocumentCombineTests
{
    private const string DateXml = "2026-06-19T12:00:00Z";

    private static TextDocument DocWith(params string[] paragraphs)
    {
        var doc = new TextDocument();
        foreach (var text in paragraphs)
            doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    [Fact]
    public void Combine_CarriesRevisionAuthorsFromBothReviewers()
    {
        var original = DocWith("one two three");
        var revisedA = DocWith("one alpha three");
        var revisedB = DocWith("one beta three");

        var combined = DocumentCombine.Combine(original, revisedA, "Alice", revisedB, "Bob", DateXml);

        combined.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.Revision != RevisionKind.None)
            .Select(run => run.RevisionAuthor)
            .Should().Contain(["Alice", "Bob"]);
    }
}
