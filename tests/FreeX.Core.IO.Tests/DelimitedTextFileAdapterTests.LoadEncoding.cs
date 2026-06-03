using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
    [Fact]
    public void Load_ReadsTabDelimitedValuesAndQuotedTabs()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name\tAmount\tNote\r\nAlice\t3.5\t\"a\tb\"\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new TextValue("a\tb"));
    }

    [Fact]
    public void Load_ReadsUtf8BomTextExport()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("Name\tAmount\tFlag\r\nCafe\t42\tTRUE\r\n"))
            .ToArray();
        using var stream = new MemoryStream(bytes);

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Cafe"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Load_ReadsExcelUnicodeTextExportWithUtf16Bom()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("Name\tAmount\tFlag\r\nCaf\u00e9\t42\tTRUE\r\n"))
            .ToArray();
        using var stream = new MemoryStream(bytes);

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Caf\u00e9"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Load_ReadsBigEndianUtf16TextExportWithBom()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');
        var bytes = Encoding.BigEndianUnicode.GetPreamble()
            .Concat(Encoding.BigEndianUnicode.GetBytes("Name\tAmount\tFlag\r\nCaf\u00e9\t42\tTRUE\r\n"))
            .ToArray();
        using var stream = new MemoryStream(bytes);

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Caf\u00e9"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new BoolValue(true));
    }

    [Theory]
    [MemberData(nameof(Utf32BomDelimitedTextPayloads))]
    public void Load_ReadsUtf32TextExportsWithBom(byte[] bytes)
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');
        using var stream = new MemoryStream(bytes);

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Caf\u00e9"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Load_FallsBackToWindows1252ForTextExportsWhenUtf8DecodingFails()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');
        using var stream = new MemoryStream([0x43, 0x61, 0x66, 0xE9, 0x09, 0x34, 0x32, 0x0D, 0x0A]);

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Caf\u00e9"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
    }

}
