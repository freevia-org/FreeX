using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Host;

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
            Text = UiText.Get("ConditionalFormatDialog_SelectRuleTypeHeader"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var ruleTypeList = new ListBox
        {
            MinHeight = 182,
            ItemsSource = ExcelRuleShellTypes,
            SelectedItem = RuleTypeShellLabel(ruleType)
        };
        AutomationProperties.SetName(ruleTypeList, UiText.Get("ConditionalFormatDialog_RuleTypeAutomationName"));
        ruleTypeList.SelectionChanged += RuleTypeList_SelectionChanged;
        left.Children.Add(ruleTypeList);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var right = new StackPanel();
        _descriptionHost = right;
        right.Children.Add(new TextBlock
        {
            Text = UiText.Get("ConditionalFormatDialog_EditRuleDescriptionHeader"),
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

    private string DefaultRuleTypeForShellLabel(string shellLabel)
        => ConditionalFormatDialogCatalog.DefaultRuleTypeForShellKey(
            LabelKeyForLocalizedOption(ConditionalFormatDialogCatalog.RuleShellOptions, shellLabel),
            _ruleType);

    private static string RuleTypeShellLabel(string ruleType) =>
        UiText.Get(ConditionalFormatDialogCatalog.ShellKeyForRuleType(ruleType));
}
