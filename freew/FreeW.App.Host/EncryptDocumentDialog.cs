using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// File &gt; Info &gt; Protect Document &gt; Encrypt with Password. Two password boxes (enter and
/// re-enter) plus Word's "cannot be recovered" caution; all validation is
/// <see cref="EncryptDocumentDialogPlanner"/>'s. Returns null when cancelled.
/// </summary>
internal sealed class EncryptDocumentDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly PasswordBox _password = new() { MinWidth = 260 };
    private readonly PasswordBox _confirmation = new() { MinWidth = 260 };
    private readonly TextBlock _error = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0x26, 0x2C)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
        Visibility = Visibility.Collapsed
    };
    private readonly BackstageInfoSafetyText _text;
    private EncryptDocumentDecision? _result;

    private EncryptDocumentDialog(Window? owner, BackstageInfoSafetyText text)
    {
        _text = text;
        Owner = owner;
        Title = text.EncryptDialogTitle;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, EncryptDocumentDialogPlanner.WindowAutomationId);
        AutomationProperties.SetAutomationId(_password, EncryptDocumentDialogPlanner.PasswordAutomationId);
        AutomationProperties.SetAutomationId(_confirmation, EncryptDocumentDialogPlanner.ConfirmationAutomationId);
        AutomationProperties.SetAutomationId(_error, EncryptDocumentDialogPlanner.ErrorAutomationId);
        AutomationProperties.SetName(_password, text.EncryptDialogPrompt);
        AutomationProperties.SetName(_confirmation, text.EncryptDialogConfirmPrompt);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(Label(text.EncryptDialogPrompt, new Thickness(0, 0, 0, 6)));
        panel.Children.Add(_password);
        panel.Children.Add(Label(text.EncryptDialogConfirmPrompt, new Thickness(0, 10, 0, 6)));
        panel.Children.Add(_confirmation);
        panel.Children.Add(Label(text.EncryptDialogCaution, new Thickness(0, 10, 0, 0)));
        panel.Children.Add(_error);
        panel.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0)));

        Content = panel;
        Loaded += (_, _) => DialogFocus.Focus(_password);
    }

    private static TextBlock Label(string text, Thickness margin) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };

    private void Accept()
    {
        var decision = EncryptDocumentDialogPlanner.Plan(_password.Password, _confirmation.Password, _text);
        if (decision.Kind == EncryptDocumentDecisionKind.Invalid)
        {
            _error.Text = decision.Error;
            _error.Visibility = Visibility.Visible;
            _confirmation.Clear();
            DialogFocus.Focus(_confirmation);
            return;
        }

        _result = decision;
        Close();
    }

    public static EncryptDocumentDecision? Prompt(Window? owner, BackstageInfoSafetyText text)
    {
        var dialog = new EncryptDocumentDialog(owner, text);
        dialog.ShowDialog();
        return dialog._result;
    }
}
