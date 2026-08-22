using System.Windows;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void InsertFormControlsBtn_Click(object sender, RoutedEventArgs e) =>
        InsertFormControl(FormControlKind.CheckBox);

    private void InsertCheckBoxFormControlMenuItem_Click(object sender, RoutedEventArgs e) =>
        InsertFormControl(FormControlKind.CheckBox);

    private void InsertOptionButtonFormControlMenuItem_Click(object sender, RoutedEventArgs e) =>
        InsertFormControl(FormControlKind.OptionButton);

    private void InsertButtonFormControlMenuItem_Click(object sender, RoutedEventArgs e) =>
        InsertFormControl(FormControlKind.Button);

    private void InsertDropDownFormControlMenuItem_Click(object sender, RoutedEventArgs e) =>
        InsertFormControl(FormControlKind.DropDown);

    private void InsertListBoxFormControlMenuItem_Click(object sender, RoutedEventArgs e) =>
        InsertFormControl(FormControlKind.ListBox);

    private void InsertSpinnerFormControlMenuItem_Click(object sender, RoutedEventArgs e) =>
        InsertFormControl(FormControlKind.Spinner);

    private void InsertScrollBarFormControlMenuItem_Click(object sender, RoutedEventArgs e) =>
        InsertFormControl(FormControlKind.ScrollBar);

    private void InsertFormControl(FormControlKind kind)
    {
        var anchor = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Insert " + FormControlDisplayName(kind),
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    return new AddFormControlCommand(
                        sheetId,
                        new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col),
                        kind);
                }))
        {
            return;
        }

        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        UpdateViewport();
    }

    private static string FormControlDisplayName(FormControlKind kind) => kind switch
    {
        FormControlKind.CheckBox => "Check Box",
        FormControlKind.OptionButton => "Option Button",
        FormControlKind.DropDown => "Drop-Down",
        FormControlKind.ListBox => "List Box",
        FormControlKind.Spinner => "Spin Button",
        FormControlKind.ScrollBar => "Scroll Bar",
        _ => "Button",
    };
}
