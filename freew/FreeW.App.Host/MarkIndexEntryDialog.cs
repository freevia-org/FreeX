using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>Thin WPF host for Word's References &gt; Mark Index Entry dialog.</summary>
internal sealed class MarkIndexEntryDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    internal sealed record Result(IndexMark Mark, bool MarkAll);

    private readonly TextBox _mainEntry;
    private readonly TextBox _subentry;
    private readonly TextBox _identifier;
    private readonly RadioButton _currentPage;
    private readonly RadioButton _pageRange;
    private readonly ComboBox _bookmarkName;
    private readonly RadioButton _crossReferenceOption;
    private readonly TextBox _crossReference;
    private readonly CheckBox _boldPageNumber;
    private readonly CheckBox _italicPageNumber;
    private readonly Button _markAll;
    private readonly TextBlock _status;
    private readonly string _selectedText;
    private Result? _result;

    private MarkIndexEntryDialog(
        Window? owner,
        MarkIndexEntryDialogState initialState,
        IReadOnlyList<string> bookmarkNames)
    {
        Owner = owner;
        Title = MarkIndexEntryDialogPlanner.Title;
        Width = MarkIndexEntryDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _selectedText = initialState.MainEntry;

        _mainEntry = CreateTextBox(initialState.MainEntry);
        _subentry = CreateTextBox(initialState.Subentry);
        _identifier = CreateTextBox(initialState.Identifier);
        _currentPage = new RadioButton
        {
            Content = MarkIndexEntryDialogPlanner.CurrentPageLabel,
            GroupName = "IndexEntryOption",
            IsChecked = initialState.ReferenceKind == IndexEntryReferenceKind.CurrentPage,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.OptionBottomMargin)
        };
        _pageRange = new RadioButton
        {
            Content = MarkIndexEntryDialogPlanner.PageRangeLabel,
            GroupName = "IndexEntryOption",
            IsChecked = initialState.ReferenceKind == IndexEntryReferenceKind.PageRange,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _bookmarkName = new ComboBox
        {
            MinWidth = 220,
            ItemsSource = bookmarkNames,
            SelectedItem = bookmarkNames.Contains(initialState.BookmarkName)
                ? initialState.BookmarkName
                : null,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.OptionBottomMargin)
        };
        _crossReferenceOption = new RadioButton
        {
            Content = MarkIndexEntryDialogPlanner.CrossReferenceLabel,
            GroupName = "IndexEntryOption",
            IsChecked = initialState.ReferenceKind == IndexEntryReferenceKind.CrossReference,
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
        _currentPage.Checked += (_, _) => UpdateReferenceState();
        _pageRange.Checked += (_, _) => UpdateReferenceState();
        _crossReferenceOption.Checked += (_, _) => UpdateReferenceState();

        _status = new TextBlock
        {
            Foreground = Brushes.DarkRed,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.StatusBottomMargin),
            Visibility = Visibility.Collapsed
        };

        var buttons = DialogButtonRowFactory.Create(
            () => Accept(markAll: false),
            buttonWidth: 80,
            acceptContent: MarkIndexEntryDialogPlanner.MarkButtonLabel,
            rowMargin: new Thickness(
                0,
                MarkIndexEntryDialogPlanner.ActionRowTopMargin,
                0,
                MarkIndexEntryDialogPlanner.ActionRowBottomMargin));
        _markAll = new Button
        {
            Content = MarkIndexEntryDialogPlanner.MarkAllButtonLabel,
            MinWidth = 80,
            IsEnabled = MarkIndexEntryDialogPlanner.CanMarkAll(initialState.MainEntry, initialState.ReferenceKind),
            Margin = new Thickness(0, 0, 8, 0)
        };
        _markAll.Click += (_, _) => Accept(markAll: true);
        buttons.Children.Insert(1, _markAll);
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
        panel.Children.Add(CreateLabel(MarkIndexEntryDialogPlanner.IdentifierLabel));
        panel.Children.Add(_identifier);
        panel.Children.Add(CreateLabel(MarkIndexEntryDialogPlanner.OptionsLabel));
        panel.Children.Add(_currentPage);
        var pageRange = new StackPanel { Orientation = Orientation.Horizontal };
        pageRange.Children.Add(_pageRange);
        pageRange.Children.Add(_bookmarkName);
        panel.Children.Add(pageRange);
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

        UpdateReferenceState();
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

    private IndexEntryReferenceKind ReferenceKind =>
        _pageRange.IsChecked == true
            ? IndexEntryReferenceKind.PageRange
            : _crossReferenceOption.IsChecked == true
                ? IndexEntryReferenceKind.CrossReference
                : IndexEntryReferenceKind.CurrentPage;

    private void UpdateReferenceState()
    {
        var referenceKind = ReferenceKind;
        _bookmarkName.IsEnabled = referenceKind == IndexEntryReferenceKind.PageRange;
        _crossReference.IsEnabled = referenceKind == IndexEntryReferenceKind.CrossReference;
        _boldPageNumber.IsEnabled = referenceKind != IndexEntryReferenceKind.CrossReference;
        _italicPageNumber.IsEnabled = referenceKind != IndexEntryReferenceKind.CrossReference;
        _markAll.IsEnabled = MarkIndexEntryDialogPlanner.CanMarkAll(_selectedText, referenceKind);
    }

    private MarkIndexEntryDialogState CurrentState() => new(
        _mainEntry.Text ?? string.Empty,
        _subentry.Text ?? string.Empty,
        _identifier.Text ?? string.Empty,
        ReferenceKind,
        _bookmarkName.SelectedItem as string ?? string.Empty,
        _crossReference.Text ?? string.Empty,
        _boldPageNumber.IsChecked == true,
        _italicPageNumber.IsChecked == true);

    private bool Accept(bool markAll, bool closeOnSuccess = true)
    {
        if (markAll && !MarkIndexEntryDialogPlanner.CanMarkAll(_selectedText, ReferenceKind))
            return false;

        if (!MarkIndexEntryDialogPlanner.TryBuildMark(CurrentState(), out var mark, out var validation))
        {
            _status.Text = validation?.Message ?? MarkIndexEntryDialogPlanner.MissingMainEntryMessage;
            _status.Visibility = Visibility.Visible;
            return false;
        }

        _status.Visibility = Visibility.Collapsed;
        _result = new Result(mark!, markAll);
        if (closeOnSuccess)
            Close();
        return true;
    }

    internal static MarkIndexEntryDialog CreateForTest(
        string seed = "",
        IReadOnlyList<string>? bookmarkNames = null) =>
        new(null, MarkIndexEntryDialogPlanner.BuildInitialState(seed), bookmarkNames ?? []);

    internal void SetForTest(
        string? mainEntry,
        string? subentry,
        bool useCrossReference,
        string? crossReference,
        bool boldPageNumber = false,
        bool italicPageNumber = false,
        string? identifier = null)
    {
        SetReferenceForTest(
            mainEntry,
            subentry,
            useCrossReference ? IndexEntryReferenceKind.CrossReference : IndexEntryReferenceKind.CurrentPage,
            bookmarkName: null,
            crossReference,
            boldPageNumber,
            italicPageNumber,
            identifier);
    }

    internal void SetReferenceForTest(
        string? mainEntry,
        string? subentry,
        IndexEntryReferenceKind referenceKind,
        string? bookmarkName,
        string? crossReference,
        bool boldPageNumber = false,
        bool italicPageNumber = false,
        string? identifier = null)
    {
        _mainEntry.Text = mainEntry;
        _subentry.Text = subentry;
        _identifier.Text = identifier;
        _currentPage.IsChecked = referenceKind == IndexEntryReferenceKind.CurrentPage;
        _pageRange.IsChecked = referenceKind == IndexEntryReferenceKind.PageRange;
        _crossReferenceOption.IsChecked = referenceKind == IndexEntryReferenceKind.CrossReference;
        _bookmarkName.SelectedItem = bookmarkName;
        _crossReference.Text = crossReference;
        _boldPageNumber.IsChecked = boldPageNumber;
        _italicPageNumber.IsChecked = italicPageNumber;
        UpdateReferenceState();
    }

    internal bool AcceptForTest() => Accept(markAll: false, closeOnSuccess: false);
    internal bool AcceptAllForTest() => Accept(markAll: true, closeOnSuccess: false);
    internal Result? ResultForTest => _result;
    internal bool CrossReferenceEnabledForTest => _crossReference.IsEnabled;
    internal bool BookmarkSelectorEnabledForTest => _bookmarkName.IsEnabled;
    internal IReadOnlyList<string> BookmarkNamesForTest => _bookmarkName.Items.Cast<string>().ToArray();
    internal bool PageNumberFormattingEnabledForTest => _boldPageNumber.IsEnabled && _italicPageNumber.IsEnabled;
    internal bool MarkAllEnabledForTest => _markAll.IsEnabled;

    public static Result? Prompt(
        Window? owner,
        MarkIndexEntryDialogState initialState,
        IReadOnlyList<string> bookmarkNames)
    {
        var dialog = new MarkIndexEntryDialog(owner, initialState, bookmarkNames);
        dialog.ShowDialog();
        return dialog._result;
    }
}
