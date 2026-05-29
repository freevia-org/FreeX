using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public sealed partial class AutoFilterDialog
{
    private static IEnumerable<AutoFilterDialogItem> CreateDialogItems(AutoFilterMenuPlan menuPlan) =>
        menuPlan.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .Select(entry => new AutoFilterDialogItem(entry.Header, entry.Value, true));

    private void ReplaceItems(IEnumerable<AutoFilterDialogItem> items)
    {
        _items.Clear();
        foreach (var item in items)
            _items.Add(item);
    }

    private void ReplaceAllItems(IEnumerable<AutoFilterDialogItem> items)
    {
        _allItems.Clear();
        _allItems.AddRange(items);
        ReplaceItems(FilterItems(_allItems, _searchBox.Text));
    }

    private AutoFilterSortDirection GetSortDirection()
    {
        if (_sortAscending.IsChecked == true)
            return AutoFilterSortDirection.Ascending;

        return _sortDescending.IsChecked == true
            ? AutoFilterSortDirection.Descending
            : AutoFilterSortDirection.None;
    }

    private void UpdateCriteriaTextFromTypedControls()
    {
        if (_criteriaOperatorBox.SelectedItem is not AutoFilterCriteriaOption option)
            return;

        if (SelectedDatePresetCriteria() is { Length: > 0 } datePresetCriteria)
        {
            _criteriaBox.Text = datePresetCriteria;
            RefreshSpecialCriteriaPanels(option);
            return;
        }

        var firstCriteria = BuildPrimaryCriteriaText(option);
        var secondCriteria = _criteriaOperatorBox2.SelectedItem is AutoFilterCriteriaOption option2 &&
            (!option2.RequiresValue || !string.IsNullOrWhiteSpace(_criteriaValueBox2.Text))
                ? BuildCriteriaText(option2, _criteriaValueBox2.Text)
                : string.Empty;
        _criteriaBox.Text = BuildCompositeCriteriaText(
            firstCriteria,
            _criteriaConnectorBox.SelectedItem as string,
            secondCriteria);
        RefreshSpecialCriteriaPanels(option);
        if (_criteriaOperatorBox2.SelectedItem is AutoFilterCriteriaOption secondOption)
            _criteriaValueBox2.IsEnabled = secondOption.RequiresValue;
    }

    private string BuildPrimaryCriteriaText(AutoFilterCriteriaOption option)
    {
        if (IsBetweenOption(option))
            return BuildBetweenCriteriaText(option, _betweenMinBox.Text, _betweenMaxBox.Text);

        if (IsTopBottomOption(option))
            return BuildTopBottomCriteriaText(option, _topBottomCountBox.Text);

        return BuildCriteriaText(option, _criteriaValueBox.Text);
    }

    private bool ValidateTypedCriteriaInputs()
    {
        if (_criteriaOperatorBox.SelectedItem is not AutoFilterCriteriaOption option)
            return true;

        if (SelectedDatePresetCriteria() is { Length: > 0 })
            return true;

        if (IsBetweenOption(option))
        {
            if (string.IsNullOrWhiteSpace(_betweenMinBox.Text))
                return ShowInvalidCriteriaWarning("Enter the first value for the between filter.", _betweenMinBox);

            if (string.IsNullOrWhiteSpace(_betweenMaxBox.Text))
                return ShowInvalidCriteriaWarning("Enter the second value for the between filter.", _betweenMaxBox);

            return true;
        }

        if (IsTopBottomOption(option))
        {
            if (!FilterInputParser.TryParseTopBottom(BuildTopBottomCriteriaText(option, _topBottomCountBox.Text), out _, out _, out _, out _))
                return ShowInvalidCriteriaWarning("Enter a valid top or bottom count.", _topBottomCountBox);

            return true;
        }

        if (option.RequiresValue && string.IsNullOrWhiteSpace(_criteriaValueBox.Text))
            return ShowInvalidCriteriaWarning("Enter a filter value.", _criteriaValueBox);

        return true;
    }

    private bool ShowInvalidCriteriaWarning(string message, TextBox target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
        return false;
    }

    private string SelectedDatePresetCriteria()
    {
        var preset = _datePresetBox.Visibility == Visibility.Visible
            ? _datePresetBox.SelectedItem as string
            : null;
        return string.IsNullOrWhiteSpace(preset) || preset == "Custom"
            ? string.Empty
            : BuildDatePresetCriteriaText(preset, DateTime.Today);
    }

    private void RefreshSpecialCriteriaPanels(AutoFilterCriteriaOption option)
    {
        var isBetween = IsBetweenOption(option);
        var isTopBottom = IsTopBottomOption(option);
        _criteriaValueBox.IsEnabled = option.RequiresValue && !isBetween && !isTopBottom;
        _criteriaValueBox.Visibility = option.RequiresValue && !isBetween && !isTopBottom
            ? Visibility.Visible
            : Visibility.Collapsed;
        _betweenCriteriaPanel.Visibility = isBetween ? Visibility.Visible : Visibility.Collapsed;
        _topBottomCriteriaPanel.Visibility = isTopBottom ? Visibility.Visible : Visibility.Collapsed;
        _topBottomUnitText.Text = option.CriteriaPrefix.Contains("percent", StringComparison.OrdinalIgnoreCase)
            ? "Percent"
            : "Items";
    }

    private static bool IsBetweenOption(AutoFilterCriteriaOption option) =>
        AutoFilterDialogCriteriaPlanner.IsBetweenOption(option);

    private static bool IsTopBottomOption(AutoFilterCriteriaOption option) =>
        AutoFilterDialogCriteriaPlanner.IsTopBottomOption(option);
}
