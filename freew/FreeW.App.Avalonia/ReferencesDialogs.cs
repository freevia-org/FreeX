using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class CrossReferenceDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private static readonly CrossReferenceDialogVisualMetrics Layout =
        CrossReferenceDialogPlanner.VisualMetrics;

    private readonly CrossReferenceDialogSession _session;
    private readonly ListBox _typeList = new()
    {
        MinWidth = Layout.TypeListMinWidth,
        Height = Layout.ChoiceListHeight
    };
    private readonly ListBox _insertAsList = new()
    {
        MinWidth = Layout.InsertAsListMinWidth,
        Height = Layout.ChoiceListHeight
    };
    private readonly ListBox _targetList = new()
    {
        MinWidth = Layout.TargetListMinWidth,
        Height = Layout.TargetListHeight
    };
    private readonly CheckBox _hyperlinkBox = new()
    {
        Content = CrossReferenceDialogPlanner.HyperlinkLabel,
        IsChecked = true,
        Margin = new Thickness(0, Layout.HyperlinkTopMargin, 0, 0),
    };
    private bool _updatingControls;

    public CrossReferenceDialogChoice? Result { get; private set; }

    public CrossReferenceDialog(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _session = new CrossReferenceDialogSession(document);

        Title = CrossReferenceDialogPlanner.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, CrossReferenceDialogPlanner.AutomationId);

        _typeList.ItemsSource = _session.TypeChoices;
        _typeList.SelectedIndex = _session.State.TypeIndex;
        AutomationProperties.SetAutomationId(
            _typeList,
            CrossReferenceDialogPlanner.TypeAutomationId);
        AutomationProperties.SetAutomationId(
            _insertAsList,
            CrossReferenceDialogPlanner.InsertAsAutomationId);
        AutomationProperties.SetAutomationId(
            _targetList,
            CrossReferenceDialogPlanner.TargetAutomationId);
        AutomationProperties.SetAutomationId(
            _hyperlinkBox,
            CrossReferenceDialogPlanner.HyperlinkAutomationId);
        _typeList.SelectionChanged += (_, _) =>
        {
            if (_updatingControls)
                return;

            _session.UpdateType(_typeList.SelectedIndex);
            ApplySessionChoices(includeInsertAs: true);
        };
        _insertAsList.SelectionChanged += (_, _) =>
        {
            if (_updatingControls)
                return;

            _session.UpdateInsertAs(_insertAsList.SelectedIndex);
            ApplySessionChoices(includeInsertAs: false);
        };
        _targetList.SelectionChanged += (_, _) =>
        {
            if (!_updatingControls)
                _session.UpdateTarget(_targetList.SelectedIndex);
        };
        _targetList.DoubleTapped += async (_, _) => await AcceptAsync();
        _hyperlinkBox.IsCheckedChanged += (_, _) =>
            _session.UpdateHyperlink(_hyperlinkBox.IsChecked == true);

        AvaloniaCompactDialogChrome.ApplyListBox(_typeList, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyListBox(_insertAsList, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyListBox(_targetList, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_hyperlinkBox, DialogChromeStyle);
        foreach (var list in new[] { _typeList, _insertAsList, _targetList })
        {
            list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(Layoutable.HeightProperty, Layout.AvaloniaListItemHeight),
                    new Setter(Layoutable.MinHeightProperty, Layout.AvaloniaListItemHeight),
                    new Setter(Layoutable.MaxHeightProperty, Layout.AvaloniaListItemHeight),
                },
            });
        }
        _insertAsList.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(
                    TemplatedControl.BackgroundProperty,
                    new ImmutableSolidColorBrush(Color.Parse(Layout.AvaloniaInactiveSelectionBackgroundHex))),
                new Setter(
                    TemplatedControl.BorderBrushProperty,
                    new ImmutableSolidColorBrush(Color.Parse(Layout.AvaloniaInactiveSelectionBorderHex))),
            },
        });

        ApplySessionChoices(includeInsertAs: true);

        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, Layout.TopRowBottomMargin) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Layout.ColumnSpacing) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.Children.Add(LabeledColumn(CrossReferenceDialogPlanner.ReferenceTypeLabel, _typeList, 0));
        topGrid.Children.Add(LabeledColumn(CrossReferenceDialogPlanner.InsertReferenceToLabel, _insertAsList, 2));

        var targetColumn = LabeledColumn(CrossReferenceDialogPlanner.TargetLabel, _targetList, -1);

        var actionPlans = CrossReferenceDialogPlanner.ActionButtons;
        var ok = Button(actionPlans[0].Label, click: null, isDefault: actionPlans[0].IsDefault);
        AvaloniaCompactDialogChrome.ApplyNeutralDefaultButtonChrome(ok);
        ok.Click += async (_, _) => await AcceptAsync();
        var cancel = Button(actionPlans[1].Label, () => Close(), isCancel: actionPlans[1].IsCancel);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(0, Layout.ActionRowTopMargin, 0, 0));

        var body = new StackPanel { Margin = new Thickness(Layout.OuterMargin) };
        body.Children.Add(topGrid);
        body.Children.Add(_hyperlinkBox);
        body.Children.Add(targetColumn);
        body.Children.Add(buttons);
        Content = body;
        Opened += (_, _) =>
        {
            _typeList.Focus();
            AvaloniaCompactDialogChrome.ApplyNeutralDefaultButtonChrome(ok);
        };
    }

    private void ApplySessionChoices(bool includeInsertAs)
    {
        _updatingControls = true;
        try
        {
            if (includeInsertAs)
            {
                _insertAsList.ItemsSource = _session.InsertAsChoices;
                _insertAsList.SelectedIndex = _session.State.InsertAsIndex;
            }

            _targetList.ItemsSource = _session.TargetChoices;
            _targetList.SelectedIndex = _session.State.TargetIndex;
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private async Task AcceptAsync()
    {
        _session.UpdateInsertAs(_insertAsList.SelectedIndex);
        _session.UpdateTarget(_targetList.SelectedIndex);
        _session.UpdateHyperlink(_hyperlinkBox.IsChecked == true);
        var acceptance = _session.PlanAcceptance();
        if (!acceptance.IsAccepted)
        {
            await AvaloniaUserMessageDialog.ShowWarningAsync(
                this,
                acceptance.ValidationMessage ?? CrossReferenceDialogPlanner.MissingTargetMessage,
                CrossReferenceDialogPlanner.Title);
            return;
        }

        Result = acceptance.Result;
        Close();
    }

    private static StackPanel LabeledColumn(string label, Control control, int column)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, Layout.LabelTopMargin, 0, Layout.LabelBottomMargin)
        });
        stack.Children.Add(control);
        if (column >= 0)
            Grid.SetColumn(stack, column);
        return stack;
    }

    private static Button Button(string label, Action? click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            minWidth: Layout.ActionButtonWidth,
            isDefault: isDefault);
        if (click is not null)
            button.Click += (_, _) => click();
        return button;
    }
}

