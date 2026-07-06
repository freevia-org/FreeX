using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// The OOXML package variants (.docm/.dotx/.dotm) are pure data over the one engine: the body round-trips
/// identically, only the document.xml content type changes, and macro parts are kept only for macro-enabled
/// targets.
/// </summary>
public class OoxmlVariantRoundTripTests
{
    private static readonly XNamespace CtNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    private static TextDocument SampleDoc(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static byte[] Save(IDocumentFileAdapter adapter, TextDocument document)
    {
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        return ms.ToArray();
    }

    private static string DocumentMainContentType(byte[] packageBytes)
    {
        using var ms = new MemoryStream(packageBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var stream = zip.GetEntry("[Content_Types].xml")!.Open();
        return XDocument.Load(stream).Root!
            .Elements(CtNs + "Override")
            .First(e => (string?)e.Attribute("PartName") == "/word/document.xml")
            .Attribute("ContentType")!.Value;
    }

    private static bool PackageHasEntry(byte[] packageBytes, string entryName)
    {
        using var ms = new MemoryStream(packageBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        return zip.GetEntry(entryName) is not null;
    }

    private static TextDocument DocWithMacro()
    {
        var document = SampleDoc("Macro document");
        document.Preserved.Parts.Add(new PreservedPart(
            "/word/vbaProject.bin",
            Encoding.ASCII.GetBytes("FAKE-VBA-BYTES"),
            "application/vnd.ms-office.vbaProject",
            "http://schemas.microsoft.com/office/2006/relationships/vbaProject"));
        return document;
    }

    [Fact]
    public void Docx_WritesPlainDocumentContentType() =>
        DocumentMainContentType(Save(DocxFileAdapter.Docx(), SampleDoc("hi")))
            .Should().Be(DocxWriteOptions.DocxMainContentType);

    [Fact]
    public void Docm_WritesMacroEnabledDocumentContentType() =>
        DocumentMainContentType(Save(DocxFileAdapter.Docm(), SampleDoc("hi")))
            .Should().Be(DocxWriteOptions.DocmMainContentType);

    [Fact]
    public void Dotx_WritesTemplateContentType() =>
        DocumentMainContentType(Save(DocxFileAdapter.Dotx(), SampleDoc("hi")))
            .Should().Be(DocxWriteOptions.DotxMainContentType);

    [Fact]
    public void Dotm_WritesMacroEnabledTemplateContentType() =>
        DocumentMainContentType(Save(DocxFileAdapter.Dotm(), SampleDoc("hi")))
            .Should().Be(DocxWriteOptions.DotmMainContentType);

    [Fact]
    public void AllVariants_RoundTripBodyText()
    {
        foreach (var adapter in new[] { DocxFileAdapter.Docx(), DocxFileAdapter.Docm(), DocxFileAdapter.Dotx(), DocxFileAdapter.Dotm() })
        {
            using var ms = new MemoryStream(Save(adapter, SampleDoc("Alpha", "Beta")));
            var text = adapter.Load(ms).Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToList();
            text.Should().Contain("Alpha", $"{adapter.Extension} should round-trip the body");
            text.Should().Contain("Beta");
        }
    }

    [Fact]
    public void MacroEnabledSave_KeepsVbaProject_AndTypesItPerPart()
    {
        var bytes = Save(DocxFileAdapter.Docm(), DocWithMacro());
        PackageHasEntry(bytes, "word/vbaProject.bin").Should().BeTrue();

        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var ctStream = zip.GetEntry("[Content_Types].xml")!.Open();
        XDocument.Load(ctStream).Root!
            .Elements(CtNs + "Override")
            .Any(e => (string?)e.Attribute("PartName") == "/word/vbaProject.bin"
                   && (string?)e.Attribute("ContentType") == "application/vnd.ms-office.vbaProject")
            .Should().BeTrue();
    }

    [Fact]
    public void DocxSave_StripsVbaProject()
    {
        PackageHasEntry(Save(DocxFileAdapter.Docx(), DocWithMacro()), "word/vbaProject.bin").Should().BeFalse();
    }

    [Fact]
    public void DotxSave_StripsVbaProject()
    {
        PackageHasEntry(Save(DocxFileAdapter.Dotx(), DocWithMacro()), "word/vbaProject.bin").Should().BeFalse();
    }

    [Fact]
    public void Docm_RoundTripsMacroBytes_ThroughReadAndReSave()
    {
        using var ms = new MemoryStream(Save(DocxFileAdapter.Docm(), DocWithMacro()));
        var reloaded = DocxFileAdapter.Docm().Load(ms);

        var macro = reloaded.Preserved.Parts.FirstOrDefault(p => p.PartName == "/word/vbaProject.bin");
        macro.Should().NotBeNull();
        Encoding.ASCII.GetString(macro!.Bytes).Should().Be("FAKE-VBA-BYTES");

        // The read path captured it with the right type + relationship, so a re-save keeps it.
        PackageHasEntry(Save(DocxFileAdapter.Docm(), reloaded), "word/vbaProject.bin").Should().BeTrue();
    }

    [Fact]
    public void Dotm_RoundTripsMacroBytes_ThroughReadAndReSave()
    {
        using var ms = new MemoryStream(Save(DocxFileAdapter.Dotm(), DocWithMacro()));
        var reloaded = DocxFileAdapter.Dotm().Load(ms);

        var macro = reloaded.Preserved.Parts.FirstOrDefault(p => p.PartName == "/word/vbaProject.bin");
        macro.Should().NotBeNull();
        Encoding.ASCII.GetString(macro!.Bytes).Should().Be("FAKE-VBA-BYTES");

        // The read path captured it with the right type + relationship, so a re-save keeps it.
        PackageHasEntry(Save(DocxFileAdapter.Dotm(), reloaded), "word/vbaProject.bin").Should().BeTrue();
    }
}
