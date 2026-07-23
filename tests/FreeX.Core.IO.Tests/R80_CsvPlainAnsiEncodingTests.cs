using System.Linq;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R80-services-export-formats-5-3: plain ".csv"/".txt" Save-As must write the OS ANSI code page
/// (matching real Excel's plain "CSV (Comma delimited)" Save-As type), not UTF-8 — UTF-8 is the
/// separate opt-in "CSV UTF-8" type (<see cref="CsvUtf8FileAdapter"/>). Writing UTF-8 for the plain
/// type mojibakes non-ASCII text when the file is later reopened by real Excel, which assumes ANSI
/// for a BOM-less plain CSV rather than sniffing UTF-8.
/// </summary>
public sealed class R80_CsvPlainAnsiEncodingTests
{
    [Fact]
    public void Save_WritesNonAsciiTextUsingWindows1252_NotUtf8()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("café"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);
        var bytes = stream.ToArray();

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var expectedBytes = Encoding.GetEncoding(1252).GetBytes("café\r\n");

        bytes.Should().BeEquivalentTo(expectedBytes, options => options.WithStrictOrdering());
        // Confirm this genuinely differs from what the (wrong) UTF-8 encoding would have produced —
        // otherwise this assertion would pass vacuously for ASCII-only content.
        bytes.Should().NotBeEquivalentTo(Encoding.UTF8.GetBytes("café\r\n"));
        // No BOM of any kind (matches Excel's plain CSV/TXT, unlike the "CSV UTF-8" type).
        bytes[0].Should().NotBe(0xEF);
    }

    // No-regression sibling: pure-ASCII content is byte-identical whether encoded as UTF-8 or
    // Windows-1252 (both are ASCII supersets), so this still round-trips exactly as before the fix.
    [Fact]
    public void Save_WritesAsciiTextIdenticallyToUtf8NoBom()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(3.5));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);
        var bytes = stream.ToArray();

        bytes.Should().BeEquivalentTo(Encoding.UTF8.GetBytes("Alice,3.5\r\n"), options => options.WithStrictOrdering());
    }

    // Sibling: the explicit "CSV UTF-8" adapter is untouched by this fix and still writes UTF-8
    // with a BOM for the same non-ASCII text.
    [Fact]
    public void CsvUtf8Adapter_StillWritesUtf8WithBom_ForNonAsciiText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("café"));

        using var stream = new MemoryStream();
        new CsvUtf8FileAdapter().Save(workbook, stream);
        var bytes = stream.ToArray();

        bytes.Should().BeEquivalentTo(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("café\r\n")), options => options.WithStrictOrdering());
    }
}
