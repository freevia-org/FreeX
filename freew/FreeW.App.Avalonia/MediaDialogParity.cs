using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class ImageAdjustDialog : FreeWDialogWindow
{
    private readonly TextBox _brightness;
    private readonly TextBox _contrast;
    private readonly TextBox _saturation;
    private readonly TextBox _transparency;
    private readonly TextBlock _status = new();

    private ImageAdjustDialog(double brightness, double contrast, double saturation, double transparency)
    {
        Title = "Picture Corrections";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var state = ImageAdjustDialogPlanner.BuildInitialState(
            brightness, contrast, saturation, transparency, CultureInfo.CurrentCulture);
        _brightness = Box(state.BrightnessText);
        _contrast = Box(state.ContrastText);
        _saturation = Box(state.SaturationText);
        _transparency = Box(state.TransparencyText);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome.Style, new Thickness(0, 6, 0, 0));

        var grid = Chrome.CreateGrid(5);
        Chrome.AddField(grid, "Brightness (-100 to 100):", _brightness, 0);
        Chrome.AddField(grid, "Contrast (-100 to 100):", _contrast, 1);
        Chrome.AddField(grid, "Saturation (0 to 400):", _saturation, 2);
        Chrome.AddField(grid, "Transparency (0 to 100):", _transparency, 3);
        Grid.SetRow(_status, 4);
        Grid.SetColumnSpan(_status, 2);
        grid.Children.Add(_status);

        var ok = Chrome.Button("OK", Accept, isDefault: true);
        var cancel = Chrome.Button("Cancel", () => Close(null), isCancel: true);
        Content = new Border
        {
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Children =
                {
                    grid,
                    AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)),
                },
            },
        };
        Opened += (_, _) => Chrome.FocusAndSelect(_brightness);
        Chrome.Escape(this, () => Close(null));
    }

    public static Task<ImageAdjustDialogResult?> ShowAsync(
        Window owner,
        double brightness,
        double contrast,
        double saturation,
        double transparency) =>
        new ImageAdjustDialog(brightness, contrast, saturation, transparency)
            .ShowDialog<ImageAdjustDialogResult?>(owner);

    private void Accept()
    {
        if (ImageAdjustDialogPlanner.TryBuildResult(
                new ImageAdjustDialogInput(_brightness.Text, _contrast.Text, _saturation.Text, _transparency.Text),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            Close(result);
            return;
        }

        _status.Text = validation?.Message ?? ImageAdjustDialogPlanner.BrightnessValidationMessage;
        Chrome.FocusAndSelect(validation?.Field switch
        {
            ImageAdjustDialogField.Contrast => _contrast,
            ImageAdjustDialogField.Saturation => _saturation,
            ImageAdjustDialogField.Transparency => _transparency,
            _ => _brightness,
        });
    }

    private static TextBox Box(string text) => Chrome.TextBox(text, 90);
}

internal sealed class ImagePositionDialog : FreeWDialogWindow
{
    private readonly TextBox _horizontal;
    private readonly TextBox _vertical;
    private readonly ComboBox _horizontalAnchor;
    private readonly ComboBox _verticalAnchor;
    private readonly TextBlock _status = new();

    private ImagePositionDialog(
        double horizontalOffset,
        double verticalOffset,
        HorizontalAnchor horizontalAnchor,
        VerticalAnchor verticalAnchor,
        string title,
        bool isGroupLocal)
    {
        Title = title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var state = ImagePositionDialogPlanner.BuildInitialState(
            horizontalOffset, verticalOffset, horizontalAnchor, verticalAnchor, CultureInfo.CurrentCulture);
        _horizontal = Chrome.TextBox(state.HorizontalOffsetText, 100);
        _vertical = Chrome.TextBox(state.VerticalOffsetText, 100);
        _horizontalAnchor = Chrome.Combo(
            ImagePositionDialogPlanner.HorizontalAnchorItems.Select(item => item.Label),
            state.HorizontalAnchorIndex);
        _verticalAnchor = Chrome.Combo(
            ImagePositionDialogPlanner.VerticalAnchorItems.Select(item => item.Label),
            state.VerticalAnchorIndex);
        _horizontalAnchor.IsEnabled = !isGroupLocal;
        _verticalAnchor.IsEnabled = !isGroupLocal;
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome.Style, new Thickness(0, 6, 0, 0));

