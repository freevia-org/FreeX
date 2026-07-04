using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class CrossReferenceDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly TextDocument _document;
    private readonly ComboBox _typeBox = new() { MinWidth = 170 };
    private readonly ComboBox _insertAsBox = new() { MinWidth = 190 };
    private readonly ListBox _targetList = new() { MinWidth = 340, Height = 180 };
    private readonly CheckBox _hyperlinkBox = new()
    {
        Content = CrossReferenceDialogPlanner.HyperlinkLabel,
        IsChecked = true,
        Margin = new Thickness(16, 8, 16, 0),
    };

    private readonly TextBlock _status = new()
    {
        Text = CrossReferenceDialogPlanner.MissingTargetMessage,
        Foreground = Brushes.DarkRed,
        Margin = new Thickness(16, 6, 16, 0),
        IsVisible = false,
    };

    public CrossReferenceDialogChoice? Result { get; private set; }

    public CrossReferenceDialog(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;

        Title = CrossReferenceDialogPlanner.Title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _typeBox.ItemsSource = CrossReferenceDialogPlanner.BuildTypeChoices();
        _typeBox.SelectedIndex = 0;
        _typeBox.SelectionChanged += (_, _) =>
        {
            ReloadInsertAs();
            ReloadTargets();
        };
        _insertAsBox.SelectionChanged += (_, _) => ReloadTargets();

        AvaloniaCompactDialogChrome.ApplyComboBox(_typeBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_insertAsBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyListBox(_targetList, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_hyperlinkBox, DialogChromeStyle);

        ReloadInsertAs();
        ReloadTargets();

        var topGrid = new Grid { Margin = new Thickness(16, 12, 16, 0) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.Children.Add(LabeledColumn(CrossReferenceDialogPlanner.ReferenceTypeLabel, _typeBox, 0));
        topGrid.Children.Add(LabeledColumn(CrossReferenceDialogPlanner.InsertReferenceToLabel, _insertAsBox, 2));

        var targetLabel = new TextBlock
        {
            Text = CrossReferenceDialogPlanner.TargetLabel,
            Margin = new Thickness(16, 10, 16, 4),
        };

        var targetHost = new Border
        {
            Margin = new Thickness(16, 0, 16, 0),
            Child = _targetList,
        };

        var ok = Button("Insert", Accept, isDefault: true);
        var cancel = Button("Cancel", () => Close(), isCancel: true);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 12, 16, 14));

        var body = new StackPanel();
        body.Children.Add(topGrid);
        body.Children.Add(_hyperlinkBox);
        body.Children.Add(targetLabel);
        body.Children.Add(targetHost);
        body.Children.Add(_status);
        body.Children.Add(buttons);
        Content = body;
    }

    private CrossRefType SelectedType =>
        (_typeBox.SelectedItem as CrossReferenceTypeChoice)?.Type ?? CrossRefType.Heading;

    private CrossRefInsertAs SelectedInsertAs =>
        (_insertAsBox.SelectedItem as CrossReferenceInsertAsChoice)?.InsertAs ?? CrossRefInsertAs.Text;

    private void ReloadInsertAs()
    {
        var previous = (_insertAsBox.SelectedItem as CrossReferenceInsertAsChoice)?.InsertAs;
        var choices = CrossReferenceDialogPlanner.BuildInsertAsChoices(SelectedType);
        _insertAsBox.ItemsSource = choices;
        _insertAsBox.SelectedIndex = CrossReferenceDialogPlanner.PreserveInsertAsSelection(choices, previous);
    }

    private void ReloadTargets()
    {
        var choices = CrossReferenceDialogPlanner.BuildTargetChoices(_document, SelectedType);
        _targetList.ItemsSource = choices;
        _targetList.SelectedIndex = choices.Count > 0 ? 0 : -1;
        _status.IsVisible = false;
    }

    private void Accept()
    {
        if (!CrossReferenceDialogPlanner.TryCreateChoice(
                _document,
                SelectedType,
                SelectedInsertAs,
                _targetList.SelectedIndex,
                _hyperlinkBox.IsChecked == true,
                out var choice))
        {
            _status.IsVisible = true;
            return;
        }

        Result = choice;
        Close();
    }

    private static StackPanel LabeledColumn(string label, Control control, int column)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(control);
        Grid.SetColumn(stack, column);
        return stack;
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 84, isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}

