using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Tests for ISO/IEC 29500 Strict OOXML read + write via <see cref="DocxFileAdapter.Strict()"/>.
/// </summary>
public class StrictOoxmlTests
{
    // The strict WordprocessingML main namespace URI (ISO 29500 purl.oclc.org family)
    private const string StrictWNs = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    // The transitional WordprocessingML main namespace URI used internally by FreeW
    private const string TransitionalWNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TextDocument SampleDocument(params string[] paragraphs)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in paragraphs)
            doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    /// <summary>
    /// Returns the root element namespace URI of <c>word/document.xml</c> inside the given zip bytes.
    /// </summary>
    private static string DocumentXmlRootNamespace(byte[] packageBytes)
    {
        using var ms = new MemoryStream(packageBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("word/document.xml not found in package.");
        using var stream = entry.Open();
        var xdoc = XDocument.Load(stream);
        return xdoc.Root?.Name.Namespace.NamespaceName ?? string.Empty;
    }

    /// <summary>
    /// Reads all paragraph text from the given package bytes using the given adapter.
    /// </summary>
    private static List<string> ReadParagraphTexts(byte[] packageBytes, DocxFileAdapter adapter)
    {
        using var ms = new MemoryStream(packageBytes);
        return adapter.Load(ms)
            .Blocks.OfType<Paragraph>()
            .Select(p => p.PlainText)
            .ToList();
    }

    /// <summary>
    /// Saves a <see cref="TextDocument"/> via the given adapter and returns the package bytes.
    /// </summary>
    private static byte[] Save(DocxFileAdapter adapter, TextDocument document)
    {
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        return ms.ToArray();
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writing through DocxFileAdapter.Strict() must produce a package whose word/document.xml
    /// root element uses the strict (purl.oclc.org) WordprocessingML namespace.
    /// </summary>
    [Fact]
    public void StrictSave_ProducesStrictNamespaceInDocumentXml()
    {
        var adapter = DocxFileAdapter.Strict();
        var bytes = Save(adapter, SampleDocument("Hello strict world"));

        var ns = DocumentXmlRootNamespace(bytes);
        ns.Should().Be(StrictWNs,
            "word/document.xml must be declared under the strict purl.oclc.org namespace");
    }

    /// <summary>
    /// A document written as strict and read back (through the auto-detecting Strict() Load)
    /// must round-trip its text content intact.
    /// </summary>
    [Fact]
    public void StrictRoundTrip_PreservesParagraphText()
    {
        var adapter = DocxFileAdapter.Strict();
        var source = SampleDocument("First paragraph", "Second paragraph", "Third paragraph");

        var bytes = Save(adapter, source);
        var texts = ReadParagraphTexts(bytes, adapter);

        texts.Should().Contain("First paragraph");
        texts.Should().Contain("Second paragraph");
        texts.Should().Contain("Third paragraph");
    }

    /// <summary>
    /// The auto-detector inside Strict().Load must also accept a normal transitional .docx
    /// (produced by DocxFileAdapter.Docx()) and read it without error, returning the correct content.
    /// This is the "auto-detect" requirement: if the file is already transitional, skip the rewrite.
    /// </summary>
    [Fact]
    public void StrictLoad_AutoDetectsTransitionalDocx_AndOpensItWithoutError()
    {
        // Build a normal transitional package
        var transitionalAdapter = DocxFileAdapter.Docx();
        var source = SampleDocument("Transitional content");
        var transitionalBytes = Save(transitionalAdapter, source);

        // Verify the package really is transitional (sanity check)
        DocumentXmlRootNamespace(transitionalBytes).Should().Be(TransitionalWNs);

        // Now open it through the Strict() adapter (auto-detect path)
        var strictAdapter = DocxFileAdapter.Strict();
        var texts = ReadParagraphTexts(transitionalBytes, strictAdapter);

        texts.Should().Contain("Transitional content",
            "Strict().Load must transparently handle transitional packages via auto-detection");
    }

    /// <summary>
    /// A package produced by Strict().Save must be detected as strict by
    /// <see cref="StrictOoxmlTransform.IsStrict"/>.
    /// </summary>
    [Fact]
    public void StrictSave_IsDetectedAsStrictByIsStrictHelper()
    {
        var adapter = DocxFileAdapter.Strict();
        var bytes = Save(adapter, SampleDocument("Detection test"));

        using var ms = new MemoryStream(bytes);
        StrictOoxmlTransform.IsStrict(ms).Should().BeTrue(
            "a package saved via Strict() must be detected as strict by IsStrict()");

        // Stream should be back at 0 after detection
        ms.Position.Should().Be(0, "IsStrict() must restore the stream position");
    }

    /// <summary>
    /// A package produced by Docx().Save (transitional) must NOT be detected as strict.
    /// </summary>
    [Fact]
    public void TransitionalSave_IsNotDetectedAsStrict()
    {
        var adapter = DocxFileAdapter.Docx();
        var bytes = Save(adapter, SampleDocument("Transitional only"));

        using var ms = new MemoryStream(bytes);
        StrictOoxmlTransform.IsStrict(ms).Should().BeFalse(
            "a standard transitional .docx must not be mistaken for strict");
    }

    /// <summary>
    /// The Strict adapter exposes the correct catalog metadata.
    /// </summary>
    [Fact]
    public void Strict_ExposesExpectedFormatDescriptor()
    {
        IDocumentFileAdapter adapter = DocxFileAdapter.Strict();

        adapter.Extension.Should().Be(".docx");
        adapter.FormatName.Should().Be("Strict Open XML Document");
        adapter.Formats.Should().ContainSingle();

        var fmt = adapter.Formats[0];
        fmt.Extension.Should().Be(".docx");
        fmt.CanOpen.Should().BeTrue();
        fmt.CanSave.Should().BeTrue();
        fmt.OpensAsTemplate.Should().BeFalse();
    }

    /// <summary>
    /// Existing Docx() adapter is unchanged by the introduction of Strict().
    /// </summary>
    [Fact]
    public void Docx_Adapter_IsUnaffectedByStrictIntroduction()
    {
        IDocumentFileAdapter adapter = DocxFileAdapter.Docx();

        adapter.FormatName.Should().Be("Word Document");
        adapter.Formats[0].CanOpen.Should().BeTrue();
        adapter.Formats[0].CanSave.Should().BeTrue();

        // Transitional save produces transitional namespace
        var bytes = Save(DocxFileAdapter.Docx(), SampleDocument("Transitional"));
        DocumentXmlRootNamespace(bytes).Should().Be(TransitionalWNs);
    }
}