        var grid = Chrome.CreateGrid(7);
        Chrome.AddField(grid, "Horizontal offset (pt):", _horizontal, 0);
        Chrome.AddField(grid, "Relative to:", _horizontalAnchor, 1);
        Chrome.AddField(grid, "Vertical offset (pt):", _vertical, 2);
        Chrome.AddField(grid, "Relative to:", _verticalAnchor, 3);
        Grid.SetRow(_status, 4);
        Grid.SetColumnSpan(_status, 2);
        grid.Children.Add(_status);
        var ok = Chrome.Button("OK", Accept, isDefault: true);
        var cancel = Chrome.Button("Cancel", () => Close(null), isCancel: true);
        Chrome.Place(grid, AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)), 5, 1);
        Content = new Border { Padding = new Thickness(14), Child = grid };
        Opened += (_, _) => Chrome.FocusAndSelect(_horizontal);
        Chrome.Escape(this, () => Close(null));
    }

    public static Task<ImagePositionDialogResult?> ShowAsync(
        Window owner,
        double horizontalOffset,
        double verticalOffset,
        HorizontalAnchor horizontalAnchor,
        VerticalAnchor verticalAnchor,
        string title = "Picture Position",
        bool isGroupLocal = false) =>
        new ImagePositionDialog(
            horizontalOffset,
            verticalOffset,
            horizontalAnchor,
            verticalAnchor,
            title,
            isGroupLocal)
            .ShowDialog<ImagePositionDialogResult?>(owner);

    private void Accept()
    {
        if (ImagePositionDialogPlanner.TryBuildResult(
                new ImagePositionDialogInput(
                    _horizontal.Text, _vertical.Text, _horizontalAnchor.SelectedIndex, _verticalAnchor.SelectedIndex),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            Close(result);
            return;
        }

        _status.Text = validation?.Message ?? ImagePositionDialogPlanner.OffsetValidationMessage;
        Chrome.FocusAndSelect(validation?.Field == ImagePositionDialogField.VerticalOffset ? _vertical : _horizontal);
    }
}

internal sealed class ChartTitleDialog : FreeWDialogWindow
{
    private readonly TextBox _title;

    private ChartTitleDialog(string? currentTitle)
    {
        Title = "Chart Title";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        _title = Chrome.TextBox(currentTitle ?? string.Empty, 220);

        var grid = Chrome.CreateGrid(2);
        Chrome.AddField(grid, "Title:", _title, 0);
        Chrome.Place(grid, Chrome.ActionRow(Accept, () => Close(null)), 1, 1);
        Content = new Border { Padding = new Thickness(14), Child = grid };
        Opened += (_, _) => Chrome.FocusAndSelect(_title);
        Chrome.Escape(this, () => Close(null));
    }

    public static Task<ChartTitleDialogResult?> ShowAsync(Window owner, string? currentTitle) =>
        new ChartTitleDialog(currentTitle).ShowDialog<ChartTitleDialogResult?>(owner);

    private void Accept() => Close(ChartTitleDialogPlanner.BuildResult(_title.Text));
}

internal sealed class ChartAxisTitlesDialog : FreeWDialogWindow
{
    private readonly TextBox _category;
    private readonly TextBox _value;

