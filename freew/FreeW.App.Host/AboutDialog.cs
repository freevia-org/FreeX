using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeW.App.Host;

public sealed class AboutDialog : Window
{
    private readonly TextBox _aboutTextBox;

    public AboutDialog()
    {
        Title = "About FreeW";
        Width = 560;
        Height = 420;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        AutomationProperties.SetName(this, "About FreeW");
        AutomationProperties.SetAutomationId(this, "AboutFreeWDialog");
        AutomationProperties.SetHelpText(this, "View version, license, privacy, and source information about FreeW.");

        _aboutTextBox = new TextBox
        {
            Text = FreeWAppInfo.AboutText,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            MinHeight = 220
        };
        AutomationProperties.SetName(_aboutTextBox, "About FreeW");
        AutomationProperties.SetAutomationId(_aboutTextBox, "AboutFreeWText");
        AutomationProperties.SetHelpText(_aboutTextBox, "Read-only FreeW version and license information.");

        Content = CreateContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private UIElement CreateContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        var buttonRow = DialogButtonRowFactory.CreateOkOnly(Close, buttonWidth: 84, rowMargin: new Thickness(0, 12, 0, 0));
        var ok = (Button)buttonRow.Children[0];
        AutomationProperties.SetAutomationId(ok, "AboutFreeWOkButton");
        AutomationProperties.SetHelpText(ok, "Close the About FreeW dialog.");
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);
        root.Children.Add(_aboutTextBox);

        return root;
    }

    private void FocusInitialKeyboardTarget()
    {
        _aboutTextBox.Focus();
        Keyboard.Focus(_aboutTextBox);
        _aboutTextBox.CaretIndex = 0;
    }
}