internal sealed class SourceConflictResolutionDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private SourceManagementSourceConflictResolutionAction? _result;

    private SourceConflictResolutionDialog(SourceManagementSourceConflict conflict)
    {
        var text = SourceManagementDialogPlanner.ResolveText(UiText.Get);
        var choices = SourceManagementDialogPlanner.BuildSourceConflictResolutionChoices(conflict, text);

        Title = text.SourceConflictDialogTitle;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var message = new TextBlock
        {
            Text = SourceManagementDialogPlanner.BuildSourceConflictMessage(conflict, text),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 14, 16, 0),
        };

        var buttons = choices
            .Select(choice => Button(choice.Label, () =>
            {
                _result = choice.Action;
                Close();
            }))
            .Append(Button(text.CancelButtonLabel, () => Close(), isCancel: true))
            .ToArray();

        var body = new StackPanel();
        body.Children.Add(message);
        body.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(buttons, new Thickness(16, 12, 16, 14)));
        Content = body;
    }

    public static async Task<SourceManagementSourceConflictResolutionAction?> AskAsync(
        Window owner,
        SourceManagementSourceConflict conflict)
    {
        var dialog = new SourceConflictResolutionDialog(conflict);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 84, isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}

internal sealed class CitationSourcePickerDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly IReadOnlyList<Source> _sources;
    private readonly ListBox _sourceList = new() { MinWidth = 340, Height = 160 };
    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.DarkRed,
        Margin = new Thickness(16, 6, 16, 0),
        IsVisible = false,
    };

    public SourceManagementPick? Pick { get; private set; }

    public CitationSourcePickerDialog(IReadOnlyList<Source> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;
        var text = SourceManagementDialogPlanner.ResolveText(UiText.Get);
        _status.Text = text.SelectSourceValidationMessage;

        Title = text.SourcePickerTitle;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _sourceList.ItemsSource = SourceManagementDialogPlanner.BuildPickerItems(sources);
        _sourceList.SelectedIndex = sources.Count > 0 ? 0 : -1;
        AvaloniaCompactDialogChrome.ApplyListBox(_sourceList, DialogChromeStyle);

        var label = new TextBlock
        {
            Text = text.SourcePickerLabel,
            Margin = new Thickness(16, 12, 16, 4),
        };
        var listHost = new Border { Margin = new Thickness(16, 0, 16, 0), Child = _sourceList };

        var addNew = Button(text.AddNewSourceButtonLabel, () =>
        {
            Pick = SourceManagementDialogPlanner.CreateAddNewPick();
            Close();
        });
        var insert = Button(text.InsertButtonLabel, Accept, isDefault: true);
        var cancel = Button(text.CancelButtonLabel, () => Close(), isCancel: true);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([addNew, insert, cancel], new Thickness(16, 12, 16, 14));

        var body = new StackPanel();
        body.Children.Add(label);
        body.Children.Add(listHost);
        body.Children.Add(_status);
        body.Children.Add(buttons);
        Content = body;
    }

    private void Accept()
    {
        if (!SourceManagementDialogPlanner.TryCreatePick(_sources, _sourceList.SelectedIndex, out var pick))
        {
            _status.IsVisible = true;
            return;
        }

        Pick = pick;
        Close();
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 84, isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}

internal sealed partial class MarkIndexEntryDialog : FreeWDialogWindow
{
    // The compact toggle templates already provide the WPF-sized indicators, so this row needs
    // less separation than the standard action-row contract to retain the WPF dialog geometry.
    private const double ActionRowTopMargin = 4;
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle with
    {
        // WPF's native radio row is effectively 16 DIP. This dialog stacks two standalone
        // radio rows, so the generic 20-DIP compact row still leaves its action row low.
        CompactRadioButtonHeight = 16,
    };