    private ChartAxisTitlesDialog(string? categoryTitle, string? valueTitle)
    {
        Title = "Axis Titles";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        _category = Chrome.TextBox(categoryTitle ?? string.Empty, 220);
        _value = Chrome.TextBox(valueTitle ?? string.Empty, 220);

        var grid = Chrome.CreateGrid(3);
        Chrome.AddField(grid, "Category axis:", _category, 0);
        Chrome.AddField(grid, "Value axis:", _value, 1);
        Chrome.Place(grid, Chrome.ActionRow(Accept, () => Close(null)), 2, 1);
        Content = new Border { Padding = new Thickness(14), Child = grid };
        Opened += (_, _) => Chrome.FocusAndSelect(_category);
        Chrome.Escape(this, () => Close(null));
    }

    public static Task<ChartAxisTitlesDialogResult?> ShowAsync(
        Window owner,
        string? categoryTitle,
        string? valueTitle) =>
        new ChartAxisTitlesDialog(categoryTitle, valueTitle).ShowDialog<ChartAxisTitlesDialogResult?>(owner);

    private void Accept() => Close(ChartAxisTitlesDialogPlanner.BuildResult(_category.Text, _value.Text));
}

internal sealed class ChartSizeDialog : FreeWDialogWindow
{
    private readonly TextBox _width;
    private readonly TextBox _height;
    private readonly TextBlock _status = new();

    private ChartSizeDialog(double widthPt, double heightPt)
    {
        Title = "Chart Size";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        var state = ChartSizeDialogPlanner.BuildInitialState(widthPt, heightPt, CultureInfo.CurrentCulture);
        _width = Chrome.TextBox(state.WidthText, 120);
        _height = Chrome.TextBox(state.HeightText, 120);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome.Style, new Thickness(0, 6, 0, 0));

        var grid = Chrome.CreateGrid(4);
        Chrome.AddField(grid, "Width (pt):", _width, 0);
        Chrome.AddField(grid, "Height (pt):", _height, 1);
        Grid.SetRow(_status, 2);
        Grid.SetColumnSpan(_status, 2);
        grid.Children.Add(_status);
        Chrome.Place(grid, Chrome.ActionRow(Accept, () => Close(null)), 3, 1);
        Content = new Border { Padding = new Thickness(14), Child = grid };
        Opened += (_, _) => Chrome.FocusAndSelect(_width);
        Chrome.Escape(this, () => Close(null));
    }

    public static Task<ChartSizeDialogResult?> ShowAsync(Window owner, double widthPt, double heightPt) =>
        new ChartSizeDialog(widthPt, heightPt).ShowDialog<ChartSizeDialogResult?>(owner);

    private void Accept()
    {
        if (ChartSizeDialogPlanner.TryBuildResult(
                new ChartSizeDialogInput(_width.Text, _height.Text),
                CultureInfo.CurrentCulture,
                out var result,
                out var errorMessage))
        {
            Close(result);
            return;
        }

        _status.Text = errorMessage ?? ChartSizeDialogPlanner.WidthValidationMessage;
        Chrome.FocusAndSelect(errorMessage == ChartSizeDialogPlanner.HeightValidationMessage ? _height : _width);
    }
}

internal sealed class InsertSmartArtDialog : FreeWDialogWindow
{
    private readonly ComboBox _kind;
    private readonly ListBox _nodes;
    private readonly TextBox _edit;
    private readonly TextBlock _status = new();
    private bool _updating;

    private InsertSmartArtDialog(SmartArt? seed)
    {
        Title = seed is null ? "Insert SmartArt" : "Edit SmartArt Text";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        var state = SmartArtDialogPlanner.BuildInitialState(seed);
        _kind = new ComboBox { ItemsSource = Enum.GetValues<SmartArtKind>(), SelectedItem = state.Kind, MinWidth = 180 };
        _nodes = new ListBox { MinHeight = 130, MaxHeight = 220 };
        foreach (var text in state.NodeTexts)
            _nodes.Items.Add(text);
        _nodes.SelectedIndex = 0;
        _edit = Chrome.TextBox(state.NodeTexts[0], 300);
        _nodes.SelectionChanged += (_, _) =>
        {
            if (_updating || _nodes.SelectedItem is not string text)
                return;
            _edit.Text = text;
        };
        _edit.TextChanged += (_, _) =>
        {
            if (_updating || _nodes.SelectedIndex < 0)
                return;
            _updating = true;
            _nodes.Items[_nodes.SelectedIndex] = _edit.Text ?? string.Empty;
            _updating = false;
        };
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome.Style, new Thickness(0, 6, 0, 0));

