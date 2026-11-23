using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Number-format rendering parity, every expectation read back from real desktop Excel
/// (COM Range.Text, en-US) over a ~830-pair (value, format) sweep. Clusters fixed:
/// 1900-date-system serial 0 / weekday quirks, "mmmmm" for September/December, scientific
/// mantissa/exponent rules, fractions (fixed multi-digit denominators, convergent-only
/// approximation), "#." decimal point, sign suppression, fill + negative values and
/// [condition] sections that only hold negatives.
/// </summary>
public sealed class NumberFormatExcelParitySweep5Tests
{
    private static string F(double value, string format) =>
        NumberFormatter.Format(new NumberValue(value), format);

    [Theory]
    [InlineData(0, "yyyy-mm-dd", "1900-01-00")]
    [InlineData(0.5, "d/m/yy", "0/1/00")]
    [InlineData(0, "mmm d", "Jan 0")]
    [InlineData(0, "dddd", "Saturday")]
    [InlineData(1, "dddd", "Sunday")]
    [InlineData(59, "dddd", "Tuesday")]
    [InlineData(60, "dddd", "Wednesday")]
    [InlineData(61, "dddd", "Thursday")]
    [InlineData(1, "ddd dd-mmm", "Sun 01-Jan")]
    [InlineData(0, "ddd dd-mmm", "Sat 00-Jan")]
    [InlineData(0, "mmmm d, yyyy", "January 0, 1900")]
    [InlineData(0, "m/d/yyyy h:mm AM/PM", "1/0/1900 12:00 AM")]
    [InlineData(60, "yyyy-mm-dd hh:mm:ss", "1900-02-29 00:00:00")]
    [InlineData(0, "yy", "00")]
    [InlineData(1, "[$-F800]dddd, mmmm dd, yyyy", "Sunday, January 1, 1900")]
    [InlineData(59, "[$-F800]dddd, mmmm dd, yyyy", "Tuesday, February 28, 1900")]
    [InlineData(60, "[$-F800]dddd, mmmm dd, yyyy", "Wednesday, February 29, 1900")]
    [InlineData(45000.123456, "[$-F400]h:mm:ss AM/PM", "2:57:47 AM")]
    [InlineData(2958465, "mmmmm", "D")]
    [InlineData(45000, "mmmmm", "M")]
    public void Dates_FollowExcel1900Quirks(double serial, string format, string expected) =>
        F(serial, format).Should().Be(expected);

    [Fact]
    public void MmmmmForSeptember_IsInitialNotSeconds()
    {
        // 2023-09-15 = serial 45184; the quoted 'S' initial must not be re-read as the s token.
        F(45184, "mmmmm").Should().Be("S");
    }

    [Theory]
    [InlineData(12345.6789, "00.0E+00", "01.2E+04")]
    [InlineData(0.000123, "00.0E+00", "01.2E-04")]
    [InlineData(-5, "00.0E+00", "-05.0E+00")]
    [InlineData(10000000000, "00.0E+00", "01.0E+10")]
    [InlineData(0.99999, "00.0E+00", "01.0E+00")]
    [InlineData(-5, "#.##E+0", "-5.E+0")]
    [InlineData(0, "#.##E+0", "0.E+0")]
    [InlineData(999999, "#.##E+0", "1.E+6")]
    [InlineData(12345.6789, "#.##E+0", "1.23E+4")]
    [InlineData(12345.6789, "##0.0E+0", "12.3E+3")]
    [InlineData(0, "##0.0E+0", "000.0E+0")]
    [InlineData(0.5, "00.0E+0", "50.0E-2")]
    [InlineData(5, "#0.0E+0", "5.0E+0")]
    [InlineData(12345.6789, "0.0##E+0", "1.235E+4")]
    [InlineData(12345.6789, "0.00E-00", "1.23E04")]
    public void Scientific_ExponentSnapsToIntegerPlaceholderCount(double value, string format, string expected) =>
        F(value, format).Should().Be(expected);

