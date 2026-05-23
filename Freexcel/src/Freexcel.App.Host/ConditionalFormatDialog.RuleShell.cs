using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Freexcel.App.Host;

public partial class ConditionalFormatDialog
{
    private Grid BuildExcelRuleShell(string ruleType, UIElement descriptionContent)
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        left.Children.Add(new TextBlock
        {
            Text = "Select a Rule Type:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var ruleTypeList = new ListBox
        {
            MinHeight = 182,
            ItemsSource = ExcelRuleShellTypes,
            SelectedItem = RuleTypeShellLabel(ruleType)
        };
        ruleTypeList.SelectionChanged += RuleTypeList_SelectionChanged;
        left.Children.Add(ruleTypeList);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var right = new StackPanel();
        _descriptionHost = right;
        right.Children.Add(new TextBlock
        {
            Text = "Edit the Rule Description:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        right.Children.Add(descriptionContent);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        return root;
    }

    private void RuleTypeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not string shellLabel)
            return;

        var newRuleType = DefaultRuleTypeForShellLabel(shellLabel);
        if (newRuleType == _ruleType || _descriptionHost is null)
            return;

        RefreshRuleDescription(newRuleType);
    }

    private static string DefaultRuleTypeForShellLabel(string shellLabel) =>
        shellLabel == ExcelRuleShellTypes[0] ? "Data Bar" :
        shellLabel == ExcelRuleShellTypes[2] ? "Top 10 Items" :
        shellLabel == ExcelRuleShellTypes[3] ? "Above Average" :
        shellLabel == ExcelRuleShellTypes[4] ? "Duplicate Values" :
        shellLabel == ExcelRuleShellTypes[5] ? "Formula" :
        "Greater Than";

    private static string RuleTypeShellLabel(string ruleType) => ruleType switch
    {
        "Data Bar" or "Color Scale" or "Icon Set" => ExcelRuleShellTypes[0],
        "Top 10 Items" or "Bottom 10 Items" or "Top 10%" or "Bottom 10%" => ExcelRuleShellTypes[2],
        "Above Average" or "Below Average" => ExcelRuleShellTypes[3],
        "Duplicate Values" => ExcelRuleShellTypes[4],
        "Formula" or "Use a Formula" => ExcelRuleShellTypes[5],
        _ => ExcelRuleShellTypes[1]
    };
}
