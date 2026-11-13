using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private readonly Border _backgroundPreview;
    private readonly TextBox _fillColorText;
    private readonly TextBox _patternColorText;
    private bool _syncingClearCheck;
    private bool _suppressRefresh;

    public FormatCellsFillEditor(
        RecentColorsStore recentColors,
        Func<string, CellColor, Task<CellColor?>> showMoreColorsAsync,
        Func<string, string> getText,
        WorkbookTheme theme,
        CellStyle current,
        Action<Func<Task>>? runGuarded = null)
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
            getText("FormatCells_MoreFillColors"),
            runGuarded);
        PatternColorPicker = new FormatCellsColorPicker(
            recentColors,
            showMoreColorsAsync,
            getText("FormatCells_NoChange"),
            includeClear: false,
            getText("FormatCells_MorePatternColors"),
            runGuarded);
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

        // Layout mirrors WPF FormatCellsDialog.xaml (Fill tab): a 2.4* / 1.1* grid inset 8px. The
        // left column stacks the stretched 10x3 swatch grid, the colour inputs (background on the
        // left half with its own preview strip, pattern colour on the right half) and the
        // pattern-style row with its 150px pattern sample; the right column holds the sample.
        var fillPaletteHeading = CreateHeading(
            getText("FormatCells_BackgroundColor"),
            new Thickness(0, 4, 0, 4));
        fillPalette.Margin = new Thickness(0, 0, 0, 8);

        _backgroundPreview = new Border
        {
            Height = 22,
            Margin = new Thickness(0, 4, 60, 0),
            Background = Brushes.White,
            // WPF frames the strip lighter than the swatches: #B6B6B6 at the top to #ACACAC below.
            BorderBrush = BackgroundPreviewFrame,
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetAutomationId(_backgroundPreview, "FormatCellsFillBackgroundPreview");
        var fillInputField = new StackPanel
        {
            Margin = new Thickness(0, 0, 10, 0),
            Children =
            {
                CreateLabel(
                    getText("FormatCells_BackgroundColor2"),
                    new Thickness(0, 0, 0, 2)),
                CreateColorInputRow(_fillColorText, FillColorPicker),
                _backgroundPreview,
            },
        };

        patternPalette.Margin = new Thickness(0, 0, 0, 6);
        var patternField = new StackPanel
        {
            Children =
            {
                CreateHeading(
                    getText("FormatCells_PatternColor"),
                    new Thickness(0, 0, 0, 4)),
                patternPalette,
                CreateLabel(
                    getText("FormatCells_PatternColor2"),
                    new Thickness(0, 0, 0, 2)),
                CreateColorInputRow(_patternColorText, PatternColorPicker),
            },
        };

        _patternPreview = CreatePreview("FormatCellsPatternPreview", getText("FormatCells_Pattern"), large: false);
        _patternPreview.VerticalAlignment = AvaloniaVerticalAlignment.Center;
        _samplePreview = CreatePreview("FormatCellsFillSamplePreview", getText("FormatCells_Sample"), large: true);

        FillPatternStyleBox.Width = double.NaN;
        FillPatternStyleBox.MinWidth = 0;
        FillPatternStyleBox.Height = FillWpfPatternStyleBoxHeight;
        FillPatternStyleBox.MinHeight = FillWpfPatternStyleBoxHeight;
        FillPatternStyleBox.MaxHeight = FillWpfPatternStyleBoxHeight;
        ClearFillCheckBox.Margin = new Thickness(0, 12, 0, 0);
        var patternStyleField = new StackPanel
        {
            Margin = new Thickness(0, 0, 10, 0),
            Children =
            {
                CreateHeading(
                    getText("FormatCells_PatternStyle"),
                    new Thickness(0, 0, 0, 2)),
                CreateLabel(
                    getText("FormatCells_PatternStyle2"),
                    new Thickness(0, 0, 0, 2)),
                FillPatternStyleBox,
                ClearFillCheckBox,
            },
        };

        var inputGrid = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Children = { fillInputField, patternField },
        };
        AvaloniaGrid.SetColumn(fillInputField, 0);
        AvaloniaGrid.SetColumn(patternField, 1);

        var styleGrid = new AvaloniaGrid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("*,150"),
            Children = { patternStyleField, _patternPreview },
        };
        AvaloniaGrid.SetColumn(patternStyleField, 0);
        AvaloniaGrid.SetColumn(_patternPreview, 1);

        var left = new AvaloniaGrid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Children = { fillPaletteHeading, fillPalette, inputGrid, styleGrid },
        };
        AvaloniaGrid.SetRow(fillPaletteHeading, 0);
        AvaloniaGrid.SetRow(fillPalette, 1);
        AvaloniaGrid.SetRow(inputGrid, 2);
        AvaloniaGrid.SetRow(styleGrid, 3);

        var right = new StackPanel
        {
            Children =
            {
                CreateHeading(
                    getText("FormatCells_Sample"),
                    new Thickness(0, 4, 0, 2)),
                _samplePreview,
            },
        };

        View = new AvaloniaGrid
        {
            Margin = new Thickness(8),
            ColumnDefinitions = new ColumnDefinitions("2.4*,1.1*"),
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

    /// <summary>WPF docks the 54px pick button right (6px gap) and lets the RGB box fill the rest.</summary>
    private static DockPanel CreateColorInputRow(TextBox textBox, FormatCellsColorPicker picker)
    {
        textBox.Width = double.NaN;
        textBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
        picker.Margin = new Thickness(6, 0, 0, 0);
        var row = new DockPanel();
        DockPanel.SetDock(picker, Dock.Right);
        row.Children.Add(picker);
        row.Children.Add(textBox);
        return row;
    }

    // WPF FormatCellsDialog.xaml (Fill tab): a TextBlock or zero-padding Label at the 12px dialog
    // font is one 16px text line. Avalonia's line box at this font is shorter, so each heading and
    // label lifted everything below it; four of them put the pattern-style block about 11px high.
    private const double FillWpfTextLineHeight = 16;
    // FormatCellsDialog.xaml: DlgFillPatternStyleBox Height="24".
    private const double FillWpfPatternStyleBoxHeight = 24;

    /// <summary>A semi-bold section heading (WPF TextBlock FontWeight=SemiBold).</summary>
    private static TextBlock CreateHeading(string text, Thickness margin) =>
        new()
        {
            Text = StripMnemonic(text),
            FontWeight = FontWeight.SemiBold,
            Margin = margin,
            MinHeight = FillWpfTextLineHeight,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
        };

    /// <summary>A regular-weight field label (WPF Label with zero padding).</summary>
    private static TextBlock CreateLabel(string text, Thickness margin) =>
        new()
        {
            Text = StripMnemonic(text),
            Margin = margin,
            MinHeight = FillWpfTextLineHeight,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
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
        // WPF hosts the swatches in a UniformGrid stretched across the column, so each
        // fixed-size swatch is centred in an evenly spaced cell.
        var panel = new UniformGrid
        {
            Columns = columns,
            Rows = (entries.Count + columns - 1) / columns,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
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
                MinWidth = 0,
                MinHeight = 0,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
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
            MinWidth = 0,
            MinHeight = 0,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };
        AutomationProperties.SetName(more, _getText(resourceKey));
        AutomationProperties.SetAutomationId(more, $"{automationId}MoreButton{index}");
        more.Click += (_, _) => picker.Flyout?.ShowAt(more);
        return more;
    }

    private static readonly IBrush PreviewFrameTopBottom = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
    private static readonly IBrush PreviewFrameSides = GradientBrush(
        new RelativePoint(1, 0, RelativeUnit.Relative),
        Color.FromRgb(0xC4, 0xC4, 0xC4),
        Color.FromRgb(0xDA, 0xDA, 0xDA));
    private static readonly IBrush BackgroundPreviewFrame = GradientBrush(
        new RelativePoint(0, 1, RelativeUnit.Relative),
        Color.FromRgb(0xB6, 0xB6, 0xB6),
        Color.FromRgb(0xAC, 0xAC, 0xAC));

    private static IBrush GradientBrush(RelativePoint end, Color from, Color to) =>
        new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = end,
            GradientStops =
            {
                new GradientStop(from, 0),
                new GradientStop(to, 1),
            },
        };

    private Border CreatePreview(string automationId, string label, bool large)
    {
        var surface = new Border
        {
            Background = Brushes.White,
            // WPF draws the samples in an etched frame: #A0A0A0 along the top and bottom, a lighter
            // #C4C4C4 left and #DADADA right edge (the side lines are added inside the content).
            BorderBrush = PreviewFrameTopBottom,
            BorderThickness = new Thickness(0, 1, 0, 1),
            // WPF: 146px sample and 62px pattern sample, both as wide as their grid column.
            Height = large ? 146 : 62,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
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
        // Index 3, after the fill, pattern and caption that SetPreview addresses by index: the
        // frame's side lines, reaching out through the padding to the surface edge.
        content.Children.Add(new Border
        {
            Margin = new Thickness(-8),
            BorderThickness = new Thickness(1, 0, 1, 0),
            BorderBrush = PreviewFrameSides,
            IsHitTestVisible = false,
        });
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
        // WPF UpdateFillPreview paints the strip under the background colour box with the fill.
        _backgroundPreview.Background = background;
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
