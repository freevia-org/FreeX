using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ConditionalFormatDialogTests
{
    [Theory]
    [InlineData("Top 10%", true, true)]
    [InlineData("Bottom 10%", false, true)]
    [InlineData("Below Average", false, false)]
    public void TopBottomParityRule_CreatesExpectedConditionalFormat(string ruleType, bool aboveAverage, bool topBottomPercent)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog(ruleType, RangeFor(SheetId.New())));

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(ruleType.Contains("Average") ? CfRuleType.AboveAverage : CfRuleType.Top10);
            dialog.ResultRule.AboveAverage.Should().Be(aboveAverage);
            dialog.ResultRule.TopBottomPercent.Should().Be(topBottomPercent);

            dialog.Close();
        });
    }

    [Fact]
    public void TextContainsRule_CreatesContainsTextConditionalFormat()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Text Contains", RangeFor(SheetId.New())));

            GetControl<TextBox>(dialog, "_value1Box").Text = "urgent";
            GetControl<ComboBox>(dialog, "_colorBox").SelectedItem = UiText.Get("ConditionalFormatDialog_FormatPreset_YellowFill");

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.ContainsText);
            dialog.ResultRule.TextRuleText.Should().Be("urgent");
            dialog.ResultRule.FormatIfTrue.Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void DateOccurringRule_CreatesTimePeriodConditionalFormat()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Date Occurring", RangeFor(SheetId.New())));

            GetControl<ComboBox>(dialog, "_dateOccurringPeriodBox").SelectedItem = UiText.Get("ConditionalFormatDialog_DatePeriod_NextMonth");

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.DateOccurring);
            dialog.ResultRule.DateOccurringPeriod.Should().Be("nextMonth");
            dialog.ResultRule.FormatIfTrue.Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void DuplicateValuesRule_CreatesDuplicateOrUniqueConditionalFormat()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Duplicate Values", RangeFor(SheetId.New())));

            GetControl<ComboBox>(dialog, "_duplicateValuesKindBox").SelectedItem = UiText.Get("ConditionalFormatDialog_DuplicateKind_Unique");

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.UniqueValues);
            dialog.ResultRule.FormatIfTrue.Should().NotBeNull();

            dialog.Close();
        });
    }

    [Theory]
    [InlineData("Blanks", CfRuleType.Blanks)]
    [InlineData("No Blanks", CfRuleType.NoBlanks)]
    [InlineData("Errors", CfRuleType.Errors)]
    [InlineData("No Errors", CfRuleType.NoErrors)]
    public void BlankAndErrorRules_CreateExpectedConditionalFormat(string ruleType, CfRuleType expectedRuleType)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog(ruleType, RangeFor(SheetId.New())));

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(expectedRuleType);
            dialog.ResultRule.FormatIfTrue.Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void ExistingLongTailHighlightRules_PrePopulateDialogFields()
    {
        StaTestRunner.Run(() =>
        {
            var textRule = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.ContainsText,
                TextRuleText = "review"
            };
            var textDialog = ShowDialogForTest(new ConditionalFormatDialog(textRule));
            GetControl<TextBox>(textDialog, "_value1Box").Text.Should().Be("review");
            textDialog.Close();

            var dateRule = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.DateOccurring,
                DateOccurringPeriod = "last7Days"
            };
            var dateDialog = ShowDialogForTest(new ConditionalFormatDialog(dateRule));
            GetControl<ComboBox>(dateDialog, "_dateOccurringPeriodBox").SelectedItem.Should().Be(UiText.Get("ConditionalFormatDialog_DatePeriod_Last7Days"));
            dateDialog.Close();

            var uniqueRule = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.UniqueValues
            };
            var uniqueDialog = ShowDialogForTest(new ConditionalFormatDialog(uniqueRule));
            GetControl<ComboBox>(uniqueDialog, "_duplicateValuesKindBox").SelectedItem.Should().Be(UiText.Get("ConditionalFormatDialog_DuplicateKind_Unique"));
            uniqueDialog.Close();
        });
    }

    [Fact]
    public void TopBottomRule_UsesEditableRankOrPercentValue()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Bottom 10%", RangeFor(SheetId.New())));

            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_PercentLabel")).Should().NotBeNull();
            GetControl<TextBox>(dialog, "_topBottomRankBox").Text = "25";

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.Top10);
            dialog.ResultRule.AboveAverage.Should().BeFalse();
            dialog.ResultRule.TopBottomPercent.Should().BeTrue();
            dialog.ResultRule.TopBottomRank.Should().Be(25);

            dialog.Close();
        });
    }
}
