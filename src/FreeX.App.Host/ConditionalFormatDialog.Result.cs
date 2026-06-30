using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class ConditionalFormatDialog
{
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var input = CreateRuleInputFromControls();
        var validation = ConditionalFormatRuleSchema.ForRuleType(input.RuleType).Validate(input);
        if (!validation.IsValid)
        {
            ShowValidationWarning(validation);
            return;
        }

        ResultRule = ConditionalFormatRuleBuilder.Build(
            input,
            _range,
            id: _existingId,
            customFormat: ConditionalFormatDialogCatalog.IsVisualRuleType(_ruleType) ? null : BuildSelectedCellStyle(),
            existingRule: _existingRule);
        DialogResult = true;
    }

    private CfRuleInput CreateRuleInputFromControls()
    {
        var ruleType = ConditionalFormatDialogPlanner.ModelRuleTypeForDialogRuleType(
            _ruleType,
            DuplicateValuesRuleType(_duplicateValuesKindBox.SelectedItem as string) == CfRuleType.UniqueValues);

        var input = new CfRuleInput
        {
            RuleType = ruleType,
            Operator = ConditionalFormatDialogPlanner.OperatorForDialogRuleType(_ruleType),
            IsTop = ConditionalFormatDialogCatalog.IsTopRuleType(_ruleType),
            IsPercent = ConditionalFormatDialogCatalog.IsTopBottomPercentRuleType(_ruleType)
        };

        return ruleType switch
        {
            CfRuleType.Formula => input with
            {
                Formula = _formulaBox?.Text
            },

            CfRuleType.CellValue => input with
            {
                Value1 = _value1Box.Text,
                Value2 = _value2Box.Text
            },

            CfRuleType.IconSet => input with
            {
                IconSetStyle = _iconSetStyleBox.SelectedItem as string ?? ConditionalFormatIconSetCatalog.DefaultStyle,
                IconSetShowValue = _iconSetShowValueBox.IsChecked == true,
                IconSetReverse = _iconSetReverseBox.IsChecked == true,
                IconSetThresholds = BuildIconSetThresholdInputs(),
                IconOverrides = BuildIconOverrideInputs()
            },

            CfRuleType.DataBar => input with
            {
                DataBarColor = RgbColor.FromCellColor(SelectedDataBarColor(SelectedColorPreset().FillColor)),
                DataBarMinType = SelectedThresholdType(_dataBarMinTypeBox, CfThresholdType.Min),
                DataBarMinValue = _dataBarMinValueBox.Text,
                DataBarMaxType = SelectedThresholdType(_dataBarMaxTypeBox, CfThresholdType.Max),
                DataBarMaxValue = _dataBarMaxValueBox.Text,
                DataBarShowValue = _dataBarShowValueBox.IsChecked != true,
                DataBarGradient = _dataBarGradientBox.IsChecked == true,
                DataBarMinLength = _dataBarMinLengthBox.Text,
                DataBarMaxLength = _dataBarMaxLengthBox.Text,
                DataBarBorder = _dataBarBorderBox.IsChecked == true,
                DataBarAxisPosition = AxisPositionToXmlValue(_dataBarAxisPositionBox.SelectedItem as string),
                DataBarAxisColor = ParseOptionalRgbColor(_dataBarAxisColorBox.Text),
                DataBarNegativeFillColor = ParseOptionalRgbColor(_dataBarNegativeFillColorBox.Text),
                DataBarNegativeBorderColor = ParseOptionalRgbColor(_dataBarNegativeBorderColorBox.Text)
            },

            CfRuleType.ColorScale => input with
            {
                ColorScaleMinType = SelectedThresholdType(_colorScaleMinTypeBox, CfThresholdType.Min),
                ColorScaleMinValue = _colorScaleMinValueBox.Text,
                MinColor = _colorScaleMinColorBox.Text,
                UseThreeColorScale = _colorScaleUseThreeColorBox.IsChecked == true,
                ColorScaleMidType = SelectedThresholdType(_colorScaleMidTypeBox, CfThresholdType.Percentile),
                ColorScaleMidValue = _colorScaleMidValueBox.Text,
                MidColor = _colorScaleMidColorBox.Text,
                ColorScaleMaxType = SelectedThresholdType(_colorScaleMaxTypeBox, CfThresholdType.Max),
                ColorScaleMaxValue = _colorScaleMaxValueBox.Text,
                MaxColor = _colorScaleMaxColorBox.Text
            },

            CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith => input with
            {
                Text = _value1Box.Text
            },

            CfRuleType.DateOccurring => input with
            {
                DatePeriod = DatePeriodValue(_dateOccurringPeriodBox.SelectedItem as string)
            },

            CfRuleType.Top10 => input with
            {
                Rank = _topBottomRankBox.Text
            },

            _ => input
        };
    }

    private IReadOnlyList<CfThresholdModel>? BuildIconSetThresholdInputs()
    {
        if (_iconSetThresholdRows.Count == 0)
            return null;

        var thresholds = new List<CfThresholdModel>(_iconSetThresholdRows.Count);
        foreach (var (typeBox, valueBox, _) in _iconSetThresholdRows)
        {
            var type = typeBox.SelectedItem is CfThresholdType selected ? selected : CfThresholdType.Percent;
            thresholds.Add(new CfThresholdModel(type, BlankToNull(valueBox.Text)));
        }

        return thresholds;
    }

    private IReadOnlyList<CfIconOverride?>? BuildIconOverrideInputs()
    {
        if (_iconSetThresholdRows.Count == 0)
            return null;

        var overrides = new List<CfIconOverride?>(_iconSetThresholdRows.Count);
        foreach (var (_, _, overrideBox) in _iconSetThresholdRows)
            overrides.Add(ChoiceToIconOverride(overrideBox?.SelectedItem as string));

        return overrides;
    }

    private bool ShowValidationWarning(CfValidationResult validation)
    {
        var error = validation.Errors[0];
        return error.Field switch
        {
            CfInputField.Formula => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidFormulaMessage"), _formulaBox),
            CfInputField.Value1 => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidValueMessage"), _value1Box),
            CfInputField.Value2 => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidMaximumValueMessage"), _value2Box),
            CfInputField.Text => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidTextMessage"), _value1Box),
            CfInputField.Rank => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidRankOrPercentMessage"), _topBottomRankBox),
            CfInputField.DataBarMinLength => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidMinimumBarLengthMessage"), _dataBarMinLengthBox),
            CfInputField.DataBarMaxLength => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidMaximumBarLengthMessage"), _dataBarMaxLengthBox),
            CfInputField.ColorScaleMinColor => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidMinimumColorMessage"), _colorScaleMinColorBox),
            CfInputField.ColorScaleMidColor => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidMidpointColorMessage"), _colorScaleMidColorBox),
            CfInputField.ColorScaleMaxColor => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidMaximumColorMessage"), _colorScaleMaxColorBox),
            _ => ShowInvalidInputWarning(UiText.Get("ConditionalFormatDialog_InvalidValueMessage"), null)
        };
    }

    private static CfThresholdType SelectedThresholdType(ComboBox comboBox, CfThresholdType fallback) =>
        comboBox.SelectedItem is CfThresholdType selected ? selected : fallback;

    private bool ShowInvalidInputWarning(string message, TextBox? target)
    {
        if (target is not null)
            DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        else
            DialogMessageHelper.ShowWarning(this, message, Title);

        return false;
    }
}