    private readonly MarkIndexEntryDialogSession _session;
    private readonly TextBox _mainEntry = new() { MinWidth = 300 };
    private readonly TextBox _subentry = new() { MinWidth = 300 };
    private readonly TextBox _identifier = new() { MinWidth = 300 };
    private readonly RadioButton _currentPage = new()
    {
        Content = MarkIndexEntryDialogPlanner.CurrentPageLabel,
        GroupName = "IndexEntryOption"
    };
    private readonly RadioButton _pageRange = new()
    {
        Content = MarkIndexEntryDialogPlanner.PageRangeLabel,
        GroupName = "IndexEntryOption"
    };
    private readonly ComboBox _bookmark = new() { MinWidth = 220 };
    private readonly RadioButton _crossReferenceOption = new()
    {
        Content = MarkIndexEntryDialogPlanner.CrossReferenceLabel,
        GroupName = "IndexEntryOption"
    };
    private readonly TextBox _crossReference = new() { MinWidth = 300 };
    private readonly CheckBox _boldPageNumber = new() { Content = MarkIndexEntryDialogPlanner.BoldLabel };
    private readonly CheckBox _italicPageNumber = new() { Content = MarkIndexEntryDialogPlanner.ItalicLabel };
    private readonly Button _markAll;
    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.DarkRed,
        Margin = new Thickness(16, 6, 16, 0),
        IsVisible = false
    };

    public IndexMark? Mark { get; private set; }
    public bool MarkAll { get; private set; }

    public MarkIndexEntryDialog(string? selectedText = null, IReadOnlyList<string>? bookmarkNames = null)
    {
        _session = new MarkIndexEntryDialogSession(selectedText, bookmarkNames);
        Title = MarkIndexEntryDialogPlanner.Title;
        Width = MarkIndexEntryDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, MarkIndexEntryDialogPlanner.AutomationId);

        var state = _session.InitialState;
        _mainEntry.Text = state.MainEntry;
        _subentry.Text = state.Subentry;
        _identifier.Text = state.Identifier;
        _currentPage.IsChecked = state.ReferenceKind == IndexEntryReferenceKind.CurrentPage;
        _pageRange.IsChecked = state.ReferenceKind == IndexEntryReferenceKind.PageRange;
        _crossReferenceOption.IsChecked = state.ReferenceKind == IndexEntryReferenceKind.CrossReference;
        var bookmarks = _session.BookmarkNames;
        _bookmark.ItemsSource = bookmarks;
        _bookmark.SelectedItem = bookmarks.FirstOrDefault(name =>
            string.Equals(name, state.BookmarkName, StringComparison.Ordinal));
        if (_bookmark.SelectedItem is null && bookmarks.Count > 0)
            _bookmark.SelectedIndex = 0;
        _crossReference.Text = state.CrossReference;
        _boldPageNumber.IsChecked = state.BoldPageNumber;
        _italicPageNumber.IsChecked = state.ItalicPageNumber;
        AutomationProperties.SetAutomationId(_mainEntry, MarkIndexEntryDialogPlanner.MainEntryAutomationId);
        AutomationProperties.SetAutomationId(_subentry, MarkIndexEntryDialogPlanner.SubentryAutomationId);
        AutomationProperties.SetAutomationId(_identifier, MarkIndexEntryDialogPlanner.IdentifierAutomationId);
        AutomationProperties.SetAutomationId(_bookmark, MarkIndexEntryDialogPlanner.BookmarkAutomationId);
        AutomationProperties.SetAutomationId(_crossReference, MarkIndexEntryDialogPlanner.CrossReferenceAutomationId);
        AutomationProperties.SetAutomationId(_status, MarkIndexEntryDialogPlanner.StatusAutomationId);
        foreach (var textBox in new[] { _mainEntry, _subentry, _identifier, _crossReference })
            AvaloniaCompactDialogChrome.ApplyTextBox(textBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_bookmark, DialogChromeStyle);
        // This compact reference dialog uses WPF's 13px native toggle indicators. The Fluent
        // defaults consume a full control row for each choice and gradually push the action row
        // down; use the shared compact templates so its geometry stays aligned with the WPF host.
        AvaloniaCompactDialogChrome.ApplyCompactRadioButton(_currentPage, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactRadioButton(_pageRange, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactRadioButton(_crossReferenceOption, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_boldPageNumber, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_italicPageNumber, DialogChromeStyle);
        _currentPage.IsCheckedChanged += (_, _) => UpdateCrossReferenceState();
        _pageRange.IsCheckedChanged += (_, _) => UpdateCrossReferenceState();
        _crossReferenceOption.IsCheckedChanged += (_, _) => UpdateCrossReferenceState();

        var fields = new StackPanel
        {
            Margin = new Thickness(
                MarkIndexEntryDialogPlanner.ContentHorizontalMargin,
                MarkIndexEntryDialogPlanner.ContentTopMargin,
                MarkIndexEntryDialogPlanner.ContentHorizontalMargin,
                0)
        };
        AddLabeledField(fields, MarkIndexEntryDialogPlanner.MainEntryLabel, _mainEntry);
        AddLabeledField(fields, MarkIndexEntryDialogPlanner.SubentryLabel, _subentry);
        AddLabeledField(fields, MarkIndexEntryDialogPlanner.IdentifierLabel, _identifier);
        fields.Children.Add(new TextBlock
        {
            Text = MarkIndexEntryDialogPlanner.OptionsLabel,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.LabelBottomMargin)
        });
        _currentPage.Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.OptionBottomMargin);
        _pageRange.Margin = new Thickness(0, 0, 8, 0);
        _pageRange.VerticalAlignment = VerticalAlignment.Center;
        _crossReferenceOption.Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.OptionBottomMargin);
        fields.Children.Add(_currentPage);
        _bookmark.Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.OptionBottomMargin);
        fields.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _pageRange, _bookmark }
        });
        fields.Children.Add(_crossReferenceOption);
        _crossReference.Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.FieldBottomMargin);
        fields.Children.Add(_crossReference);
        fields.Children.Add(new TextBlock
        {
            Text = MarkIndexEntryDialogPlanner.PageNumberFormatLabel,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.LabelBottomMargin)
        });
        _boldPageNumber.Margin = new Thickness(0, 0, 12, MarkIndexEntryDialogPlanner.FieldBottomMargin);
        _italicPageNumber.Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.FieldBottomMargin);
        fields.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _boldPageNumber, _italicPageNumber }
        });

        var markButton = Button(MarkIndexEntryDialogPlanner.MarkButtonLabel, () => Accept(markAll: false), isDefault: true);
        _markAll = Button(MarkIndexEntryDialogPlanner.MarkAllButtonLabel, () => Accept(markAll: true));
        var cancelButton = Button(MarkIndexEntryDialogPlanner.CancelButtonLabel, () => Close(), isCancel: true);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [markButton, _markAll, cancelButton],
            new Thickness(
                MarkIndexEntryDialogPlanner.ContentHorizontalMargin,
                ActionRowTopMargin,
                MarkIndexEntryDialogPlanner.ContentHorizontalMargin,
                MarkIndexEntryDialogPlanner.ActionRowBottomMargin));

        var body = new StackPanel();
        body.Children.Add(fields);
        body.Children.Add(_status);
        body.Children.Add(buttons);
        Content = body;
        UpdateCrossReferenceState();
        Opened += (_, _) =>
        {
            _mainEntry.Focus();
            _mainEntry.SelectAll();
        };
    }

    private IndexEntryReferenceKind ReferenceKind =>
        _pageRange.IsChecked == true
            ? IndexEntryReferenceKind.PageRange
            : _crossReferenceOption.IsChecked == true
                ? IndexEntryReferenceKind.CrossReference
                : IndexEntryReferenceKind.CurrentPage;

    private MarkIndexEntryDialogState CurrentState() => new(
        _mainEntry.Text ?? string.Empty,
        _subentry.Text ?? string.Empty,
        _identifier.Text ?? string.Empty,
        ReferenceKind,
        _bookmark.SelectedItem as string ?? string.Empty,
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
                _status.IsVisible = true;
            }
            return false;
        }

        _status.IsVisible = false;
        Mark = acceptance.Result!.Mark;
        MarkAll = acceptance.Result.MarkAll;
        if (closeOnSuccess)
            Close();
        return true;
    }

    private void UpdateCrossReferenceState()
    {
        var enabled = _session.PlanEnabledState(ReferenceKind);
        _bookmark.IsEnabled = enabled.BookmarkSelectorEnabled;
        _crossReference.IsEnabled = enabled.CrossReferenceEnabled;
        _boldPageNumber.IsEnabled = enabled.PageNumberFormattingEnabled;
        _italicPageNumber.IsEnabled = enabled.PageNumberFormattingEnabled;
        _markAll.IsEnabled = enabled.MarkAllEnabled;
    }

    private static void AddLabeledField(StackPanel panel, string label, Control field)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.LabelBottomMargin)
        });
        field.Margin = new Thickness(0, 0, 0, MarkIndexEntryDialogPlanner.FieldBottomMargin);
        panel.Children.Add(field);
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 84, isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}

