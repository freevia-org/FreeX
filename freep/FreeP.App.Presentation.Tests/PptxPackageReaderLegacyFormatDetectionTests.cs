using Free.Shared.Opc;
using FreeP.Core.IO;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// shared-file-format-detect F2: <see cref="PptxPackageReader.Read(Stream)"/> used to hand its
/// buffered stream straight to <see cref="System.IO.Compression.ZipArchive"/> with no content
/// sniffing at all, so ANY non-ZIP content -- including a legacy PowerPoint 97-2003 (.ppt,
/// OLE2/CFB) file renamed or misidentified as .pptx -- surfaced as ZipArchive's raw, opaque
/// "End of Central Directory record could not be found" <see cref="InvalidDataException"/>.
/// FreeP has no dedicated legacy-binary .ppt reader at all (unlike FreeW's LegacyDocFileAdapter /
/// FreeX's LegacyXlsFileAdapter), which makes this the one legacy-format scenario the app can
/// never actually handle -- so at minimum the failure must name the problem instead of leaking a
/// ZIP-parser implementation detail. Reached in production via
/// <c>PresentationFilePersistenceWorkflow.Open</c> -> <c>PptxPackageReader.Read(path)</c> for
/// every non-.fxp extension.
/// </summary>
public sealed class PptxPackageReaderLegacyFormatDetectionTests
{
    [Fact]
    public void Read_LegacyOle2CompoundFile_ThrowsClearLegacyPptMessage_NotRawZipParserText()
    {
        // OLE2/Compound File Binary signature -- what every legacy binary Office document
        // (.doc/.xls/.ppt 97-2003) actually starts with when renamed or misidentified as .pptx.
        byte[] cfbSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        using var stream = new MemoryStream([.. cfbSignature, .. new byte[512]]);

        Action act = () => PptxPackageReader.Read(stream);

        var message = act.Should().Throw<InvalidDataException>().Which.Message;
        message.Should().Contain("97-2003");
        message.Should().Contain("legacy", "a renamed legacy .ppt file must be reported by name, " +
            "not with ZipArchive's raw ZIP-parser text");
        message.Should().NotContain("Central Directory",
            "the raw ZipArchive exception text must not leak through to the user-facing message");
    }

    /// <summary>
    /// Sibling no-regression case: content that is neither a ZIP package nor an OLE2/CFB legacy
    /// document (e.g. plain text mistakenly saved with a .pptx extension) must still fail with a
    /// clear, non-raw mismatch message -- just not the CFB-specific "legacy PowerPoint 97-2003"
    /// wording, since that would misidentify the actual problem.
    /// </summary>
    [Fact]
    public void Read_UnrecognizedNonZipContent_ThrowsGenericMismatchMessage_NotLegacyPptMessage()
    {
        using var stream = new MemoryStream("this is not a presentation file at all"u8.ToArray());

        Action act = () => PptxPackageReader.Read(stream);

        var message = act.Should().Throw<InvalidDataException>().Which.Message;
        message.Should().NotContain("Central Directory",
            "the raw ZipArchive exception text must not leak through to the user-facing message");
        message.Should().NotContain("97-2003",
            "plain non-ZIP, non-CFB content is not identifiably a legacy .ppt file, so the message " +
            "must stay generic rather than making a misleading legacy-.ppt claim");
    }

    /// <summary>
    /// Sibling no-regression check: an ordinary, genuine .pptx package must still open normally --
    /// the new sniff must not reject real ZIP-signed content.
    /// </summary>
    [Fact]
    public void Read_GenuinePptxPackage_StillOpensNormally_AfterSniffAdded()
    {
        var presentation = PresentationModel.CreateEmpty();
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        Action act = () => PptxPackageReader.Read(stream);

        act.Should().NotThrow(
            "a genuine ZIP-signed .pptx package must still load after the content sniff was added");
    }
}
