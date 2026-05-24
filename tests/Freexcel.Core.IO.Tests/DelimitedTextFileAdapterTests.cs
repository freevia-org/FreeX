using System.Text;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class DelimitedTextFileAdapterTests
{
    [Fact]
    public void Load_DecodesBufferedTextWithoutCopyingMemoryStreamToArray()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "Freexcel.Core.IO", "DelimitedTextWorkbookReader.cs"));

        source.Should().NotContain(
            "memory.ToArray()",
            "text load should decode the buffered stream segment without duplicating the full byte array");
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".tsv")]
    [InlineData(".tab")]
    public void Formats_AreOpenOnly(string extension)
    {
        var adapter = new DelimitedTextFileAdapter(extension, "Text (Tab delimited)", '\t');

        adapter.Formats.Should().ContainSingle(format =>
            format.Extension == extension &&
            format.CanOpen &&
            !format.CanSave);
    }

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
    public void Load_UsesExcelLikeTextCoercionForBooleansAndQuotedNumbers()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("TRUE\tfalse\t\"0042\"\t\"text\"\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new BoolValue(false));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(42));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(new TextValue("text"));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForPercentages()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12.5%\t-3%\t\"-4%\"\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(0.125));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(-0.03));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(-0.04));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForErrorLiterals()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("#N/A\t#DIV/0!\t#REF!\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(ErrorValue.NA);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(ErrorValue.DivByZero);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoDatesAndTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17\t2026-05-17 09:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoSlashDatesAndTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026/5/17\t2026/05/17 9:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoDateTimesWithSingleDigitHours()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17 9:30\t2026-05-17T9:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 15, 250)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForUsSlashDatesWithFourDigitYears()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("5/17/2026\t5/17/2026 09:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForUsHyphenDatesWithFourDigitYears()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("5-17-2026\t5-17-2026 9:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForUsSlashDatesWithSingleDigit24HourTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("5/17/2026 9:30\t5/17/26 9:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 15, 250)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForUsSlashDatesWithTwoDigitYears()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("5/17/26\t5/17/26 09:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForMonthNameDateTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("May 17, 2026 9:30 AM\tMay 17, 2026 21:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 21, 30, 15, 250)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForMonthNameDateTimesWithoutCommas()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("May 17 2026 9:30 AM\tMay 17 2026 21:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 21, 30, 15, 250)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForDayFirstMonthNameDateTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("17 May 2026 9:30\t17-May-26 9:45 PM\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 21, 45, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForStandaloneTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("09:30\t21:45:15\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new DateTimeValue(new TimeSpan(21, 45, 15).TotalDays));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForFractionalSecondDateTimesAndTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17 09:30:15.250\t09:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 15, 250)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(new DateTimeValue(new TimeSpan(0, 9, 30, 15, 250).TotalDays));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForStandaloneAmPmTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("9:30 AM\t9:45:15 PM\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new DateTimeValue(new TimeSpan(21, 45, 15).TotalDays));
    }

    [Fact]
    public void Load_IgnoresFieldsBeyondExcelColumnLimit()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        var fields = Enumerable.Repeat("", (int)CellAddress.MaxCol + 1).ToArray();
        fields[CellAddress.MaxCol - 1] = "last";
        fields[CellAddress.MaxCol] = "overflow";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\t', fields)));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, CellAddress.MaxCol)).Should().Be(new TextValue("last"));
        sheet.GetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol + 1)).Should().BeNull();
    }

    [Fact]
    public void Load_IgnoresRecordsBeyondExcelRowLimit()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        var builder = new StringBuilder();
        for (var row = 1; row < CellAddress.MaxRow; row++)
            builder.AppendLine();
        builder.AppendLine("last");
        builder.AppendLine("overflow");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, CellAddress.MaxRow, 1)).Should().Be(new TextValue("last"));
        sheet.GetCell(new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1)).Should().BeNull();
    }

    [Fact]
    public void Load_TreatsStandaloneCarriageReturnsAsRecordSeparators()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("A\tB\rC\tD\r"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("C"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("D"));
    }

    [Fact]
    public void Load_KeepsQuotesInsideUnquotedFieldsAsLiteralText()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ab\"cd\t\"quoted\"\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("ab\"cd"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("quoted"));
    }

    [Fact]
    public void Save_IsNotSupported()
    {
        var adapter = new DelimitedTextFileAdapter(".txt", "Text (Tab delimited)", '\t');

        var act = () => adapter.Save(new Workbook("Book1"), new MemoryStream());

        act.Should().Throw<NotSupportedException>();
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate workspace file {Path.Combine(parts)}.");
    }
}
