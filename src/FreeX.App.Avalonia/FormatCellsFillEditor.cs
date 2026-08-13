using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation;
using FreeX.App.Services;
using FreeX.Core.Model;
using Free.Shared.Shell.Avalonia;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The complete Format Cells Fill surface. The palette and pattern catalog come from shared
/// planners; the control only owns Avalonia layout and the small amount of state needed to keep
/// the RGB fields, pickers, and live previews synchronized.
/// </summary>
internal sealed class FormatCellsFillEditor
{
    private readonly Func<string, string> _getText;
    private readonly WorkbookTheme _theme;
    private readonly Border _samplePreview;
    private readonly Border _patternPreview;
    private readonly TextBox _fillColorText;
    private readonly TextBox _patternColorText;
    private bool _syncingClearCheck;
    private bool _suppressRefresh;

    public FormatCellsFillEditor(
        RecentColorsStore recentColors,
        Func<string, CellColor, Task<CellColor?>> showMoreColorsAsync,
        Func<string, string> getText,
        WorkbookTheme theme,
        CellStyle current)
    {
        ArgumentNullException.ThrowIfNull(recentColors);
        ArgumentNullException.ThrowIfNull(showMoreColorsAsync);
        ArgumentNullException.ThrowIfNull(getText);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(current);

        _getText = getText;
        _theme = theme;

        FillColorPicker = new FormatCellsColorPicker(
            recentColors,
            showMoreColorsAsync,
            getText("FormatCells_NoChange"),
            includeClear: true,
            getText("FormatCells_MoreFillColors"));
        PatternColorPicker = new FormatCellsColorPicker(
            recentColors,
            showMoreColorsAsync,
            getText("FormatCells_NoChange"),
            includeClear: false,
            getText("FormatCells_MorePatternColors"));
        FillColorPicker.ConfigureCompactPickButton();
        PatternColorPicker.ConfigureCompactPickButton();
        AutomationProperties.SetName(FillColorPicker, getText("FormatCells_BackgroundColor2"));
        AutomationProperties.SetName(PatternColorPicker, getText("FormatCells_PatternColor2"));
        AutomationProperties.SetAutomationId(FillColorPicker, "FormatCellsFillColorBox");
        AutomationProperties.SetAutomationId(PatternColorPicker, "FormatCellsFillPatternColorBox");

        FillPatternStyleBox = CreatePatternStyleBox(current.FillPatternStyle);
        AutomationProperties.SetName(FillPatternStyleBox, getText("FormatCells_PatternStyle2"));
        AutomationProperties.SetAutomationId(FillPatternStyleBox, "FormatCellsFillPatternStyleBox");
        AvaloniaCompactDialogChrome.ApplyComboBox(FillPatternStyleBox, AvaloniaCompactDialogChrome.WindowsStyle);

        _fillColorText = CreateColorTextBox("FormatCellsFillColorTextBox", "FormatCells_BackgroundColor2");
        _patternColorText = CreateColorTextBox("FormatCellsFillPatternColorTextBox", "FormatCells_PatternColor2");

        var fillPalette = CreatePalette(
            FillColorPicker,
            FormatCellsFillPalettePlanner.BackgroundEntries,
            "FormatCellsFillPalette",
            columns: 10,
            cellWidth: 28,
            cellHeight: 20);
        var patternPalette = CreatePalette(
            PatternColorPicker,
            FormatCellsFillPalettePlanner.PatternEntries,
            "FormatCellsFillPatternPalette",
            columns: 8,
            cellWidth: 24,
            cellHeight: 19);

        ClearFillCheckBox = new CheckBox
        {
            Content = getText("FormatCells_ClearFill"),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        AutomationProperties.SetName(ClearFillCheckBox, getText("FormatCells_ClearFill"));
        AutomationProperties.SetAutomationId(ClearFillCheckBox, "FormatCellsClearFillCheckBox");
        AvaloniaCompactDialogChrome.ApplyCheckBox(ClearFillCheckBox, AvaloniaCompactDialogChrome.WindowsStyle);
        ClearFillCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_syncingClearCheck)
                return;

            if (ClearFillCheckBox.IsChecked != true &&
                (FillColorPicker.SelectedItem as FormatCellsColorChoice)?.Clear == true)
                FillColorPicker.SelectNoChange();
            if (!_suppressRefresh)
                RefreshTextAndPreview();
        };

