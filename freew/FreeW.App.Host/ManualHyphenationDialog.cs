using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

internal sealed class ManualHyphenationDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ManualHyphenationDialogSession _session;
    private readonly ComboBox _choices;
    private ManualHyphenationDialogResult? _result;

    private ManualHyphenationDialog(Window? owner, ManualHyphenationCandidate candidate)
    {
        _session = new ManualHyphenationDialogSession(candidate);
        Owner = owner;
        Title = ManualHyphenationPlanner.Title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, ManualHyphenationPlanner.AutomationId);

        _choices = new ComboBox
        {
            ItemsSource = _session.Options,
            DisplayMemberPath = nameof(ManualHyphenationOption.DisplayText),
            SelectedIndex = 0,
            MinWidth = 230
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_choices, ManualHyphenationPlanner.ChoicesAutomationId);

        var yes = new Button { Content = ManualHyphenationPlanner.YesAccessLabel, MinWidth = 72, IsDefault = true };
        yes.Click += (_, _) => Accept();
        var no = new Button { Content = ManualHyphenationPlanner.NoAccessLabel, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0) };
        no.Click += (_, _) => CloseWith(_session.PlanSkip());
        var cancel = new Button { Content = ManualHyphenationPlanner.CancelLabel, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        cancel.Click += (_, _) => CloseWith(_session.PlanCancel());
        System.Windows.Automation.AutomationProperties.SetAutomationId(yes, ManualHyphenationPlanner.YesButtonAutomationId);
        System.Windows.Automation.AutomationProperties.SetAutomationId(no, ManualHyphenationPlanner.NoButtonAutomationId);
        System.Windows.Automation.AutomationProperties.SetAutomationId(cancel, ManualHyphenationPlanner.CancelButtonAutomationId);

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
        content.Children.Add(new TextBlock { Text = ManualHyphenationPlanner.HyphenateAtLabel, Margin = new Thickness(0, 12, 0, 4) });
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
