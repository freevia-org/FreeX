using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R86-services-file-format-detect-5-3: a real .xlsx/.zip file renamed to .csv must not be silently
/// parsed as garbage delimited-text cells. <see cref="CsvFileAdapter.Load"/> (via
/// <see cref="DelimitedTextWorkbookReader"/>'s shared decode path) previously had no content sniff: the
/// strict UTF-8 decode throws on the compressed binary bytes, gets caught, and falls back to
/// Windows-1252, which decodes ANY byte sequence without error -- so the workbook "opened" full of
/// mojibake cells instead of surfacing a clear format-mismatch error. This mirrors, in the opposite
/// direction, the existing <c>WorkbookOpenTargetPlanner.LooksLikeZipPackage</c> guard for .xlsx files
/// renamed to .csv.
/// </summary>
public sealed class R86_CsvBinaryZipContentDetectionTests
{
    [Fact]
    public void Load_RejectsZipMagicBytesInsteadOfProducingGarbageCells()
    {
        // Minimal ZIP local-file-header signature ("PK\x03\x04") followed by bytes that would otherwise
        // be misread as delimited text (e.g. stray commas), matching a real OOXML .xlsx renamed to .csv.
        byte[] zipLikeBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, (byte)',', (byte)'1'];
        using var stream = new MemoryStream(zipLikeBytes);

        var act = () => new CsvFileAdapter().Load(stream);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Load_RejectsEmbeddedNulBytesInsteadOfProducingGarbageCells()
    {
        // Binary content (e.g. an OLE2 .xls renamed to .csv) that isn't ZIP-signed but still contains
        // NUL bytes, which never legitimately appear in delimited text.
        byte[] binaryBytes = [(byte)'A', 0x00, (byte)'B', (byte)',', (byte)'1'];
        using var stream = new MemoryStream(binaryBytes);

        var act = () => new CsvFileAdapter().Load(stream);

        act.Should().Throw<InvalidDataException>();
    }

    // No-regression sibling: a genuine CSV -- including UTF-8 BOM and quoted fields -- still parses
    // normally and is unaffected by the new binary sniff.
    [Fact]
    public void Load_StillParsesGenuineCsvWithBomAndQuotedFields()
    {
        var bytes = EncodedTextPayloads.WithBom(Encoding.UTF8, "Name,Amount,Note\r\nAlice,3.5,\"a,b\"\r\n");
        using var stream = new MemoryStream(bytes);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new TextValue("a,b"));
    }

    // No-regression sibling: a genuine ANSI/Windows-1252 CSV (non-ASCII byte that fails strict UTF-8,
    // but no ZIP signature or NUL byte) still falls back to Windows-1252 rather than being rejected.
    [Fact]
    public void Load_StillFallsBackToWindows1252ForNonZipNonNulBytes()
    {
        using var stream = new MemoryStream([0x43, 0x61, 0x66, 0xE9, 0x0D, 0x0A]);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Café"));
    }
}
