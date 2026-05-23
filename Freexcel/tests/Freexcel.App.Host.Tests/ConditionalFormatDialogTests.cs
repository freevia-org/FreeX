using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ConditionalFormatDialogTests
{
    [Fact]
    public void BaseRuleDialog_ExposesKeyboardAccessKeysForFieldsAndButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ConditionalFormatDialog.cs"));

        source.Should().Contain("isBetween ? \"_Minimum:\" : \"_Value:\"");
        source.Should().Contain("Content = \"Ma_ximum:\"");
        source.Should().Contain("isDataBar ? \"_Bar color:\" : \"_Format:\"");
        source.Should().Contain("CreateAccessLabel(\"_Formula:\", _formulaBox)");
        source.Should().Contain("CreateAccessLabel(\"_Minimum type:\", _dataBarMinTypeBox)");
        source.Should().Contain("CreateAccessLabel(\"Minimum _value:\", _dataBarMinValueBox)");
        source.Should().Contain("CreateAccessLabel(\"Ma_ximum type:\", _dataBarMaxTypeBox)");
        source.Should().Contain("CreateAccessLabel(\"Maximum _value:\", _dataBarMaxValueBox)");
        source.Should().Contain("CreateAccessLabel(\"_Minimum bar length (%):\", _dataBarMinLengthBox)");
        source.Should().Contain("CreateAccessLabel(\"Ma_ximum bar length (%):\", _dataBarMaxLengthBox)");
        source.Should().Contain("CreateAccessLabel(\"_Minimum color:\", _colorScaleMinColorBox)");
        source.Should().Contain("CreateAccessLabel(\"_Midpoint type:\", _colorScaleMidTypeBox)");
        source.Should().Contain("CreateAccessLabel(\"Midpoint _value:\", _colorScaleMidValueBox)");
        source.Should().Contain("CreateAccessLabel(\"Midpoint _color:\", _colorScaleMidColorBox)");
        source.Should().Contain("CreateAccessLabel(\"Ma_ximum color:\", _colorScaleMaxColorBox)");
        source.Should().Contain("CreateAccessLabel(\"_Icon set:\", _iconSetStyleBox)");
        source.Should().Contain("CreateAccessLabel(\"_Date period:\", _dateOccurringPeriodBox)");
        source.Should().Contain("CreateAccessLabel(\"Format cells that _contain:\", _duplicateValuesKindBox)");
        source.Should().Contain("Content = \"_Show value\"");
        source.Should().Contain("Content = \"_Reverse icon order\"");
        source.Should().Contain("Content = \"_Show Bar Only\"");
        source.Should().Contain("Content = \"Use _three-color scale\"");
        source.Should().Contain("CreateAccessLabel(ruleType is \"Top 10%\" or \"Bottom 10%\" ? \"_Percent:\" : \"_Rank:\", _topBottomRankBox)");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
    }

    [Fact]
    public void NewRuleDialog_UsesExcelRuleShell()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new NewConditionalFormatRuleDialog("Formula", RangeFor(SheetId.New())));

            dialog.Title.Should().Be("New Formatting Rule");
            FindText(dialog.Content, "Select a Rule Type:").Should().NotBeNull();
            FindText(dialog.Content, "Edit the Rule Description:").Should().NotBeNull();

            var ruleTypeList = FindControl<ListBox>(dialog.Content);
            ruleTypeList.Should().NotBeNull();
            ruleTypeList!.Items.Cast<object>().Select(item => item.ToString()).Should().Contain([
                "Format all cells based on their values",
                "Format only cells that contain",
                "Use a formula to determine which cells to format"
            ]);
            ruleTypeList.SelectedItem.Should().Be("Use a formula to determine which cells to format");

            dialog.Close();
        });
    }

    [Fact]
    public void NewRuleDialog_ChangingRuleShellSelectionRefreshesEditor()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new NewConditionalFormatRuleDialog("Greater Than", RangeFor(SheetId.New())));

            var ruleTypeList = FindControl<ListBox>(dialog.Content);
            ruleTypeList.Should().NotBeNull();
            ruleTypeList!.SelectedItem = "Use a formula to determine which cells to format";

            FindLabel(dialog.Content, "_Formula:").Should().NotBeNull();
            GetControl<TextBox>(dialog, "_formulaBox").Text = "=A1>10";

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.Formula);
            dialog.ResultRule.FormulaText.Should().Be("A1>10");

            dialog.Close();
        });
    }

    [Fact]
    public void NewRuleDialog_ChangingToValueBasedShellRefreshesToDataBarControls()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new NewConditionalFormatRuleDialog("Formula", RangeFor(SheetId.New())));

            var ruleTypeList = FindControl<ListBox>(dialog.Content);
            ruleTypeList.Should().NotBeNull();
            ruleTypeList!.SelectedItem = "Format all cells based on their values";

            FindLabel(dialog.Content, "_Minimum type:").Should().NotBeNull();
            FindNamedControl<Border>(dialog.Content, "DataBarPreview").Should().NotBeNull();
            GetControl<CheckBox>(dialog, "_dataBarShowValueBox").Content.Should().Be("_Show Bar Only");

            dialog.Close();
        });
    }

    [Fact]
    public void HighlightRuleDialog_OffersExcelFormatPresetsAndFormatButton()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Greater Than", RangeFor(SheetId.New())));

            var formatBox = GetControl<ComboBox>(dialog, "_colorBox");
            formatBox.Items.Cast<object>().Select(item => item.ToString()).Should().Contain([
                "Light Red Fill with Dark Red Text",
                "Yellow Fill with Dark Yellow Text",
                "Custom Format..."
            ]);
            FindButton(dialog.Content, "Format...").Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void EditRuleDialog_UsesExcelEditTitleAndRuleShell()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.CellValue,
                Operator = CfOperator.GreaterThan,
                Value1 = "10"
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            dialog.Title.Should().Be("Edit Formatting Rule");
            FindText(dialog.Content, "Select a Rule Type:").Should().NotBeNull();
            FindText(dialog.Content, "Edit the Rule Description:").Should().NotBeNull();

            dialog.Close();
        });
    }

    [Theory]
    [InlineData("Data Bar", "DataBarPreview")]
    [InlineData("Color Scale", "ColorScalePreview")]
    [InlineData("Icon Set", "IconSetPreview")]
    public void VisualRuleDialogs_ShowExcelLikePreviewArea(string ruleType, string previewLabel)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog(ruleType, RangeFor(SheetId.New())));

            FindText(dialog.Content, "Preview:").Should().NotBeNull();
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
            GetControl<ComboBox>(dialog, "_colorBox").SelectedItem = "Yellow Fill";

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

            GetControl<ComboBox>(dialog, "_dateOccurringPeriodBox").SelectedItem = "Next Month";

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

            GetControl<ComboBox>(dialog, "_duplicateValuesKindBox").SelectedItem = "Unique";

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
            GetControl<ComboBox>(dateDialog, "_dateOccurringPeriodBox").SelectedItem.Should().Be("Last 7 Days");
            dateDialog.Close();

            var uniqueRule = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.UniqueValues
            };
            var uniqueDialog = ShowDialogForTest(new ConditionalFormatDialog(uniqueRule));
            GetControl<ComboBox>(uniqueDialog, "_duplicateValuesKindBox").SelectedItem.Should().Be("Unique");
            uniqueDialog.Close();
        });
    }

    [Fact]
    public void IconSetRule_CreatesIconSetWithoutFormatIfTrue()
    {
        StaTestRunner.Run(() =>
        {
            var range = RangeFor(SheetId.New());
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Icon Set", range));

            GetControl<ComboBox>(dialog, "_iconSetStyleBox").SelectedItem = "5Arrows";
            GetControl<CheckBox>(dialog, "_iconSetShowValueBox").IsChecked = false;
            GetControl<CheckBox>(dialog, "_iconSetReverseBox").IsChecked = true;

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.IconSet);
            dialog.ResultRule.IconSetStyle.Should().Be("5Arrows");
            dialog.ResultRule.IconSetShowValue.Should().BeFalse();
            dialog.ResultRule.IconSetReverse.Should().BeTrue();
            dialog.ResultRule.FormatIfTrue.Should().BeNull();

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
            GetControl<ComboBox>(dialog, "_colorBox").SelectedItem = "Green Fill";

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
            showBarOnly.Content.Should().Be("_Show Bar Only");
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
            GetControl<Button>(dialog, "_dataBarColorButton").ToolTip.Should().Be("Choose data bar color");
            FindLabel(dialog.Content, "_Bar color:").Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void DataBarRule_SourceUsesSharedColorPickerForCustomBarColors()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ConditionalFormatDialog.cs"));

        source.Should().Contain("CreateDataBarColorButton");
        source.Should().Contain("CreateDataBarColorEditor");
        source.Should().Contain("Choose data bar color");
        source.Should().Contain("SelectedDataBarColor");
    }

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
            GetControl<Button>(dialog, "_colorScaleMinColorButton").ToolTip.Should().Be("Choose minimum color");
            GetControl<Button>(dialog, "_colorScaleMidColorButton").ToolTip.Should().Be("Choose midpoint color");
            GetControl<Button>(dialog, "_colorScaleMaxColorButton").ToolTip.Should().Be("Choose maximum color");

            FindLabel(dialog.Content, "_Minimum color:").Should().NotBeNull();
            FindLabel(dialog.Content, "Midpoint _color:").Should().NotBeNull();
            FindLabel(dialog.Content, "Ma_ximum color:").Should().NotBeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void ColorScaleRule_SourceUsesSharedColorPickerDialog()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ConditionalFormatDialog.cs"));

        source.Should().Contain("CreateColorScaleColorButton");
        source.Should().Contain("CreateColorScaleColorEditor");
        source.Should().Contain("ColorScaleColorButton_Click");
        source.Should().Contain("new ColorPickerDialog(initialColor)");
        source.Should().NotContain("_Minimum color (R,G,B):");
        source.Should().NotContain("Midpoint _color (R,G,B):");
        source.Should().NotContain("Ma_ximum color (R,G,B):");
    }

    [Fact]
    public void TopBottomRule_UsesEditableRankOrPercentValue()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Bottom 10%", RangeFor(SheetId.New())));

            FindLabel(dialog.Content, "_Percent:").Should().NotBeNull();
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

            GetControl<ComboBox>(dialog, "_colorBox").SelectedItem.Should().Be("Custom Format...");

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.DataBarColor.Should().Be(new RgbColor(12, 34, 56));

            dialog.Close();
        });
    }

    [Fact]
    public void IconSetRule_CreatesThresholdsForSelectedIconCount()
    {
        StaTestRunner.Run(() =>
        {
            var range = RangeFor(SheetId.New());
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Icon Set", range));

            GetControl<ComboBox>(dialog, "_iconSetStyleBox").SelectedItem = "5Quarters";

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.IconSetStyle.Should().Be("5Quarters");
            dialog.ResultRule.IconSetThresholds.Should().Equal(
                new CfThresholdModel(CfThresholdType.Percent, "0"),
                new CfThresholdModel(CfThresholdType.Percent, "20"),
                new CfThresholdModel(CfThresholdType.Percent, "40"),
                new CfThresholdModel(CfThresholdType.Percent, "60"),
                new CfThresholdModel(CfThresholdType.Percent, "80"));

            dialog.Close();
        });
    }

    [Fact]
    public void IconSetRule_OffersExcelIconSetGalleryStyles()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Icon Set", RangeFor(SheetId.New())));

            var styles = GetControl<ComboBox>(dialog, "_iconSetStyleBox").Items.Cast<string>();

            styles.Should().Contain([
                "3ArrowsGray",
                "3Flags",
                "4RedToBlack",
                "4Rating",
                "5Boxes"
            ]);

            dialog.Close();
        });
    }

    [Fact]
    public void ExistingIconSetRule_PrePopulatesIconSetFields()
    {
        StaTestRunner.Run(() =>
        {
            var id = Guid.NewGuid();
            var existing = new ConditionalFormat
            {
                Id = id,
                AppliesTo = RangeFor(SheetId.New()),
                Priority = 4,
                RuleType = CfRuleType.IconSet,
                IconSetStyle = "4TrafficLights",
                IconSetShowValue = false,
                IconSetReverse = true,
                StopIfTrue = true
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            GetControl<ComboBox>(dialog, "_iconSetStyleBox").SelectedItem.Should().Be("4TrafficLights");
            GetControl<CheckBox>(dialog, "_iconSetShowValueBox").IsChecked.Should().BeFalse();
            GetControl<CheckBox>(dialog, "_iconSetReverseBox").IsChecked.Should().BeTrue();

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.IconSet);
            dialog.ResultRule.Id.Should().Be(id);
            dialog.ResultRule.Priority.Should().Be(4);
            dialog.ResultRule.IconSetStyle.Should().Be("4TrafficLights");
            dialog.ResultRule.IconSetShowValue.Should().BeFalse();
            dialog.ResultRule.IconSetReverse.Should().BeTrue();
            dialog.ResultRule.StopIfTrue.Should().BeTrue();

            dialog.Close();
        });
    }

    [Fact]
    public void ExistingIconSetRule_PreservesUnlistedIconSetStyleAndHiddenFields()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.IconSet,
                IconSetStyle = "3ArrowsGray",
                IconSetShowValue = false,
                IconSetReverse = true,
                TopBottomRank = 5,
                StopIfTrue = true
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            GetControl<ComboBox>(dialog, "_iconSetStyleBox").SelectedItem.Should().Be("3ArrowsGray");
            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.IconSetStyle.Should().Be("3ArrowsGray");
            dialog.ResultRule.TopBottomRank.Should().Be(5);
            dialog.ResultRule.StopIfTrue.Should().BeTrue();

            dialog.Close();
        });
    }

    private static ConditionalFormatDialog ShowDialogForTest(ConditionalFormatDialog dialog)
    {
        dialog.Show();
        return dialog;
    }

    private static T GetControl<T>(ConditionalFormatDialog dialog, string name)
        where T : class
    {
        var field = typeof(ConditionalFormatDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static TextBlock? FindText(object? root, string text) =>
        EnumerateLogical(root).OfType<TextBlock>().FirstOrDefault(block => Equals(block.Text, text));

    private static Label? FindLabel(object? root, string content) =>
        EnumerateLogical(root).OfType<Label>().FirstOrDefault(label => Equals(label.Content, content));

    private static T? FindControl<T>(object? root)
        where T : DependencyObject =>
        EnumerateLogical(root).OfType<T>().FirstOrDefault();

    private static T? FindNamedControl<T>(object? root, string name)
        where T : FrameworkElement =>
        EnumerateLogical(root).OfType<T>().FirstOrDefault(element => element.Name == name);

    private static Button? FindButton(object? root, string content) =>
        EnumerateLogical(root).OfType<Button>().FirstOrDefault(button => Equals(button.Content, content));

    private static IEnumerable<object> EnumerateLogical(object? root)
    {
        if (root is null)
            yield break;

        yield return root;

        if (root is not DependencyObject dependencyObject)
            yield break;

        foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject).Cast<object>())
        {
            foreach (var descendant in EnumerateLogical(child))
                yield return descendant;
        }
    }

    private static void ClickOkForTest(ConditionalFormatDialog dialog)
    {
        var method = typeof(ConditionalFormatDialog).GetMethod("Ok_Click", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        try
        {
            method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalidOperation
            && invalidOperation.Message.Contains("DialogResult"))
        {
            // The handler creates ResultRule before setting DialogResult. Direct modeless invocation in
            // tests reaches WPF's modal-only postcondition after the behavior under test runs.
        }
    }

    private static GridRange RangeFor(SheetId sheetId) =>
        new(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3));
}
