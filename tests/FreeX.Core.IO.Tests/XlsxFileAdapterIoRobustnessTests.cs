using FluentAssertions;
using Free.Shared.Opc;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for U-io-robustness findings K42 (password-encrypted .xlsx detection)
/// and P1 (corrupt/non-zip .xlsx must surface a graceful error instead of crashing the
/// sanitizer's unguarded reopen of the same unreadable bytes).
/// </summary>
public sealed class XlsxFileAdapterIoRobustnessTests
{
    // Real "Encrypt with Password" .xlsx files are OLE/CFB compound files whose payload is an
    // EncryptedPackage stream. We don't need a fully valid CFB structure to prove the detection
    // fires — only the well-known 8-byte compound-file signature Excel/Office always writes.
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    [Fact]
    public void Load_PasswordEncryptedCfbWorkbook_ThrowsClearPasswordProtectedError()
    {
        using var stream = new MemoryStream();
        stream.Write(CompoundFileSignature);
        // Pad out some trailing bytes so this looks like more than just a bare signature.
        stream.Write(new byte[512]);
        stream.Position = 0;

        var adapter = new XlsxFileAdapter();
        var act = () => adapter.Load(stream);

        act.Should().Throw<WorkbookPasswordProtectedException>()
            .WithMessage("*password*");
    }

    [Fact]
    public void Load_PasswordEncryptedCfbWorkbook_DoesNotThrowRawZipException()
    {
        using var stream = new MemoryStream();
        stream.Write(CompoundFileSignature);
        stream.Write(new byte[512]);
        stream.Position = 0;

        var adapter = new XlsxFileAdapter();
        var act = () => adapter.Load(stream);

        // Must not surface as a low-level zip/InvalidDataException — the whole point of the
        // fix is that the user sees the real reason, not a confusing format error.
        act.Should().NotThrow<InvalidDataException>();
    }

    [Fact]
    public void Load_TruncatedNonZipXlsx_ThrowsGracefulCorruptFileErrorInsteadOfCrashing()
    {
        // Not a zip at all (and not a CFB file either) — e.g. a truncated download or some
        // unrelated file renamed to .xlsx. Must not let a raw low-level zip exception escape
        // from the sanitizer's unguarded reopen of the same unreadable bytes.
        using var stream = new MemoryStream(
        [
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
            0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F
        ]);

        var adapter = new XlsxFileAdapter();
        var act = () => adapter.Load(stream);

        act.Should().Throw<WorkbookInvalidException>()
            .WithMessage("*not a valid*");
    }

    [Fact]
    public void Load_EmptyStream_DoesNotThrowPasswordProtectedFalsePositive()
    {
        // Guard against the signature check false-triggering on tiny/empty inputs.
        using var stream = new MemoryStream();

        var adapter = new XlsxFileAdapter();
        var act = () => adapter.Load(stream);

        act.Should().NotThrow<WorkbookPasswordProtectedException>();
    }

    // default-masks-missing F1: exercises the exact production call site named in the finding --
    // XlsxFileAdapter.cs, "workbookTheme = packageParts.HasTheme ? XlsxWorkbookThemeReader.Load(packageArchive) : ...".
    // A real workbook is saved (so xl/theme/theme1.xml legitimately exists), then that one entry's
    // bytes are corrupted in place before reloading. Before the fix, the corrupt-but-present part
    // was silently swapped for WorkbookTheme.Office and the file "opened successfully" with wrong
    // colors -- setting up the next save to permanently overwrite the still-corrupt original
    // theme1.xml with a synthesized default. The file must now fail to open instead, with a clear
    // reason, so no subsequent save can clobber the original bytes.
    [Fact]
    public void Load_WorkbookWithCorruptedThemePart_ThrowsInsteadOfSilentlyDefaultingTheme()
    {
        var workbook = new Workbook("ThemeCorruption");
        workbook.AddSheet("Sheet1");
        var savingAdapter = new XlsxFileAdapter();

        using var saved = new MemoryStream();
        savingAdapter.Save(workbook, saved);
        saved.Position = 0;

        using var corrupted = new MemoryStream();
        saved.CopyTo(corrupted);
        saved.Position = 0;
        corrupted.Position = 0;

        using (var archive = new System.IO.Compression.ZipArchive(corrupted, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var themeEntry = archive.GetEntry("xl/theme/theme1.xml");
            themeEntry.Should().NotBeNull("a freshly saved workbook must carry a theme part for this test to be meaningful");
            themeEntry!.Delete();
            var replacement = archive.CreateEntry("xl/theme/theme1.xml");
            using var writer = new StreamWriter(replacement.Open());
            // Truncated/malformed XML -- simulates a corrupted or partially-written zip entry.
            writer.Write("<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">");
        }

        corrupted.Position = 0;
        var loadingAdapter = new XlsxFileAdapter();
        var act = () => loadingAdapter.Load(corrupted);

        act.Should().Throw<XlsxThemePartCorruptException>();
    }
}