        var add = Chrome.Button("Add Shape", AddNode);
        var remove = Chrome.Button("Remove Shape", RemoveNode);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        actions.Children.Add(add);
        actions.Children.Add(remove);
        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock { Text = "Layout:", Margin = new Thickness(0, 0, 0, 4) },
                _kind,
                new TextBlock { Text = SmartArtDialogPlanner.NodeTextLabel, Margin = new Thickness(0, 10, 0, 4) },
                _nodes,
                _edit,
                actions,
                _status,
                Chrome.ActionRow(Accept, () => Close(null)),
            },
        };
        Opened += (_, _) => Chrome.FocusAndSelect(_edit);
        Chrome.Escape(this, () => Close(null));
    }

    public static Task<SmartArt?> ShowAsync(Window owner, SmartArt? seed = null) =>
        new InsertSmartArtDialog(seed).ShowDialog<SmartArt?>(owner);

    private void AddNode()
    {
        _nodes.Items.Add("New Item");
        _nodes.SelectedIndex = _nodes.Items.Count - 1;
        Chrome.FocusAndSelect(_edit);
    }

    private void RemoveNode()
    {
        if (_nodes.Items.Count <= 1 || _nodes.SelectedIndex < 0)
            return;
        var index = _nodes.SelectedIndex;
        _nodes.Items.RemoveAt(index);
        _nodes.SelectedIndex = Math.Min(index, _nodes.Items.Count - 1);
    }

    private void Accept()
    {
        var kind = _kind.SelectedItem is SmartArtKind selected ? selected : SmartArtKind.Process;
        if (SmartArtDialogPlanner.TryBuildResult(
                kind, _nodes.Items.Cast<string>(), out var result, out var errorMessage))
        {
            Close(result);
            return;
        }
        _status.Text = errorMessage ?? SmartArtDialogPlanner.EmptyNodesValidationMessage;
    }
}

internal sealed class InsertChartDialog : FreeWDialogWindow
{
    private sealed class RowControls
    {
        public required TextBox Category { get; init; }
        public required IReadOnlyList<TextBox> Values { get; init; }
        public required Grid View { get; init; }
    }

    private readonly ComboBox _kind;
    private readonly TextBox _title;
    private readonly StackPanel _rowsPanel = new();
    private readonly List<RowControls> _rows = [];
    private readonly IReadOnlyList<string> _seriesNames;
    private readonly TextBlock _status = new();

