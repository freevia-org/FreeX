using System.Windows;
using System.Windows.Controls;
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
                cf.Value1 = _value1Box.Text.Trim();
                cf.Value2 = _value2Box.Text.Trim();
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
                cf.DataBarMinLength = ParseOptionalPercent(_dataBarMinLengthBox.Text);
                cf.DataBarMaxLength = ParseOptionalPercent(_dataBarMaxLengthBox.Text);
            }
            else if (cf.RuleType == CfRuleType.ColorScale)
            {
                cf.MinThresholdType = SelectedThresholdType(_colorScaleMinTypeBox, CfThresholdType.Min);
                cf.MinThresholdValue = BlankToNull(_colorScaleMinValueBox.Text);
                cf.MinColor = ParseRgbOrFallback(_colorScaleMinColorBox.Text, cf.MinColor);
                cf.UseThreeColorScale = _colorScaleUseThreeColorBox.IsChecked == true;
                cf.MidThresholdType = SelectedThresholdType(_colorScaleMidTypeBox, CfThresholdType.Percentile);
                cf.MidThresholdValue = BlankToNull(_colorScaleMidValueBox.Text);
                cf.MidColor = ParseRgbOrFallback(_colorScaleMidColorBox.Text, cf.MidColor);
                cf.MaxThresholdType = SelectedThresholdType(_colorScaleMaxTypeBox, CfThresholdType.Max);
                cf.MaxThresholdValue = BlankToNull(_colorScaleMaxValueBox.Text);
                cf.MaxColor = ParseRgbOrFallback(_colorScaleMaxColorBox.Text, cf.MaxColor);
            }
            else if (cf.RuleType is CfRuleType.ContainsText or CfRuleType.NotContainsText or CfRuleType.BeginsWith or CfRuleType.EndsWith)
            {
                cf.TextRuleText = _value1Box.Text.Trim();
            }
            else if (cf.RuleType == CfRuleType.DateOccurring)
            {
                cf.DateOccurringPeriod = DatePeriodValue(_dateOccurringPeriodBox.SelectedItem as string);
            }

            cf.AboveAverage = _ruleType is not ("Below Average" or "Bottom 10 Items" or "Bottom 10%");
            cf.TopBottomPercent = _ruleType is "Top 10%" or "Bottom 10%";
            if (cf.RuleType == CfRuleType.Top10)
                cf.TopBottomRank = ParseTopBottomRank(_topBottomRankBox.Text);
        }

        if (cf.RuleType is not (CfRuleType.IconSet or CfRuleType.DataBar or CfRuleType.ColorScale))
        {
            cf.FormatIfTrue = BuildSelectedCellStyle();
        }
        else
        {
            cf.FormatIfTrue = null;
        }

        ResultRule = cf;
        DialogResult = true;
    }

    private static CfThresholdType SelectedThresholdType(ComboBox comboBox, CfThresholdType fallback) =>
        comboBox.SelectedItem is CfThresholdType selected ? selected : fallback;

}
