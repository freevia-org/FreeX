using System.IO;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// R162/shared-file-format-detect F1: <see cref="DocxReader"/> must sniff a stream's real content
/// before handing it to <see cref="System.IO.Compression.ZipArchive"/>, so a mismatched file (a
/// legacy Word 97-2003 .doc/.dot OLE2/CFB binary, a truncated download, an empty file, or any other
/// non-ZIP binary saved with a WordprocessingML extension) produces a clear, actionable message
/// instead of ZipArchive's raw "End of Central Directory record could not be found" exception.
/// </summary>
public sealed class DocxReaderFormatSniffTests
{
    [Fact]
    public void Read_RejectsCompoundFileDocument_WithClearMessage()
    {
        // The 8-byte OLE2/CFB signature shared by legacy Word 97-2003 .doc/.dot binaries (and other
        // compound-file documents), padded out to a plausible minimum CFB sector size -- exactly the
        // shape produced by renaming an old .doc to .docx.
        using var stream = new MemoryStream(BuildCompoundFileHeader());

        Action act = () => DocxReader.Read(stream);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*legacy Word 97-2003*",
                "the raw ZipArchive 'End of Central Directory record could not be found' message gives " +
                "the user no indication that the file is actually an old .doc renamed to .docx");
    }

    [Fact]
    public void Read_RejectsEmptyStream_WithClearMessage()
    {
        using var stream = new MemoryStream();

        Action act = () => DocxReader.Read(stream);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*not a valid OOXML package*",
                "an empty file (e.g. a zero-byte stub left by an interrupted download) must not surface " +
                "ZipArchive's raw low-level exception");
    }

    [Fact]
    public void Read_RejectsArbitraryNonZipBinary_WithClearMessage()
    {
        // Some other binary format entirely (not CFB, not ZIP) saved under a .docx extension.
        using var stream = new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A]); // "%PDF-1.4\n"

        Action act = () => DocxReader.Read(stream);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*not a valid OOXML package*");
    }

    [Fact]
    public void Read_StillOpensGenuineDocxPackage_AfterSniffAdded()
    {
        // Sibling no-regression check: an ordinary document package written by FreeW's own writer
        // (real ZIP/OPC bytes) must continue to load normally through the new sniff.
        var document = new TextDocument();
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;

        Action act = () => DocxReader.Read(stream);

        act.Should().NotThrow();
    }

    private static byte[] BuildCompoundFileHeader()
    {
        // 512 bytes: the standard CFB header sector size. Only the leading 8-byte signature is
        // meaningful for the sniff; the rest is zero-padding, matching the minimal repro described
        // in the finding (D0 CF 11 E0 A1 B1 1A E1 padded to 512 bytes).
        var buffer = new byte[512];
        byte[] signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        signature.CopyTo(buffer, 0);
        return buffer;
    }
}
