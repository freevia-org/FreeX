using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

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
            AutomationProperties.SetName(button, GetChoiceAutomationName(option.Choice));
            AutomationProperties.SetAutomationId(button, $"CellShift{option.Choice}Option");
            AutomationProperties.SetHelpText(button, GetChoiceHelpText(option.Choice));
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
        CellShiftDialogPlanner.GetAvailableChoices(mode);

    public static KeyboardInsertDeleteDialogChoice ToKeyboardChoice(CellShiftDialogMode mode, CellShiftDialogChoice choice) =>
        CellShiftDialogPlanner.ToKeyboardChoice(mode, choice);

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

    private static string GetChoiceAutomationName(CellShiftDialogChoice choice) =>
        choice switch
        {
            CellShiftDialogChoice.ShiftCellsRight => "Shift cells right",
            CellShiftDialogChoice.ShiftCellsDown => "Shift cells down",
            CellShiftDialogChoice.ShiftCellsLeft => "Shift cells left",
            CellShiftDialogChoice.ShiftCellsUp => "Shift cells up",
            CellShiftDialogChoice.EntireRow => "Entire row",
            _ => "Entire column"
        };

    private static string GetChoiceHelpText(CellShiftDialogChoice choice) =>
        choice switch
        {
            CellShiftDialogChoice.ShiftCellsRight => "Insert cells and shift existing cells to the right.",
            CellShiftDialogChoice.ShiftCellsDown => "Insert cells and shift existing cells down.",
            CellShiftDialogChoice.ShiftCellsLeft => "Delete cells and shift remaining cells left.",
            CellShiftDialogChoice.ShiftCellsUp => "Delete cells and shift remaining cells up.",
            CellShiftDialogChoice.EntireRow => "Apply the operation to the entire selected row.",
            _ => "Apply the operation to the entire selected column."
        };
}