    private InsertChartDialog(Chart? seed)
    {
        Title = "Insert Chart";
        Width = 500;
        MinHeight = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        var state = InsertChartDialogPlanner.BuildInitialState(seed, CultureInfo.CurrentCulture);
        _seriesNames = state.SeriesNames;
        _kind = new ComboBox
        {
            ItemsSource = Enum.GetValues<ChartKind>(),
            SelectedItem = state.Kind,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(_kind, Chrome.Style);
        _title = Chrome.TextBox(state.Title, 0);
        _title.HorizontalAlignment = HorizontalAlignment.Stretch;
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Chrome.Style, new Thickness(0, 6, 0, 0));
        BuildTableHeader();
        foreach (var row in state.Rows)
            AddRow(row.Category, row.SeriesValues);

        var scroll = new ScrollViewer
        {
            Content = _rowsPanel,
            Height = 138,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var table = new Border
        {
            Background = new ImmutableSolidColorBrush(Color.FromRgb(240, 240, 240)),
            BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(104, 140, 175)),
            BorderThickness = new Thickness(1),
            Height = 140,
            Child = scroll,
        };

        var panel = new StackPanel();
        AddLabeledControl(panel, "Chart type:", _kind);
        AddLabeledControl(panel, "Title (optional):", _title);
        panel.Children.Add(new TextBlock
        {
            Text = "Chart data  (first column = category labels, remaining columns = series values):",
            Margin = new Thickness(0, 3, 0, 4),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(table);
        var actionPlans = InsertChartDialogPlanner.ActionButtons;
        panel.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [
                Chrome.Button(
                    ResolveShellButtonLabel(actionPlans[0].Label),
                    Accept,
                    isDefault: actionPlans[0].IsDefault),
                Chrome.Button(
                    ResolveShellButtonLabel(actionPlans[1].Label),
                    () => Close(null),
                    isCancel: actionPlans[1].IsCancel),
            ],
            new Thickness(0, 12, 0, 0)));
        Content = new Border { Padding = new Thickness(14), Child = panel };
        Opened += (_, _) => Chrome.FocusAndSelect(_title);
        Chrome.Escape(this, () => Close(null));
    }

    public static Task<Chart?> ShowAsync(Window owner, Chart? seed = null) =>
        new InsertChartDialog(seed).ShowDialog<Chart?>(owner);

    private void BuildTableHeader()
    {
        var header = new Grid
        {
            Background = Brushes.White,
            Height = 24,
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(100) } },
        };
        for (var i = 0; i < _seriesNames.Count; i++)
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = SeriesColumnWidth() });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddHeaderCell(header, "Category", 0);
        for (var i = 0; i < _seriesNames.Count; i++)
            AddHeaderCell(header, _seriesNames[i], i + 1);
        _rowsPanel.Children.Add(header);
    }

    private void AddRow(string category, IReadOnlyList<string> values)
    {
        var row = new Grid
        {
            Background = Brushes.White,
            Height = 22,
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(100) } },
        };
        for (var i = 0; i < _seriesNames.Count; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = SeriesColumnWidth() });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var categoryBox = CellBox(category);
        Chrome.Place(row, categoryBox, 0, 0);
        var boxes = new List<TextBox>();
        for (var i = 0; i < _seriesNames.Count; i++)
        {
            var box = CellBox(i < values.Count ? values[i] : string.Empty);
            boxes.Add(box);
            Chrome.Place(row, box, 0, i + 1);
        }
        var controls = new RowControls { Category = categoryBox, Values = boxes, View = row };
        _rows.Add(controls);
        _rowsPanel.Children.Add(row);
        ApplyRowContextMenu(controls);
        foreach (var box in new[] { categoryBox }.Concat(boxes))
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && ReferenceEquals(_rows.LastOrDefault(), controls))
                {
                    AddRow(string.Empty, _seriesNames.Select(_ => string.Empty).ToArray());
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete && _rows.Count > 1 && IsEmpty(controls))
                {
                    RemoveRow(controls);
                    e.Handled = true;
                }
            };
        }
    }

    private void RemoveRow(RowControls controls)
    {
        if (_rows.Count <= 1)
            return;
        _rows.Remove(controls);
        _rowsPanel.Children.Remove(controls.View);
    }

    private void ApplyRowContextMenu(RowControls controls)
    {
        var menu = new ContextMenu();
        var add = new MenuItem { Header = "Add Row" };
        add.Click += (_, _) => AddRow(string.Empty, _seriesNames.Select(_ => string.Empty).ToArray());
        var remove = new MenuItem { Header = "Remove Row" };
        remove.Click += (_, _) => RemoveRow(controls);
        menu.Items.Add(add);
        menu.Items.Add(remove);
        controls.View.ContextMenu = menu;
    }

    private void Accept()
    {
        var kind = _kind.SelectedItem is ChartKind selected ? selected : ChartKind.Column;
        var rows = _rows.Select(row => new InsertChartDialogRow(row.Category.Text ?? string.Empty, row.Values.Select(box => box.Text ?? string.Empty).ToArray()));
        if (InsertChartDialogPlanner.TryBuildResult(
                kind, _title.Text, _seriesNames, rows, CultureInfo.CurrentCulture, out var result, out var errorMessage))
        {
            Close(result);
            return;
        }
        _status.Text = errorMessage ?? InsertChartDialogPlanner.EmptyRowsValidationMessage;
    }

    private static void AddLabeledControl(StackPanel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        control.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(control);
    }

    private static void AddHeaderCell(Grid header, string text, int column)
    {
        var cell = new Border
        {
            Background = Brushes.White,
            BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(210, 210, 210)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = Chrome.Style.FontFamily,
                FontSize = Chrome.Style.FontSize,
                Padding = new Thickness(4, 3),
                VerticalAlignment = VerticalAlignment.Stretch,
            },
        };
        Grid.SetColumn(cell, column);
        header.Children.Add(cell);
    }

    private GridLength SeriesColumnWidth() =>
        _seriesNames.Count == 1 ? new GridLength(20) : new GridLength(1, GridUnitType.Star);

    private static TextBox CellBox(string text)
    {
        var box = new TextBox
        {
            Text = text,
            Background = Brushes.White,
            Height = 21,
            MinHeight = 21,
            MaxHeight = 21,
            Padding = new Thickness(4, 1),
            FontFamily = Chrome.Style.FontFamily,
            FontSize = Chrome.Style.FontSize,
            BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(210, 210, 210)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        return box;
    }

    private static bool IsEmpty(RowControls row) =>
        string.IsNullOrWhiteSpace(row.Category.Text)
        && row.Values.All(box => string.IsNullOrWhiteSpace(box.Text));

    private static string ResolveShellButtonLabel(string label) =>
        label.Equals("OK", StringComparison.OrdinalIgnoreCase)
            ? ShellStrings.Current.CreateAutomationName(ShellStrings.Current.Ok)
            : label.Equals("Cancel", StringComparison.OrdinalIgnoreCase)
                ? ShellStrings.Current.CreateAutomationName(ShellStrings.Current.Cancel)
                : label;
}

