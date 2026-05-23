using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public enum CellShiftDialogMode
{
    Insert,
    Delete
}

public enum CellShiftDialogChoice
{
    ShiftCellsRight,
    ShiftCellsDown,
    ShiftCellsLeft,
    ShiftCellsUp,
    EntireRow,
    EntireColumn
}

public sealed record CellShiftDialogOption(CellShiftDialogChoice Choice, string Label);

public sealed class CellShiftDialog : Window
{
    private readonly CellShiftDialogMode _mode;
    private readonly List<RadioButton> _buttons = [];

    public CellShiftDialogChoice SelectedChoice { get; private set; }

    public CellShiftDialog(CellShiftDialogMode mode)
    {
        _mode = mode;
        Title = mode == CellShiftDialogMode.Insert ? "Insert" : "Delete";
        Width = 310;
        Height = 245;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var optionPanel = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
        DockPanel.SetDock(optionPanel, Dock.Top);
        root.Children.Add(new TextBlock
        {
            Text = mode == CellShiftDialogMode.Insert ? "Insert cells" : "Delete cells",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var group = new GroupBox
        {
            Header = mode == CellShiftDialogMode.Insert ? "Insert" : "Delete",
            Margin = new Thickness(0, 0, 0, 10),
            Content = optionPanel
        };
        DockPanel.SetDock(group, Dock.Top);
        root.Children.Add(group);

        foreach (var option in GetAvailableChoices(mode))
        {
            var button = new RadioButton
            {
                Content = option.Label,
                Tag = option.Choice,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _buttons.Add(button);
            optionPanel.Children.Add(button);
        }

        if (_buttons.Count > 0)
            _buttons[0].IsChecked = true;

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static IReadOnlyList<CellShiftDialogOption> GetAvailableChoices(CellShiftDialogMode mode) =>
        mode == CellShiftDialogMode.Insert
            ? [
                new(CellShiftDialogChoice.ShiftCellsRight, "Shift cells _right"),
                new(CellShiftDialogChoice.ShiftCellsDown, "Shift cells _down"),
                new(CellShiftDialogChoice.EntireRow, "Entire _row"),
                new(CellShiftDialogChoice.EntireColumn, "Entire _column")
            ]
            : [
                new(CellShiftDialogChoice.ShiftCellsLeft, "Shift cells _left"),
                new(CellShiftDialogChoice.ShiftCellsUp, "Shift cells _up"),
                new(CellShiftDialogChoice.EntireRow, "Entire _row"),
                new(CellShiftDialogChoice.EntireColumn, "Entire _column")
            ];

    public static KeyboardInsertDeleteDialogChoice ToKeyboardChoice(CellShiftDialogMode mode, CellShiftDialogChoice choice) =>
        (mode, choice) switch
        {
            (CellShiftDialogMode.Insert, CellShiftDialogChoice.ShiftCellsDown) => KeyboardInsertDeleteDialogChoice.ShiftDown,
            (CellShiftDialogMode.Insert, CellShiftDialogChoice.EntireRow) => KeyboardInsertDeleteDialogChoice.EntireRow,
            (CellShiftDialogMode.Insert, CellShiftDialogChoice.EntireColumn) => KeyboardInsertDeleteDialogChoice.EntireColumn,
            (CellShiftDialogMode.Delete, CellShiftDialogChoice.ShiftCellsUp) => KeyboardInsertDeleteDialogChoice.ShiftUp,
            (CellShiftDialogMode.Delete, CellShiftDialogChoice.EntireRow) => KeyboardInsertDeleteDialogChoice.EntireRow,
            (CellShiftDialogMode.Delete, CellShiftDialogChoice.EntireColumn) => KeyboardInsertDeleteDialogChoice.EntireColumn,
            (CellShiftDialogMode.Delete, _) => KeyboardInsertDeleteDialogChoice.ShiftLeft,
            _ => KeyboardInsertDeleteDialogChoice.ShiftRight
        };

    private void FocusInitialKeyboardTarget()
    {
        var firstButton = _buttons.FirstOrDefault();
        _buttons.FirstOrDefault()?.Focus();
        if (firstButton is not null)
            Keyboard.Focus(firstButton);
    }

    private void Accept()
    {
        var selected = _buttons.FirstOrDefault(button => button.IsChecked == true);
        SelectedChoice = selected?.Tag is CellShiftDialogChoice choice
            ? choice
            : GetAvailableChoices(_mode)[0].Choice;
        DialogResult = true;
    }
}
