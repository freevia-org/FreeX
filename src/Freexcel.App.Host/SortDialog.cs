using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record SortColumnChoice(string Label, uint ColumnOffset);

public sealed record SortDirectionChoice(string Label, bool Ascending);

public sealed record SortOnChoice(string Label);

public sealed record SortColorChoice(string Label);

public sealed record SortDialogOptions(bool CaseSensitive = false, bool LeftToRight = false);

public sealed class SortDialogLevel : IEquatable<SortDialogLevel>, INotifyPropertyChanged
{
    private uint _columnOffset;
    private bool _ascending;
    private string _sortOn = "Cell Values";
    private string _targetColor = "";
    private IReadOnlyList<SortColorChoice> _colorChoices = [new SortColorChoice("")];

    public SortDialogLevel(uint columnOffset, bool ascending)
    {
        _columnOffset = columnOffset;
        _ascending = ascending;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public uint ColumnOffset
    {
        get => _columnOffset;
        set => SetField(ref _columnOffset, value);
    }

    public bool Ascending
    {
        get => _ascending;
        set => SetField(ref _ascending, value);
    }

    public string SortOn
    {
        get => _sortOn;
        set
        {
            if (SetField(ref _sortOn, value))
                OnPropertyChanged(nameof(OrderChoices));
        }
    }

    public string TargetColor
    {
        get => _targetColor;
        set => SetField(ref _targetColor, value);
    }

    public IReadOnlyList<SortDirectionChoice> OrderChoices => SortDialog.BuildOrderChoices(SortOn);

    public IReadOnlyList<SortColorChoice> ColorChoices => _colorChoices;

    public bool Equals(SortDialogLevel? other) =>
        other is not null &&
        ColumnOffset == other.ColumnOffset &&
        Ascending == other.Ascending &&
        string.Equals(SortOn, other.SortOn, StringComparison.Ordinal) &&
        string.Equals(TargetColor, other.TargetColor, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as SortDialogLevel);

    public override int GetHashCode() => HashCode.Combine(ColumnOffset, Ascending, SortOn, TargetColor.ToUpperInvariant());

    public override string ToString() => $"Column offset {ColumnOffset}, {(Ascending ? "Ascending" : "Descending")}";

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    internal void SetColorChoices(IReadOnlyList<SortColorChoice> colorChoices)
    {
        _colorChoices = colorChoices.Count == 0 ? [new SortColorChoice("")] : colorChoices;
        if (!string.IsNullOrWhiteSpace(TargetColor) &&
            !_colorChoices.Any(choice => string.Equals(choice.Label, TargetColor, StringComparison.OrdinalIgnoreCase)))
            TargetColor = "";
        OnPropertyChanged(nameof(ColorChoices));
    }
}

public sealed partial class SortDialog : Window
{
    private static readonly IReadOnlyList<SortDirectionChoice> DirectionChoices =
    [
        new("A to Z", true),
        new("Z to A", false)
    ];

    private static readonly IReadOnlyList<SortDirectionChoice> ColorDirectionChoices =
    [
        new("On Top", true),
        new("On Bottom", false)
    ];

    private static readonly IReadOnlyList<SortOnChoice> SortOnChoices =
    [
        new("Cell Values"),
        new("Cell Color"),
        new("Font Color")
    ];

    private readonly ObservableCollection<SortDialogLevel> _levels;
    private readonly IReadOnlyList<SortColumnChoice> _columnChoices;
    private readonly IReadOnlyList<SortColumnChoice> _genericColumnChoices;
    private readonly IReadOnlyList<SortColumnChoice> _rowChoices;
    private readonly IReadOnlyList<SortColorChoice> _cellColorChoices;
    private readonly IReadOnlyList<SortColorChoice> _fontColorChoices;
    private readonly CheckBox _headerCheck;
    private readonly DataGridComboBoxColumn _sortByColumn;
    private readonly DataGrid _levelsGrid;
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

