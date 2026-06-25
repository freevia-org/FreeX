using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// Shared WPF About dialog. Each app passes its own title, about text, and automation-ID
/// suffixes so that per-app automation IDs (e.g. "AboutFreeXDialog" vs "AboutFreeWDialog")
/// are preserved exactly, keeping per-app UI tests green.
/// </summary>
public class SharedAboutDialog : Window
{
    private readonly TextBox _aboutTextBox;

    /// <param name="windowTitle">Window title string (e.g. "About FreeX").</param>
    /// <param name="aboutText">Body text shown in the read-only text box.</param>
    /// <param name="dialogAutomationId">AutomationId for the window (e.g. "AboutFreeXDialog").</param>
    /// <param name="textAutomationId">AutomationId for the text box (e.g. "AboutFreeXText").</param>
    /// <param name="okAutomationId">AutomationId for the OK button (e.g. "AboutFreeXOkButton").</param>
    /// <param name="helpText">AutomationHelpText applied to the window, text box, and OK button.</param>
    public SharedAboutDialog(
        string windowTitle,
        string aboutText,
        string dialogAutomationId,
        string textAutomationId,
        string okAutomationId,
        string helpText)
    {
        Title = windowTitle;
        Width = 560;
        Height = 420;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        AutomationProperties.SetName(this, windowTitle);
        AutomationProperties.SetAutomationId(this, dialogAutomationId);
        AutomationProperties.SetHelpText(this, helpText);

        _aboutTextBox = new TextBox
        {
            Text = aboutText,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            MinHeight = 220
        };
        AutomationProperties.SetName(_aboutTextBox, windowTitle);
        AutomationProperties.SetAutomationId(_aboutTextBox, textAutomationId);
        AutomationProperties.SetHelpText(_aboutTextBox, helpText);

        Content = CreateContent(okAutomationId, helpText);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private UIElement CreateContent(string okAutomationId, string helpText)
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        var buttonRow = DialogButtonRowFactory.CreateOkOnly(
            Close,
            buttonWidth: 84,
            rowMargin: new Thickness(0, 12, 0, 0));
        if (buttonRow.Children[0] is Button okButton)
        {
            AutomationProperties.SetAutomationId(okButton, okAutomationId);
            AutomationProperties.SetHelpText(okButton, helpText);
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
