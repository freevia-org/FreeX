using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class SortDialog : Window
{
    private const double DialogDefaultWidth = 760d;
    private const double DialogDefaultHeight = 500d;
    private const double DialogMinimumWidth = 680d;
    private const double DialogMinimumHeight = 420d;

    private readonly ObservableCollection<SortDialogLevel> _levels;
    private readonly IReadOnlyList<SortColumnChoice> _columnChoices;
    private readonly IReadOnlyList<SortColumnChoice> _genericColumnChoices;
    private readonly IReadOnlyList<SortColumnChoice> _rowChoices;
    private readonly IReadOnlyList<SortColorChoice> _cellColorChoices;
    private readonly IReadOnlyList<SortColorChoice> _fontColorChoices;
    private readonly Workbook? _iconWorkbook;
    private readonly Sheet? _iconSheet;
    private readonly GridRange? _iconRange;
    private readonly CheckBox _headerCheck;
    private readonly DataGridComboBoxColumn _sortByColumn;
    private readonly DataGrid _levelsGrid;
    private readonly Button _addLevelButton;
    private readonly Button _deleteLevelButton;
    private readonly Button _copyLevelButton;
    private readonly Button _moveUpButton;
    private readonly Button _moveDownButton;
    private readonly Button _optionsButton;
    private SortDialogOptions _options;

    public IReadOnlyList<SortDialogLevel> Levels => _levels.ToList();

    public IReadOnlyList<SortKey> ResultSortKeys { get; private set; }

    public bool ResultHasHeaders { get; private set; }

    public SortDialogOptions ResultOptions { get; private set; }

    public SortDialog(
        IEnumerable<SortDialogLevel>? levels = null,
        IEnumerable<SortColumnChoice>? columnChoices = null,
        IEnumerable<SortColumnChoice>? genericColumnChoices = null,
        IEnumerable<SortColumnChoice>? rowChoices = null,
        IEnumerable<SortColorChoice>? colorChoices = null,
        IEnumerable<SortColorChoice>? cellColorChoices = null,
        IEnumerable<SortColorChoice>? fontColorChoices = null,
        bool hasHeaders = true,
        Workbook? iconWorkbook = null,
        Sheet? iconSheet = null,
        GridRange? iconRange = null)
    {
        _levels = new ObservableCollection<SortDialogLevel>(NormalizeLevels(levels));
        _columnChoices = NormalizeColumnChoices(columnChoices);
        _genericColumnChoices = NormalizeColumnChoices(genericColumnChoices ?? columnChoices);
        _rowChoices = NormalizeColumnChoices(rowChoices);
        _cellColorChoices = NormalizeColorChoices(cellColorChoices ?? colorChoices);
        _fontColorChoices = NormalizeColorChoices(fontColorChoices ?? colorChoices);
        _iconWorkbook = iconWorkbook;
        _iconSheet = iconSheet;
        _iconRange = iconRange;
        _options = new SortDialogOptions();
        ResultSortKeys = BuildSortKeys(_levels);
        ResultHasHeaders = hasHeaders;
        ResultOptions = _options;

        Title = UiText.Get("Sort_CustomSort");
        Width = DialogDefaultWidth;
        Height = DialogDefaultHeight;
        MinWidth = DialogMinimumWidth;
        MinHeight = DialogMinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 220 });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _headerCheck = new CheckBox
        {
            Content = UiText.Get("Sort_MyDataHasHeaders"),
            IsChecked = hasHeaders,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        DockPanel.SetDock(_headerCheck, Dock.Right);
        headerRow.Children.Add(_headerCheck);
        headerRow.Children.Add(new TextBlock
        {
            Text = UiText.Get("Sort_SortLevels"),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        Grid.SetRow(headerRow, 0);
        root.Children.Add(headerRow);
        _headerCheck.Checked += (_, _) => { UpdateColumnChoices(); RefreshAllIconChoices(); };
        _headerCheck.Unchecked += (_, _) => { UpdateColumnChoices(); RefreshAllIconChoices(); };
        foreach (var level in _levels)
            AttachLevel(level);
        _levels.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (SortDialogLevel level in e.NewItems)
                AttachLevel(level);
            UpdateToolbarButtonStates();
        };

        _levelsGrid = new DataGrid
        {
            ItemsSource = _levels,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            MinHeight = 220,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _levelsGrid.SelectionChanged += (_, _) => UpdateToolbarButtonStates();
        _levelsGrid.KeyDown += LevelsGrid_KeyDown;
        _sortByColumn = new DataGridComboBoxColumn
        {
            Header = UiText.Get("Sort_SortBy"),
            DisplayMemberPath = nameof(SortColumnChoice.Label),
            SelectedValuePath = nameof(SortColumnChoice.ColumnOffset),
            SelectedValueBinding = new Binding(nameof(SortDialogLevel.ColumnOffset))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
        UpdateColumnChoices();
        _levelsGrid.Columns.Add(_sortByColumn);
        _levelsGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = UiText.Get("Sort_SortOn"),
            ItemsSource = SortOnChoices,
            DisplayMemberPath = nameof(SortOnChoice.Label),
            SelectedValuePath = nameof(SortOnChoice.Label),
            SelectedValueBinding = new Binding(nameof(SortDialogLevel.SortOn))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(140)
        });
        _levelsGrid.Columns.Add(CreateOrderColumn());
        _levelsGrid.Columns.Add(CreateColorColumn());
        _levelsGrid.Columns.Add(CreateIconColumn());
        Grid.SetRow(_levelsGrid, 1);
        root.Children.Add(_levelsGrid);

        var commandRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        commandRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        commandRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var helperRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };
        _addLevelButton = new Button { Content = UiText.Get("Sort_AddLevel"), MinWidth = 98, Margin = new Thickness(0, 0, 8, 6) };
        _addLevelButton.Click += (_, _) =>
        {
            ReplaceLevels(AddLevel(_levels));
            _levelsGrid.SelectedIndex = _levels.Count - 1;
            UpdateToolbarButtonStates();
        };
        _deleteLevelButton = new Button { Content = UiText.Get("Sort_DeleteLevel"), MinWidth = 104, Margin = new Thickness(0, 0, 8, 6) };
        _deleteLevelButton.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? _levels.Count - 1 : _levelsGrid.SelectedIndex;
            ReplaceLevels(RemoveLevel(_levels, selectedIndex));
            _levelsGrid.SelectedIndex = Math.Min(selectedIndex, _levels.Count - 1);
            UpdateToolbarButtonStates();
        };
        _copyLevelButton = new Button { Content = UiText.Get("Sort_CopyLevel"), MinWidth = 98, Margin = new Thickness(0, 0, 8, 6) };
        _copyLevelButton.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? _levels.Count - 1 : _levelsGrid.SelectedIndex;
            ReplaceLevels(CopyLevel(_levels, selectedIndex));
            _levelsGrid.SelectedIndex = Math.Min(selectedIndex + 1, _levels.Count - 1);
            UpdateToolbarButtonStates();
        };
        _moveUpButton = new Button { Content = UiText.Get("Sort_MoveUp"), MinWidth = 86, Margin = new Thickness(0, 0, 8, 6) };
        _moveUpButton.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? 0 : _levelsGrid.SelectedIndex;
            ReplaceLevels(MoveLevel(_levels, selectedIndex, -1));
            _levelsGrid.SelectedIndex = Math.Max(0, selectedIndex - 1);
            UpdateToolbarButtonStates();
        };
        _moveDownButton = new Button { Content = UiText.Get("Sort_MoveDown"), MinWidth = 92, Margin = new Thickness(0, 0, 8, 6) };
        _moveDownButton.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? _levels.Count - 1 : _levelsGrid.SelectedIndex;
            ReplaceLevels(MoveLevel(_levels, selectedIndex, 1));
            _levelsGrid.SelectedIndex = Math.Min(_levels.Count - 1, selectedIndex + 1);
            UpdateToolbarButtonStates();
        };
        helperRow.Children.Add(_addLevelButton);
        helperRow.Children.Add(_deleteLevelButton);
        helperRow.Children.Add(_copyLevelButton);
        helperRow.Children.Add(_moveUpButton);
        helperRow.Children.Add(_moveDownButton);
        Grid.SetColumn(helperRow, 0);
        commandRow.Children.Add(helperRow);
        _optionsButton = new Button
        {
            Content = UiText.Get("Sort_Options"),
            MinWidth = 92,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        _optionsButton.Click += (_, _) =>
        {
            var dialog = new SortOptionsDialog(_options) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _options = dialog.Result;
                UpdateColumnChoices();
            }
        };
        Grid.SetColumn(_optionsButton, 1);
        commandRow.Children.Add(_optionsButton);
        Grid.SetRow(commandRow, 2);
        root.Children.Add(commandRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var ok = new Button { Content = UiText.Ok, IsDefault = true, MinWidth = 76, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            ResultSortKeys = BuildSortKeys(_levels);
            ResultHasHeaders = _headerCheck.IsChecked == true;
            ResultOptions = _options;
            DialogResult = true;
        };
        var cancel = new Button { Content = UiText.Cancel, IsCancel = true, MinWidth = 76 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
        UpdateToolbarButtonStates();
    }

    private void FocusInitialKeyboardTarget()
    {
        _levelsGrid.SelectedIndex = 0;
        _levelsGrid.Focus();
        Keyboard.Focus(_levelsGrid);
        UpdateToolbarButtonStates();
    }

    private void LevelsGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _deleteLevelButton.IsEnabled)
        {
            _deleteLevelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            e.Handled = true;
        }
    }

    private void UpdateToolbarButtonStates()
    {
        var selectedIndex = _levelsGrid.SelectedIndex;
        var hasSelection = selectedIndex >= 0 && selectedIndex < _levels.Count;
        _deleteLevelButton.IsEnabled = hasSelection && _levels.Count > 1;
        _copyLevelButton.IsEnabled = hasSelection;
        _moveUpButton.IsEnabled = hasSelection && selectedIndex > 0;
        _moveDownButton.IsEnabled = hasSelection && selectedIndex < _levels.Count - 1;
    }

    private void UpdateColumnChoices()
    {
        _sortByColumn.Header = _options.LeftToRight ? UiText.Get("Sort_SortByRowHeader") : UiText.Get("Sort_SortByHeader");
        _headerCheck.IsEnabled = !_options.LeftToRight;
        _sortByColumn.ItemsSource = SortDialogPlanner.BuildActiveColumnChoices(
            _options,
            _headerCheck.IsChecked == true,
            _columnChoices,
            _genericColumnChoices,
            _rowChoices);
    }

    private void AttachLevel(SortDialogLevel level)
    {
        ApplyColorChoices(level);
        ApplyIconChoices(level);
        level.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SortDialogLevel.SortOn))
            {
                ApplyColorChoices(level);
                ApplyIconChoices(level);
            }
            else if (e.PropertyName == nameof(SortDialogLevel.ColumnOffset))
            {
                ApplyIconChoices(level);
            }
        };
    }

    private void ApplyColorChoices(SortDialogLevel level)
    {
        level.SetColorChoices(SortDialogPlanner.BuildColorChoicesForSortOn(
            level.SortOn,
            _cellColorChoices,
            _fontColorChoices,
            PlannerText));
    }

    /// <summary>
    /// Mirrors <see cref="ApplyColorChoices"/> for "Sort On: Cell Icon", but the icon set a column
    /// carries is column-specific (unlike the whole-range color scan), so the choices are rescanned
    /// from <see cref="SortDialogPlanner.BuildIconChoices"/> against the level's own
    /// <see cref="SortDialogLevel.ColumnOffset"/> every time the sort-on mode or the target column
    /// changes, instead of being precomputed once like <see cref="_cellColorChoices"/>.
    /// </summary>
    private void ApplyIconChoices(SortDialogLevel level)
    {
        if (_iconWorkbook is null || _iconSheet is null || _iconRange is not { } range ||
            SortDialogPlanner.SortOnFromLabel(level.SortOn, PlannerText) != SortOn.CellIcon)
        {
            level.SetIconChoices([new SortIconChoice("")]);
            return;
        }

        level.SetIconChoices(SortDialogPlanner.BuildIconChoices(
            _iconWorkbook,
            _iconSheet,
            range,
            level.ColumnOffset,
            _headerCheck.IsChecked == true));
    }

    private void RefreshAllIconChoices()
    {
        foreach (var level in _levels)
            ApplyIconChoices(level);
    }

    private void ReplaceLevels(IEnumerable<SortDialogLevel> levels)
    {
        _levels.Clear();
        foreach (var level in levels)
            _levels.Add(level);
    }

    private static DataGridTemplateColumn CreateOrderColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = UiText.Get("Sort_Order"),
            Width = new DataGridLength(150)
        };
        column.CellTemplate = CreateOrderTemplate(isReadOnly: true);
        column.CellEditingTemplate = CreateOrderTemplate(isReadOnly: false);
        return column;
    }

    private static DataGridTemplateColumn CreateColorColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = UiText.Get("Sort_Color"),
            Width = new DataGridLength(115)
        };
        column.CellTemplate = CreateColorTemplate(isReadOnly: true);
        column.CellEditingTemplate = CreateColorTemplate(isReadOnly: false);
        return column;
    }

    private static DataTemplate CreateColorTemplate(bool isReadOnly)
    {
        var combo = new FrameworkElementFactory(typeof(ComboBox));
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SortDialogLevel.ColorChoices)));
        combo.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(SortColorChoice.Label));
        combo.SetValue(Selector.SelectedValuePathProperty, nameof(SortColorChoice.Label));
        combo.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(SortDialogLevel.TargetColor))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        combo.SetValue(UIElement.IsHitTestVisibleProperty, !isReadOnly);
        combo.SetValue(Control.IsTabStopProperty, !isReadOnly);
        return new DataTemplate { VisualTree = combo };
    }

    private static DataGridTemplateColumn CreateIconColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = UiText.Get("Sort_Icon"),
            Width = new DataGridLength(115)
        };
        column.CellTemplate = CreateIconTemplate(isReadOnly: true);
        column.CellEditingTemplate = CreateIconTemplate(isReadOnly: false);
        return column;
    }

    private static DataTemplate CreateIconTemplate(bool isReadOnly)
    {
        var combo = new FrameworkElementFactory(typeof(ComboBox));
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SortDialogLevel.IconChoices)));
        combo.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(SortIconChoice.Label));
        combo.SetValue(Selector.SelectedValuePathProperty, nameof(SortIconChoice.Label));
        combo.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(SortDialogLevel.TargetIcon))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        combo.SetValue(UIElement.IsHitTestVisibleProperty, !isReadOnly);
        combo.SetValue(Control.IsTabStopProperty, !isReadOnly);
        return new DataTemplate { VisualTree = combo };
    }

    private static DataTemplate CreateOrderTemplate(bool isReadOnly)
    {
        var combo = new FrameworkElementFactory(typeof(ComboBox));
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SortDialogLevel.OrderChoices)));
        combo.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(SortDirectionChoice.Label));
        combo.SetValue(Selector.SelectedValuePathProperty, nameof(SortDirectionChoice.Ascending));
        combo.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(SortDialogLevel.Ascending))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        combo.SetValue(UIElement.IsHitTestVisibleProperty, !isReadOnly);
        combo.SetValue(Control.IsTabStopProperty, !isReadOnly);
        return new DataTemplate { VisualTree = combo };
    }

}

