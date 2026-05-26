using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class ConditionalFormatDialog
{
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var cf = _existingRule is not null
            ? ConditionalFormatDialogPlanner.CloneRule(_existingRule)
            : new ConditionalFormat { Id = _existingId, AppliesTo = _range };
        cf.AppliesTo = _range;
        var selectedFormat = SelectedColorPreset();
        var fillColor = selectedFormat.FillColor;

        bool isFormula = _ruleType is "Formula" or "Use a Formula";

        if (isFormula)
        {
            cf.RuleType = CfRuleType.Formula;
            var raw = _formulaBox?.Text.Trim() ?? "";
            if (raw is "" or "=")
            {
                ShowInvalidInputWarning("Enter a formula for this conditional formatting rule.", _formulaBox);
                return;
            }

            cf.FormulaText = raw.StartsWith('=') ? raw[1..] : raw;
        }
        else
        {
            cf.RuleType = _ruleType switch
            {
                "Data Bar"    => CfRuleType.DataBar,
                "Color Scale" => CfRuleType.ColorScale,
                "Icon Set"    => CfRuleType.IconSet,
                "Text Contains" => CfRuleType.ContainsText,
                "Date Occurring" => CfRuleType.DateOccurring,
                "Duplicate Values" => DuplicateValuesRuleType(_duplicateValuesKindBox.SelectedItem as string),
                "Blanks" => CfRuleType.Blanks,
                "No Blanks" => CfRuleType.NoBlanks,
                "Errors" => CfRuleType.Errors,
                "No Errors" => CfRuleType.NoErrors,
                "Above Average" or "Below Average" => CfRuleType.AboveAverage,
                "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%" => CfRuleType.Top10,
                _ => CfRuleType.CellValue
            };

            if (cf.RuleType == CfRuleType.CellValue)
            {
                cf.Operator = _ruleType switch
                {
                    "Greater Than" => CfOperator.GreaterThan,
                    "Less Than"    => CfOperator.LessThan,
                    "Equal To"     => CfOperator.Equal,
                    "Between"      => CfOperator.Between,
                    _              => CfOperator.NotEqual
                };
                var value1 = _value1Box.Text.Trim();
                var value2 = _value2Box.Text.Trim();
                if (string.IsNullOrWhiteSpace(value1))
                {
                    ShowInvalidInputWarning("Enter a value for this conditional formatting rule.", _value1Box);
                    return;
                }

                if (cf.Operator == CfOperator.Between && string.IsNullOrWhiteSpace(value2))
                {
                    ShowInvalidInputWarning("Enter a maximum value for this conditional formatting rule.", _value2Box);
                    return;
                }

                cf.Value1 = value1;
                cf.Value2 = value2;
            }
            else if (cf.RuleType == CfRuleType.IconSet)
            {
                cf.IconSetStyle = _iconSetStyleBox.SelectedItem as string ?? IconSetStyles[0];
                cf.IconSetShowValue = _iconSetShowValueBox.IsChecked == true;
                cf.IconSetReverse = _iconSetReverseBox.IsChecked == true;
                cf.IconSetThresholds.Clear();
                if (_iconSetThresholdRows.Count > 0)
                {
                    foreach (var (typeBox, valueBox) in _iconSetThresholdRows)
                    {
                        var type = typeBox.SelectedItem is CfThresholdType t ? t : CfThresholdType.Percent;
                        cf.IconSetThresholds.Add(new CfThresholdModel(type, BlankToNull(valueBox.Text)));
                    }
                }
                else
                {
                    cf.IconSetThresholds.AddRange(ConditionalFormatIconSetPlanner.CreateThresholds(cf.IconSetStyle));
                }
            }
            else if (cf.RuleType == CfRuleType.DataBar)
            {
                var barColor = SelectedDataBarColor(fillColor);
                cf.DataBarColor = new RgbColor(barColor.R, barColor.G, barColor.B);
                cf.DataBarMinThresholdType = SelectedThresholdType(_dataBarMinTypeBox, CfThresholdType.Min);
                cf.DataBarMinThresholdValue = BlankToNull(_dataBarMinValueBox.Text);
                cf.DataBarMaxThresholdType = SelectedThresholdType(_dataBarMaxTypeBox, CfThresholdType.Max);
                cf.DataBarMaxThresholdValue = BlankToNull(_dataBarMaxValueBox.Text);
                cf.DataBarShowValue = _dataBarShowValueBox.IsChecked != true;
                cf.DataBarGradient = _dataBarGradientBox.IsChecked == true;
                if (!TryParseOptionalPercent(_dataBarMinLengthBox.Text, out var minLength))
                {
                    ShowInvalidInputWarning("Enter a minimum bar length from 0 to 100 percent, or leave it blank.", _dataBarMinLengthBox);
                    return;
                }

                if (!TryParseOptionalPercent(_dataBarMaxLengthBox.Text, out var maxLength))
                {
                    ShowInvalidInputWarning("Enter a maximum bar length from 0 to 100 percent, or leave it blank.", _dataBarMaxLengthBox);
                    return;
                }

                cf.DataBarMinLength = minLength;
                cf.DataBarMaxLength = maxLength;
            }
            else if (cf.RuleType == CfRuleType.ColorScale)
            {
                cf.MinThresholdType = SelectedThresholdType(_colorScaleMinTypeBox, CfThresholdType.Min);
                cf.MinThresholdValue = BlankToNull(_colorScaleMinValueBox.Text);
                if (!TryParseRgbColor(_colorScaleMinColorBox.Text, out var minColor))
                {
                    ShowInvalidInputWarning("Enter a minimum color as R, G, B.", _colorScaleMinColorBox);
                    return;
                }

                cf.MinColor = minColor;
                cf.UseThreeColorScale = _colorScaleUseThreeColorBox.IsChecked == true;
                cf.MidThresholdType = SelectedThresholdType(_colorScaleMidTypeBox, CfThresholdType.Percentile);
                cf.MidThresholdValue = BlankToNull(_colorScaleMidValueBox.Text);
                if (cf.UseThreeColorScale)
                {
                    if (!TryParseRgbColor(_colorScaleMidColorBox.Text, out var midColor))
                    {
                        ShowInvalidInputWarning("Enter a midpoint color as R, G, B.", _colorScaleMidColorBox);
                        return;
                    }

                    cf.MidColor = midColor;
                }
                cf.MaxThresholdType = SelectedThresholdType(_colorScaleMaxTypeBox, CfThresholdType.Max);
                cf.MaxThresholdValue = BlankToNull(_colorScaleMaxValueBox.Text);
                if (!TryParseRgbColor(_colorScaleMaxColorBox.Text, out var maxColor))
                {
                    ShowInvalidInputWarning("Enter a maximum color as R, G, B.", _colorScaleMaxColorBox);
                    return;
                }

                cf.MaxColor = maxColor;
            }
            else if (cf.RuleType is CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith)
            {
                var text = _value1Box.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    ShowInvalidInputWarning("Enter text for this conditional formatting rule.", _value1Box);
                    return;
                }

                cf.TextRuleText = text;
            }
            else if (cf.RuleType == CfRuleType.DateOccurring)
            {
                cf.DateOccurringPeriod = DatePeriodValue(_dateOccurringPeriodBox.SelectedItem as string);
            }

            cf.AboveAverage = _ruleType is not ("Below Average" or "Bottom 10 Items" or "Bottom 10%");
            cf.TopBottomPercent = _ruleType is "Top 10%" or "Bottom 10%";
            if (cf.RuleType == CfRuleType.Top10)
            {
                if (!TryParseTopBottomRank(_topBottomRankBox.Text, out var topBottomRank))
                {
                    ShowInvalidInputWarning("Enter a rank or percent from 1 to 1000.", _topBottomRankBox);
                    return;
                }

                cf.TopBottomRank = topBottomRank;
            }
        }

        if (cf.RuleType is not (CfRuleType.IconSet or CfRuleType.DataBar or CfRuleType.ColorScale))
        {
            cf.FormatIfTrue = BuildSelectedCellStyle();
        }
        else
        {
            cf.FormatIfTrue = null;
        }

        if (_existingRule is not null && cf.RuleType != _existingRule.RuleType)
            ClearNativeConditionalFormatMetadata(cf);

        ResultRule = cf;
        DialogResult = true;
    }

    private static void ClearNativeConditionalFormatMetadata(ConditionalFormat cf)
    {
        cf.NativeAttributes = null;
        cf.NativeChildXmls = null;
        cf.NativePayloadAttributes = null;
        cf.NativePayloadChildXmls = null;
        cf.NativeContainerAttributes = null;
        cf.NativeContainerChildXmls = null;
    }

    private static CfThresholdType SelectedThresholdType(ComboBox comboBox, CfThresholdType fallback) =>
        comboBox.SelectedItem is CfThresholdType selected ? selected : fallback;

    private bool ShowInvalidInputWarning(string message, TextBox? target)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        if (target is null)
            return false;

        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
        return false;
    }

}
