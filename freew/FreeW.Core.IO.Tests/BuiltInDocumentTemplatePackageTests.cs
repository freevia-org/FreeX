using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// The built-in templates are generated from the model, so the proof that they are credible Word
/// starting points is that each one writes a schema-valid package -- both as the .docx a new document
/// is saved as and as a .dotx Word template carrying the template main-part content type -- and that
/// the content survives the round trip.
/// </summary>
public sealed class BuiltInDocumentTemplatePackageTests
{
    private static readonly XNamespace CtNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static TheoryData<string> TemplateIds()
    {
        var data = new TheoryData<string>();
        foreach (var template in BuiltInDocumentTemplates.All)
            data.Add(template.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void SavedAsDotx_IsASchemaValidWordTemplate(string id)
    {
        var bytes = Save(DocxFileAdapter.Dotx(), BuiltInDocumentTemplates.Create(id)!);

        MainContentType(bytes).Should().Be(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml");
        using (var stream = new MemoryStream(bytes))
        using (var package = WordprocessingDocument.Open(stream, isEditable: false))
            package.DocumentType.Should().Be(WordprocessingDocumentType.Template);
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void SavedAsDocx_IsASchemaValidDocument(string id)
    {
        var bytes = Save(DocxFileAdapter.Docx(), BuiltInDocumentTemplates.Create(id)!);

        MainContentType(bytes).Should().Be(DocxWriteOptions.DocxMainContentType);
        ValidateSchema(bytes).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void DotxRoundTrip_PreservesContentStylesAndPageSetup(string id)
    {
        var original = BuiltInDocumentTemplates.Create(id)!;
        using var stream = new MemoryStream(Save(DocxFileAdapter.Dotx(), original));

        var reloaded = DocxFileAdapter.Dotx().Load(stream);

        reloaded.PlainText.Should().Be(original.PlainText);
        reloaded.Blocks.Select(block => block.GetType()).Should().Equal(original.Blocks.Select(block => block.GetType()));
        reloaded.Paragraphs.Select(paragraph => paragraph.StyleId ?? "Normal")
            .Should().Equal(original.Paragraphs.Select(paragraph => paragraph.StyleId ?? "Normal"));
        original.Paragraphs
            .Select(paragraph => paragraph.StyleId)
            .Where(styleId => styleId is not null)
            .Should().OnlyContain(styleId => reloaded.Styles.ContainsKey(styleId!));
        reloaded.Page.MarginLeftPt.Should().Be(original.Page.MarginLeftPt);
        reloaded.Page.DifferentFirstPage.Should().Be(original.Page.DifferentFirstPage);
        HeaderText(reloaded).Should().Be(HeaderText(original));
        FooterText(reloaded).Should().Be(FooterText(original));
    }

    [Fact]
    public void ReportTemplate_WritesALiveTableOfContentsField()
    {
        var bytes = Save(DocxFileAdapter.Dotx(), BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.ReportId)!);

        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = reader.ReadToEnd();

        documentXml.Should().Contain("TOC ", "Word must be able to update the contents page");
        documentXml.Should().Contain("w:fldChar");
        zip.Entries.Should().Contain(entry => entry.FullName.StartsWith("word/header", StringComparison.Ordinal));
        zip.Entries.Should().Contain(entry => entry.FullName.StartsWith("word/footer", StringComparison.Ordinal));
    }

    /// <summary>
    /// A document created from a macro-enabled personal template is a new .docx: the template's VBA
    /// project bytes load with it, are kept if it is saved back as .dotm, and are dropped for .docx.
    /// </summary>
    [Fact]
    public void DotmTemplate_MacroBytesSurviveDotmAndAreDroppedForDocx()
    {
        var template = BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.MemoId)!;
        template.Preserved.Parts.Add(new PreservedPart(
            "/word/vbaProject.bin",
            "FAKE-VBA-BYTES"u8.ToArray(),
            "application/vnd.ms-office.vbaProject",
            "http://schemas.microsoft.com/office/2006/relationships/vbaProject"));
        var dotm = Save(DocxFileAdapter.Dotm(), template);
        MainContentType(dotm).Should().Be(DocxWriteOptions.DotmMainContentType);

        using var stream = new MemoryStream(dotm);
        var loaded = DocxFileAdapter.Dotm().Load(stream);

        HasEntry(Save(DocxFileAdapter.Dotm(), loaded), "word/vbaProject.bin").Should().BeTrue();
        HasEntry(Save(DocxFileAdapter.Docx(), loaded), "word/vbaProject.bin").Should().BeFalse();
        HasEntry(Save(DocxFileAdapter.Dotx(), loaded), "word/vbaProject.bin").Should().BeFalse();
    }

    private static string HeaderText(TextDocument document) =>
        string.Join("\n", document.FinalSectionHeadersFooters.Header?.Paragraphs.Select(p => p.PlainText) ?? []);

    private static string FooterText(TextDocument document) =>
        string.Join("\n", document.FinalSectionHeadersFooters.Footer?.Paragraphs.Select(p => p.PlainText) ?? []);

    private static byte[] Save(IDocumentFileAdapter adapter, TextDocument document)
    {
        using var stream = new MemoryStream();
        adapter.Save(document, stream);
        return stream.ToArray();
    }

    private static string MainContentType(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("[Content_Types].xml")!.Open();
        return XDocument.Load(entry).Root!
            .Elements(CtNs + "Override")
            .First(element => (string?)element.Attribute("PartName") == "/word/document.xml")
            .Attribute("ContentType")!.Value;
    }

    private static bool HasEntry(byte[] bytes, string name)
    {
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.GetEntry(name) is not null;
    }

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
}
