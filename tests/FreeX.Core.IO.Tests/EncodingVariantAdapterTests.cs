using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the encoding-variant delimited adapters: "CSV UTF-8 (Comma delimited)" (UTF-8 + BOM) and
/// "Unicode Text" (UTF-16LE + BOM). They share the existing comma/tab engine — only the on-disk
/// encoding differs — so the tests focus on the BOM bytes and a value round-trip with non-ASCII text.
/// </summary>
public sealed class EncodingVariantAdapterTests
{
    private static Workbook BuildSample()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Café"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("naïve—Ω"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new BoolValue(true));
        return wb;
    }

    [Fact]
    public void CsvUtf8_WritesUtf8ByteOrderMark()
    {
        using var stream = new MemoryStream();
        new CsvUtf8FileAdapter().Save(BuildSample(), stream);

        var bytes = stream.ToArray();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Fact]
    public void CsvUtf8_RoundTripsNonAsciiValues()
    {
        var adapter = new CsvUtf8FileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(BuildSample(), stream);
        stream.Position = 0;

        var sheet = adapter.Load(stream).Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Café"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("naïve—Ω"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void CsvUtf8_IsCommaDelimited()
    {
        using var stream = new MemoryStream();
        new CsvUtf8FileAdapter().Save(BuildSample(), stream);

        // Skip the 3-byte BOM; the first row must be comma-separated.
        var text = Encoding.UTF8.GetString(stream.ToArray()[3..]);
        text.Should().StartWith("Café,42");
    }

    [Fact]
    public void UnicodeText_WritesUtf16LeByteOrderMark()
    {
        using var stream = new MemoryStream();
        new UnicodeTextFileAdapter().Save(BuildSample(), stream);

        var bytes = stream.ToArray();
        bytes.Take(2).Should().Equal(0xFF, 0xFE); // UTF-16 little-endian BOM
    }

    [Fact]
    public void UnicodeText_RoundTripsNonAsciiValues()
    {
        var adapter = new UnicodeTextFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(BuildSample(), stream);
        stream.Position = 0;

        var sheet = adapter.Load(stream).Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Café"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("naïve—Ω"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void UnicodeText_IsTabDelimited()
    {
        using var stream = new MemoryStream();
        new UnicodeTextFileAdapter().Save(BuildSample(), stream);

        // Decode honoring the UTF-16LE BOM; the first row must be tab-separated.
        var text = new UnicodeEncoding(bigEndian: false, byteOrderMark: true).GetString(stream.ToArray())[1..];
        text.Should().StartWith("Café\t42");
    }
}
