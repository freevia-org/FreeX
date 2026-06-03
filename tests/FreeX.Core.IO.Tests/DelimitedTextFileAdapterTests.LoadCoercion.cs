using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
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
    public void Load_StripsQuotedTextMarkersForPercentagesAndErrors()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"'12%\"\t\"'#N/A\"\t\"'#FIELD!\"\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("12%"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("#N/A"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("#FIELD!"));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForWhitespacePaddedBooleans()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(" TRUE \t\" false \"\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForPercentages()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12.5%\t-3%\t4%\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(0.125));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(-0.03));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(0.04));
    }

    [Fact]
    public void Load_UsesCurrentCultureForNumbersAndPercentagesWithInvariantFallback()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1,25\t12,5%\t1.25\tNaN\tInfinity\tNaN%\r\n"));

            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(1.25));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(0.125));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(1.25));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(new TextValue("NaN"));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 5)).Should().Be(new TextValue("Infinity"));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 6)).Should().Be(new TextValue("NaN%"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Load_UsesCurrentCultureForDateTimesWithInvariantFallback()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
                "17/05/2026\t17/05/2026 09:30\t17 mai 2026 21:45\tMay 17, 2026 9:30 AM\r\n"));

            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
                .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
                .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 3))
                .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 21, 45, 0)));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 4))
                .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Load_KeepsNonFiniteNumericTextAsText()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("NaN\tInfinity\t-Infinity\tInfinity%\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("NaN"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Infinity"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("-Infinity"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(new TextValue("Infinity%"));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForErrorLiterals()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("#N/A\t#DIV/0!\t#REF!\t#CIRCULAR!\t#FIELD!\t#BLOCKED!\t#GETTING_DATA\t#CONNECT!\t#UNKNOWN!\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(ErrorValue.NA);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(ErrorValue.DivByZero);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(ErrorValue.Ref);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(ErrorValue.Circular);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 5)).Should().Be(new ErrorValue("#FIELD!"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 6)).Should().Be(new ErrorValue("#BLOCKED!"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 7)).Should().Be(new ErrorValue("#GETTING_DATA"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 8)).Should().Be(new ErrorValue("#CONNECT!"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 9)).Should().Be(new ErrorValue("#UNKNOWN!"));
    }

    [Fact]
    public void Load_TrimsOnceForPaddedCoercionValues()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(" #N/A \t 12.5% \t 2026-05-17 \t 09:30 \t $1,234.50 \r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(ErrorValue.NA);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(0.125));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 5)).Should().Be(new NumberValue(1234.50));
    }

}
