using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-33 regression tests for delimited-typeinfer:
/// - R33-io-delimited-typeinference-2-1: a 2-digit-year date must use Excel's documented
///   1930-2029 pivot window (mirrors the r31/r32 CellEntryParser fix), not .NET's default
///   Calendar.TwoDigitYearMax (2049).
/// - R33-io-delimited-typeinference-2-2: TryParseCurrency must reject a malformed thousands
///   grouping (e.g. "$1,2") instead of silently parsing it as a plain number.
/// - R33-io-delimited-typeinference-2-3: a numeric literal with more than 15 integer digits must
///   be decimal-truncated (not left as a no-op) to Excel's 15-significant-digit storage cap.
/// </summary>
public sealed class R33_DelimitedTypeInferenceTests
{
    [Fact]
    public void CoerceValue_TwoDigitYearDate_UsesExcelNineteenThirtyToTwentyTwentyNinePivot()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        // .NET's default Calendar.TwoDigitYearMax (2049) would misdate this to 2045; Excel's
        // documented pivot (00-29 -> 20xx, 30-99 -> 19xx) reads it as 1945.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("6/15/45\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        var value = sheet.GetValue(new CellAddress(sheet.Id, 1, 1));
        value.Should().BeOfType<DateTimeValue>();
        ((DateTimeValue)value).ToDateTime().Should().Be(new DateTime(1945, 6, 15));
    }

    [Fact]
    public void CoerceValue_TwoDigitYearDate_BelowPivot_StillReadsAsTwentyFirstCentury()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        // Sibling case: a two-digit year below the pivot (00-29) still reads as 20xx.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("6/15/25\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        var value = sheet.GetValue(new CellAddress(sheet.Id, 1, 1));
        value.Should().BeOfType<DateTimeValue>();
        ((DateTimeValue)value).ToDateTime().Should().Be(new DateTime(2025, 6, 15));
    }

    [Fact]
    public void CoerceValue_CurrencyWithMalformedGrouping_StaysText()
    {
        // "$1,2" has a malformed thousands grouping (the trailing group has only 1 digit, not 3);
        // real Excel leaves this as text instead of silently reading it as 12. Quoted so the comma
        // is not also read as the field delimiter.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"$1,2\"\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("$1,2"));
    }

    [Fact]
    public void CoerceValue_CurrencyWithValidGrouping_StillParsesAsNumber()
    {
        // Sibling case: a well-formed 3-digit grouping must still parse as currency. Quoted so the
        // comma is not also read as the field delimiter.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"$1,234.56\"\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(1234.56));
    }

    [Fact]
    public void CoerceValue_EighteenDigitInteger_TruncatesToFifteenSignificantDigitsWithZeroPadding()
    {
        // Excel caps stored numbers at 15 significant digits by decimal-truncating (not rounding)
        // the excess low-order digits to zero.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("123456789012345678\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        var value = sheet.GetValue(new CellAddress(sheet.Id, 1, 1));
        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value).Value.Should().Be(123456789012345000d);
    }
}
