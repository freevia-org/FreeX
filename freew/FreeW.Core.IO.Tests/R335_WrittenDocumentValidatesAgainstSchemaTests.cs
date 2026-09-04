using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r335: r334's lens, applied to the .docx writer.
///
/// <para>r334 found that FreeP wrote a schema-invalid package for an empty text body, behind eight
/// existing <c>OpenXmlValidator</c> tests -- because each of those validated a deck built for its
/// own feature, so no package ever carried two features at once. FreeW has twenty-one validating
/// tests with the same shape: BuildingBlockGallery, CheckBox, Citation, TabIndex, one content-control
/// feature per file.</para>
///
/// <para>So this writes ONE document carrying several unrelated features together -- styled
/// paragraphs, a table, an inline image, a hyperlink, an empty paragraph -- and validates the whole
/// package. The combination is the subject; any single one of these is already covered.</para>
/// </summary>
public sealed class R335_WrittenDocumentValidatesAgainstSchemaTests
{
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static string[] ValidateSchema(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Office2013)
            .Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => error.Description + " @ " + error.Path?.XPath)
            .ToArray();
    }

    [Fact]
    public void ADocumentCombiningManyFeaturesValidates()
    {
        var document = new TextDocument();

        document.Blocks.Add(new Paragraph("A heading") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph("Body text with a "));

        var linkParagraph = new Paragraph();
        linkParagraph.Runs.Add(new Run("clickable link") { HyperlinkUrl = "https://example.invalid/r335" });
        document.Blocks.Add(linkParagraph);

        var table = new Table();
        for (var r = 0; r < 2; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < 3; c++)
                row.Cells.Add(new TableCell($"r{r}c{c}"));
            table.Rows.Add(row);
        }

        document.Blocks.Add(table);

        var imageParagraph = new Paragraph();
        imageParagraph.Runs.Add(new Run("img") { Image = new InlineImage(OnePixelPng(), 24, 24) });
        document.Blocks.Add(imageParagraph);

        // The state r334 found in FreeP: a paragraph carrying no runs at all.
        document.Blocks.Add(new Paragraph());

        using var stream = new MemoryStream();
        new DocxFileAdapter().Save(document, stream);
        var bytes = stream.ToArray();

        // Vacuity guarded by CONTENT, not size. The first version required 4096 bytes and failed on a
        // perfectly good 3902-byte package -- a compressed .docx of a few paragraphs is simply small,
        // so the number measured nothing except my guess about it. What matters is that the features
        // this test claims to combine actually reached the file.
        var documentXml = ReadDocumentXml(bytes);
        documentXml.Should().Contain("<w:tbl", "the table must be in the package being validated");
        documentXml.Should().Contain("w:drawing", "the inline image must be in it too");
        documentXml.Should().Contain("hyperlink", "and the hyperlink");

        ValidateSchema(bytes).Should().BeEmpty(
            "the written package must satisfy the OOXML schema; a part that is well formed beside "
            + "its own feature can still be wrong beside a neighbour, which is what a multi-feature "
            + "document exposes and a single-feature one hides");
    }
    private static string ReadDocumentXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml");
        entry.Should().NotBeNull("a .docx without word/document.xml is not a document");
        using var entryStream = entry!.Open();
        using var reader = new StreamReader(entryStream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The product path, so this guards the defect rather than my fixture. <c>Table.Create</c> is the
    /// model's own factory for a uniform table and leaves <c>ColumnWidthsPt</c> empty, which is
    /// exactly the state that wrote a <c>w:tbl</c> with no <c>w:tblGrid</c>.
    /// </summary>
    [Fact]
    public void ATableCreatedByTheModelsOwnFactoryWritesAGrid()
    {
        var document = new TextDocument();
        var table = Table.Create(2, 3);
        table.ColumnWidthsPt.Should().BeEmpty("the factory does not assign widths, which is the point");
        document.Blocks.Add(table);

        using var stream = new MemoryStream();
        new DocxFileAdapter().Save(document, stream);
        var bytes = stream.ToArray();

        ReadDocumentXml(bytes).Should().Contain("<w:tblGrid",
            "CT_Tbl requires a grid before its rows, whether or not widths are known");
        ValidateSchema(bytes).Should().BeEmpty(
            "inserting a table and saving must produce a schema-valid document");
    }

    /// <summary>
    /// r337: r336 left a hypothesis -- the two hand-built writers emit MANDATORY elements
    /// conditionally, so more features in one package should surface more of them. This adds the
    /// features r335's document did not carry: a header, a footnote, an endnote and a bookmark,
    /// each of which brings its own part or required child.
    /// </summary>
    [Fact]
    public void ADocumentWithNotesHeaderAndBookmarkValidates()
    {
        var document = new TextDocument();

        var body = new Paragraph("Body with notes") { BookmarkName = "r337_mark" };
        body.Runs.Add(Run.FootnoteReference(1));
        body.Runs.Add(Run.EndnoteReference(1));
        document.Blocks.Add(body);

        var header = new HeaderFooter();
        header.Paragraphs.Add(new Paragraph("Header text"));
        document.FinalSectionHeadersFooters.Header = header;

        var footer = new HeaderFooter();
        footer.Paragraphs.Add(new Paragraph());   // deliberately empty, per r334's finding
        document.FinalSectionHeadersFooters.Footer = footer;

        var footnote = new Footnote(1);
        footnote.Content.Add(new Paragraph("Footnote text"));
        document.Footnotes[1] = footnote;

        var endnote = new Endnote(1);
        endnote.Content.Add(new Paragraph("Endnote text"));
        document.Endnotes[1] = endnote;

        using var stream = new MemoryStream();
        new DocxFileAdapter().Save(document, stream);
        var bytes = stream.ToArray();

        var documentXml = ReadDocumentXml(bytes);
        documentXml.Should().Contain("bookmarkStart", "the bookmark must reach the package");
        documentXml.Should().Contain("footnoteReference", "and the footnote reference");

        ValidateSchema(bytes).Should().BeEmpty(
            "notes, headers and bookmarks each add required children of their own, which is exactly "
            + "where conditional emission of a mandatory element hides");
    }

}