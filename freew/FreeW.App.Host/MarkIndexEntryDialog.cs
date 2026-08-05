using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>Thin WPF host for Word's References &gt; Mark Index Entry dialog.</summary>
internal sealed class MarkIndexEntryDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    internal sealed record Result(IndexMark Mark);

    private readonly TextBox _mainEntry;
    private readonly TextBox _subentry;
    private readonly RadioButton _currentPage;
    private readonly RadioButton _crossReferenceOption;
    private readonly TextBox _crossReference;
    private readonly CheckBox _boldPageNumber;
    private readonly CheckBox _italicPageNumber;
    private readonly TextBlock _status;
    private Result? _result;

    private MarkIndexEntryDialog(Window? owner, MarkIndexEntryDialogState initialState)
    {
        Owner = owner;
        Title = MarkIndexEntryDialogPlanner.Title;
        Width = MarkIndexEntryDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _mainEntry = CreateTextBox(initialState.MainEntry);
        _subentry = CreateTextBox(initialState.Subentry);
        _currentPage = new RadioButton
        {
            Content = MarkIndexEntryDialogPlanner.CurrentPageLabel,
            GroupName = "IndexEntryOption",
            IsChecked = !initialState.UseCrossReference,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.OptionBottomMargin)
        };
        _crossReferenceOption = new RadioButton
        {
            Content = MarkIndexEntryDialogPlanner.CrossReferenceLabel,
            GroupName = "IndexEntryOption",
            IsChecked = initialState.UseCrossReference,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.OptionBottomMargin)
        };
        _crossReference = CreateTextBox(initialState.CrossReference);
        _boldPageNumber = new CheckBox
        {
            Content = MarkIndexEntryDialogPlanner.BoldLabel,
            IsChecked = initialState.BoldPageNumber,
            Margin = new Thickness(0, 0, 12, MarkIndexEntryDialogPlanner.FieldBottomMargin)
        };
        _italicPageNumber = new CheckBox
        {
            Content = MarkIndexEntryDialogPlanner.ItalicLabel,
            IsChecked = initialState.ItalicPageNumber,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.FieldBottomMargin)
        };
        _currentPage.Checked += (_, _) => UpdateCrossReferenceState();
        _crossReferenceOption.Checked += (_, _) => UpdateCrossReferenceState();

        _status = new TextBlock
        {
            Foreground = Brushes.DarkRed,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.StatusBottomMargin),
            Visibility = Visibility.Collapsed
        };

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 80,
            acceptContent: MarkIndexEntryDialogPlanner.MarkButtonLabel,
            rowMargin: new Thickness(
                0,
                MarkIndexEntryDialogPlanner.ActionRowTopMargin,
                0,
                MarkIndexEntryDialogPlanner.ActionRowBottomMargin));
        var panel = new StackPanel
        {
            Margin = new Thickness(
                MarkIndexEntryDialogPlanner.ContentHorizontalMargin,
                MarkIndexEntryDialogPlanner.ContentTopMargin,
                MarkIndexEntryDialogPlanner.ContentHorizontalMargin,
                0)
        };
        panel.Children.Add(CreateLabel(MarkIndexEntryDialogPlanner.MainEntryLabel));
        panel.Children.Add(_mainEntry);
        panel.Children.Add(CreateLabel(MarkIndexEntryDialogPlanner.SubentryLabel));
        panel.Children.Add(_subentry);
        panel.Children.Add(CreateLabel(MarkIndexEntryDialogPlanner.OptionsLabel));
        panel.Children.Add(_currentPage);
        panel.Children.Add(_crossReferenceOption);
        panel.Children.Add(_crossReference);
        panel.Children.Add(CreateLabel(MarkIndexEntryDialogPlanner.PageNumberFormatLabel));
        var pageNumberFormat = new StackPanel { Orientation = Orientation.Horizontal };
        pageNumberFormat.Children.Add(_boldPageNumber);
        pageNumberFormat.Children.Add(_italicPageNumber);
        panel.Children.Add(pageNumberFormat);
        panel.Children.Add(_status);
        panel.Children.Add(buttons);
        Content = panel;

        UpdateCrossReferenceState();
        Loaded += (_, _) =>
        {
            _mainEntry.Focus();
            _mainEntry.SelectAll();
        };
    }

    private static TextBox CreateTextBox(string text) => new()
    {
        MinWidth = 320,
        Text = text,
        Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.FieldBottomMargin)
    };

    private static TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.LabelBottomMargin)
    };

    private void UpdateCrossReferenceState()
    {
        var useCrossReference = _crossReferenceOption.IsChecked == true;
        _crossReference.IsEnabled = useCrossReference;
        _boldPageNumber.IsEnabled = !useCrossReference;
        _italicPageNumber.IsEnabled = !useCrossReference;
    }

    private MarkIndexEntryDialogState CurrentState() => new(
        _mainEntry.Text ?? string.Empty,
        _subentry.Text ?? string.Empty,
        _crossReferenceOption.IsChecked == true,
        _crossReference.Text ?? string.Empty,
        _boldPageNumber.IsChecked == true,
        _italicPageNumber.IsChecked == true);

    private bool Accept(bool closeOnSuccess = true)
    {
        if (!MarkIndexEntryDialogPlanner.TryBuildMark(CurrentState(), out var mark, out var validation))
        {
            _status.Text = validation?.Message ?? MarkIndexEntryDialogPlanner.MissingMainEntryMessage;
            _status.Visibility = Visibility.Visible;
            return false;
        }

        _status.Visibility = Visibility.Collapsed;
        _result = new Result(mark!);
        if (closeOnSuccess)
            Close();
        return true;
    }

    private void Accept() => Accept(closeOnSuccess: true);

    internal static MarkIndexEntryDialog CreateForTest(string seed = "") =>
        new(null, MarkIndexEntryDialogPlanner.BuildInitialState(seed));

    internal void SetForTest(
        string? mainEntry,
        string? subentry,
        bool useCrossReference,
        string? crossReference,
        bool boldPageNumber = false,
        bool italicPageNumber = false)
    {
        _mainEntry.Text = mainEntry;
        _subentry.Text = subentry;
        _currentPage.IsChecked = !useCrossReference;
        _crossReferenceOption.IsChecked = useCrossReference;
        _crossReference.Text = crossReference;
        _boldPageNumber.IsChecked = boldPageNumber;
        _italicPageNumber.IsChecked = italicPageNumber;
        UpdateCrossReferenceState();
    }

    internal bool AcceptForTest() => Accept(closeOnSuccess: false);
    internal Result? ResultForTest => _result;
    internal bool CrossReferenceEnabledForTest => _crossReference.IsEnabled;
    internal bool PageNumberFormattingEnabledForTest => _boldPageNumber.IsEnabled && _italicPageNumber.IsEnabled;

    public static Result? Prompt(Window? owner, MarkIndexEntryDialogState initialState)
    {
        var dialog = new MarkIndexEntryDialog(owner, initialState);
        dialog.ShowDialog();
        return dialog._result;
    }
}
