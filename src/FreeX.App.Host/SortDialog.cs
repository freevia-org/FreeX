using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class SortDialog : Window
{
    private readonly ObservableCollection<SortDialogLevel> _levels;
    private readonly IReadOnlyList<SortColumnChoice> _columnChoices;
    private readonly IReadOnlyList<SortColumnChoice> _genericColumnChoices;
    private readonly IReadOnlyList<SortColumnChoice> _rowChoices;
    private readonly IReadOnlyList<SortColorChoice> _cellColorChoices;
    private readonly IReadOnlyList<SortColorChoice> _fontColorChoices;
    private readonly CheckBox _headerCheck;
    private readonly DataGridComboBoxColumn _sortByColumn;
    private readonly DataGrid _levelsGrid;
    private readonly Button _deleteLevelButton;
    private readonly Button _copyLevelButton;
    private readonly Button _moveUpButton;
    private readonly Button _moveDownButton;
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
        bool hasHeaders = true)
    {
        _levels = new ObservableCollection<SortDialogLevel>(NormalizeLevels(levels));
        _columnChoices = NormalizeColumnChoices(columnChoices);
        _genericColumnChoices = NormalizeColumnChoices(genericColumnChoices ?? columnChoices);
        _rowChoices = NormalizeColumnChoices(rowChoices);
        _cellColorChoices = NormalizeColorChoices(cellColorChoices ?? colorChoices);
        _fontColorChoices = NormalizeColorChoices(fontColorChoices ?? colorChoices);
        _options = new SortDialogOptions();
        ResultSortKeys = BuildSortKeys(_levels);
        ResultHasHeaders = hasHeaders;
        ResultOptions = _options;

        Title = UiText.Get("Sort_CustomSort");
        Width = 640;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = false };
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
        DockPanel.SetDock(headerRow, Dock.Top);
        root.Children.Add(headerRow);
        _headerCheck.Checked += (_, _) => UpdateColumnChoices();
        _headerCheck.Unchecked += (_, _) => UpdateColumnChoices();
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
            Height = 220,
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
        DockPanel.SetDock(_levelsGrid, Dock.Top);
        root.Children.Add(_levelsGrid);

        var commandDock = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var helperRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };
        var add = new Button { Content = UiText.Get("Sort_AddLevel"), Width = 98, Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) =>
        {
            ReplaceLevels(AddLevel(_levels));
            _levelsGrid.SelectedIndex = _levels.Count - 1;
            UpdateToolbarButtonStates();
        };
        _deleteLevelButton = new Button { Content = UiText.Get("Sort_DeleteLevel"), Width = 104, Margin = new Thickness(0, 0, 8, 0) };
        _deleteLevelButton.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? _levels.Count - 1 : _levelsGrid.SelectedIndex;
            ReplaceLevels(RemoveLevel(_levels, selectedIndex));
            _levelsGrid.SelectedIndex = Math.Min(selectedIndex, _levels.Count - 1);
            UpdateToolbarButtonStates();
        };
        _copyLevelButton = new Button { Content = UiText.Get("Sort_CopyLevel"), Width = 98 };
        _copyLevelButton.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? _levels.Count - 1 : _levelsGrid.SelectedIndex;
            ReplaceLevels(CopyLevel(_levels, selectedIndex));
            _levelsGrid.SelectedIndex = Math.Min(selectedIndex + 1, _levels.Count - 1);
            UpdateToolbarButtonStates();
        };
        _moveUpButton = new Button { Content = UiText.Get("Sort_MoveUp"), Width = 86, Margin = new Thickness(8, 0, 8, 0) };
        _moveUpButton.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? 0 : _levelsGrid.SelectedIndex;
            ReplaceLevels(MoveLevel(_levels, selectedIndex, -1));
            _levelsGrid.SelectedIndex = Math.Max(0, selectedIndex - 1);
            UpdateToolbarButtonStates();
        };
        _moveDownButton = new Button { Content = UiText.Get("Sort_MoveDown"), Width = 92 };
        _moveDownButton.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? _levels.Count - 1 : _levelsGrid.SelectedIndex;
            ReplaceLevels(MoveLevel(_levels, selectedIndex, 1));
            _levelsGrid.SelectedIndex = Math.Min(_levels.Count - 1, selectedIndex + 1);
            UpdateToolbarButtonStates();
        };
        helperRow.Children.Add(add);
        helperRow.Children.Add(_deleteLevelButton);
        helperRow.Children.Add(_copyLevelButton);
        helperRow.Children.Add(_moveUpButton);
        helperRow.Children.Add(_moveDownButton);
        commandDock.Children.Add(helperRow);
        var options = new Button
        {
            Content = UiText.Get("Sort_Options"),
            Width = 92,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        options.Click += (_, _) =>
        {
            var dialog = new SortOptionsDialog(_options) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _options = dialog.Result;
                UpdateColumnChoices();
            }
        };
        DockPanel.SetDock(options, Dock.Right);
        commandDock.Children.Add(options);
        DockPanel.SetDock(commandDock, Dock.Bottom);
        root.Children.Add(commandDock);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var ok = new Button { Content = UiText.Ok, IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            ResultSortKeys = BuildSortKeys(_levels);
            ResultHasHeaders = _headerCheck.IsChecked == true;
            ResultOptions = _options;
            DialogResult = true;
        };
        var cancel = new Button { Content = UiText.Cancel, IsCancel = true, Width = 76 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
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
        level.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SortDialogLevel.SortOn))
                ApplyColorChoices(level);
        };
    }

    private void ApplyColorChoices(SortDialogLevel level)
    {
        level.SetColorChoices(SortDialogPlanner.BuildColorChoicesForSortOn(level.SortOn, _cellColorChoices, _fontColorChoices));
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

