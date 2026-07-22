using System.Linq;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ConditionalFormatDialogTests
{
    [Fact]
    public void DataBarRule_MinMaxTypePickers_OfferAutomaticAlongsideExplicitEndpointsAndDefaultToIt()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Data Bar", RangeFor(SheetId.New())));

            var minTypeBox = GetControl<ComboBox>(dialog, "_dataBarMinTypeBox");
            var maxTypeBox = GetControl<ComboBox>(dialog, "_dataBarMaxTypeBox");

            // Excel's Data Bar dialog offers Automatic, Lowest Value, Highest Value, Number, Percent,
            // Formula, and Percentile in both the minimum- and maximum-type pickers.
            minTypeBox.Items.Cast<CfThresholdType>().Should().Contain(
                [CfThresholdType.AutoMin, CfThresholdType.AutoMax, CfThresholdType.Min, CfThresholdType.Max]);
            maxTypeBox.Items.Cast<CfThresholdType>().Should().Contain(
                [CfThresholdType.AutoMin, CfThresholdType.AutoMax, CfThresholdType.Min, CfThresholdType.Max]);

            // A brand-new data bar defaults to Excel's "Automatic" endpoint, not the explicit
            // Lowest/Highest Value endpoint.
            minTypeBox.SelectedItem.Should().Be(CfThresholdType.AutoMin);
            maxTypeBox.SelectedItem.Should().Be(CfThresholdType.AutoMax);

            dialog.Close();
        });
    }

    [Fact]
    public void DataBarRule_AcceptedImmediately_DefaultsToAutomaticMinAndMaxWithoutRequiringAValue()
    {
        StaTestRunner.Run(() =>
        {
            // A user who opens New Rule > Data Bar and clicks OK without touching anything must get
            // Excel's "Automatic" endpoint on both sides — and must not be blocked by a spurious
            // "enter a valid number" validation error on the (intentionally blank) value boxes.
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Data Bar", RangeFor(SheetId.New())));

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.DataBarMinThresholdType.Should().Be(CfThresholdType.AutoMin);
            dialog.ResultRule.DataBarMaxThresholdType.Should().Be(CfThresholdType.AutoMax);

            dialog.Close();
        });
    }

    [Fact]
    public void DataBarRule_CreatesDataBarOptionsWithoutFormatIfTrue()
    {
        StaTestRunner.Run(() =>
        {
            var range = RangeFor(SheetId.New());
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Data Bar", range));

            GetControl<ComboBox>(dialog, "_dataBarMinTypeBox").SelectedItem = CfThresholdType.Percentile;
            GetControl<TextBox>(dialog, "_dataBarMinValueBox").Text = "10";
            GetControl<ComboBox>(dialog, "_dataBarMaxTypeBox").SelectedItem = CfThresholdType.Number;
            GetControl<TextBox>(dialog, "_dataBarMaxValueBox").Text = "99";
            GetControl<CheckBox>(dialog, "_dataBarShowValueBox").IsChecked = true;
            GetControl<TextBox>(dialog, "_dataBarMinLengthBox").Text = "5";
            GetControl<TextBox>(dialog, "_dataBarMaxLengthBox").Text = "95";
            GetControl<ComboBox>(dialog, "_colorBox").SelectedItem = UiText.Get("ConditionalFormatDialog_FormatPreset_GreenFill");

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.DataBar);
            dialog.ResultRule.DataBarColor.Should().Be(new RgbColor(198, 239, 206));
            dialog.ResultRule.DataBarMinThresholdType.Should().Be(CfThresholdType.Percentile);
            dialog.ResultRule.DataBarMinThresholdValue.Should().Be("10");
            dialog.ResultRule.DataBarMaxThresholdType.Should().Be(CfThresholdType.Number);
            dialog.ResultRule.DataBarMaxThresholdValue.Should().Be("99");
            dialog.ResultRule.DataBarShowValue.Should().BeFalse();
            dialog.ResultRule.DataBarMinLength.Should().Be(5);
            dialog.ResultRule.DataBarMaxLength.Should().Be(95);
            dialog.ResultRule.FormatIfTrue.Should().BeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void DataBarRule_ShowBarOnlyCheckboxUsesExcelSemantics()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Data Bar", RangeFor(SheetId.New())));

            var showBarOnly = GetControl<CheckBox>(dialog, "_dataBarShowValueBox");
            showBarOnly.Content.Should().Be(UiText.Get("ConditionalFormatDialog_ShowBarOnly"));
            showBarOnly.IsChecked.Should().BeFalse();

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.DataBarShowValue.Should().BeTrue();

            dialog.Close();
        });
    }

    [Fact]
    public void DataBarRule_ExposesExcelLikeBarColorPickerButton()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Data Bar", RangeFor(SheetId.New())));

            GetControl<Button>(dialog, "_dataBarColorButton").Content.Should().Be("...");
            GetControl<Button>(dialog, "_dataBarColorButton").ToolTip.Should().Be(UiText.Get("ConditionalFormatDialog_ChooseDataBarColorToolTip"));
            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_BarColorLabel")).Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void DataBarRule_SourceUsesSharedColorPickerForCustomBarColors()
    {
        var source = ReadConditionalFormatDialogSource();

        source.Should().Contain("CreateDataBarColorButton");
        source.Should().Contain("CreateDataBarColorEditor");
        source.Should().Contain("ConditionalFormatDialog_ChooseDataBarColorToolTip");
        source.Should().Contain("SelectedDataBarColor");
    }

    [Fact]
    public void ExistingDataBarRule_PrePopulatesDataBarFields()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.DataBar,
                DataBarColor = new RgbColor(198, 239, 206),
                DataBarMinThresholdType = CfThresholdType.Percentile,
                DataBarMinThresholdValue = "15",
                DataBarMaxThresholdType = CfThresholdType.Percent,
                DataBarMaxThresholdValue = "90",
                DataBarShowValue = false,
                DataBarMinLength = 7,
                DataBarMaxLength = 88
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            GetControl<ComboBox>(dialog, "_dataBarMinTypeBox").SelectedItem.Should().Be(CfThresholdType.Percentile);
            GetControl<TextBox>(dialog, "_dataBarMinValueBox").Text.Should().Be("15");
            GetControl<ComboBox>(dialog, "_dataBarMaxTypeBox").SelectedItem.Should().Be(CfThresholdType.Percent);
            GetControl<TextBox>(dialog, "_dataBarMaxValueBox").Text.Should().Be("90");
            GetControl<CheckBox>(dialog, "_dataBarShowValueBox").IsChecked.Should().BeTrue();
            GetControl<TextBox>(dialog, "_dataBarMinLengthBox").Text.Should().Be("7");
            GetControl<TextBox>(dialog, "_dataBarMaxLengthBox").Text.Should().Be("88");

            dialog.Close();
        });
    }

    [Fact]
    public void ExistingDataBarRule_PreservesCustomBarColor()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.DataBar,
                DataBarColor = new RgbColor(12, 34, 56)
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            GetControl<ComboBox>(dialog, "_colorBox").SelectedItem.Should().Be(UiText.Get("ConditionalFormatDialog_FormatPreset_CustomFormat"));

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.DataBarColor.Should().Be(new RgbColor(12, 34, 56));

            dialog.Close();
        });
    }

    [Fact]
    public void DataBarRule_ExposesAdvancedAxisAndNegativeColorOptions()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Data Bar", RangeFor(SheetId.New())));

            GetControl<CheckBox>(dialog, "_dataBarBorderBox").Content.Should().Be(UiText.Get("ConditionalFormatDialog_ShowBorder"));
            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_AxisPositionLabel")).Should().NotBeNull();
            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_AxisColorLabel")).Should().NotBeNull();
            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_NegativeBarColorLabel")).Should().NotBeNull();
            FindLabel(dialog.Content, UiText.Get("ConditionalFormatDialog_NegativeBorderColorLabel")).Should().NotBeNull();

            GetControl<CheckBox>(dialog, "_dataBarBorderBox").Should().NotBeNull();
            GetControl<ComboBox>(dialog, "_dataBarAxisPositionBox").Should().NotBeNull();
            GetControl<TextBox>(dialog, "_dataBarAxisColorBox").Should().NotBeNull();
            GetControl<TextBox>(dialog, "_dataBarNegativeFillColorBox").Should().NotBeNull();
            GetControl<TextBox>(dialog, "_dataBarNegativeBorderColorBox").Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void DataBarRule_AdvancedOptionsRoundTripThroughDialog()
    {
        StaTestRunner.Run(() =>
        {
            var range = RangeFor(SheetId.New());
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Data Bar", range));

            GetControl<CheckBox>(dialog, "_dataBarBorderBox").IsChecked = true;
            GetControl<ComboBox>(dialog, "_dataBarAxisPositionBox").SelectedItem = UiText.Get("ConditionalFormatDialog_AxisPosition_Middle");
            GetControl<TextBox>(dialog, "_dataBarAxisColorBox").Text = "1,2,3";
            GetControl<TextBox>(dialog, "_dataBarNegativeFillColorBox").Text = "4,5,6";
            GetControl<TextBox>(dialog, "_dataBarNegativeBorderColorBox").Text = "7,8,9";

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.DataBarBorder.Should().BeTrue();
            dialog.ResultRule.DataBarAxisPosition.Should().Be("middle");
            dialog.ResultRule.DataBarAxisColor.Should().Be(new RgbColor(1, 2, 3));
            dialog.ResultRule.DataBarNegativeFillColor.Should().Be(new RgbColor(4, 5, 6));
            dialog.ResultRule.DataBarNegativeBorderColor.Should().Be(new RgbColor(7, 8, 9));

            dialog.Close();
        });
    }

    [Fact]
    public void DataBarRule_AdvancedOptions_DefaultsAreEmpty()
    {
        StaTestRunner.Run(() =>
        {
            var range = RangeFor(SheetId.New());
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Data Bar", range));

            GetControl<CheckBox>(dialog, "_dataBarBorderBox").IsChecked.Should().BeFalse();
            GetControl<ComboBox>(dialog, "_dataBarAxisPositionBox").SelectedItem.Should().Be(UiText.Get("ConditionalFormatDialog_AxisPosition_Automatic"));
            GetControl<TextBox>(dialog, "_dataBarAxisColorBox").Text.Should().BeEmpty();
            GetControl<TextBox>(dialog, "_dataBarNegativeFillColorBox").Text.Should().BeEmpty();
            GetControl<TextBox>(dialog, "_dataBarNegativeBorderColorBox").Text.Should().BeEmpty();

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.DataBarBorder.Should().BeFalse();
            dialog.ResultRule.DataBarAxisPosition.Should().BeNull();
            dialog.ResultRule.DataBarAxisColor.Should().BeNull();
            dialog.ResultRule.DataBarNegativeFillColor.Should().BeNull();
            dialog.ResultRule.DataBarNegativeBorderColor.Should().BeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void ExistingDataBarRule_PrePopulatesAdvancedDataBarFields()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.DataBar,
                DataBarBorder = true,
                DataBarAxisPosition = "middle",
                DataBarAxisColor = new RgbColor(10, 20, 30),
                DataBarNegativeFillColor = new RgbColor(40, 50, 60),
                DataBarNegativeBorderColor = new RgbColor(70, 80, 90)
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            GetControl<CheckBox>(dialog, "_dataBarBorderBox").IsChecked.Should().BeTrue();
            GetControl<ComboBox>(dialog, "_dataBarAxisPositionBox").SelectedItem.Should().Be(UiText.Get("ConditionalFormatDialog_AxisPosition_Middle"));
            GetControl<TextBox>(dialog, "_dataBarAxisColorBox").Text.Should().Be("10,20,30");
            GetControl<TextBox>(dialog, "_dataBarNegativeFillColorBox").Text.Should().Be("40,50,60");
            GetControl<TextBox>(dialog, "_dataBarNegativeBorderColorBox").Text.Should().Be("70,80,90");

            dialog.Close();
        });
    }
}
