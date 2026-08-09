using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Thin WPF host for Word's References > Mark Citation dialog.
/// </summary>
internal sealed class MarkCitationDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    internal sealed record Result(Citation Citation);

    private readonly IReadOnlyList<MarkCitationCategoryChoice> _categoryChoices;
    private readonly ComboBox _categoryCombo;
    private readonly TextBox _longForm;
    private readonly TextBox _shortForm;
    private readonly TextBlock _status;
    private Result? _result;

    private MarkCitationDialog(Window? owner, MarkCitationDialogState initialState)
    {
        Owner = owner;
        Title = MarkCitationDialogPlanner.Title;
        Width = MarkCitationDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _categoryChoices = MarkCitationDialogPlanner.BuildCategoryChoices();
        _categoryCombo = new ComboBox { Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.FieldBottomMargin) };
        foreach (var choice in _categoryChoices)
            _categoryCombo.Items.Add(choice);
        _categoryCombo.SelectedIndex = MarkCitationDialogPlanner.SelectCategoryIndex(_categoryChoices, initialState.Category);

        _longForm = new TextBox
        {
            MinWidth = 320,
            Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.FieldBottomMargin),
            Text = initialState.LongCitation
        };
        _shortForm = new TextBox
        {
            MinWidth = 320,
            Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.FieldBottomMargin),
            Text = initialState.ShortCitation
        };
        _status = new TextBlock
        {
            Foreground = Brushes.DarkRed,
            Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.StatusBottomMargin),
            Visibility = Visibility.Collapsed
        };

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
        var category = _categoryCombo.SelectedIndex >= 0 && _categoryCombo.SelectedIndex < _categoryChoices.Count
            ? _categoryChoices[_categoryCombo.SelectedIndex].Category
            : CitationCategory.Cases;
        return new MarkCitationDialogState(
            category,
            _longForm.Text ?? string.Empty,
            _shortForm.Text ?? string.Empty);
    }

    private bool Accept(bool closeOnSuccess = true)
    {
        if (!MarkCitationDialogPlanner.TryBuildCitation(CurrentState(), out var citation, out var validation))
        {
            _status.Text = validation?.Message ?? MarkCitationDialogPlanner.MissingLongCitationMessage;
            _status.Visibility = Visibility.Visible;
            return false;
        }

        _status.Visibility = Visibility.Collapsed;
        _result = new Result(citation!);
        if (closeOnSuccess)
            Close();
        return true;
    }

    private void Accept() => Accept(closeOnSuccess: true);

    internal static MarkCitationDialog CreateForTest(
        string longCitation = "",
        CitationCategory category = CitationCategory.Cases,
        string shortCitation = "") =>
        new(null, new MarkCitationDialogState(category, longCitation, shortCitation));

    internal void SetForTest(CitationCategory category, string? longCitation, string? shortCitation)
    {
        _categoryCombo.SelectedIndex = MarkCitationDialogPlanner.SelectCategoryIndex(_categoryChoices, category);
        _longForm.Text = longCitation;
        _shortForm.Text = shortCitation;
    }

    internal bool AcceptForTest() =>
        Accept(closeOnSuccess: false);

    internal Result? ResultForTest => _result;

    public static Result? Prompt(Window? owner, MarkCitationDialogState initialState)
    {
        var dlg = new MarkCitationDialog(owner, initialState);
        dlg.ShowDialog();
        return dlg._result;
    }
}
