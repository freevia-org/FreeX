using FluentAssertions;
using Free.Shared.Opc;
using FreeX.Core.IO;

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
}
