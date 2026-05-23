using Freexcel.Core.Calc;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Calc.Tests;

public class NumberFormatterTests
{
    [Theory]
    [InlineData("General", 42.0,    "42")]
    [InlineData("General", 42.5,    "42.5")]
    [InlineData("0.00",    42.0,    "42.00")]
    [InlineData("0.00",    3.14159, "3.14")]
    [InlineData("#,##0",   1234567.0, "1,234,567")]
    [InlineData("0%",      0.42,    "42%")]
    [InlineData("0.0%",    0.4225,  "42.3%")]
    public void Format_NumberValue_AppliesFormatString(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1234.5, "$ 1,234.50")]
    [InlineData(-1234.5, "$ (1,234.50)")]
    [InlineData(0, "$ -")]
    public void AccountingSubset_RemovesSpacingDirectivesAndPreservesVisibleLiterals(double value, string expected)
    {
        const string format = "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)";

        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("#,##0.0###", 1234.567, "1,234.567")]
    [InlineData("# ?/?", 0.125, "1/8")]
    [InlineData("# ??/??", 1.25, "1  1/4 ")]
    [InlineData("# ??/16", 0.3125, " 5/16")]
    [InlineData("# ?/4", 0.5, "2/4")]
    [InlineData("0.00E+00\" kg\"", 1200, "1.20E+03 kg")]
    [InlineData("0.00E+00", 1200, "1.20E+03")]
    [InlineData("0.00E-00", 1200, "1.20E03")]
    [InlineData("0*-", 12, "12")]
    public void CustomNumberSubset_FormatsVariableDecimalsFractionsAndScientific(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0%", 0.125, "13%")]
    [InlineData("0% \"done\"", 0.125, "13% done")]
    [InlineData("0%%", 0.125, "1250%%")]
    [InlineData("0\"%\"", 12, "12%")]
    [InlineData("0\\%", 12, "12%")]
    public void CustomNumberSubset_ScalesOnlyActivePercentTokens(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0.00;0.00", -1.25, "1.25")]
    [InlineData("0.00;-0.00", -1.25, "-1.25")]
    [InlineData("# ?/?;# ?/?", -0.125, "1/8")]
    [InlineData("# ?/?;-# ?/?", -0.125, "-1/8")]
    public void CustomNumberSubset_FormatsNegativeSectionsUsingAbsoluteValue(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[>100]0.0;[<=100]0.00", 125, "125.0")]
    [InlineData("[>100]0.0;[<=100]0.00", 25, "25.00")]
    [InlineData("[<0]0.0;[=0]\"zero\";0.00", 0, "zero")]
    [InlineData("[>=1E3]0,\"K\";0", 1500, "2K")]
    [InlineData("[>=1E3]0,\"K\";0", 500, "500")]
    [InlineData("[>=+100]0;0.00", 125, "125")]
    [InlineData("[ >= 100 ]0;0.00", 25, "25.00")]
    public void CustomNumberSubset_UsesConditionalSections(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[Blue] [>100]0;0", 50, "50", null)]
    [InlineData("[Blue] [>100]0;0", 150, "150", "#0070C0")]
    public void CustomNumberSubset_AllowsWhitespaceBetweenLeadingBracketDirectives(
        string format,
        double value,
        string expectedText,
        string? expectedColor)
    {
        var result = NumberFormatter.FormatWithColor(new NumberValue(value), format);

        Assert.Equal(expectedText, result.Text);
        Assert.Equal(expectedColor, result.ColorHex);
    }

    [Theory]
    [InlineData("[Red][<0]0.00;[Blue]0.00", -2.5, "-2.50", "#FF0000")]
    [InlineData("[Red][<0]0.00;[Blue]0.00", 2.5, "2.50", "#0070C0")]
    [InlineData("[Color3][<0]0.00;[Color5]0.00", -2.5, "-2.50", "#FF0000")]
    [InlineData("[Color3][<0]0.00;[Color5]0.00", 2.5, "2.50", "#0070C0")]
    [InlineData("[Color6]0.00", 2.5, "2.50", "#FFFF00")]
    [InlineData("[Color9]0.00", 2.5, "2.50", "#800000")]
    [InlineData("[Color16]0.00", 2.5, "2.50", "#808080")]
    [InlineData("[Color46]0.00", 2.5, "2.50", "#FF6600")]
    [InlineData("[Color56]0.00", 2.5, "2.50", "#333333")]
    [InlineData("[ Red ]0.00", 2.5, "2.50", "#FF0000")]
    [InlineData("[ Color5 ]0.00", 2.5, "2.50", "#0070C0")]
    public void CustomNumberSubset_ReturnsColorFromConditionalSections(
        string format,
        double value,
        string expectedText,
        string expectedColor)
    {
        var result = NumberFormatter.FormatWithColor(new NumberValue(value), format);

        Assert.Equal(expectedText, result.Text);
        Assert.Equal(expectedColor, result.ColorHex);
    }

    [Theory]
    [InlineData("[Color9]m/d/yyyy", 45292, "1/1/2024", "#800000")]
    [InlineData("0;0;0;[Red]@", 0, "hello", "#FF0000")]
    public void CustomNumberSubset_ReturnsColorFromDateAndTextSections(
        string format,
        double numericValue,
        string expectedText,
        string expectedColor)
    {
        ScalarValue value = format.Contains('@', StringComparison.Ordinal)
            ? new TextValue(expectedText)
            : new DateTimeValue(numericValue);

        var result = NumberFormatter.FormatWithColor(value, format);

        Assert.Equal(expectedText, result.Text);
        Assert.Equal(expectedColor, result.ColorHex);
    }

    [Theory]
    [InlineData("[<45293][Red]m/d/yyyy;[Blue]m/d/yyyy", 45292, "1/1/2024", "#FF0000")]
    [InlineData("[<45293][Red]m/d/yyyy;[Blue]m/d/yyyy", 45294, "1/3/2024", "#0070C0")]
    public void CustomNumberSubset_SelectsConditionalDateTimeSections(
        string format,
        double numericValue,
        string expectedText,
        string expectedColor)
    {
        var result = NumberFormatter.FormatWithColor(new DateTimeValue(numericValue), format);

        Assert.Equal(expectedText, result.Text);
        Assert.Equal(expectedColor, result.ColorHex);
    }

    [Theory]
    [InlineData("0\\ kg", 12, "12 kg")]
    [InlineData("\\#0", 12, "#12")]
    [InlineData("0\\,", 12, "12,")]
    [InlineData("0\\;", 12, "12;")]
    [InlineData("\"ID \"\\0", 12, "ID 0")]
    [InlineData("\"ID \"\\#", 12, "ID #")]
    [InlineData("\"ID \"\\?", 12, "ID ?")]
    [InlineData("\"ID \"\\#0", 12, "ID #12")]
    [InlineData("\"ID \"0\\%0", 0.12, "ID 0%0")]
    [InlineData("\"ID \"0\\,0", 12, "ID 1,2")]
    [InlineData("0,,", 1234567, "1")]
    [InlineData("0.0,", 12345, "12.3")]
    public void CustomNumberSubset_HandlesEscapedLiteralsAndCommaScaling(string format, double value, string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("??", 5, " 5")]
    [InlineData("???", 12, " 12")]
    [InlineData("??0", 5, "  5")]
    [InlineData("??0", 1234, "1234")]
    [InlineData("0.??", 1.2, "1.2 ")]
    [InlineData("0.??", 1, "1.  ")]
    [InlineData("??0.??", 12.3, " 12.3 ")]
    [InlineData("\"ID \"??0.??", 12.3, "ID  12.3 ")]
    public void CustomNumberSubset_FormatsQuestionPlaceholdersAsAlignmentSpaces(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[$\u20AC-407]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$\u00A3-809] #,##0.00", 1234.5, "\u00A3 1,234.50")]
    [InlineData("[$-409]#,##0.00", 1234.5, "1,234.50")]
    [InlineData("-[$\u20AC-407]#,##0.00", 1234.5, "-\u20AC1.234,50")]
    [InlineData("([$\u20AC-407]#,##0.00)", 1234.5, "(\u20AC1.234,50)")]
    [InlineData("[$\u20AC-407]* #,##0.00", 1234.5, "\u20AC 1.234,50")]
    [InlineData("[$CHF-807]* #,##0.00", 1234.5, "CHF 1'234.50")]
    [InlineData("[$\u20AC-407]* \"-\"??", 0, "\u20AC -")]
    [InlineData("[$CHF-807]* \"-\"??", 0, "CHF -")]
    public void CustomNumberSubset_PreservesVisibleCurrencyFromLocaleTokens(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[$\u20AC-407]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$\u20AC-40C]#,##0.00", 1234.5, "\u20AC1 234,50")]
    [InlineData("[$\u20B4-422]#,##0.00", 1234.5, "\u20B41 234,50")]
    [InlineData("[$\u20AC-C0A]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$\u20AC-410]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$\u20AC-413]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$z\u0142-415]#,##0.00", 1234.5, "z\u01421 234,50")]
    [InlineData("[$R$-416]#,##0.00", 1234.5, "R$1.234,50")]
    [InlineData("[$\u20AC-813]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$CHF-807]#,##0.00", 1234.5, "CHF1'234.50")]
    [InlineData("[$CHF-100C]#,##0.00", 1234.5, "CHF1'234.50")]
    [InlineData("[$kr-406]#,##0.00", 1234.5, "kr1.234,50")]
    [InlineData("[$kr-414]#,##0.00", 1234.5, "kr1 234,50")]
    [InlineData("[$K\u010D-405]#,##0.00", 1234.5, "K\u010D1 234,50")]
    [InlineData("[$\u20AC-40B]#,##0.00", 1234.5, "\u20AC1 234,50")]
    [InlineData("[$Ft-40E]#,##0.00", 1234.5, "Ft1 234,50")]
    [InlineData("[$\u20BA-41F]#,##0.00", 1234.5, "\u20BA1.234,50")]
    [InlineData("[$\u20AC-816]#,##0.00", 1234.5, "\u20AC1 234,50")]
    [InlineData("[$kr-41D]#,##0.00", 1234.5, "kr1 234,50")]
    [InlineData("[$\u20BD-419]#,##0.00", 1234.5, "\u20BD1 234,50")]
    [InlineData("[$\u00A5-411]#,##0.00", 1234.5, "\u00A51,234.50")]
    [InlineData("[$\u20A9-412]#,##0.00", 1234.5, "\u20A91,234.50")]
    [InlineData("[$\u00A5-804]#,##0.00", 1234.5, "\u00A51,234.50")]
    [InlineData("[$NT$-404]#,##0.00", 1234.5, "NT$1,234.50")]
    [InlineData("[$HK$-C04]#,##0.00", 1234.5, "HK$1,234.50")]
    [InlineData("[$\u20AC-40A]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$MX$-80A]#,##0.00", 1234.5, "MX$1,234.50")]
    [InlineData("[$$-2C0A]#,##0.00", 1234.5, "$1.234,50")]
    [InlineData("[$$-340A]#,##0.00", 1234.5, "$1.234,50")]
    [InlineData("[$$-240A]#,##0.00", 1234.5, "$1.234,50")]
    [InlineData("[$S/-280A]#,##0.00", 1234.5, "S/1,234.50")]
    [InlineData("[$Bs-200A]#,##0.00", 1234.5, "Bs1.234,50")]
    [InlineData("[$$U-380A]#,##0.00", 1234.5, "$U1.234,50")]
    [InlineData("[$Bs-400A]#,##0.00", 1234.5, "Bs1.234,50")]
    [InlineData("[$Q-100A]#,##0.00", 1234.5, "Q1,234.50")]
    [InlineData("[$$-300A]#,##0.00", 1234.5, "$1.234,50")]
    [InlineData("[$\u20A1-140A]#,##0.00", 1234.5, "\u20A11\u00A0234,50")]
    [InlineData("[$RD$-1C0A]#,##0.00", 1234.5, "RD$1,234.50")]
    [InlineData("[$B/.-180A]#,##0.00", 1234.5, "B/.1,234.50")]
    [InlineData("[$Gs-3C0A]#,##0.00", 1234.5, "Gs1.234,50")]
    [InlineData("[$$-440A]#,##0.00", 1234.5, "$1,234.50")]
    [InlineData("[$$-500A]#,##0.00", 1234.5, "$1,234.50")]
    [InlineData("[$\u20B9-4009]#,##0.00", 1234567.89, "\u20B912,34,567.89")]
    [InlineData("[$\u20B9-439]#,##0.00", 1234567.89, "\u20B912,34,567.89")]
    [InlineData("[$\u20B9-445]#,##0.00", 1234567.89, "\u20B912,34,567.89")]
    [InlineData("[$\u20B9-449]#,##0.00", 1234567.89, "\u20B912,34,567.89")]
    [InlineData("[$\u20B9-44A]#,##0.00", 1234567.89, "\u20B912,34,567.89")]
    [InlineData("[$\u20B9-44E]#,##0.00", 1234567.89, "\u20B912,34,567.89")]
    [InlineData("[$$-C0C]#,##0.00", 1234.5, "$1\u00A0234,50")]
    [InlineData("[$R-1C09]#,##0.00", 1234.5, "R1\u00A0234,50")]
    [InlineData("[$\u20AB-42A]#,##0.00", 1234.5, "\u20AB1.234,50")]
    [InlineData("[$Rp-421]#,##0.00", 1234.5, "Rp1.234,50")]
    [InlineData("[$RM-43E]#,##0.00", 1234.5, "RM1,234.50")]
    [InlineData("[$\u20AA-40D]#,##0.00", 1234.5, "\u20AA1,234.50")]
    [InlineData("[$\u0E3F-41E]#,##0.00", 1234.5, "\u0E3F1,234.50")]
    [InlineData("[$\u20AC-408]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$lei-418]#,##0.00", 1234.5, "lei1.234,50")]
    [InlineData("[$\u043B\u0432.-402]#,##0.00", 1234.5, "\u043B\u0432.1\u00A0234,50")]
    [InlineData("[$\u20AC-41A]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$\u20AC-41B]#,##0.00", 1234.5, "\u20AC1\u00A0234,50")]
    [InlineData("[$\u20AC-424]#,##0.00", 1234.5, "\u20AC1.234,50")]
    [InlineData("[$RSD-241A]#,##0.00", 1234.5, "RSD1.234,50")]
    [InlineData("[$\u20AC-427]#,##0.00", 1234.5, "\u20AC1\u00A0234,50")]
    [InlineData("[$\u20AC-426]#,##0.00", 1234.5, "\u20AC1\u00A0234,50")]
    [InlineData("[$\u20AC-425]#,##0.00", 1234.5, "\u20AC1\u00A0234,50")]
    [InlineData("[$SAR-401]#,##0.00", 1234.5, "SAR1,234.50")]
    [InlineData("[$EGP-C01]#,##0.00", 1234.5, "EGP1,234.50")]
    [InlineData("[$AED-3801]#,##0.00", 1234.5, "AED1,234.50")]
    [InlineData("[$MAD-1801]#,##0.00", 1234.5, "MAD1,234.50")]
    [InlineData("[$IRR-429]#,##0.00", 1234.5, "IRR1,234/50")]
    [InlineData("[$Rs-420]#,##0.00", 1234.5, "Rs1,234.50")]
    [InlineData("[$\u060B-463]#,##0.00", 1234.5, "\u060B1.234,50")]
    [InlineData("[$IQD-492]#,##0.00", 1234.5, "IQD1,234.50")]
    [InlineData("[$R-436]#,##0.00", 1234.5, "R1\u00A0234,50")]
    [InlineData("[$R-435]#,##0.00", 1234.5, "R1,234.50")]
    [InlineData("[$R-434]#,##0.00", 1234.5, "R1\u00A0234.50")]
    [InlineData("[$Ksh-441]#,##0.00", 1234.5, "Ksh1,234.50")]
    [InlineData("[$ETB-45E]#,##0.00", 1234.5, "ETB1,234.50")]
    [InlineData("[$\u20A6-468]#,##0.00", 1234.5, "\u20A61,234.50")]
    [InlineData("[$\u20A6-46A]#,##0.00", 1234.5, "\u20A61,234.50")]
    [InlineData("[$\u20A6-470]#,##0.00", 1234.5, "\u20A61,234.50")]
    [InlineData("[$DH-380C]#,##0.00", 1234.5, "DH1.234,50")]
    [InlineData("[$CFA-280C]#,##0.00", 1234.5, "CFA1\u202F234,50")]
    [InlineData("[$\u20B8-43F]#,##0.00", 1234.5, "\u20B81\u00A0234,50")]
    [InlineData("[$KGS-440]#,##0.00", 1234.5, "KGS1\u00A0234,50")]
    [InlineData("[$UZS-443]#,##0.00", 1234.5, "UZS1\u00A0234,50")]
    [InlineData("[$\u20BC-42C]#,##0.00", 1234.5, "\u20BC1.234,50")]
    [InlineData("[$\u20BE-437]#,##0.00", 1234.5, "\u20BE1\u00A0234,50")]
    [InlineData("[$\u058F-42B]#,##0.00", 1234.5, "\u058F1,234.50")]
    [InlineData("[$\u20AE-450]#,##0.00", 1234.5, "\u20AE1,234.50")]
    [InlineData("[$NPR-461]#,##0.00", 1234.5, "NPR1,234.50")]
    [InlineData("[$LKR-45B]#,##0.00", 1234.5, "LKR1,234.50")]
    [InlineData("[$\u20AD-454]#,##0.00", 1234.5, "\u20AD1.234,50")]
    [InlineData("[$KHR-453]#,##0.00", 1234.5, "KHR1,234.50")]
    [InlineData("[$MMK-455]#,##0.00", 1234.5, "MMK1,234.50")]
    [InlineData("[$-409]#,##0.00", 1234.5, "1,234.50")]
    [InlineData("[$XYZ-999]#,##0.00", 1234.5, "XYZ1,234.50")]
    public void CustomNumberSubset_UsesKnownLcidDecimalAndGroupSeparators(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[$-407]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-414]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-41F]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-422]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-412]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-411]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-804]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-1009]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-80A]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-340A]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-2C0A]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-140A]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-500A]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-C0C]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-439]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-445]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-44A]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-42A]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-421]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-43E]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-40D]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-41E]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-408]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-418]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-402]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-41A]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-41B]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-424]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-241A]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-427]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-426]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-425]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-401]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-C01]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-3801]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-1801]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-429]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-420]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-463]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-492]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-436]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-435]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-434]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-441]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-45E]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-468]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-46A]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-470]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-380C]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-280C]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-43F]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-440]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-443]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-42C]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-437]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-42B]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-450]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-461]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-45B]dd/mm/yyyy", "01-01-2024")]
    [InlineData("[$-454]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-453]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-455]dd/mm/yyyy", "01/01/2024")]
    [InlineData("[$-409]m/d/yyyy", "1/1/2024")]
    [InlineData("[$-999]dd/mm/yyyy", "01/01/2024")]
    public void CustomNumberSubset_UsesKnownLcidDateSeparatorsForDateValues(
        string format,
        string expected)
    {
        var result = NumberFormatter.Format(new DateTimeValue(45292), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[$-407]dd/mm/yyyy", "01.01.2024")]
    [InlineData("[$-422]dd/mm/yyyy", "01.01.2024")]
    public void CustomNumberSubset_UsesKnownLcidDateSeparatorsForDateSerials(
        string format,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(45292), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("m/d/yyyy_)", "1/1/2024")]
    [InlineData("m/d/yyyy*-", "1/1/2024")]
    [InlineData("\\D: m/d/yyyy", "D: 1/1/2024")]
    public void CustomNumberSubset_CleansDateTimeSectionSpacingFillAndEscapes(
        string format,
        string expected)
    {
        var result = NumberFormatter.Format(new DateTimeValue(45292), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("h:mm:ss", 45292.52425925926, "12:34:56")]
    [InlineData("hh:mm AM/PM", 45292.56527777778, "01:34 PM")]
    [InlineData("m/d/yyyy h:mm", 45292.52430555556, "1/1/2024 12:35")]
    public void CustomNumberSubset_TreatsMinuteTokensAsMinutesNearTimeTokens(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new DateTimeValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("h:mm:ss.0", "12:34:56.8")]
    [InlineData("h:mm:ss.00", "12:34:56.79")]
    [InlineData("h:mm:ss.000", "12:34:56.789")]
    public void CustomNumberSubset_FormatsFractionalSecondTokens(
        string format,
        string expected)
    {
        var value = new DateTime(2024, 1, 1, 12, 34, 56, 789).ToOADate();

        var result = NumberFormatter.Format(new DateTimeValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("h:mm A/P", 45292.06527777778, "1:34 A")]
    [InlineData("h:mm A/P", 45292.56527777778, "1:34 P")]
    public void CustomNumberSubset_FormatsCompactAmPmMarkers(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new DateTimeValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("h \"hours\" m \"minutes\"", "12 hours 34 minutes")]
    [InlineData("m \"month\" d", "1 month 1")]
    public void CustomNumberSubset_DisambiguatesMinutesAcrossQuotedLiterals(
        string format,
        string expected)
    {
        var result = NumberFormatter.Format(new DateTimeValue(45292.52425925926), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("mmmmm d, yyyy", 45292, "J 1, 2024")]
    [InlineData("mmmmm d, yyyy", 45323, "F 1, 2024")]
    public void CustomNumberSubset_FormatsFiveMonthTokensAsMonthInitial(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new DateTimeValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[h]:mm:ss_)", "36:00:00")]
    [InlineData("[h]:mm:ss*-", "36:00:00")]
    [InlineData("\\T [h]:mm:ss", "T 36:00:00")]
    public void CustomNumberSubset_CleansElapsedTimeSectionSpacingFillAndEscapes(
        string format,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(1.5), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[h]:mm:ss.000", "36:00:00.789")]
    [InlineData("[m]:ss.00", "2160:00.79")]
    [InlineData("[s].0", "129600.8")]
    public void CustomNumberSubset_FormatsElapsedFractionalSecondTokens(
        string format,
        string expected)
    {
        var value = (TimeSpan.FromHours(36) + TimeSpan.FromMilliseconds(789)).TotalDays;

        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("[h]:mm:ss", "36:00:00")]
    [InlineData("[m]:ss.00", "2160:00.79")]
    public void CustomNumberSubset_FormatsElapsedTimeForDateTimeValues(
        string format,
        string expected)
    {
        var value = (TimeSpan.FromHours(36) + TimeSpan.FromMilliseconds(789)).TotalDays;

        var result = NumberFormatter.Format(new DateTimeValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("\\_0", 12, "_12")]
    [InlineData("\\*0", 12, "*12")]
    [InlineData("\\_m/d/yyyy", 45292, "_1/1/2024")]
    [InlineData("\\*[h]:mm:ss", 1.5, "*36:00:00")]
    public void CustomNumberSubset_PreservesEscapedSpacingAndFillDirectiveCharacters(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CustomNumberSubset_FormatsQuotedOnlyZeroSectionAsLiteral()
    {
        var result = NumberFormatter.Format(new NumberValue(0), "0;0;\"-\"");

        Assert.Equal("-", result);
    }

    [Theory]
    [InlineData("\"0\"", 12, "0")]
    [InlineData("\"??\"", 12, "??")]
    public void CustomNumberSubset_TreatsQuotedPlaceholdersAsLiterals(
        string format,
        double value,
        string expected)
    {
        var result = NumberFormatter.Format(new NumberValue(value), format);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_DateSerial_WithDateFormat_ReturnsFormattedDate()
    {
        // OADate 45292 = 2024-01-01
        var result = NumberFormatter.Format(new NumberValue(45292), "m/d/yyyy");
        Assert.Equal("1/1/2024", result);
    }

    [Fact]
    public void Format_DateTimeValue_WithGeneralFormat_ReturnsShortDate()
    {
        var result = NumberFormatter.Format(new DateTimeValue(45292), "General");
        Assert.Equal("01/01/2024", result);
    }

    [Theory]
    [InlineData("General", "hello", "hello")]
    [InlineData("@",       "hello", "hello")]
    public void Format_TextValue_PassesThrough(string format, string value, string expected)
    {
        var result = NumberFormatter.Format(new TextValue(value), format);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0;0;0;_(@_)", "hello", "hello")]
    [InlineData("0;0;0;@*-", "hello", "hello")]
    [InlineData("0;0;0;\"SKU \"@", "A1", "SKU A1")]
    [InlineData("0;0;0;\\@@", "A1", "@A1")]
    public void CustomNumberSubset_CleansTextSectionSpacingFillAndEscapes(
        string format,
        string value,
        string expected)
    {
        var result = NumberFormatter.Format(new TextValue(value), format);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("@ \"units\"", "A1", "A1 units")]
    [InlineData("\"SKU \"@", "A1", "SKU A1")]
    [InlineData("\\@@", "A1", "@A1")]
    public void CustomNumberSubset_AppliesSingleTextSectionWhenItContainsPlaceholder(
        string format,
        string value,
        string expected)
    {
        var result = NumberFormatter.Format(new TextValue(value), format);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_BlankValue_ReturnsEmpty()
    {
        Assert.Equal("", NumberFormatter.Format(BlankValue.Instance, "General"));
    }

    [Fact]
    public void Format_ErrorValue_ReturnsCode()
    {
        Assert.Equal("#DIV/0!", NumberFormatter.Format(new ErrorValue("#DIV/0!"), "General"));
    }

    [Fact]
    public void Format_LcidToken_FallsBackToDotNetCultureSeparatorsForUncatalogedLocale()
    {
        var result = NumberFormatter.Format(new NumberValue(1234.5), "[$-0C07]#,##0.00");

        Assert.Equal("1\u00A0234,50", result);
    }

    [Fact]
    public void Format_LcidCurrencyToken_PreservesSymbolWhenUsingCultureFallback()
    {
        var result = NumberFormatter.Format(new NumberValue(1234.5), "[$€-0C07]#,##0.00");

        Assert.Equal("€1\u00A0234,50", result);
    }
}