internal sealed partial class MarkCitationDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly MarkCitationDialogSession _session;
    private readonly ComboBox _categoryBox = new() { MinWidth = 300 };
    private readonly TextBox _longCitationBox = new() { MinWidth = 300 };
    private readonly TextBox _shortCitationBox = new() { MinWidth = 300 };
    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.DarkRed,
        Margin = new Thickness(16, 6, 16, 0),
        IsVisible = false,
    };

    public Citation? Citation { get; private set; }

    public MarkCitationDialog(string? seedLongCitation = null)
    {
        _session = new MarkCitationDialogSession(seedLongCitation);
        Title = MarkCitationDialogPlanner.Title;
        Width = MarkCitationDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        AutomationProperties.SetAutomationId(this, MarkCitationDialogPlanner.AutomationId);

        var state = _session.InitialState;

        _categoryBox.ItemsSource = _session.CategoryChoices;
        _categoryBox.SelectedIndex = _session.CategoryIndex(state.Category);
        _longCitationBox.Text = state.LongCitation;
        _shortCitationBox.Text = state.ShortCitation;
        AutomationProperties.SetAutomationId(_categoryBox, MarkCitationDialogPlanner.CategoryAutomationId);
        AutomationProperties.SetAutomationId(_longCitationBox, MarkCitationDialogPlanner.LongCitationAutomationId);
        AutomationProperties.SetAutomationId(_shortCitationBox, MarkCitationDialogPlanner.ShortCitationAutomationId);
        AutomationProperties.SetAutomationId(_status, MarkCitationDialogPlanner.StatusAutomationId);

        AvaloniaCompactDialogChrome.ApplyComboBox(_categoryBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_longCitationBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_shortCitationBox, DialogChromeStyle);

        var fields = new StackPanel
        {
            Margin = new Thickness(
                MarkCitationDialogPlanner.ContentHorizontalMargin,
                MarkCitationDialogPlanner.ContentTopMargin,
                MarkCitationDialogPlanner.ContentHorizontalMargin,
                0)
        };
        AddLabeledField(fields, MarkCitationDialogPlanner.CategoryLabel, _categoryBox);
        AddLabeledField(fields, MarkCitationDialogPlanner.LongCitationLabel, _longCitationBox);
        AddLabeledField(fields, MarkCitationDialogPlanner.ShortCitationLabel, _shortCitationBox);

        var mark = Button(MarkCitationDialogPlanner.MarkButtonLabel, () => Accept(), isDefault: true);
        var cancel = Button(MarkCitationDialogPlanner.CancelButtonLabel, () => Close(), isCancel: true);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [mark, cancel],
            new Thickness(
                MarkCitationDialogPlanner.ContentHorizontalMargin,
                MarkCitationDialogPlanner.ActionRowTopMargin,
                MarkCitationDialogPlanner.ContentHorizontalMargin,
                MarkCitationDialogPlanner.ActionRowBottomMargin));

        var body = new StackPanel();
        body.Children.Add(fields);
        body.Children.Add(_status);
        body.Children.Add(buttons);
        Content = body;

        Opened += (_, _) =>
        {
            _longCitationBox.Focus();
            _longCitationBox.SelectAll();
        };
    }

    private MarkCitationDialogState CurrentState()
    {
        var category = _categoryBox.SelectedIndex >= 0 && _categoryBox.SelectedIndex < _session.CategoryChoices.Count
            ? _session.CategoryChoices[_categoryBox.SelectedIndex].Category
            : CitationCategory.Cases;
        return new MarkCitationDialogState(
            category,
            _longCitationBox.Text ?? string.Empty,
            _shortCitationBox.Text ?? string.Empty);
    }

    private bool Accept(bool closeOnSuccess = true)
    {
        var acceptance = _session.PlanAcceptance(CurrentState());
        if (!acceptance.IsAccepted)
        {
            _status.Text = acceptance.Validation?.Message ?? MarkCitationDialogPlanner.MissingLongCitationMessage;
            _status.IsVisible = true;
            return false;
        }

        _status.IsVisible = false;
        Citation = acceptance.Result!.Citation;
        if (closeOnSuccess)
            Close();
        return true;
    }

    private static void AddLabeledField(StackPanel panel, string label, Control field)
    {
        var text = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.LabelBottomMargin),
        };
        field.Margin = new Thickness(0, 0, 0, MarkCitationDialogPlanner.FieldBottomMargin);
        panel.Children.Add(text);
        panel.Children.Add(field);
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 84, isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}