internal sealed class CitationSourcePickerDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly IReadOnlyList<Source> _sources;
    private readonly ListBox _sourceList = new() { MinWidth = 340, Height = 160 };
    private readonly TextBlock _status = new()
    {
        Text = "Select a source or add a new one.",
        Foreground = Brushes.DarkRed,
        Margin = new Thickness(16, 6, 16, 0),
        IsVisible = false,
    };

    public SourceManagementPick? Pick { get; private set; }

    public CitationSourcePickerDialog(IReadOnlyList<Source> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;

        Title = SourceManagementDialogPlanner.SourcePickerTitle;
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
            Text = SourceManagementDialogPlanner.SourcePickerLabel,
            Margin = new Thickness(16, 12, 16, 4),
        };
        var listHost = new Border { Margin = new Thickness(16, 0, 16, 0), Child = _sourceList };

        var addNew = Button(SourceManagementDialogPlanner.AddNewSourceButtonLabel, () =>
        {
            Pick = SourceManagementDialogPlanner.CreateAddNewPick();
            Close();
        });
        var insert = Button("Insert", Accept, isDefault: true);
        var cancel = Button("Cancel", () => Close(), isCancel: true);
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

internal sealed class MarkCitationDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly IReadOnlyList<MarkCitationCategoryChoice> _categoryChoices;
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
        Title = MarkCitationDialogPlanner.Title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _categoryChoices = MarkCitationDialogPlanner.BuildCategoryChoices();
        var state = MarkCitationDialogPlanner.BuildInitialState(seedLongCitation);

        _categoryBox.ItemsSource = _categoryChoices;
        _categoryBox.SelectedIndex = MarkCitationDialogPlanner.SelectCategoryIndex(_categoryChoices, state.Category);
        _longCitationBox.Text = state.LongCitation;
        _shortCitationBox.Text = state.ShortCitation;

        AvaloniaCompactDialogChrome.ApplyComboBox(_categoryBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_longCitationBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_shortCitationBox, DialogChromeStyle);

        var grid = new Grid { Margin = new Thickness(16, 12, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddLabeledRow(grid, 0, MarkCitationDialogPlanner.CategoryLabel, _categoryBox);
        AddLabeledRow(grid, 1, MarkCitationDialogPlanner.LongCitationLabel, _longCitationBox);
        AddLabeledRow(grid, 2, MarkCitationDialogPlanner.ShortCitationLabel, _shortCitationBox);

        var mark = Button(MarkCitationDialogPlanner.MarkButtonLabel, () => Accept(), isDefault: true);
        var cancel = Button("Cancel", () => Close(), isCancel: true);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([mark, cancel], new Thickness(16, 12, 16, 14));

        var body = new StackPanel();
        body.Children.Add(grid);
        body.Children.Add(_status);
        body.Children.Add(buttons);
        Content = body;
    }

    internal void SetForTests(CitationCategory category, string? longCitation, string? shortCitation)
    {
        _categoryBox.SelectedIndex = MarkCitationDialogPlanner.SelectCategoryIndex(_categoryChoices, category);
        _longCitationBox.Text = longCitation;
        _shortCitationBox.Text = shortCitation;
    }

    internal bool AcceptForTests() => Accept(closeOnSuccess: false);

    private MarkCitationDialogState CurrentState()
    {
        var category = _categoryBox.SelectedIndex >= 0 && _categoryBox.SelectedIndex < _categoryChoices.Count
            ? _categoryChoices[_categoryBox.SelectedIndex].Category
            : CitationCategory.Cases;
        return new MarkCitationDialogState(
            category,
            _longCitationBox.Text ?? string.Empty,
            _shortCitationBox.Text ?? string.Empty);
    }

    private bool Accept(bool closeOnSuccess = true)
    {
        if (!MarkCitationDialogPlanner.TryBuildCitation(CurrentState(), out var citation, out var validation))
        {
            _status.Text = validation?.Message ?? MarkCitationDialogPlanner.MissingLongCitationMessage;
            _status.IsVisible = true;
            return false;
        }

        _status.IsVisible = false;
        Citation = citation;
        if (closeOnSuccess)
            Close();
        return true;
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

internal sealed class SourceEntryDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly IReadOnlyList<SourceManagementSourceTypeChoice> _typeChoices;
    private readonly ComboBox _typeBox = new() { MinWidth = 260 };
    private readonly Grid _grid = new() { Margin = new Thickness(16, 12, 16, 0) };
    private readonly Dictionary<SourceManagementSourceField, TextBox> _fields;
    private readonly SourceManagementSourceEntry _initialEntry;

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
        _initialEntry = entry;
        _fields = SourceManagementDialogPlanner.BuildEntryFieldPlans(entry).ToDictionary(plan => plan.Field, plan =>
        {
            var box = new TextBox
            {
                Text = plan.Text,
                MinWidth = 260,
                Margin = new Thickness(0, 6, 0, 0),
            };
            AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
            return box;
        });

        _typeBox.ItemsSource = _typeChoices.Select(choice => choice.Label).ToArray();
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
            _initialEntry);

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
                field = new TextBox
                {
                    Text = plan.Text,
                    MinWidth = 260,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                AvaloniaCompactDialogChrome.ApplyTextBox(field, DialogChromeStyle);
                _fields[plan.Field] = field;
            }

            AddLabeledRow(_grid, row++, plan.Label, field);
        }
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

