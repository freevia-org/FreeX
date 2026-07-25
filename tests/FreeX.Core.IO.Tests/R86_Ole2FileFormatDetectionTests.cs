using FluentAssertions;
using FreeX.Core.IO;
using NPOI.POIFS.FileSystem;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R86-services-file-format-detect-5-2: the OLE/CFB 8-byte compound-file
/// signature is shared by EVERY compound-file document -- a real "Encrypt with Password" OOXML
/// wrapper, but ALSO a genuinely unencrypted legacy .xls/.xlt/.xlb workbook (or any other OLE
/// compound-file document) that merely ended up with a .xlsx extension. Before the fix, ANY CFB
/// header unconditionally threw <see cref="WorkbookPasswordProtectedException"/> -- even for a
/// real, unencrypted compound file. The fix inspects the compound file's own directory for the
/// "EncryptedPackage"/"EncryptionInfo" stream pair that MS-OFFCRYPTO's password wrapper always
/// carries, and only reports password-protection when both are present.
/// </summary>
public sealed class R86_Ole2FileFormatDetectionTests
{
    private static byte[] BuildCompoundFile(params string[] streamNames)
    {
        var poifs = new POIFSFileSystem();
        foreach (var name in streamNames)
        {
            using var docStream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);
            poifs.CreateDocument(docStream, name);
        }

        using var output = new MemoryStream();
        poifs.WriteFileSystem(output);
        return output.ToArray();
    }

    [Fact]
    public void Load_UnencryptedOle2WorkbookWithXlsxExtension_DoesNotReportPasswordProtected()
    {
        // A real, valid OLE/CFB compound file (as NPOI's own POIFSFileSystem produces) whose only
        // stream is "Workbook" -- exactly what a genuine, unencrypted legacy .xls binary workbook
        // looks like at the container level -- given a .xlsx extension.
        var bytes = BuildCompoundFile("Workbook");

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream(bytes, writable: false);
        var act = () => adapter.Load(stream);

        // Pre-fix this unconditionally threw WorkbookPasswordProtectedException -- a false claim
        // for a file that isn't encrypted at all.
        act.Should().NotThrow<WorkbookPasswordProtectedException>();
        act.Should().Throw<WorkbookInvalidException>()
            .WithMessage("*not a valid*");
    }

    [Fact]
    public void Load_GenuinelyPasswordEncryptedOle2Workbook_StillReportsPasswordProtected()
    {
        // The real MS-OFFCRYPTO "Encrypt with Password" shape: an EncryptedPackage stream (the
        // encrypted OOXML zip payload) alongside an EncryptionInfo stream (key/algorithm
        // metadata). This must still be reported as password-protected -- the fix must not
        // over-correct and stop detecting genuine encrypted packages.
        var bytes = BuildCompoundFile("EncryptedPackage", "EncryptionInfo");

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream(bytes, writable: false);
        var act = () => adapter.Load(stream);

        act.Should().Throw<WorkbookPasswordProtectedException>()
            .WithMessage("*password*");
    }
}