internal sealed class SourceEntryDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly IReadOnlyList<SourceManagementSourceTypeChoice> _typeChoices;
    private readonly ComboBox _typeBox = new() { MinWidth = 260 };
    private readonly Grid _grid = new() { Margin = new Thickness(16, 12, 16, 0) };
    private readonly Dictionary<SourceManagementSourceField, TextBox> _fields;
    private SourceManagementSourceEntry _entryBaseline;

    public SourceManagementSourceEntry? Entry { get; private set; }

    public SourceEntryDialog(Source? source = null)
    {
        Title = source is null
            ? SourceManagementDialogPlanner.AddNewSourceTitle
            : SourceManagementDialogPlanner.EditSourceTitle;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _typeChoices = SourceManagementDialogPlanner.BuildSourceTypeChoices();
        var entry = SourceManagementDialogPlanner.ProjectEntry(source);
        _entryBaseline = entry;
        _fields = SourceManagementDialogPlanner
            .BuildEntryFieldPlans(entry)
            .ToDictionary(plan => plan.Field, plan => NewField(plan.Text));

        _typeBox.ItemsSource = _typeChoices;
        _typeBox.SelectedIndex = SourceManagementDialogPlanner.SourceTypeSelectedIndex(entry.Type);
        _typeBox.SelectionChanged += (_, _) => RefreshFields();
        AvaloniaCompactDialogChrome.ApplyComboBox(_typeBox, DialogChromeStyle);

        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RefreshFields();

        var ok = Button("OK", Accept, isDefault: true);
        var cancel = Button("Cancel", () => Close(), isCancel: true);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 12, 16, 14));

        var body = new StackPanel();
        body.Children.Add(_grid);
        body.Children.Add(buttons);
        Content = body;
    }

    private void Accept()
    {
        Entry = CurrentEntry();
        Close();
    }

    private SourceType SelectedType =>
        _typeBox.SelectedIndex >= 0 && _typeBox.SelectedIndex < _typeChoices.Count
            ? _typeChoices[_typeBox.SelectedIndex].Type
            : SourceType.Book;

    private SourceManagementSourceEntry CurrentEntry() =>
        SourceManagementDialogPlanner.CreateEntry(
            SelectedType,
            _fields.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.Text),
            _entryBaseline);

    private async Task EditPrimaryAuthorAsync()
    {
        var current = CurrentEntry();
        var dialog = new SourceAuthorEditorDialog(current);
        await dialog.ShowDialog(this);
        if (dialog.State is null)
            return;

        _entryBaseline = SourceManagementDialogPlanner.ApplyPrimaryAuthorEditorState(current, dialog.State);
        if (!_fields.TryGetValue(SourceManagementSourceField.Author, out var authorField))
        {
            authorField = NewField();
            _fields[SourceManagementSourceField.Author] = authorField;
        }

        authorField.Text = _entryBaseline.Author;
        RefreshFields();
        authorField.Focus();
    }

    private void RefreshFields()
    {
        _grid.RowDefinitions.Clear();
        _grid.Children.Clear();
        AddLabeledRow(_grid, 0, SourceManagementDialogPlanner.SourceTypeLabel, _typeBox);

        var row = 1;
        foreach (var plan in SourceManagementDialogPlanner.BuildEntryFieldPlans(CurrentEntry()))
        {
            if (!_fields.TryGetValue(plan.Field, out var field))
            {
                field = NewField(plan.Text);
                _fields[plan.Field] = field;
            }

            AddLabeledRow(
                _grid,
                row++,
                plan.Label,
                plan.Field == SourceManagementSourceField.Author
                    ? CreateAuthorField(field)
                    : field);
        }
    }

    private Control CreateAuthorField(TextBox field)
    {
        var edit = new Button
        {
            Content = SourceManagementDialogPlanner.PrimaryAuthorEditorButtonLabel,
            Margin = new Thickness(0, 6, 0, 0),
        };
        AvaloniaCompactDialogChrome.ApplyButton(edit, DialogChromeStyle, minWidth: 32);
        ToolTip.SetTip(edit, SourceManagementDialogPlanner.PrimaryAuthorEditorButtonToolTip);
        edit.Click += (_, _) => _ = EditPrimaryAuthorAsync();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        row.Children.Add(field);
        row.Children.Add(edit);
        return row;
    }

    private static TextBox NewField(string? value = null)
    {
        var box = new TextBox
        {
            Text = value ?? string.Empty,
            MinWidth = 260,
            Margin = new Thickness(0, 6, 0, 0),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static void AddLabeledRow(Grid grid, int row, string label, Control field)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 8, 0),
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 84, isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}

