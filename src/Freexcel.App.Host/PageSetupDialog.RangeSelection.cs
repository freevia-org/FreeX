using System.Windows;
using System.Windows.Controls;
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
            target.Text = PageSetupRangeSelectionFormatter.Format(
                GetRangeSelectionTarget(targetName),
                selection,
                useR1C1ReferenceStyle: false);
        }

        DialogFocus.FocusAndSelect(target);
    }

    public static PageSetupRangeSelectionRequest CreateRangeSelectionRequest(
        PageSetupRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    public void ApplyRangeSelection(PageSetupRangeSelectionTarget target, string rangeText)
    {
        var textBox = target switch
        {
            PageSetupRangeSelectionTarget.RepeatRows => RowsRepeatBox,
            PageSetupRangeSelectionTarget.RepeatColumns => ColumnsRepeatBox,
            _ => PrintAreaBox
        };

        textBox.Text = rangeText;
        DialogFocus.FocusAndSelect(textBox);
    }

    private static PageSetupRangeSelectionTarget GetRangeSelectionTarget(string targetName) =>
        targetName switch
        {
            nameof(RowsRepeatBox) => PageSetupRangeSelectionTarget.RepeatRows,
            nameof(ColumnsRepeatBox) => PageSetupRangeSelectionTarget.RepeatColumns,
            _ => PageSetupRangeSelectionTarget.PrintArea
        };
}
