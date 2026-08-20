using System.IO;
using System.IO.Compression;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Word represents "Reviewer B deleted text Reviewer A had inserted, both still pending" as a nested
/// <c>&lt;w:ins w:author="A"&gt;&lt;w:del w:author="B"&gt;...&lt;/w:del&gt;&lt;/w:ins&gt;</c> -- a routine
/// two-person Track Changes pattern. <see cref="DocxReader"/> must not silently discard the outer wrapper's
/// author/date when the run's single Revision/RevisionAuthor/RevisionDateXml triple is set from the
/// innermost (visually authoritative) wrapper.
/// </summary>
public class NestedRevisionAuthorshipRoundTripTests
{
    [Fact]
    public void InsertionLaterDeletedByAnotherReviewer_PreservesBothAuthors()
    {
        using var input = BuildPackage("""
            <w:p><w:ins w:id="1" w:author="Reviewer A" w:date="2026-01-01T10:00:00Z"><w:del w:id="2" w:author="Reviewer B" w:date="2026-01-02T11:30:00Z"><w:r><w:delText>hello </w:delText></w:r></w:del></w:ins></w:p>
            """);
        var paragraph = DocxReader.Read(input).Paragraphs.Single();
        var run = paragraph.Runs.Single(r => r.Text == "hello ");

        // The innermost wrapper (Reviewer B's deletion) stays authoritative for rendering/accept-reject --
        // this part of the existing behaviour is already correct and must not change.
        run.Revision.Should().Be(RevisionKind.Deleted);
        run.RevisionAuthor.Should().Be("Reviewer B");
        run.RevisionDateXml.Should().Be("2026-01-02T11:30:00Z");

        // The outer wrapper's authorship/date must survive instead of vanishing.
        run.NestedRevision.Should().NotBeNull();
        run.NestedRevision!.Kind.Should().Be(RevisionKind.Inserted);
        run.NestedRevision.Author.Should().Be("Reviewer A");
        run.NestedRevision.DateXml.Should().Be("2026-01-01T10:00:00Z");
    }

    [Fact]
    public void OrdinarySingleWrapperDeletion_CarriesNoNestedRevision()
    {
        // Sibling/no-regression case: an ordinary (non-nested) tracked deletion must behave exactly as
        // before -- no NestedRevision synthesised out of nothing.
        using var input = BuildPackage("""
            <w:p><w:del w:id="1" w:author="Reviewer B" w:date="2026-01-02T11:30:00Z"><w:r><w:delText>hello </w:delText></w:r></w:del></w:p>
            """);
        var paragraph = DocxReader.Read(input).Paragraphs.Single();
        var run = paragraph.Runs.Single(r => r.Text == "hello ");

        run.Revision.Should().Be(RevisionKind.Deleted);
        run.RevisionAuthor.Should().Be("Reviewer B");
        run.RevisionDateXml.Should().Be("2026-01-02T11:30:00Z");
        run.NestedRevision.Should().BeNull();
    }

    private static MemoryStream BuildPackage(string bodyXml)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/document.xml", $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    {bodyXml}
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(xml);
    }
}
