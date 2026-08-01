namespace FreeW.Core.Model.Tests;

public class SubDocumentReferenceTests
{
    [Fact]
    public void InsertTextFromFile_ClonesSubDocumentAnchorIndependently()
    {
        var source = new TextDocument();
        var sourceParagraph = new Paragraph();
        sourceParagraph.Runs.Add(new Run("Before"));
        sourceParagraph.Runs.Add(Run.FromSubDocument("Chapter1.docx"));
        sourceParagraph.Runs.Add(new Run("After"));
        source.Blocks.Add(sourceParagraph);

        var target = new TextDocument();
        var inserted = DocumentMerge.Merge(target, 0, source);
        var insertedParagraph = inserted.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;

        insertedParagraph.Runs[1].Should().NotBeSameAs(sourceParagraph.Runs[1]);
        insertedParagraph.Runs[1].SubDocument.Should().Be(new SubDocumentReference("Chapter1.docx"));

        insertedParagraph.Runs[1].SubDocument = new SubDocumentReference("Changed.docx");
        sourceParagraph.Runs[1].SubDocument.Should().Be(new SubDocumentReference("Chapter1.docx"));
    }
}
