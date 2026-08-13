using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Editing;

namespace FreeX.App.Host;

public sealed class CellShiftDialog : Window
{
    private readonly CellShiftDialogMode _mode;
    private readonly List<RadioButton> _buttons = [];

    public CellShiftDialogChoice SelectedChoice { get; private set; }

    public CellShiftDialog(CellShiftDialogMode mode)
    {
        _mode = mode;
        var surface = CellShiftDialogPlanner.GetSurface(mode);
        Title = UiText.Get(surface.TitleKey);
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
            Text = UiText.Get(surface.PromptKey),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var group = new GroupBox
        {
            Header = UiText.Get(surface.GroupHeaderKey),
            Margin = new Thickness(0, 0, 0, 10),
            Content = optionPanel
        };
        DockPanel.SetDock(group, Dock.Top);
        root.Children.Add(group);

        foreach (var option in surface.Options)
        {
            var button = new RadioButton
            {
                Content = UiText.Get(option.LabelKey),
                Tag = option.Choice,
                Margin = new Thickness(0, 0, 0, 6)
            };
            AutomationProperties.SetName(button, option.AutomationName);
            AutomationProperties.SetAutomationId(button, option.AutomationId);
            AutomationProperties.SetHelpText(button, option.HelpText);
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

    private void FocusInitialKeyboardTarget()
    {
        var firstButton = FindFirstButton();
        firstButton?.Focus();
        if (firstButton is not null)
            Keyboard.Focus(firstButton);
    }

    private void Accept()
    {
        var selected = FindSelectedButton();
        SelectedChoice = selected?.Tag is CellShiftDialogChoice choice
            ? choice
            : CellShiftDialogPlanner.GetAvailableChoices(_mode)[0].Choice;
        DialogResult = true;
    }

    private RadioButton? FindFirstButton() =>
        _buttons.Count > 0 ? _buttons[0] : null;

    private RadioButton? FindSelectedButton()
    {
        foreach (var button in _buttons)
            if (button.IsChecked == true)
                return button;

        return null;
    }

}
