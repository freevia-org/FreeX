using System.Xml.Linq;

using FluentAssertions;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxConditionalFormatRuleSupportTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Theory]
    [InlineData("cellIs")]
    [InlineData("duplicateValues")]
    [InlineData("notContainsErrors")]
    public void IsSupportedRuleType_ReturnsTrueForKnownRuleTypes(string ruleType)
    {
        XlsxConditionalFormatRuleSupport.IsSupportedRuleType(ruleType, allowBlankType: false)
            .Should().BeTrue();
    }

    [Fact]
    public void IsSupportedRuleType_PreservesCallerBlankTypePolicy()
    {
        XlsxConditionalFormatRuleSupport.IsSupportedRuleType(null, allowBlankType: true)
            .Should().BeTrue();
        XlsxConditionalFormatRuleSupport.IsSupportedRuleType(null, allowBlankType: false)
            .Should().BeFalse();
    }

    [Fact]
    public void ConditionalFormattingHasUnsupportedRule_PreservesCallerComparisonPolicy()
    {
        var block = new XElement(
            WorksheetNs + "conditionalFormatting",
            new XElement(WorksheetNs + "cfRule", new XAttribute("type", "CELLIS")));

        XlsxConditionalFormatRuleSupport.ConditionalFormattingHasUnsupportedRule(
                block,
                WorksheetNs,
                allowBlankType: true,
                comparison: StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
        XlsxConditionalFormatRuleSupport.ConditionalFormattingHasUnsupportedRule(
                block,
                WorksheetNs,
                allowBlankType: true,
                comparison: StringComparison.Ordinal)
            .Should().BeTrue();
    }
}
