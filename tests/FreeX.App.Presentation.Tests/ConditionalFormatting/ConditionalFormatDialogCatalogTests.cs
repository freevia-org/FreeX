using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class ConditionalFormatDialogCatalogTests
{
    [Fact]
    public void Catalog_ExposesDialogOptionsAsResourceKeys()
    {
        ConditionalFormatDialogCatalog.FormatStyleOptions
            .Should()
            .Contain(option => option.LabelKey == "ConditionalFormatDialog_FormatStyle_3ColorScale"
                && option.RuleType == ConditionalFormatDialogCatalog.ColorScaleRule
                && option.UseThreeColorScale);

        ConditionalFormatDialogCatalog.ColorPresets
            .Should()
            .Contain(option => option.LabelKey == "ConditionalFormatDialog_FormatPreset_CustomFormat"
                && option.IsCustom);
    }

    [Theory]
    [InlineData("ConditionalFormatDialog_RuleShell_FormatAllCells", ConditionalFormatDialogCatalog.GreaterThanRule, ConditionalFormatDialogCatalog.DataBarRule)]
    [InlineData("ConditionalFormatDialog_RuleShell_FormatAllCells", ConditionalFormatDialogCatalog.IconSetRule, ConditionalFormatDialogCatalog.IconSetRule)]
    [InlineData("ConditionalFormatDialog_RuleShell_FormatTopBottom", ConditionalFormatDialogCatalog.GreaterThanRule, ConditionalFormatDialogCatalog.Top10ItemsRule)]
    [InlineData("ConditionalFormatDialog_RuleShell_UseFormula", ConditionalFormatDialogCatalog.GreaterThanRule, ConditionalFormatDialogCatalog.FormulaRule)]
    public void DefaultRuleTypeForShellKey_UsesSharedExcelDefaults(string shellKey, string currentRuleType, string expected)
    {
        ConditionalFormatDialogCatalog.DefaultRuleTypeForShellKey(shellKey, currentRuleType)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData(ConditionalFormatDialogCatalog.TextBeginsWithRule, "ConditionalFormatDialog_ConditionKind_SpecificText")]
    [InlineData(ConditionalFormatDialogCatalog.DateOccurringRule, "ConditionalFormatDialog_ConditionKind_DatesOccurring")]
    [InlineData(ConditionalFormatDialogCatalog.NoErrorsRule, "ConditionalFormatDialog_ConditionKind_NoErrors")]
    [InlineData(ConditionalFormatDialogCatalog.GreaterThanRule, "ConditionalFormatDialog_ConditionKind_CellValue")]
    public void ConditionKindKeyForRuleType_MapsDialogRuleFamilies(string ruleType, string expectedKey)
    {
        ConditionalFormatDialogCatalog.ConditionKindKeyForRuleType(ruleType).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData(ConditionalFormatDialogCatalog.ColorScaleRule, false, "ConditionalFormatDialog_FormatStyle_2ColorScale")]
    [InlineData(ConditionalFormatDialogCatalog.ColorScaleRule, true, "ConditionalFormatDialog_FormatStyle_3ColorScale")]
    [InlineData(ConditionalFormatDialogCatalog.IconSetRule, false, "ConditionalFormatDialog_FormatStyle_IconSet")]
    [InlineData(ConditionalFormatDialogCatalog.DataBarRule, false, "ConditionalFormatDialog_FormatStyle_DataBar")]
    public void FormatStyleKeyForRuleType_TracksVisualRuleDefaults(string ruleType, bool threeColor, string expectedKey)
    {
        ConditionalFormatDialogCatalog.FormatStyleKeyForRuleType(ruleType, threeColor).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData(ConditionalFormatDialogCatalog.IconSetRule, false, CfRuleType.IconSet)]
    [InlineData(ConditionalFormatDialogCatalog.DuplicateValuesRule, false, CfRuleType.DuplicateValues)]
    [InlineData(ConditionalFormatDialogCatalog.DuplicateValuesRule, true, CfRuleType.UniqueValues)]
    [InlineData(ConditionalFormatDialogCatalog.Bottom10PercentRule, false, CfRuleType.Top10)]
    [InlineData(ConditionalFormatDialogCatalog.FormulaRule, false, CfRuleType.Formula)]
    [InlineData(ConditionalFormatDialogCatalog.GreaterThanRule, false, CfRuleType.CellValue)]
    public void Planner_MapsDialogRuleNamesToModelRuleTypes(string dialogRuleType, bool uniqueDuplicate, CfRuleType expected)
    {
        ConditionalFormatDialogPlanner.ModelRuleTypeForDialogRuleType(dialogRuleType, uniqueDuplicate)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData(ConditionalFormatDialogCatalog.BetweenRule, CfOperator.Between)]
    [InlineData(ConditionalFormatDialogCatalog.LessThanOrEqualToRule, CfOperator.LessThanOrEqual)]
    [InlineData(ConditionalFormatDialogCatalog.NotEqualToRule, CfOperator.NotEqual)]
    public void Planner_MapsDialogRuleNamesToCellValueOperators(string dialogRuleType, CfOperator expected)
    {
        ConditionalFormatDialogPlanner.OperatorForDialogRuleType(dialogRuleType).Should().Be(expected);
    }
}
