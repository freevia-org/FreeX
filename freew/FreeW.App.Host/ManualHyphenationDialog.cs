using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

internal sealed partial class ManualHyphenationDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ManualHyphenationDialogSession _session;
    private readonly ComboBox _choices;
    private ManualHyphenationDialogResult? _result;

    private ManualHyphenationDialog(Window? owner, ManualHyphenationCandidate candidate)
    {
        var surface = ManualHyphenationPlanner.HostSurface;
        _session = new ManualHyphenationDialogSession(candidate);
        Owner = owner;
        Title = surface.Title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WpfDialogSurfaceSemantics.Apply(this, surface);

        _choices = new ComboBox
        {
            ItemsSource = _session.Options,
            DisplayMemberPath = nameof(ManualHyphenationOption.DisplayText),
            SelectedIndex = 0,
            MinWidth = 230
        };
        WpfDialogSurfaceSemantics.Apply(
            _choices,
            surface.Field(ManualHyphenationDialogField.Choices));

        var yes = new Button { Content = surface.Field(ManualHyphenationDialogField.Yes).Label, MinWidth = 72, IsDefault = true };
        yes.Click += (_, _) => Accept();
        var no = new Button { Content = surface.Field(ManualHyphenationDialogField.No).Label, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0) };
        no.Click += (_, _) => CloseWith(_session.PlanSkip());
        var cancelContent = ShellStrings.Current.Cancel;
        var cancel = new Button { Content = cancelContent, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        cancel.Click += (_, _) => CloseWith(_session.PlanCancel());
        WpfDialogSurfaceSemantics.Apply(yes, surface.Field(ManualHyphenationDialogField.Yes));
        WpfDialogSurfaceSemantics.Apply(no, surface.Field(ManualHyphenationDialogField.No));
        WpfDialogSurfaceSemantics.Apply(cancel, surface.Field(ManualHyphenationDialogField.Cancel));
        AutomationProperties.SetName(cancel, ShellStrings.Current.CreateAutomationName(cancelContent));
        var cancelAccelerator = ShellStringText.CreateAcceleratorKey(cancelContent);
        if (!string.IsNullOrEmpty(cancelAccelerator))
            AutomationProperties.SetAcceleratorKey(cancel, cancelAccelerator);

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
        content.Children.Add(new TextBlock { Text = _session.CandidateLabel, Margin = new Thickness(0, 0, 0, 4) });
        content.Children.Add(new TextBlock { Text = _session.Candidate.Word, FontWeight = FontWeights.SemiBold, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = surface.Field(ManualHyphenationDialogField.Choices).Label, Margin = new Thickness(0, 12, 0, 4) });
        content.Children.Add(_choices);
        content.Children.Add(buttons);
        Content = content;
    }

    private void Accept()
    {
        _result = _session.PlanAcceptance(_choices.SelectedIndex);
        if (_result is null)
            return;
        Close();
    }

    private void CloseWith(ManualHyphenationDialogResult result)
    {
        _result = result;
        Close();
    }

    public static ManualHyphenationDialogResult? Prompt(Window? owner, ManualHyphenationCandidate candidate)
    {
        var dialog = new ManualHyphenationDialog(owner, candidate);
        dialog.ShowDialog();
        return dialog._result;
    }
}