    [Theory]
    [InlineData(3.14159, "# ??/??", "3  1/7 ")]
    [InlineData(0.333, "# ???/???", "   1/3  ")]
    [InlineData(12.999, "# ???/???", "13        ")]
    [InlineData(12.999, "?/?", "13/1")]
    [InlineData(3.14159, "??/??", "22/7 ")]
    [InlineData(3.14159, "# ?/100", "3 14/100")]
    [InlineData(0.5, "# ?/100", " 50/100")]
    [InlineData(2.75, "# ?/100", "2 75/100")]
    [InlineData(-1.25, "# ?/100", "-1 25/100")]
    [InlineData(1, "# ?/100", "1      ")]
    public void Fractions_UseConvergentsAndMultiDigitDenominators(double value, string format, string expected) =>
        F(value, format).Should().Be(expected);

    [Theory]
    [InlineData(5, "#.##", "5.")]
    [InlineData(-5, "#.##", "-5.")]
    [InlineData(0, "#.##", ".")]
    [InlineData(1234.5, "#.", "1235.")]
    [InlineData(-0.0001, "#.##", ".")]
    [InlineData(-1234.5, "#,,", "")]
    public void DecimalPointKept_AndSignSuppressedWhenNothingDisplays(double value, string format, string expected) =>
        F(value, format).Should().Be(expected);

    [Theory]
    [InlineData(-5, "[>100]\"big\";[<=100]\"small\"", "-small")]
    [InlineData(-5, "[Red][>=100]0;[Blue]0.0", "-5.0")]
    [InlineData(-5, "[=0]\"zero\";0.0", "-5.0")]
    [InlineData(-50, "[>100]0;[<=100]0.0", "-50.0")]
    [InlineData(-50, "[<-1]0.0;0", "50.0")]
    [InlineData(-0.5, "[<-1]0.0;0", "1")]
    [InlineData(-0.5, "[<-1]0.0;[<0]0.00;0", "0.50")]
    [InlineData(-5, "[>5]0.0;[<-5]0.0;0.00", "-5.00")]
    [InlineData(-50, "[<0]\"neg\";[>0]\"pos\"", "neg")]
    [InlineData(-2.5, "[Red][<0]0.00;[Blue]0.00", "2.50")]
    [InlineData(-2.5, "[<0]??.??", " 2.5 ")]
    [InlineData(-50, "[>0]0.0;[<0]-0.0;\"zero\"", "-50.0")]
    public void ConditionalSections_DropTheMinusOnlyForNegativeOnlySections(double value, string format, string expected) =>
        F(value, format).Should().Be(expected);

    [Theory]
    [InlineData(-5, "$* #,##0")]
    [InlineData(-5, "_-* #,##0_-")]
    [InlineData(-5, "_(* #,##0_)")]
    public void FillWithNegativeValue_IsNotTheOverflowIndicator(double value, string format)
    {
        var text = NumberFormatter.Format(new NumberValue(value), format, 20);
        text.Should().NotContain("#");
        text.Should().StartWith("-");
        text.TrimEnd().Should().EndWith("5");
        text.Length.Should().BeLessThanOrEqualTo(20);
    }

    [Fact]
    public void NegativeSectionWithLiteralMinusBeforeFill_KeepsMinusAtTheLeftEdge()
    {
        const string format = "_-* #,##0.00_-;-* #,##0.00_-;_-* \"-\"??_-;_-@_-";
        var text = NumberFormatter.Format(new NumberValue(-1234.5), format, 20);

        text.Should().StartWith("-");
        text.TrimEnd().Should().EndWith("1,234.50");
    }

    [Fact]
    public void AccountingFill_KeepsSkipSpacesAtBothEdges()
    {
        const string format = "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)";

        NumberFormatter.Format(new NumberValue(1234.5), format, 20).Should().StartWith(" $").And.EndWith("1,234.50 ");
        NumberFormatter.Format(new NumberValue(-1234.5), format, 20).Should().StartWith(" $").And.EndWith("(1,234.50)");
    }
}
