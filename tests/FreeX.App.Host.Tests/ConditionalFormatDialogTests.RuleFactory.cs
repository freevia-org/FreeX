using System;
using System.Windows;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ConditionalFormatDialogTests
{
    [Theory]
    [InlineData("Data Bar", "DataBarPreview")]
    [InlineData("Color Scale", "ColorScalePreview")]
    [InlineData("Icon Set", "IconSetPreview")]
    public void VisualRuleDialogs_ShowExcelLikePreviewArea(string ruleType, string previewLabel)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog(ruleType, RangeFor(SheetId.New())));

            FindText(dialog.Content, UiText.Get("ConditionalFormatDialog_PreviewHeader")).Should().NotBeNull();
            var preview = FindNamedControl<FrameworkElement>(dialog.Content, previewLabel);
            preview.Should().NotBeNull();

            dialog.Close();
        });
    }

    [Theory]
    [InlineData("Greater Than", typeof(HighlightCellsRuleDialog))]
    [InlineData("Top 10%", typeof(TopBottomRuleDialog))]
    [InlineData("Data Bar", typeof(DataBarRuleDialog))]
    [InlineData("Color Scale", typeof(ColorScaleRuleDialog))]
    [InlineData("Icon Set", typeof(IconSetRuleDialog))]
    [InlineData("Date Occurring", typeof(HighlightCellsRuleDialog))]
    [InlineData("Duplicate Values", typeof(HighlightCellsRuleDialog))]
    [InlineData("Blanks", typeof(HighlightCellsRuleDialog))]
    [InlineData("No Blanks", typeof(HighlightCellsRuleDialog))]
    [InlineData("Errors", typeof(HighlightCellsRuleDialog))]
    [InlineData("No Errors", typeof(HighlightCellsRuleDialog))]
    [InlineData("Formula", typeof(NewConditionalFormatRuleDialog))]
    public void Factory_CreatesRuleFamilySpecificDialogs(string ruleType, Type expectedDialogType)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ConditionalFormatDialogFactory.Create(ruleType, RangeFor(SheetId.New()));

            dialog.Should().BeOfType(expectedDialogType);
            dialog.Close();
        });
    }

    [Fact]
    public void ExistingRule_WhenRuleTypeChanges_DropsNativeMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.DataBar,
                NativeChildXmls =
                [
                    """<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><ext uri="{B025F937-6E4E-48BE-B07C-B91C50BE2FA4}"><x14:id xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">{11111111-2222-3333-4444-555555555555}</x14:id></ext></extLst>"""
                ],
                NativePayloadChildXmls = ["""<axisColor xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main" theme="1" />"""],
                NativeContainerChildXmls = ["""<extLst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" />"""]
            };
            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            RefreshRuleDescriptionForTest(dialog, "Color Scale");
            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.ColorScale);
            dialog.ResultRule.NativeChildXmls.Should().BeNull();
            dialog.ResultRule.NativePayloadChildXmls.Should().BeNull();
            dialog.ResultRule.NativeContainerChildXmls.Should().BeNull();

            dialog.Close();
        });
    }
}
