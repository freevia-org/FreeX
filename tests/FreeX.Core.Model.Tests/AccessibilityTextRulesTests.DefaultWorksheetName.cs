using FluentAssertions;
using FreeX.Core.Commands;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed class AccessibilityTextRulesTests_DefaultWorksheetName
{
    [Theory]
    [InlineData("Sheet1")]
    [InlineData("Sheet2")]
    [InlineData("sheet3")]
    [InlineData("SHEET42")]
    [InlineData("Sheet007")]
    public void IsDefaultWorksheetName_MatchesExcelAutoNamingPattern(string name)
    {
        AccessibilityTextRules.IsDefaultWorksheetName(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("Sheet-1")]
    [InlineData("Sheet+1")]
    [InlineData("Sheet 1")]
    [InlineData("SheetX")]
    [InlineData("Sheet")]
    [InlineData("Sheet1a")]
    [InlineData("Sheet 1 ")]
    public void IsDefaultWorksheetName_RejectsNonDigitSuffixes(string name)
    {
        AccessibilityTextRules.IsDefaultWorksheetName(name).Should().BeFalse();
    }
}
