using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.Shell.Wpf;

namespace FreeX.App.Host;

public sealed class AddWatchDialog : DialogWindow
{
    private readonly TextBox _rangeBox = new();

    public AddWatchDialog(string selectedRangeText)
    {
        Title = UiText.Get("AddWatch_Title");
        Width = 360;
        Height = 170;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(12) };

        var add = new Button { Content = UiText.Get("AddWatch_AddButton"), Width = 76 };
        AutomationProperties.SetName(add, UiText.Get("AddWatch_AddAutomationName"));
        AutomationProperties.SetAutomationId(add, "AddWatchAddButton");
        AutomationProperties.SetHelpText(add, UiText.Get("AddWatch_AddHelpText"));
        add.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = UiText.Cancel, Width = 76 };
        AutomationProperties.SetName(cancel, UiText.Get("AddWatch_CancelAutomationName"));
        AutomationProperties.SetAutomationId(cancel, "AddWatchCancelButton");
        AutomationProperties.SetHelpText(cancel, UiText.Get("AddWatch_CancelHelpText"));

        var buttons = DialogButtonRowFactory.Create(add, cancel, new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();
        root.Children.Add(body);
        _rangeBox.Text = selectedRangeText;
        _rangeBox.IsReadOnly = true;
        _rangeBox.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetName(_rangeBox, UiText.Get("AddWatch_SelectedRangeAutomationName"));
        AutomationProperties.SetAutomationId(_rangeBox, "AddWatchSelectedRangeBox");
        AutomationProperties.SetHelpText(_rangeBox, UiText.Get("AddWatch_SelectedRangeHelpText"));
        body.Children.Add(new Label
        {
            Content = UiText.Get("AddWatch_SelectedRangeLabel"),
            Target = _rangeBox,
            Padding = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(_rangeBox);
        body.Children.Add(new TextBlock
        {
            Text = UiText.Get("AddWatch_BodyText"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush
        });

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        DialogFocus.FocusAndSelect(_rangeBox);
    }
}