internal sealed class ManageSourcesDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private SourceManagementDialogState _state;
    private readonly ListBox _masterList = new() { MinWidth = 220, Height = 190 };
    private readonly ListBox _currentList = new() { MinWidth = 220, Height = 190 };
    private readonly TextBlock _status = new()
    {
        Foreground = Brushes.DarkRed,
        Margin = new Thickness(16, 6, 16, 0),
        IsVisible = false,
    };

    public SourceManagementDialogResult? Result { get; private set; }

    public ManageSourcesDialog(
        IReadOnlyList<Source> currentSources,
        IReadOnlyList<Source> masterSources)
    {
        _state = SourceManagementDialogPlanner.BuildInitialState(currentSources, masterSources);

        Title = "Manage Sources";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        AvaloniaCompactDialogChrome.ApplyListBox(_masterList, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyListBox(_currentList, DialogChromeStyle);
        RefreshMasterList();
        RefreshCurrentList();

        var masterPane = Pane(
            SourceManagementDialogPlanner.MasterListLabel,
            _masterList,
            [
                Button("Add...", () => _ = AddMasterAsync()),
                Button("Delete", DeleteMaster)
            ]);

        var copy = Button("Copy ->", CopyMasterToCurrent);
        var centerPane = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        centerPane.Children.Add(copy);

        var currentPane = Pane(
            SourceManagementDialogPlanner.CurrentDocumentListLabel,
            _currentList,
            [
                Button("Add...", () => _ = AddCurrentAsync()),
                Button("Edit...", () => _ = EditCurrentAsync()),
                Button("Delete", DeleteCurrent)
            ]);

        var lists = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 12, 16, 0),
        };
        lists.Children.Add(masterPane);
        lists.Children.Add(centerPane);
        lists.Children.Add(currentPane);

        var ok = Button("OK", Accept, isDefault: true);
        var cancel = Button("Cancel", () => Close(), isCancel: true);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 12, 16, 14));

        var body = new StackPanel();
        body.Children.Add(lists);
        body.Children.Add(_status);
        body.Children.Add(buttons);
        Content = body;
    }

    private async Task AddMasterAsync()
    {
        var entry = await AskEntryAsync();
        if (entry is null)
            return;

        var plan = SourceManagementDialogPlanner.AddMasterSource(_state, entry);
        if (!ApplyValidation(plan.Validation))
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

    private void CopyMasterToCurrent()
    {
        var plan = SourceManagementDialogPlanner.CopyMasterToCurrent(
            _state,
            _masterList.SelectedIndex,
            _currentList.SelectedIndex);
        _state = plan.State;
        RefreshCurrentList(plan.SelectedIndex);
    }

    private async Task AddCurrentAsync()
    {
        var entry = await AskEntryAsync();
        if (entry is null)
            return;

        var plan = SourceManagementDialogPlanner.AddCurrentSource(_state, entry);
        if (!ApplyValidation(plan.Validation))
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
        if (!ApplyValidation(plan.Validation))
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

    private bool ApplyValidation(SourceManagementValidation? validation)
    {
        if (validation is null)
        {
            _status.IsVisible = false;
            return true;
        }

        _status.Text = validation.Message;
        _status.IsVisible = true;
        return false;
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
        pane.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        pane.Children.Add(list);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        foreach (var button in buttons)
            row.Children.Add(button);
        pane.Children.Add(row);
        return pane;
    }

    private static Button Button(string label, Action click, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 84, isDefault: isDefault);
        button.Click += (_, _) => click();
        return button;
    }
}
