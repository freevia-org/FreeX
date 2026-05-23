using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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

public sealed class SortDialog : Window
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

        var list = new DataGrid
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
        list.Columns.Add(_sortByColumn);
        list.Columns.Add(new DataGridComboBoxColumn
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
        list.Columns.Add(CreateOrderColumn());
        list.Columns.Add(CreateColorColumn());
        DockPanel.SetDock(list, Dock.Top);
        root.Children.Add(list);

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
            var selectedIndex = list.SelectedIndex < 0 ? _levels.Count - 1 : list.SelectedIndex;
            var updated = RemoveLevel(_levels, selectedIndex);
            _levels.Clear();
            foreach (var level in updated)
                _levels.Add(level);
        };
        var copy = new Button { Content = "_Copy Level", Width = 98 };
        copy.Click += (_, _) =>
        {
            var selectedIndex = list.SelectedIndex < 0 ? _levels.Count - 1 : list.SelectedIndex;
            var updated = CopyLevel(_levels, selectedIndex);
            _levels.Clear();
            foreach (var level in updated)
                _levels.Add(level);
            list.SelectedIndex = Math.Min(selectedIndex + 1, _levels.Count - 1);
        };
        var moveUp = new Button { Content = "Move _Up", Width = 86, Margin = new Thickness(8, 0, 8, 0) };
        moveUp.Click += (_, _) =>
        {
            var selectedIndex = list.SelectedIndex < 0 ? 0 : list.SelectedIndex;
            var updated = MoveLevel(_levels, selectedIndex, -1);
            _levels.Clear();
            foreach (var level in updated)
                _levels.Add(level);
            list.SelectedIndex = Math.Max(0, selectedIndex - 1);
        };
        var moveDown = new Button { Content = "Move Do_wn", Width = 92 };
        moveDown.Click += (_, _) =>
        {
            var selectedIndex = list.SelectedIndex < 0 ? _levels.Count - 1 : list.SelectedIndex;
            var updated = MoveLevel(_levels, selectedIndex, 1);
            _levels.Clear();
            foreach (var level in updated)
                _levels.Add(level);
            list.SelectedIndex = Math.Min(_levels.Count - 1, selectedIndex + 1);
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
    }

    public static IReadOnlyList<SortKey> BuildSortKeys(IEnumerable<SortDialogLevel> levels)
    {
        return NormalizeLevels(levels)
            .Select(level =>
            {
                var sortOn = SortOnFromLabel(level.SortOn);
                return new SortKey(level.ColumnOffset, level.Ascending, sortOn, TargetColorFromText(level.TargetColor, sortOn));
            })
            .ToList();
    }

    public static IReadOnlyList<SortDirectionChoice> BuildOrderChoices(string? sortOn) =>
        SortOnFromLabel(sortOn) is Freexcel.Core.Commands.SortOn.CellColor or Freexcel.Core.Commands.SortOn.FontColor
            ? ColorDirectionChoices
            : DirectionChoices;

    public static IReadOnlyList<SortDialogLevel> AddLevel(
        IEnumerable<SortDialogLevel> levels,
        uint columnOffset = 0,
        bool ascending = true)
    {
        return NormalizeLevels(levels)
            .Append(new SortDialogLevel(columnOffset, ascending))
            .ToList();
    }

    public static IReadOnlyList<SortDialogLevel> RemoveLevel(IEnumerable<SortDialogLevel> levels, int index)
    {
        var updated = NormalizeLevels(levels).ToList();
        if (index >= 0 && index < updated.Count)
            updated.RemoveAt(index);

        return updated.Count == 0 ? [new SortDialogLevel(0, true)] : updated;
    }

    public static IReadOnlyList<SortDialogLevel> CopyLevel(IEnumerable<SortDialogLevel> levels, int index)
    {
        var updated = NormalizeLevels(levels).ToList();
        if (index >= 0 && index < updated.Count)
        {
            var level = updated[index];
            updated.Insert(index + 1, new SortDialogLevel(level.ColumnOffset, level.Ascending)
            {
                SortOn = level.SortOn,
                TargetColor = level.TargetColor
            });
        }

        return updated;
    }

    public static IReadOnlyList<SortDialogLevel> MoveLevel(IEnumerable<SortDialogLevel> levels, int index, int direction)
    {
        var updated = NormalizeLevels(levels).ToList();
        var targetIndex = index + Math.Sign(direction);
        if (index < 0 || index >= updated.Count || targetIndex < 0 || targetIndex >= updated.Count)
            return updated;

        (updated[index], updated[targetIndex]) = (updated[targetIndex], updated[index]);
        return updated;
    }

    public static IReadOnlyList<SortDialogLevel> UpdateLevel(
        IEnumerable<SortDialogLevel> levels,
        int index,
        uint columnOffset,
        bool ascending)
    {
        var updated = NormalizeLevels(levels).ToList();
        if (index >= 0 && index < updated.Count)
            updated[index] = new SortDialogLevel(columnOffset, ascending)
            {
                SortOn = updated[index].SortOn,
                TargetColor = updated[index].TargetColor
            };

        return updated;
    }

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(GridRange range)
    {
        return BuildColumnChoices(null, range, hasHeaders: false);
    }

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(Sheet? sheet, GridRange range, bool hasHeaders)
    {
        var choices = new List<SortColumnChoice>();
        for (uint offset = 0; offset < range.ColCount; offset++)
        {
            var columnName = CellAddress.NumberToColumnName(range.Start.Col + offset);
            var label = hasHeaders && sheet is not null
                ? GetHeaderLabel(sheet, range, offset, columnName)
                : $"Column {columnName}";
            choices.Add(new SortColumnChoice(label, offset));
        }

        return choices.Count == 0 ? [new SortColumnChoice("Column A", 0)] : choices;
    }

    public static IReadOnlyList<SortColumnChoice> BuildRowChoices(GridRange range)
    {
        var choices = new List<SortColumnChoice>();
        for (uint offset = 0; offset < range.RowCount; offset++)
            choices.Add(new SortColumnChoice($"Row {range.Start.Row + offset}", offset));

        return choices.Count == 0 ? [new SortColumnChoice("Row 1", 0)] : choices;
    }

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(Workbook workbook, Sheet? sheet, GridRange range)
    {
        if (sheet is null)
            return [new SortColorChoice("")];

        var colors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in range.AllCells())
        {
            var cell = sheet.GetCell(address);
            var style = workbook.GetStyle(cell?.StyleId ?? StyleId.Default);
            if (style.FillColor is { } fillColor)
                colors.Add(ColorInputParser.FormatHexColor(fillColor));
            if (style.FontColor is { } fontColor)
                colors.Add(ColorInputParser.FormatHexColor(fontColor));
        }

        return [new SortColorChoice(""), .. colors.Select(color => new SortColorChoice(color))];
    }

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(
        Workbook workbook,
        Sheet? sheet,
        GridRange range,
        Freexcel.Core.Commands.SortOn sortOn)
    {
        if (sheet is null)
            return [new SortColorChoice("")];

        var colors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in range.AllCells())
        {
            var cell = sheet.GetCell(address);
            var style = workbook.GetStyle(cell?.StyleId ?? StyleId.Default);
            var color = sortOn == Freexcel.Core.Commands.SortOn.FontColor
                ? style.FontColor
                : style.FillColor;
            if (color is { } resolvedColor)
                colors.Add(ColorInputParser.FormatHexColor(resolvedColor));
        }

        return [new SortColorChoice(""), .. colors.Select(color => new SortColorChoice(color))];
    }

    public static GridRange ExcludeHeaderRow(GridRange range, bool hasHeaders)
    {
        if (!hasHeaders || range.Start.Row >= range.End.Row)
            return range;

        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row + 1, range.Start.Col),
            range.End);
    }

    private static IReadOnlyList<SortDialogLevel> NormalizeLevels(IEnumerable<SortDialogLevel>? levels)
    {
        var normalized = levels?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortDialogLevel(0, true)] : normalized;
    }

    private static IReadOnlyList<SortColumnChoice> NormalizeColumnChoices(IEnumerable<SortColumnChoice>? choices)
    {
        var normalized = choices?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortColumnChoice("Column A", 0)] : normalized;
    }

    private static IReadOnlyList<SortColorChoice> NormalizeColorChoices(IEnumerable<SortColorChoice>? choices)
    {
        var normalized = choices?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortColorChoice("")] : normalized;
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

    private static string GetHeaderLabel(Sheet sheet, GridRange range, uint offset, string fallbackColumnName)
    {
        var address = new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col + offset);
        var text = sheet.GetCell(address)?.Value switch
        {
            TextValue value => value.Value.Trim(),
            NumberValue value => value.Value.ToString("G15", System.Globalization.CultureInfo.CurrentCulture),
            DateTimeValue value => value.Value.ToString("d", System.Globalization.CultureInfo.CurrentCulture),
            BoolValue value => value.Value ? "TRUE" : "FALSE",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(text) ? $"Column {fallbackColumnName}" : text;
    }

    private static Freexcel.Core.Commands.SortOn SortOnFromLabel(string? label) =>
        label switch
        {
            "Cell Color" => Freexcel.Core.Commands.SortOn.CellColor,
            "Font Color" => Freexcel.Core.Commands.SortOn.FontColor,
            _ => Freexcel.Core.Commands.SortOn.CellValues
        };

    private static CellColor? TargetColorFromText(string? text, Freexcel.Core.Commands.SortOn sortOn)
    {
        if (sortOn is not Freexcel.Core.Commands.SortOn.CellColor and not Freexcel.Core.Commands.SortOn.FontColor)
            return null;

        return ColorInputParser.TryParseColorText(text ?? "", out var color) ? color : null;
    }
}