        var fillPaletteField = CreateField(
            getText("FormatCells_BackgroundColor"),
            fillPalette);

        var fillInputField = CreateField(
            getText("FormatCells_BackgroundColor2"),
            CreateColorInputRow(_fillColorText, FillColorPicker, textWidth: 115));

        var patternField = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = StripMnemonic(getText("FormatCells_PatternColor")),
                    FontWeight = FontWeight.SemiBold,
                },
                patternPalette,
                CreateField(
                    getText("FormatCells_PatternColor2"),
                    CreateColorInputRow(_patternColorText, PatternColorPicker)),
            },
        };

        _patternPreview = CreatePreview("FormatCellsPatternPreview", getText("FormatCells_Pattern"), large: false);
        _patternPreview.Margin = new Thickness(-7, 15, 0, 0);
        _samplePreview = CreatePreview("FormatCellsFillSamplePreview", getText("FormatCells_Sample"), large: true);

        var patternStyleField = CreateField(
            getText("FormatCells_PatternStyle"),
            new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    FillPatternStyleBox,
                    ClearFillCheckBox,
                },
            });

        var inputGrid = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions("174,*"),
            ColumnSpacing = 10,
            Children = { fillInputField, patternField },
        };
        AvaloniaGrid.SetColumn(fillInputField, 0);
        AvaloniaGrid.SetColumn(patternField, 1);

        var styleGrid = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions("211,*"),
            ColumnSpacing = 10,
            Children = { patternStyleField, _patternPreview },
        };
        AvaloniaGrid.SetColumn(patternStyleField, 0);
        AvaloniaGrid.SetColumn(_patternPreview, 1);

        var left = new AvaloniaGrid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Width = 382,
            RowSpacing = 8,
            Children = { fillPaletteField, inputGrid, styleGrid },
        };
        AvaloniaGrid.SetRow(fillPaletteField, 0);
        AvaloniaGrid.SetRow(inputGrid, 1);
        AvaloniaGrid.SetRow(styleGrid, 2);

        var right = new StackPanel
        {
            Width = 177,
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = StripMnemonic(getText("FormatCells_Sample")), FontWeight = FontWeight.SemiBold },
                _samplePreview,
            },
        };

        View = new AvaloniaGrid
        {
            Margin = new Thickness(8),
            ColumnDefinitions = new ColumnDefinitions("*,177"),
            ColumnSpacing = 9,
            Children = { left, right },
        };
        AvaloniaGrid.SetColumn(left, 0);
        AvaloniaGrid.SetColumn(right, 1);
        AutomationProperties.SetAutomationId(View, "FormatCellsFillEditor");

        Seed(current);
        FillColorPicker.SelectionChanged += (_, _) =>
        {
            var choice = FillColorPicker.SelectedItem as FormatCellsColorChoice;
            if (choice?.Clear == true)
            {
                _syncingClearCheck = true;
                try { ClearFillCheckBox.IsChecked = true; }
                finally { _syncingClearCheck = false; }
            }
            else if (choice?.Color is not null)
            {
                _syncingClearCheck = true;
                try { ClearFillCheckBox.IsChecked = false; }
                finally { _syncingClearCheck = false; }
            }
            if (!_suppressRefresh)
                RefreshTextAndPreview();
        };
        PatternColorPicker.SelectionChanged += (_, _) =>
        {
            if (!_suppressRefresh)
                RefreshTextAndPreview();
        };
        FillPatternStyleBox.SelectionChanged += (_, _) =>
        {
            if (!_suppressRefresh)
                RefreshTextAndPreview();
        };
        RefreshTextAndPreview();
    }

    public Control View { get; }
    public FormatCellsColorPicker FillColorPicker { get; }
    public FormatCellsColorPicker PatternColorPicker { get; }
    public ComboBox FillPatternStyleBox { get; }
    public CheckBox ClearFillCheckBox { get; }
    internal TextBox FillColorTextBox => _fillColorText;
    internal TextBox PatternColorTextBox => _patternColorText;

    public CellColor? FillColor => (FillColorPicker.SelectedItem as FormatCellsColorChoice)?.Color;
    public CellColor? PatternColor => (PatternColorPicker.SelectedItem as FormatCellsColorChoice)?.Color;
    public bool ClearFill => ClearFillCheckBox.IsChecked == true;

    public CellFillPatternStyle PatternStyle =>
        (FillPatternStyleBox.SelectedItem as MainWindow.FormatCellsNullableChoice<CellFillPatternStyle>)?.Value
        ?? CellFillPatternStyle.None;

    private ComboBox CreatePatternStyleBox(CellFillPatternStyle current)
    {
        var choices = FormatCellsDialogPlanner.CreateFillPatternDisplayChoices(_getText)
            .Select(choice => new MainWindow.FormatCellsNullableChoice<CellFillPatternStyle>(choice.Label, choice.Style))
            .ToArray();
        var selected = choices.FirstOrDefault(choice => choice.Value == current) ?? choices[0];
        return new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = selected,
            MinWidth = 160,
            Width = 210,
        };
    }

    private TextBox CreateColorTextBox(string automationId, string nameKey)
    {
        var box = new TextBox
        {
            Width = 125,
            Height = 24,
            FontSize = 12,
            FontFamily = AvaloniaCompactDialogChrome.WindowsStyle.FontFamily,
        };
        AutomationProperties.SetName(box, _getText(nameKey));
        AutomationProperties.SetAutomationId(box, automationId);
        AvaloniaCompactDialogChrome.ApplyTextBox(box, AvaloniaCompactDialogChrome.WindowsStyle);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == global::Avalonia.Input.Key.Enter)
            {
                ApplyTextColor(box, ReferenceEquals(box, _fillColorText));
                e.Handled = true;
            }
        };
        box.LostFocus += (_, _) => ApplyTextColor(box, ReferenceEquals(box, _fillColorText));
        return box;
    }

    private StackPanel CreateColorInputRow(
        TextBox textBox,
        FormatCellsColorPicker picker,
        double textWidth = 125) =>
        new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                SetWidth(textBox, textWidth),
                picker,
            },
        };

    private static TextBox SetWidth(TextBox textBox, double width)
    {
        textBox.Width = width;
        return textBox;
    }

    private StackPanel CreateField(string label, Control content) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = StripMnemonic(label), FontWeight = FontWeight.SemiBold },
                content,
            },
        };

    public bool TryCommitInput(out string message, out Control? invalidControl)
    {
        if (!TryParseOrEmpty(_fillColorText.Text, out _, out var fillInvalid))
        {
            message = _getText("FormatCells_InvalidFillColorMessage");
            invalidControl = fillInvalid ? _fillColorText : null;
            return false;
        }

        if (!TryParseOrEmpty(_patternColorText.Text, out _, out var patternInvalid))
        {
            message = _getText("FormatCells_InvalidPatternColorMessage");
            invalidControl = patternInvalid ? _patternColorText : null;
            return false;
        }

        _suppressRefresh = true;
        try
        {
            ApplyTextColor(_fillColorText, isFill: true);
            ApplyTextColor(_patternColorText, isFill: false);
        }
        finally
        {
            _suppressRefresh = false;
        }
        RefreshTextAndPreview();
        message = string.Empty;
        invalidControl = null;
        return true;
    }

    private Control CreatePalette(
        FormatCellsColorPicker picker,
        IReadOnlyList<FormatCellsFillPaletteEntry> entries,
        string automationId,
        int columns,
        double cellWidth,
        double cellHeight)
    {
        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            MaxWidth = columns * cellWidth,
            ItemWidth = cellWidth,
            ItemHeight = cellHeight,
        };
        AutomationProperties.SetAutomationId(panel, automationId);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.IsMore)
            {
                var more = CreateMoreButton(picker, entry.ResourceKey, automationId, index, cellWidth, cellHeight);
                panel.Children.Add(more);
                continue;
            }

            var button = new Button
            {
                Width = cellWidth,
                Height = cellHeight,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                Background = entry.Color is { } color
                    ? new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))
                    : Brushes.White,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
            };
            AutomationProperties.SetName(button, _getText(entry.ResourceKey));
            AutomationProperties.SetAutomationId(button, $"{automationId}Swatch{index}");
            button.Click += (_, _) =>
            {
                if (entry.IsClear)
                    picker.SelectClear();
                else if (entry.Color is { } color)
                    picker.SelectColor(color);
            };
            panel.Children.Add(button);
        }

        return panel;
    }

    private Button CreateMoreButton(
        FormatCellsColorPicker picker,
        string resourceKey,
        string automationId,
        int index,
        double cellWidth,
        double cellHeight)
    {
        var more = new Button
        {
            Content = "...",
            Width = cellWidth,
            Height = cellHeight,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };
        AutomationProperties.SetName(more, _getText(resourceKey));
        AutomationProperties.SetAutomationId(more, $"{automationId}MoreButton{index}");
        more.Click += (_, _) => picker.Flyout?.ShowAt(more);
        return more;
    }

    private Border CreatePreview(string automationId, string label, bool large)
    {
        var surface = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Height = large ? 146 : 62,
            Width = large ? 177 : 150,
            Padding = new Thickness(8),
        };
        var content = new AvaloniaGrid();
        content.Children.Add(new Border { Background = Brushes.White });
        content.Children.Add(new AvaloniaRectangle { Fill = Brushes.Transparent, IsHitTestVisible = false });
        var text = new TextBlock
        {
            Text = label,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        content.Children.Add(text);
        surface.Child = content;
        AutomationProperties.SetAutomationId(surface, automationId);
        return surface;
    }

    private void Seed(CellStyle current)
    {
        if (current.FillColor is { } fill)
            FillColorPicker.SelectColor(fill);
        else if (current.FillThemeColor is { } fillTheme)
            FillColorPicker.SelectColor(fillTheme.Resolve(_theme));
        else
            FillColorPicker.SelectNoChange();

        if (current.FillPatternColor is { } pattern)
            PatternColorPicker.SelectColor(pattern);
        else if (current.FillPatternThemeColor is { } patternTheme)
            PatternColorPicker.SelectColor(patternTheme.Resolve(_theme));
        else
            PatternColorPicker.SelectNoChange();

        ClearFillCheckBox.IsChecked = false;
        _fillColorText.Text = FillColor is { } fillColor ? FormatRgb(fillColor) : string.Empty;
        _patternColorText.Text = PatternColor is { } patternColor ? FormatRgb(patternColor) : string.Empty;
    }

    private void ApplyTextColor(TextBox box, bool isFill)
    {
        var text = box.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            if (isFill)
                FillColorPicker.SelectNoChange();
            else
                PatternColorPicker.SelectNoChange();
            return;
        }

        if (TryParseColor(text, out var color))
        {
            if (isFill)
                FillColorPicker.SelectColor(color);
            else
                PatternColorPicker.SelectColor(color);
        }
    }

    private void RefreshTextAndPreview()
    {
        var fill = FillColor;
        var pattern = PatternColor;
        _fillColorText.Text = fill is { } fillColor ? FormatRgb(fillColor) : string.Empty;
        _patternColorText.Text = pattern is { } patternColor ? FormatRgb(patternColor) : string.Empty;

        var style = new CellStyle
        {
            FillColor = ClearFill ? null : fill,
            FillPatternStyle = ClearFill ? CellFillPatternStyle.None : PatternStyle,
            FillPatternColor = ClearFill ? null : pattern,
        };
        var background = ClearFill || fill is null ? Brushes.White : Brush(fill.Value);
        var patternBrush = ClearFill ? null : CellPatternFill.Build(style, _theme);
        SetPreview(_samplePreview, background, patternBrush, ClearFill ? _getText("FormatCells_NoFill") : _getText("FormatCells_Sample"));
        SetPreview(_patternPreview, background, patternBrush, _getText("FormatCells_Pattern"));
    }

    private static void SetPreview(Border preview, IBrush background, IBrush? pattern, string label)
    {
        if (preview.Child is not AvaloniaGrid grid || grid.Children.Count < 3)
            return;

        if (grid.Children[0] is Border fill)
            fill.Background = background;
        if (grid.Children[1] is AvaloniaRectangle overlay)
            overlay.Fill = pattern ?? Brushes.Transparent;
        if (grid.Children[2] is TextBlock text)
            text.Text = label;
    }

    private static IBrush Brush(CellColor color) =>
        new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));

    private static string FormatRgb(CellColor color) => ColorInputParser.FormatRgbColor(color);

    private static bool TryParseColor(string text, out CellColor color) =>
        ColorInputParser.TryParseColorText(text, out color);

    private static bool TryParseOrEmpty(string? text, out CellColor color, out bool invalid)
    {
        color = default;
        invalid = false;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (TryParseColor(text.Trim(), out color))
            return true;

        invalid = true;
        return false;
    }

    private static string StripMnemonic(string text) => text.Replace("_", string.Empty, StringComparison.Ordinal);
}
