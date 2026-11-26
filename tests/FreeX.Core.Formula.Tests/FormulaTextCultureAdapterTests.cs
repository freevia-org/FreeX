using System.Globalization;

using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class FormulaTextCultureAdapterTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    [Fact]
    public void ToInvariantFormulaText_ConvertsDecimalAndListSeparatorsWithoutChangingStringsOrNames()
    {
        var localized = "SUM(\"1,5; still text\";IF('West; Coast'!A1=1,5;Table[Col;Part];2,5))";

        var invariant = FormulaTextCultureAdapter.ToInvariantFormulaText(localized, French);

        invariant.Should().Be("SUM(\"1,5; still text\",IF('West; Coast'!A1=1.5,Table[Col;Part],2.5))");
    }

    [Fact]
    public void ToInvariantFormulaText_ConvertsRegionalArrayColumnSeparatorAndKeepsRows()
    {
        var localized = "SUM({1,5\\2,5;3,5\\4,5})";

        FormulaTextCultureAdapter.ToInvariantFormulaText(localized, French)
            .Should().Be("SUM({1.5,2.5;3.5,4.5})");
    }

    [Fact]
    public void ToInvariantFormulaText_ConvertsLeadingRegionalDecimalSeparator()
    {
        FormulaTextCultureAdapter.ToInvariantFormulaText("SUM(,5;1,5)", French)
            .Should().Be("SUM(.5,1.5)");
    }

    [Fact]
    public void ToRegionalFormulaText_RoundTripsNumbersArgumentsAndArrayConstants()
    {
        const string invariant = "SUM(1.5,IF(A1=2.5,3.5,4.5),{6.5,7.5;8.5,9.5})";

        var localized = FormulaTextCultureAdapter.ToRegionalFormulaText(invariant, French);

        localized.Should().Be("SUM(1,5;IF(A1=2,5;3,5;4,5);{6,5\\7,5;8,5\\9,5})");
        FormulaTextCultureAdapter.ToInvariantFormulaText(localized, French).Should().Be(invariant);
    }

    [Fact]
    public void ToInvariantFormulaText_LeavesInvariantCultureFormulaUnchanged()
    {
        const string formula = "SUM(1.5,IF(A1=2.5,3.5,4.5))";

        FormulaTextCultureAdapter.ToInvariantFormulaText(formula, CultureInfo.GetCultureInfo("en-US"))
            .Should().Be(formula);
    }
}