internal sealed class SourceAuthorEditorDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            ActionSpacing = SourceManagementAuthorEditorVisualMetrics.ActionSpacing,
        };

    private sealed record RowControls(
        TextBox First,
        TextBox Middle,
        TextBox Last,
        Grid Host);

    public SourceManagementAuthorEditorState? State { get; private set; }

    public SourceAuthorEditorDialog(SourceManagementSourceEntry entry)
    {
        var session = new SourceManagementAuthorEditorSession(entry);
        var initial = session.CurrentPlan;
        var text = SourceManagementDialogPlanner.ResolveText(UiText.Get);

        Title = SourceManagementDialogPlanner.PrimaryAuthorEditorTitle;
        Width = SourceManagementAuthorEditorVisualMetrics.AvaloniaWindowWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var personalMode = new RadioButton
        {
            Content = SourceManagementDialogPlanner.PersonalAuthorModeLabel,
            GroupName = "PrimaryAuthorMode",
            IsChecked = initial.Mode == SourceManagementAuthorEditorMode.Personal,
            Margin = new Thickness(
                0,
                0,
                0,
                SourceManagementAuthorEditorVisualMetrics.PersonalModeBottomMargin),
        };
        var corporateMode = new RadioButton
        {
            Content = SourceManagementDialogPlanner.CorporateAuthorModeLabel,
            GroupName = "PrimaryAuthorMode",
            IsChecked = initial.Mode == SourceManagementAuthorEditorMode.Corporate,
            Margin = new Thickness(
                0,
                SourceManagementAuthorEditorVisualMetrics.CorporateModeTopMargin,
                0,
                SourceManagementAuthorEditorVisualMetrics.PersonalModeBottomMargin),
        };
        AvaloniaCompactDialogChrome.ApplyRadioButton(personalMode, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(corporateMode, DialogChromeStyle);

        var peoplePanel = new StackPanel
        {
            Margin = new Thickness(
                SourceManagementAuthorEditorVisualMetrics.PeoplePanelIndent,
                0,
                0,
                0),
        };
        var rowsPanel = new StackPanel();
        var corporateLabel = new TextBlock
        {
            Text = SourceManagementDialogPlanner.CorporateAuthorLabel,
            Margin = new Thickness(
                SourceManagementAuthorEditorVisualMetrics.PeoplePanelIndent,
                0,
                0,
                SourceManagementAuthorEditorVisualMetrics.CorporateLabelBottomMargin),
        };
        var corporateBox = NewAuthorTextBox(
            initial.CorporateAuthor,
            minWidth: SourceManagementAuthorEditorVisualMetrics.CorporateFieldMinimumWidth);

        var personRows = new SourceManagementAuthorRowCollection<RowControls>(
            row =>
            {
                var grid = CreatePersonRowGrid();
                var first = NewAuthorTextBox(row.First);
                var middle = NewAuthorTextBox(row.Middle);
                var last = NewAuthorTextBox(
                    row.Last,
                    minWidth: SourceManagementAuthorEditorVisualMetrics.LastNameFieldMinimumWidth);
                AddGridChild(grid, first, 0);
                AddGridChild(grid, middle, 1);
                AddGridChild(grid, last, 2);
                return new RowControls(first, middle, last, grid);
            },
            row => new SourceManagementAuthorPersonRow(
                row.First.Text ?? string.Empty,
                row.Middle.Text ?? string.Empty,
                row.Last.Text ?? string.Empty),
            row => rowsPanel.Children.Add(row.Host),
            () => rowsPanel.Children.Clear());

        void ApplyMode(SourceManagementAuthorEditorPlan plan)
        {
            peoplePanel.IsEnabled = plan.PersonalAuthorFieldsEnabled;
            corporateLabel.IsEnabled = plan.CorporateAuthorFieldEnabled;
            corporateBox.IsEnabled = plan.CorporateAuthorFieldEnabled;
        }

        personRows.Render(initial.PersonalRows);

        var header = CreatePersonRowGrid();
        AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorFirstNameLabel), 0);
        AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorMiddleNameLabel), 1);
        AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorLastNameLabel), 2);
        peoplePanel.Children.Add(header);
        peoplePanel.Children.Add(rowsPanel);

        var addRow = Button(SourceManagementDialogPlanner.AddAuthorRowButtonLabel, () =>
            personRows.Render(session.AddPersonalAuthorRow(
                personRows.Read(),
                corporateBox.Text).PersonalRows));
        var removeRow = Button(SourceManagementDialogPlanner.RemoveAuthorRowButtonLabel, () =>
            personRows.Render(session.RemoveFinalPersonalAuthorRow(
                personRows.Read(),
                corporateBox.Text).PersonalRows));
        peoplePanel.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [addRow, removeRow],
            new Thickness(
                0,
                SourceManagementAuthorEditorVisualMetrics.InlineActionTopMargin,
                0,
                0)));

        personalMode.Click += (_, _) => ApplyMode(session.SelectMode(
            SourceManagementAuthorEditorMode.Personal,
            personRows.Read(),
            corporateBox.Text));
        corporateMode.Click += (_, _) => ApplyMode(session.SelectMode(
            SourceManagementAuthorEditorMode.Corporate,
            personRows.Read(),
            corporateBox.Text));

        var ok = Button(text.OkButtonLabel, () =>
        {
            State = session.Accept(personRows.Read(), corporateBox.Text);
            Close();
        }, isDefault: true);
        var cancel = Button(text.CancelButtonLabel, () => Close(), isCancel: true);

        var body = new StackPanel
        {
            Margin = new Thickness(SourceManagementAuthorEditorVisualMetrics.BodyInset),
        };
        body.Children.Add(personalMode);
        body.Children.Add(peoplePanel);
        body.Children.Add(corporateMode);
        body.Children.Add(corporateLabel);
        body.Children.Add(corporateBox);
        body.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [ok, cancel],
            new Thickness(
                0,
                SourceManagementAuthorEditorVisualMetrics.DialogActionTopMargin,
                0,
                0)));
        Content = body;

        ApplyMode(initial);
    }

    private static Grid CreatePersonRowGrid()
    {
        var grid = new Grid
        {
            Margin = new Thickness(
                0,
                0,
                0,
                SourceManagementAuthorEditorVisualMetrics.PersonRowBottomMargin),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(SourceManagementAuthorEditorVisualMetrics.FirstNameColumnWidth),
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(SourceManagementAuthorEditorVisualMetrics.MiddleNameColumnWidth),
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(SourceManagementAuthorEditorVisualMetrics.LastNameColumnWidth),
        });
        return grid;
    }

    private static TextBlock NewHeader(string text) =>
        new()
        {
            Text = text,
            Margin = new Thickness(
                0,
                0,
                SourceManagementAuthorEditorVisualMetrics.HeaderRightMargin,
                SourceManagementAuthorEditorVisualMetrics.HeaderBottomMargin),
        };

    private static TextBox NewAuthorTextBox(
        string? text,
        double minWidth = SourceManagementAuthorEditorVisualMetrics.DefaultNameFieldMinimumWidth)
    {
        var box = new TextBox
        {
            Text = text ?? string.Empty,
            MinWidth = minWidth,
            Margin = new Thickness(
                0,
                0,
                SourceManagementAuthorEditorVisualMetrics.FieldRightMargin,
                0),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            minWidth: SourceManagementAuthorEditorVisualMetrics.ButtonMinimumWidth,
            isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }

    private static void AddGridChild(Grid grid, Control child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
}

internal sealed class ManageSourcesDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            ActionSpacing = ManageSourcesDialogVisualMetrics.CloseActionSpacing,
        };

    private readonly IUserMessageService _messageService;
    private SourceManagementDialogState _state;
    private readonly ListBox _masterList = new()
    {
        MinWidth = ManageSourcesDialogVisualMetrics.ListMinimumWidth,
        MinHeight = ManageSourcesDialogVisualMetrics.ListMinimumHeight,
        Margin = new Thickness(
            0,
            0,
            0,
            ManageSourcesDialogVisualMetrics.ListBottomMargin),
    };
    private readonly ListBox _currentList = new()
    {
        MinWidth = ManageSourcesDialogVisualMetrics.ListMinimumWidth,
        MinHeight = ManageSourcesDialogVisualMetrics.ListMinimumHeight,
        Margin = new Thickness(
            0,
            0,
            0,
            ManageSourcesDialogVisualMetrics.ListBottomMargin),
    };

    public SourceManagementDialogResult? Result { get; private set; }

    public ManageSourcesDialog(
        IReadOnlyList<Source> currentSources,
        IReadOnlyList<Source> masterSources,
        IUserMessageService? messageService = null)
    {
        _messageService = messageService ?? new AvaloniaUserMessageService(this);
        _state = SourceManagementDialogPlanner.BuildInitialState(currentSources, masterSources);
        var text = SourceManagementDialogPlanner.ResolveText(UiText.Get);

        Title = text.ManageSourcesTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        AvaloniaCompactDialogChrome.ApplyListBox(_masterList, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyListBox(_currentList, DialogChromeStyle);
        _masterList.DoubleTapped += (_, _) => _ = EditMasterAsync();
        _currentList.DoubleTapped += (_, _) => _ = EditCurrentAsync();
        RefreshMasterList();
        RefreshCurrentList();

        var masterPane = Pane(
            SourceManagementDialogPlanner.MasterListLabel,
            _masterList,
            [
                Button(text.AddButtonLabel, () => _ = AddMasterAsync()),
                Button(text.EditButtonLabel, () => _ = EditMasterAsync()),
                Button(text.DeleteButtonLabel, DeleteMaster)
            ]);

        var copy = Button(text.CopyToCurrentButtonLabel, () => _ = CopyMasterToCurrentAsync());
        var copyBack = Button(text.CopyToMasterButtonLabel, () => _ = CopyCurrentToMasterAsync());
        var centerPane = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = ManageSourcesDialogVisualMetrics.CopyActionSpacing,
            Margin = new Thickness(
                0,
                0,
                ManageSourcesDialogVisualMetrics.PaneGap,
                0),
        };
        centerPane.Children.Add(copy);
        centerPane.Children.Add(copyBack);

        var currentPane = Pane(
            SourceManagementDialogPlanner.CurrentDocumentListLabel,
            _currentList,
            [
                Button(text.AddButtonLabel, () => _ = AddCurrentAsync()),
                Button(text.EditButtonLabel, () => _ = EditCurrentAsync()),
                Button(text.DeleteButtonLabel, DeleteCurrent)
            ]);

        var lists = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(
                0,
                0,
                0,
                ManageSourcesDialogVisualMetrics.ListsBottomMargin),
        };
        masterPane.Margin = new Thickness(
            0,
            0,
            ManageSourcesDialogVisualMetrics.PaneGap,
            0);
        lists.Children.Add(masterPane);
        lists.Children.Add(centerPane);
        lists.Children.Add(currentPane);

        var ok = Button(text.OkButtonLabel, Accept, isDefault: true);
        var cancel = Button(text.CancelButtonLabel, () => Close(), isCancel: true);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0));

        var body = new StackPanel
        {
            Margin = new Thickness(ManageSourcesDialogVisualMetrics.OuterInset),
        };
        body.Children.Add(lists);
        body.Children.Add(buttons);
        Content = body;
    }

    private async Task AddMasterAsync()
    {
        var entry = await AskEntryAsync();
        if (entry is null)
            return;

        var plan = SourceManagementDialogPlanner.AddMasterSource(_state, entry);
        if (!await ApplyValidationAsync(plan.Validation))
            return;

        _state = plan.State;
        RefreshMasterList(plan.SelectedIndex);
    }

    private void DeleteMaster()
    {
        var plan = SourceManagementDialogPlanner.DeleteMasterSource(_state, _masterList.SelectedIndex);
        _state = plan.State;
        RefreshMasterList(plan.SelectedIndex);
    }

    private async Task EditMasterAsync()
    {
        var index = _masterList.SelectedIndex;
        if (index < 0 || index >= _state.MasterSources.Count)
            return;

        var entry = await AskEntryAsync(_state.MasterSources[index]);
        if (entry is null)
            return;

        var plan = SourceManagementDialogPlanner.EditMasterSource(_state, index, entry);
        if (!await ApplyValidationAsync(plan.Validation))
            return;

        _state = plan.State;
        RefreshMasterList(plan.SelectedIndex);
    }

    private async Task CopyMasterToCurrentAsync()
    {
        var plan = SourceManagementDialogPlanner.CopyMasterToCurrent(
            _state,
            _masterList.SelectedIndex,
            _currentList.SelectedIndex);
        await ApplyCopyPlanAsync(plan, selectedIndex => RefreshCurrentList(selectedIndex));
    }

    private async Task CopyCurrentToMasterAsync()
    {
        var plan = SourceManagementDialogPlanner.CopyCurrentToMaster(
            _state,
            _currentList.SelectedIndex,
            _masterList.SelectedIndex);
        await ApplyCopyPlanAsync(plan, selectedIndex => RefreshMasterList(selectedIndex));
    }

    private async Task AddCurrentAsync()
    {
        var entry = await AskEntryAsync();
        if (entry is null)
            return;

        var plan = SourceManagementDialogPlanner.AddCurrentSource(_state, entry);
        if (!await ApplyValidationAsync(plan.Validation))
            return;

        _state = plan.State;
        RefreshCurrentList(plan.SelectedIndex);
    }

    private async Task EditCurrentAsync()
    {
        var index = _currentList.SelectedIndex;
        if (index < 0 || index >= _state.CurrentSources.Count)
            return;

        var entry = await AskEntryAsync(_state.CurrentSources[index]);
        if (entry is null)
            return;

        var plan = SourceManagementDialogPlanner.EditCurrentSource(_state, index, entry);
        if (!await ApplyValidationAsync(plan.Validation))
            return;

        _state = plan.State;
        RefreshCurrentList(plan.SelectedIndex);
    }

    private void DeleteCurrent()
    {
        var plan = SourceManagementDialogPlanner.DeleteCurrentSource(_state, _currentList.SelectedIndex);
        _state = plan.State;
        RefreshCurrentList(plan.SelectedIndex);
    }

    private void Accept()
    {
        Result = SourceManagementDialogPlanner.BuildResult(_state);
        Close();
    }

    private async Task<SourceManagementSourceEntry?> AskEntryAsync(Source? source = null)
    {
        var dialog = new SourceEntryDialog(source);
        await dialog.ShowDialog(this);
        return dialog.Entry;
    }

    private async Task<bool> ApplyValidationAsync(SourceManagementValidation? validation)
    {
        if (validation is null)
            return true;

        await _messageService.ShowWarningAsync(
            validation.Message,
            Title ?? SourceManagementDialogPlanner.ResolveText(UiText.Get).ManageSourcesTitle);
        return false;
    }

    private async Task<bool> ApplyCopyPlanAsync(SourceManagementListMutationPlan plan, Action<int?> refresh)
    {
        if (plan.Conflict is not null)
        {
            var action = await SourceConflictResolutionDialog.AskAsync(this, plan.Conflict);
            if (action is null)
                return false;

            plan = SourceManagementDialogPlanner.ResolveSourceConflict(
                _state,
                plan.Conflict,
                action.Value);
        }

        _state = plan.State;
        refresh(plan.SelectedIndex);
        return true;
    }

    private void RefreshMasterList(int? selectedIndex = null)
    {
        RefreshList(_masterList, SourceManagementDialogPlanner.BuildPickerItems(_state.MasterSources));
        SelectIndex(_masterList, selectedIndex ?? _masterList.SelectedIndex, _state.MasterSources.Count);
    }

    private void RefreshCurrentList(int? selectedIndex = null)
    {
        RefreshList(_currentList, SourceManagementDialogPlanner.BuildPickerItems(_state.CurrentSources));
        SelectIndex(_currentList, selectedIndex ?? _currentList.SelectedIndex, _state.CurrentSources.Count);
    }

    private static void RefreshList(ListBox list, IReadOnlyList<string> items) =>
        list.ItemsSource = items;

    private static void SelectIndex(ListBox list, int selectedIndex, int count) =>
        list.SelectedIndex = count == 0 ? -1 : Math.Clamp(selectedIndex, 0, count - 1);

    private static StackPanel Pane(string label, ListBox list, IReadOnlyList<Button> buttons)
    {
        var pane = new StackPanel();
        pane.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(
                0,
                0,
                0,
                ManageSourcesDialogVisualMetrics.LabelBottomMargin),
        });
        pane.Children.Add(list);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = ManageSourcesDialogVisualMetrics.PaneActionSpacing,
        };
        foreach (var button in buttons)
            row.Children.Add(button);
        pane.Children.Add(row);
        return pane;
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            minWidth: ManageSourcesDialogVisualMetrics.ButtonMinimumWidth,
            isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}