public sealed class SortOptionsDialog : Window
{
    private readonly CheckBox _caseSensitiveBox;
    private readonly RadioButton _topToBottomButton;
    private readonly RadioButton _leftToRightButton;

    public SortDialogOptions Result { get; private set; }

    public SortOptionsDialog(SortDialogOptions? current = null)
    {
        current ??= new SortDialogOptions();
        Result = current;
        Title = "Sort Options";
        Width = 330;
        Height = 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var body = new StackPanel();
        DockPanel.SetDock(body, Dock.Top);
        root.Children.Add(body);

        _caseSensitiveBox = new CheckBox
        {
            Content = "_Case sensitive",
            IsChecked = current.CaseSensitive,
            Margin = new Thickness(0, 0, 0, 10)
        };
        body.Children.Add(_caseSensitiveBox);

        _topToBottomButton = new RadioButton { Content = "Sort top to _bottom", IsChecked = !current.LeftToRight };
        _leftToRightButton = new RadioButton { Content = "Sort left to _right", IsChecked = current.LeftToRight };

        var orientation = new GroupBox
        {
            Header = "Orientation",
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10),
            Content = new StackPanel
            {
                Children =
                {
                    _topToBottomButton,
                    _leftToRightButton
                }
            }
        };
        body.Children.Add(orientation);

        root.Children.Add(DialogButtonRowFactory.Create(() =>
        {
            Result = new SortDialogOptions(
                CaseSensitive: _caseSensitiveBox.IsChecked == true,
                LeftToRight: _leftToRightButton.IsChecked == true);
            DialogResult = true;
        }, buttonWidth: 72));
        Content = root;
    }
}
