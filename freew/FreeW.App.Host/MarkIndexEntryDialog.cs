using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>Thin WPF host for Word's References &gt; Mark Index Entry dialog.</summary>
internal sealed partial class MarkIndexEntryDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly MarkIndexEntryDialogSession _session;
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
    private MarkIndexEntryDialogResult? _result;

    private MarkIndexEntryDialog(
        Window? owner,
        MarkIndexEntryDialogState initialState,
        IReadOnlyList<string> bookmarkNames)
    {
        _session = new MarkIndexEntryDialogSession(initialState, bookmarkNames);
        initialState = _session.InitialState;
        bookmarkNames = _session.BookmarkNames;
        Owner = owner;
        Title = MarkIndexEntryDialogPlanner.Title;
        Width = MarkIndexEntryDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, MarkIndexEntryDialogPlanner.AutomationId);

        _mainEntry = CreateTextBox(initialState.MainEntry);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_mainEntry, MarkIndexEntryDialogPlanner.MainEntryAutomationId);
        _subentry = CreateTextBox(initialState.Subentry);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_subentry, MarkIndexEntryDialogPlanner.SubentryAutomationId);
        _identifier = CreateTextBox(initialState.Identifier);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_identifier, MarkIndexEntryDialogPlanner.IdentifierAutomationId);
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
        System.Windows.Automation.AutomationProperties.SetAutomationId(_bookmarkName, MarkIndexEntryDialogPlanner.BookmarkAutomationId);
        _crossReferenceOption = new RadioButton
        {
            Content = MarkIndexEntryDialogPlanner.CrossReferenceLabel,
            GroupName = "IndexEntryOption",
            IsChecked = initialState.ReferenceKind == IndexEntryReferenceKind.CrossReference,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.OptionBottomMargin)
        };
        _crossReference = CreateTextBox(initialState.CrossReference);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_crossReference, MarkIndexEntryDialogPlanner.CrossReferenceAutomationId);
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
        System.Windows.Automation.AutomationProperties.SetAutomationId(_status, MarkIndexEntryDialogPlanner.StatusAutomationId);

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
            IsEnabled = _session.PlanEnabledState(initialState.ReferenceKind).MarkAllEnabled,
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
        Loaded += (_, _) => DialogFocus.FocusAndSelect(_mainEntry);
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
        var enabled = _session.PlanEnabledState(ReferenceKind);
        _bookmarkName.IsEnabled = enabled.BookmarkSelectorEnabled;
        _crossReference.IsEnabled = enabled.CrossReferenceEnabled;
        _boldPageNumber.IsEnabled = enabled.PageNumberFormattingEnabled;
        _italicPageNumber.IsEnabled = enabled.PageNumberFormattingEnabled;
        _markAll.IsEnabled = enabled.MarkAllEnabled;
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
        var acceptance = _session.PlanAcceptance(CurrentState(), markAll);
        if (!acceptance.IsAccepted)
        {
            if (acceptance.Validation is not null)
            {
                _status.Text = acceptance.Validation.Message;
                _status.Visibility = Visibility.Visible;
            }
            return false;
        }

        _status.Visibility = Visibility.Collapsed;
        _result = acceptance.Result;
        if (closeOnSuccess)
            Close();
        return true;
    }

    public static MarkIndexEntryDialogResult? Prompt(
        Window? owner,
        MarkIndexEntryDialogState initialState,
        IReadOnlyList<string> bookmarkNames)
    {
        var dialog = new MarkIndexEntryDialog(owner, initialState, bookmarkNames);
        dialog.ShowDialog();
        return dialog._result;
    }
}
