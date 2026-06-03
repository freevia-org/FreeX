using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ConditionalFormatDialogTests
{
    [Fact]
    public void ColorScaleRule_CreatesThresholdAndColorOptionsWithoutFormatIfTrue()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Color Scale", RangeFor(SheetId.New())));

            GetControl<ComboBox>(dialog, "_colorScaleMinTypeBox").SelectedItem = CfThresholdType.Number;
            GetControl<TextBox>(dialog, "_colorScaleMinValueBox").Text = "1";
            GetControl<TextBox>(dialog, "_colorScaleMinColorBox").Text = "10,20,30";
            GetControl<CheckBox>(dialog, "_colorScaleUseThreeColorBox").IsChecked = true;
            GetControl<ComboBox>(dialog, "_colorScaleMidTypeBox").SelectedItem = CfThresholdType.Percentile;
            GetControl<TextBox>(dialog, "_colorScaleMidValueBox").Text = "50";
            GetControl<TextBox>(dialog, "_colorScaleMidColorBox").Text = "40,50,60";
            GetControl<ComboBox>(dialog, "_colorScaleMaxTypeBox").SelectedItem = CfThresholdType.Formula;
            GetControl<TextBox>(dialog, "_colorScaleMaxValueBox").Text = "MAX(A:A)";
            GetControl<TextBox>(dialog, "_colorScaleMaxColorBox").Text = "70,80,90";

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.ColorScale);
            dialog.ResultRule.MinThresholdType.Should().Be(CfThresholdType.Number);
            dialog.ResultRule.MinThresholdValue.Should().Be("1");
            dialog.ResultRule.MinColor.Should().Be(new RgbColor(10, 20, 30));
            dialog.ResultRule.UseThreeColorScale.Should().BeTrue();
            dialog.ResultRule.MidThresholdType.Should().Be(CfThresholdType.Percentile);
            dialog.ResultRule.MidThresholdValue.Should().Be("50");
            dialog.ResultRule.MidColor.Should().Be(new RgbColor(40, 50, 60));
            dialog.ResultRule.MaxThresholdType.Should().Be(CfThresholdType.Formula);
            dialog.ResultRule.MaxThresholdValue.Should().Be("MAX(A:A)");
            dialog.ResultRule.MaxColor.Should().Be(new RgbColor(70, 80, 90));
            dialog.ResultRule.FormatIfTrue.Should().BeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void ColorScaleRule_DisablesMidpointControlsUntilThreeColorScaleSelected()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Color Scale", RangeFor(SheetId.New())));

            var threeColor = GetControl<CheckBox>(dialog, "_colorScaleUseThreeColorBox");
            var midType = GetControl<ComboBox>(dialog, "_colorScaleMidTypeBox");
            var midValue = GetControl<TextBox>(dialog, "_colorScaleMidValueBox");
            var midColor = GetControl<TextBox>(dialog, "_colorScaleMidColorBox");
            var midColorButton = GetControl<Button>(dialog, "_colorScaleMidColorButton");

            threeColor.IsChecked.Should().BeFalse();
            midType.IsEnabled.Should().BeFalse();
            midValue.IsEnabled.Should().BeFalse();
            midColor.IsEnabled.Should().BeFalse();
            midColorButton.IsEnabled.Should().BeFalse();

            threeColor.IsChecked = true;
            midType.IsEnabled.Should().BeTrue();
            midValue.IsEnabled.Should().BeTrue();
            midColor.IsEnabled.Should().BeTrue();
            midColorButton.IsEnabled.Should().BeTrue();

            dialog.Close();
        });
    }

    [Fact]
    public void ColorScaleRule_ExposesExcelLikeColorPickerButtons()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Color Scale", RangeFor(SheetId.New())));

            GetControl<Button>(dialog, "_colorScaleMinColorButton").Content.Should().Be("...");
            GetControl<Button>(dialog, "_colorScaleMinColorButton").ToolTip.Should().Be(UiText.Get("ConditionalFormatDialog_ChooseMinimumColorToolTip"));
            GetControl<Button>(dialog, "_colorScaleMidColorButton").ToolTip.Should().Be(UiText.Get("ConditionalFormatDialog_ChooseMidpointColorToolTip"));
            GetControl<Button>(dialog, "_colorScaleMaxColorButton").ToolTip.Should().Be(UiText.Get("ConditionalFormatDialog_ChooseMaximumColorToolTip"));

            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_MinimumColorLabel")).Should().NotBeNull();
            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_MidpointColorLabel")).Should().NotBeNull();
            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_MaximumColorLabel")).Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void ColorScaleRule_SourceUsesSharedColorPickerDialog()
    {
        var source = ReadConditionalFormatDialogSource();

        source.Should().Contain("CreateColorScaleColorButton");
        source.Should().Contain("CreateColorScaleColorEditor");
        source.Should().Contain("ColorScaleColorButton_Click");
        source.Should().Contain("new ColorPickerDialog(initialColor)");
        source.Should().NotContain("_Minimum color (R,G,B):");
        source.Should().NotContain("Midpoint _color (R,G,B):");
        source.Should().NotContain("Ma_ximum color (R,G,B):");
    }

    [Fact]
    public void ExistingColorScaleRule_PrePopulatesColorScaleFields()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.ColorScale,
                MinThresholdType = CfThresholdType.Number,
                MinThresholdValue = "2",
                MinColor = new RgbColor(1, 2, 3),
                UseThreeColorScale = true,
                MidThresholdType = CfThresholdType.Percent,
                MidThresholdValue = "40",
                MidColor = new RgbColor(4, 5, 6),
                MaxThresholdType = CfThresholdType.Max,
                MaxThresholdValue = null,
                MaxColor = new RgbColor(7, 8, 9)
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            GetControl<ComboBox>(dialog, "_colorScaleMinTypeBox").SelectedItem.Should().Be(CfThresholdType.Number);
            GetControl<TextBox>(dialog, "_colorScaleMinValueBox").Text.Should().Be("2");
            GetControl<TextBox>(dialog, "_colorScaleMinColorBox").Text.Should().Be("1,2,3");
            GetControl<CheckBox>(dialog, "_colorScaleUseThreeColorBox").IsChecked.Should().BeTrue();
            GetControl<ComboBox>(dialog, "_colorScaleMidTypeBox").SelectedItem.Should().Be(CfThresholdType.Percent);
            GetControl<TextBox>(dialog, "_colorScaleMidValueBox").Text.Should().Be("40");
            GetControl<TextBox>(dialog, "_colorScaleMidColorBox").Text.Should().Be("4,5,6");
            GetControl<ComboBox>(dialog, "_colorScaleMaxTypeBox").SelectedItem.Should().Be(CfThresholdType.Max);
            GetControl<TextBox>(dialog, "_colorScaleMaxColorBox").Text.Should().Be("7,8,9");

            dialog.Close();
        });
    }
}
