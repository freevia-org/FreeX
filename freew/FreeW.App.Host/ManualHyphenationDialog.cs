using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

internal sealed class ManualHyphenationDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ComboBox _choices;
    private ManualHyphenationDialogResult? _result;

    private ManualHyphenationDialog(Window? owner, ManualHyphenationCandidate candidate)
    {
        Owner = owner;
        Title = "Manual Hyphenation";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _choices = new ComboBox
        {
            ItemsSource = candidate.Options,
            DisplayMemberPath = nameof(ManualHyphenationOption.DisplayText),
            SelectedIndex = 0,
            MinWidth = 230
        };

        var yes = new Button { Content = "_Yes", MinWidth = 72, IsDefault = true };
        yes.Click += (_, _) => Accept();
        var no = new Button { Content = "_No", MinWidth = 72, Margin = new Thickness(8, 0, 0, 0) };
        no.Click += (_, _) => CloseWith(ManualHyphenationDialogAction.Skip);

        // Cancel routes through the shared shell-strings pipeline (same source ShellStrings.Current
        // that DialogButtonRowFactory uses) so this dialog gets a localized "Annuler"/etc label and
        // the Alt+ accelerator every other WPF dialog's Cancel button gets, instead of a hardcoded
        // English literal. See DialogButtonRowFactoryLocalizationTests for the shared contract.
        var cancelContent = ShellStrings.Current.Cancel;
        var cancel = new Button { Content = cancelContent, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        AutomationProperties.SetName(cancel, ShellStrings.Current.CreateAutomationName(cancelContent));
        var cancelAccelerator = ShellStringText.CreateAcceleratorKey(cancelContent);
        if (!string.IsNullOrEmpty(cancelAccelerator))
            AutomationProperties.SetAcceleratorKey(cancel, cancelAccelerator);
        cancel.Click += (_, _) => CloseWith(ManualHyphenationDialogAction.Cancel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(new TextBlock { Text = $"Word {candidate.Number}", Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(new TextBlock { Text = candidate.Word, FontWeight = FontWeights.SemiBold, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = "Hyphenate at:", Margin = new Thickness(0, 12, 0, 4) });
        content.Children.Add(_choices);
        content.Children.Add(buttons);
        Content = content;
    }

    private void Accept()
    {
        if (_choices.SelectedItem is ManualHyphenationOption option)
            _result = new ManualHyphenationDialogResult(ManualHyphenationDialogAction.Accept, option.BreakPoint);
        Close();
    }

    private void CloseWith(ManualHyphenationDialogAction action)
    {
        _result = new ManualHyphenationDialogResult(action);
        Close();
    }

    public static ManualHyphenationDialogResult? Prompt(Window? owner, ManualHyphenationCandidate candidate)
    {
        var dialog = new ManualHyphenationDialog(owner, candidate);
        dialog.ShowDialog();
        return dialog._result;
    }
}
