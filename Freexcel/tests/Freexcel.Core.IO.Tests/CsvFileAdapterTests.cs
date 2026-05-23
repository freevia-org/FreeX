using System.Text;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class CsvFileAdapterTests
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
    public void Load_TreatsStandaloneCarriageReturnsAsRecordSeparators()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("A,B\rC,D\r"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("C"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("D"));
    }

    [Fact]
    public void Load_KeepsQuotesInsideUnquotedFieldsAsLiteralText()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ab\"cd,\"quoted\"\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("ab\"cd"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("quoted"));
    }
}
