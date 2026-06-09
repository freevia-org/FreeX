using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;

namespace FreeX.App.Host;

public enum OutlineGroupDialogMode
{
    Group,
    Ungroup
}

public sealed class OutlineGroupDialog : Window
{
    private readonly List<RadioButton> _buttons = [];

    public OutlineGroupingAxis SelectedAxis { get; private set; } = OutlineGroupingAxis.Rows;

    public OutlineGroupDialog(OutlineGroupDialogMode mode)
    {
        Title = mode == OutlineGroupDialogMode.Group
            ? UiText.Get("MainWindow_Content_Group")
            : UiText.Get("MainWindow_Content_Ungroup");
        Width = 230;
        Height = 160;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var optionPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        var group = new GroupBox
        {
            Header = Title,
            Content = optionPanel,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(group, Dock.Top);
        root.Children.Add(group);

        AddOption(optionPanel, OutlineGroupingAxis.Rows, UiText.Get("MainWindow_Text_Rows"));
        AddOption(optionPanel, OutlineGroupingAxis.Columns, UiText.Get("MainWindow_Text_Columns"));
        _buttons[0].IsChecked = true;

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void AddOption(Panel panel, OutlineGroupingAxis axis, string label)
    {
        var button = new RadioButton
        {
            Content = label,
            Tag = axis,
            Margin = new Thickness(0, 0, 0, 6)
        };
        AutomationProperties.SetName(button, axis == OutlineGroupingAxis.Rows ? "Rows" : "Columns");
        AutomationProperties.SetAutomationId(button, $"OutlineGroup{axis}Option");
        AutomationProperties.SetHelpText(
            button,
            axis == OutlineGroupingAxis.Rows
                ? "Apply the outline command to selected rows."
                : "Apply the outline command to selected columns.");
        _buttons.Add(button);
        panel.Children.Add(button);
    }

    private void FocusInitialKeyboardTarget()
    {
        var firstButton = GetFirstButton();
        firstButton?.Focus();
        if (firstButton is not null)
            Keyboard.Focus(firstButton);
    }

    private void Accept()
    {
        var selected = GetSelectedButton();
        SelectedAxis = selected?.Tag is OutlineGroupingAxis axis ? axis : OutlineGroupingAxis.Rows;
        DialogResult = true;
    }

    private RadioButton? GetFirstButton()
    {
        foreach (var button in _buttons)
            return button;

        return null;
    }

    private RadioButton? GetSelectedButton()
    {
        foreach (var button in _buttons)
        {
            if (button.IsChecked == true)
                return button;
        }

        return null;
    }
}