        Title = "Custom Sort";
        Width = 640;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = false };
        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _headerCheck = new CheckBox
        {
            Content = "My data has _headers",
            IsChecked = hasHeaders,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        DockPanel.SetDock(_headerCheck, Dock.Right);
        headerRow.Children.Add(_headerCheck);
        headerRow.Children.Add(new TextBlock
        {
            Text = "Sort levels",
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
        _sortByColumn = new DataGridComboBoxColumn
        {
            Header = "Sort by",
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
            Header = "Sort On",
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
        var add = new Button { Content = "_Add Level", Width = 98, Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => _levels.Add(new SortDialogLevel(0, true));
        var remove = new Button { Content = "_Delete Level", Width = 104, Margin = new Thickness(0, 0, 8, 0) };
        remove.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? _levels.Count - 1 : _levelsGrid.SelectedIndex;
            var updated = RemoveLevel(_levels, selectedIndex);
            _levels.Clear();
            foreach (var level in updated)
                _levels.Add(level);
        };
        var copy = new Button { Content = "_Copy Level", Width = 98 };
        copy.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? _levels.Count - 1 : _levelsGrid.SelectedIndex;
            var updated = CopyLevel(_levels, selectedIndex);
            _levels.Clear();
            foreach (var level in updated)
                _levels.Add(level);
            _levelsGrid.SelectedIndex = Math.Min(selectedIndex + 1, _levels.Count - 1);
        };
        var moveUp = new Button { Content = "Move _Up", Width = 86, Margin = new Thickness(8, 0, 8, 0) };
        moveUp.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? 0 : _levelsGrid.SelectedIndex;
            var updated = MoveLevel(_levels, selectedIndex, -1);
            _levels.Clear();
            foreach (var level in updated)
                _levels.Add(level);
            _levelsGrid.SelectedIndex = Math.Max(0, selectedIndex - 1);
        };
        var moveDown = new Button { Content = "Move Do_wn", Width = 92 };
        moveDown.Click += (_, _) =>
        {
            var selectedIndex = _levelsGrid.SelectedIndex < 0 ? _levels.Count - 1 : _levelsGrid.SelectedIndex;
            var updated = MoveLevel(_levels, selectedIndex, 1);
            _levels.Clear();
            foreach (var level in updated)
                _levels.Add(level);
            _levelsGrid.SelectedIndex = Math.Min(_levels.Count - 1, selectedIndex + 1);
        };
        helperRow.Children.Add(add);
        helperRow.Children.Add(remove);
        helperRow.Children.Add(copy);
        helperRow.Children.Add(moveUp);
        helperRow.Children.Add(moveDown);
        commandDock.Children.Add(helperRow);
        var options = new Button
        {
            Content = "_Options...",
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
        var ok = new Button { Content = "_OK", IsDefault = true, Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            ResultSortKeys = BuildSortKeys(_levels);
            ResultHasHeaders = _headerCheck.IsChecked == true;
            ResultOptions = _options;
            DialogResult = true;
        };
        var cancel = new Button { Content = "_Cancel", IsCancel = true, Width = 76 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
    {
        _levelsGrid.SelectedIndex = 0;
        _levelsGrid.Focus();
        Keyboard.Focus(_levelsGrid);
    }

    private void UpdateColumnChoices()
    {
        _sortByColumn.Header = _options.LeftToRight ? "Sort by row" : "Sort by";
        _headerCheck.IsEnabled = !_options.LeftToRight;
        _sortByColumn.ItemsSource = _options.LeftToRight
            ? _rowChoices
            : _headerCheck.IsChecked == true
            ? _columnChoices
            : _genericColumnChoices;
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
        level.SetColorChoices(SortOnFromLabel(level.SortOn) switch
        {
            Freexcel.Core.Commands.SortOn.CellColor => _cellColorChoices,
            Freexcel.Core.Commands.SortOn.FontColor => _fontColorChoices,
            _ => [new SortColorChoice("")]
        });
    }

    private static DataGridTemplateColumn CreateOrderColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = "Order",
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
            Header = "Color",
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

