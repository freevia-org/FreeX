using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public sealed class AboutDialog : Window
{
    private readonly TextBox _aboutTextBox;

    public AboutDialog()
    {
        Title = UiText.Get("MainWindowMessage_AboutFreeXTitle");
        Width = 560;
        Height = 420;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        AutomationProperties.SetName(this, UiText.Get("MainWindowMessage_AboutFreeXTitle"));
        AutomationProperties.SetAutomationId(this, "AboutFreeXDialog");
        AutomationProperties.SetHelpText(this, UiText.Get("MainWindow_TooltipDescription_ViewVersionAndLicenseInformationAboutFreeX"));

        _aboutTextBox = new TextBox
        {
            Text = AppInfo.AboutText,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            MinHeight = 220
        };
        AutomationProperties.SetName(_aboutTextBox, UiText.Get("MainWindowMessage_AboutFreeXTitle"));
        AutomationProperties.SetAutomationId(_aboutTextBox, "AboutFreeXText");
        AutomationProperties.SetHelpText(_aboutTextBox, UiText.Get("MainWindow_TooltipDescription_ViewVersionAndLicenseInformationAboutFreeX"));

        Content = CreateContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private UIElement CreateContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        var buttonRow = DialogButtonRowFactory.CreateOkOnly(
            Close,
            buttonWidth: 84,
            rowMargin: new Thickness(0, 12, 0, 0));
        if (buttonRow.Children[0] is Button okButton)
        {
            AutomationProperties.SetAutomationId(okButton, "AboutFreeXOkButton");
            AutomationProperties.SetHelpText(okButton, UiText.Get("MainWindow_TooltipDescription_ViewVersionAndLicenseInformationAboutFreeX"));
        }

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