internal static class Chrome
{
    public static AvaloniaCompactDialogChromeStyle Style { get; } = AvaloniaCompactDialogChrome.WindowsStyle;

    public static TextBox TextBox(string text, double minWidth)
    {
        var box = new TextBox { Text = text, MinWidth = minWidth };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, Style);
        return box;
    }

    public static ComboBox Combo(IEnumerable<string> items, int selectedIndex)
    {
        var box = new ComboBox { ItemsSource = items.ToArray(), SelectedIndex = selectedIndex, MinWidth = 120 };
        AvaloniaCompactDialogChrome.ApplyComboBox(box, Style);
        return box;
    }

    public static Button Button(string text, Action action, bool isDefault = false, bool isCancel = false, double minWidth = 72)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, Style, minWidth, isDefault);
        button.Click += (_, _) => action();
        return button;
    }

    public static StackPanel ActionRow(Action accept, Action cancel) =>
        AvaloniaCompactDialogChrome.CreateActionRow([Button("OK", accept, isDefault: true), Button("Cancel", cancel, isCancel: true)], new Thickness(0, 12, 0, 0));

    public static Grid CreateGrid(int rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    public static void AddField(Grid grid, string label, Control field, int row)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, row == 0 ? 0 : 4, 8, 0) };
        Place(grid, text, row, 0);
        Place(grid, field, row, 1);
    }

    public static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    public static void FocusAndSelect(TextBox box)
    {
        box.Focus();
        box.SelectAll();
    }

    public static void Escape(Window window, Action close) => window.KeyDown += (_, e) =>
    {
        if (e.Key != Key.Escape)
            return;
        close();
        e.Handled = true;
    };
}
