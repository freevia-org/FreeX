using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class PageSetupDialog
{
    private void RangePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string targetName } ||
            FindName(targetName) is not TextBox target)
            return;

        RangeSelectionRequest = CreateRangeSelectionRequest(GetRangeSelectionTarget(targetName), target.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);

        if (_requestRangeSelection is null && _currentSelection is { } selection)
        {
            target.Text = targetName switch
            {
                nameof(RowsRepeatBox) => $"{selection.Start.Row}:{selection.End.Row}",
                nameof(ColumnsRepeatBox) => $"{CellAddress.NumberToColumnName(selection.Start.Col)}:{CellAddress.NumberToColumnName(selection.End.Col)}",
                _ => selection.ToString()
            };
        }

        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    public static PageSetupRangeSelectionRequest CreateRangeSelectionRequest(
        PageSetupRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private static PageSetupRangeSelectionTarget GetRangeSelectionTarget(string targetName) =>
        targetName switch
        {
            nameof(RowsRepeatBox) => PageSetupRangeSelectionTarget.RepeatRows,
            nameof(ColumnsRepeatBox) => PageSetupRangeSelectionTarget.RepeatColumns,
            _ => PageSetupRangeSelectionTarget.PrintArea
        };
}
