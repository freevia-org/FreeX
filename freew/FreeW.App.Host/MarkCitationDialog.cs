using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Thin WPF host for Word's References > Mark Citation dialog.
/// </summary>
internal sealed partial class MarkCitationDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly MarkCitationDialogSession _session;
    private readonly ComboBox _categoryCombo;
    private readonly TextBox _longForm;
    private readonly TextBox _shortForm;
    private readonly TextBlock _status;
    private MarkCitationDialogResult? _result;

    private MarkCitationDialog(Window? owner, MarkCitationDialogState initialState)
    {
        _session = new MarkCitationDialogSession(initialState.LongCitation);
        Owner = owner;
        Title = MarkCitationDialogPlanner.Title;
        Width = MarkCitationDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, MarkCitationDialogPlanner.AutomationId);

        _categoryCombo = new ComboBox { Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.FieldBottomMargin) };
        foreach (var choice in _session.CategoryChoices)
            _categoryCombo.Items.Add(choice);
        _categoryCombo.SelectedIndex = _session.CategoryIndex(initialState.Category);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_categoryCombo, MarkCitationDialogPlanner.CategoryAutomationId);

        _longForm = new TextBox
        {
            MinWidth = 320,
            Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.FieldBottomMargin),
            Text = initialState.LongCitation
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_longForm, MarkCitationDialogPlanner.LongCitationAutomationId);
        _shortForm = new TextBox
        {
            MinWidth = 320,
            Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.FieldBottomMargin),
            Text = initialState.ShortCitation
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_shortForm, MarkCitationDialogPlanner.ShortCitationAutomationId);
        _status = new TextBlock
        {
            Foreground = Brushes.DarkRed,
            Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.StatusBottomMargin),
            Visibility = Visibility.Collapsed
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_status, MarkCitationDialogPlanner.StatusAutomationId);

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 80,
            acceptContent: MarkCitationDialogPlanner.MarkButtonLabel,
            rowMargin: new Thickness(0, MarkCitationDialogPlanner.ActionRowTopMargin, 0, MarkCitationDialogPlanner.ActionRowBottomMargin));

        var panel = new StackPanel
        {
            Margin = new Thickness(
                MarkCitationDialogPlanner.ContentHorizontalMargin,
                MarkCitationDialogPlanner.ContentTopMargin,
                MarkCitationDialogPlanner.ContentHorizontalMargin,
                0)
        };
        panel.Children.Add(MakeLabel(MarkCitationDialogPlanner.CategoryLabel));
        panel.Children.Add(_categoryCombo);
        panel.Children.Add(MakeLabel(MarkCitationDialogPlanner.LongCitationLabel));
        panel.Children.Add(_longForm);
        panel.Children.Add(MakeLabel(MarkCitationDialogPlanner.ShortCitationLabel));
        panel.Children.Add(_shortForm);
        panel.Children.Add(_status);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) => DialogFocus.FocusAndSelect(_longForm);
    }

    private static TextBlock MakeLabel(string text) =>
        new() { Text = text, Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.LabelBottomMargin) };

    private MarkCitationDialogState CurrentState()
    {
        var category = _categoryCombo.SelectedIndex >= 0 && _categoryCombo.SelectedIndex < _session.CategoryChoices.Count
            ? _session.CategoryChoices[_categoryCombo.SelectedIndex].Category
            : CitationCategory.Cases;
        return new MarkCitationDialogState(
            category,
            _longForm.Text ?? string.Empty,
            _shortForm.Text ?? string.Empty);
    }

    private bool Accept(bool closeOnSuccess = true)
    {
        var acceptance = _session.PlanAcceptance(CurrentState());
        if (!acceptance.IsAccepted)
        {
            _status.Text = acceptance.Validation?.Message ?? MarkCitationDialogPlanner.MissingLongCitationMessage;
            _status.Visibility = Visibility.Visible;
            return false;
        }

        _status.Visibility = Visibility.Collapsed;
        _result = acceptance.Result;
        if (closeOnSuccess)
            Close();
        return true;
    }

    private void Accept() => Accept(closeOnSuccess: true);

    public static MarkCitationDialogResult? Prompt(Window? owner, MarkCitationDialogState initialState)
    {
        var dlg = new MarkCitationDialog(owner, initialState);
        dlg.ShowDialog();
        return dlg._result;
    }
}
