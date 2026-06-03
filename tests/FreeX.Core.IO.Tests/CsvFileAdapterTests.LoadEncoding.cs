using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

public sealed partial class CsvFileAdapterTests
{
    [Fact]
    public void Load_UsesExcelLikeTextCoercionForBooleans()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("TRUE,false\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Load_UsesCurrentCultureForSeparatorDirectedCsvNumbersWithInvariantFallback()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=;\r\nValue;Rate;Invariant;Bad\r\n1,25;12,5%;1.25;Infinity\r\n"));

            var workbook = new CsvFileAdapter().Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(1.25));
            sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(0.125));
            sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new NumberValue(1.25));
            sheet.GetValue(new CellAddress(sheet.Id, 2, 4)).Should().Be(new TextValue("Infinity"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Load_HonorsUtf8ByteOrderMark()
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("Name,Amount,Flag\r\nCafe,42,TRUE\r\n"))
            .ToArray();
        using var stream = new MemoryStream(bytes);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Cafe"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new BoolValue(true));
    }

    [Theory]
    [MemberData(nameof(Utf16BomCsvPayloads))]
    public void Load_HonorsUtf16ByteOrderMarks(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Caf\u00e9"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Load_AccessibleMemoryStreamWithNonzeroPositionReadsRemainingSliceAndConsumesStream()
    {
        var padding = Encoding.UTF8.GetBytes("outside segment\r\n");
        var prefix = Encoding.UTF8.GetBytes("ignored,prefix\r\n");
        var csv = Encoding.UTF8.GetBytes("Name,Amount\r\nAlice,3.5\r\n");
        var buffer = padding.Concat(prefix).Concat(csv).ToArray();
        using var stream = new MemoryStream(
            buffer,
            index: padding.Length,
            count: prefix.Length + csv.Length,
            writable: false,
            publiclyVisible: true);
        stream.Position = prefix.Length;

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1)).Should().BeNull();
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public void Load_InaccessibleMemoryStreamWithNonzeroPositionUsesCopyPathAndConsumesStream()
    {
        var prefix = Encoding.UTF8.GetBytes("ignored,prefix\r\n");
        var csv = Encoding.UTF8.GetBytes("Name,Amount\r\nAlice,3.5\r\n");
        using var stream = new MemoryStream(prefix.Concat(csv).ToArray(), writable: false);
        stream.Position = prefix.Length;

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public void Load_NonMemoryStreamDecodesOnlyCopiedBytes()
    {
        using var stream = new ForwardOnlyReadStream(Encoding.UTF8.GetBytes("Name,Amount\r\nAlice,3.5\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1)).Should().BeNull();
    }

    [Fact]
    public void Load_AccessibleMemoryStreamPastLengthReadsEmptySliceAndConsumesStream()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes("Name,Amount\r\nAlice,3.5\r\n"));
        stream.Position = stream.Length + 10;

        var workbook = new CsvFileAdapter().Load(stream);

        workbook.Sheets.Single().CellCount.Should().Be(0);
        stream.Position.Should().Be(stream.Length);
    }

    [Fact]
    public void Load_FallsBackToWindows1252WhenUtf8DecodingFails()
    {
        using var stream = new MemoryStream([0x43, 0x61, 0x66, 0xE9, 0x0D, 0x0A]);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Café"));
    }

    [Theory]
    [MemberData(nameof(Utf32BomCsvPayloads))]
    public void Load_HonorsUtf32ByteOrderMarks(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
    }

}
