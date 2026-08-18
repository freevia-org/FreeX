using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// A tracked edit made inside a content control is recorded by Word as w:sdt/w:sdtContent/w:ins (or
/// w:del) — the revision nests INSIDE the field. The writer used to end a control's run span at the
/// first revision boundary, which re-emitted the same field twice, each copy claiming part of its text.
/// </summary>
public sealed class TrackedChangeInsideContentControlRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void A_tracked_insertion_inside_a_field_stays_one_control_with_a_nested_revision()
    {
        var document = ReadDocx(CreateSource());

        var paragraph = document.Paragraphs.Single();
        var controls = paragraph.Runs.Where(run => run.Control is not null).ToList();
        controls.Should().HaveCount(3, "the field's plain, inserted and deleted runs all carry the control");
        controls.Select(run => run.Text).Should().Equal("Bob", "by", "ster");
        controls.Select(run => run.Revision)
            .Should().Equal(RevisionKind.None, RevisionKind.Inserted, RevisionKind.Deleted);
        controls.Select(run => run.Control!.Tag).Distinct().Should().Equal("Applicant");

        var bytes = WriteDocx(document);
        var sdts = ContentControls(bytes);
        sdts.Should().ContainSingle("the field must survive as ONE w:sdt, not one per revision boundary");

        var content = sdts[0].Element(W + "sdtContent")!;
        content.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("r", "ins", "del");
        content.Element(W + "ins")!.Elements(W + "r").Single()
            .Element(W + "t")!.Value.Should().Be("by");
        content.Element(W + "del")!.Elements(W + "r").Single()
            .Element(W + "delText")!.Value.Should().Be("ster");
        SchemaErrors(bytes).Should().BeEmpty();

        // Reopening keeps the same shape, so the field does not multiply across successive saves.
        var reopened = ReadDocx(bytes);
        reopened.Paragraphs.Single().Runs.Where(run => run.Control is not null)
            .Select(run => (run.Text, run.Revision))
            .Should().Equal(
                ("Bob", RevisionKind.None),
                ("by", RevisionKind.Inserted),
                ("ster", RevisionKind.Deleted));
        ContentControls(WriteDocx(reopened)).Should().ContainSingle();
    }

    private static byte[] CreateSource()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(
                """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:sdt>
                        <w:sdtPr><w:tag w:val="Applicant"/><w:text/></w:sdtPr>
                        <w:sdtContent>
                          <w:r><w:t>Bob</w:t></w:r>
                          <w:ins w:id="1" w:author="Ada" w:date="2026-08-18T10:00:00Z">
                            <w:r><w:t>by</w:t></w:r>
                          </w:ins>
                          <w:del w:id="2" w:author="Ada" w:date="2026-08-18T10:00:00Z">
                            <w:r><w:delText>ster</w:delText></w:r>
                          </w:del>
                        </w:sdtContent>
                      </w:sdt>
                    </w:p>
                  </w:body>
                </w:document>
                """);
        }

        return stream.ToArray();
    }

    private static List<XElement> ContentControls(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Descendants(W + "sdt").ToList();
    }

    private static byte[] WriteDocx(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument ReadDocx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
    }

    private static List<string> SchemaErrors(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Microsoft365)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}
