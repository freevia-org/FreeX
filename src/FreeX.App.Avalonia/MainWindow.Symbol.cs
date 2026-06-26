using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly string[] SymbolPickerFontChoices =
    [
        "Segoe UI Symbol",
        "Segoe UI Emoji",
        "Segoe UI",
        "Calibri",
        "Arial",
        "Times New Roman",
        "Cambria Math",
    ];

    private static readonly string[] SymbolPickerRecentSymbols =
    [
        "\u20ac", "\u00a3", "\u00a5", "\u00a9", "\u00ae", "\u2122",
        "\u00b0", "\u00b1", "\u2192", "\u03c0", "\u221e", "\u2713",
    ];

    private static readonly IReadOnlyDictionary<string, string> SymbolPickerNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["\u00a1"] = "Inverted Exclamation Mark",
        ["\u00a2"] = "Cent Sign",
        ["\u00a3"] = "Pound Sign",
        ["\u00a4"] = "Currency Sign",
        ["\u00a5"] = "Yen Sign",
        ["\u00a7"] = "Section Sign",
        ["\u00a9"] = "Copyright Sign",
        ["\u00ae"] = "Registered Sign",
        ["\u00b0"] = "Degree Sign",
        ["\u00b1"] = "Plus-Minus Sign",
        ["\u00b5"] = "Micro Sign",
        ["\u00b6"] = "Pilcrow Sign",
        ["\u00d7"] = "Multiplication Sign",
        ["\u00f7"] = "Division Sign",
    };

    private async Task ShowSymbolPickerAsync()
    {
        var symbols = CreateLatinSupplementSymbols();
        var selectedSymbol = symbols[0];
        var selectedName = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var selectedSubset = new TextBlock { FontSize = 12, FontFamily = FormulaBarFontFamily, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var selectedCode = new TextBox
        {
            Width = 120,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            BorderBrush = Brush(130, 130, 130),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var preview = new TextBlock
        {
            FontSize = 44,
            Width = 116,
            Height = 96,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dialog = new Window
        {
            Title = "Symbol",
            Width = 840,
            Height = 620,
            MinWidth = 760,
            MinHeight = 540,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var accepted = false;
        void AcceptAndClose()
        {
            accepted = true;
            dialog.Close();
        }

        var symbolCells = new List<Button>();
        var selectedCellBrush = Brush(0, 120, 215);
        void HighlightSelectedCell(string symbol)
        {
            foreach (var cell in symbolCells)
            {
                var isSelected = string.Equals(cell.Content as string, symbol, StringComparison.Ordinal);
                cell.Background = isSelected ? Brush(204, 232, 255) : Brushes.Transparent;
                cell.BorderBrush = isSelected ? selectedCellBrush : Brushes.Transparent;
            }
        }

        void ApplySelection(string symbol)
        {
            selectedSymbol = symbol;
            var selection = SymbolPickerSelectionPlanner.CreateSelection(symbol);
            preview.Text = symbol;
            selectedCode.Text = selection.CodeText;
            selectedName.Text = SymbolPickerNames.TryGetValue(symbol, out var name)
                ? name
                : $"Unicode U+{selection.CodeText}";
            selectedSubset.Text = "Latin-1 Supplement";
            HighlightSelectedCell(symbol);
        }

        var fontBox = new ComboBox
        {
            ItemsSource = SymbolPickerFontChoices,
            SelectedIndex = 0,
            MinWidth = 150,
            Width = 150,
            FontSize = 12,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(6, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var subsetBox = new ComboBox
        {
            ItemsSource = new[] { "Latin-1 Supplement" },
            SelectedIndex = 0,
            MinWidth = 150,
            Width = 150,
            FontSize = 12,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(6, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var searchBox = new TextBox
        {
            MinWidth = 150,
            MaxWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            // Right margin keeps the field inside the tab pane (it sits in the trailing
            // star column and would otherwise stretch flush to / past the dialog edge).
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(4, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var resultCount = new TextBlock
        {
            Text = $"Symbols shown: {symbols.Count}",
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = Brush(96, 96, 96),
            Margin = new Thickness(0, 4, 0, 6),
        };

        var symbolGrid = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 31,
            ItemHeight = 31,
            Margin = new Thickness(3),
        };
        foreach (var symbol in symbols)
        {
            var cell = CreateSymbolCell(symbol, ApplySelection, AcceptAndClose);
            symbolCells.Add(cell);
            symbolGrid.Children.Add(cell);
        }

        var symbolListHost = new Border
        {
            BorderBrush = Brush(205, 205, 205),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = symbolGrid,
            },
        };

        var recentPanel = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };
        var recentLabel = new TextBlock
        {
            Text = "Recently used symbols",
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 150,
        };
        var recentGrid = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = 32,
            ItemHeight = 30,
            Margin = new Thickness(4, 0),
        };
        foreach (var symbol in SymbolPickerRecentSymbols)
            recentGrid.Children.Add(CreateSymbolCell(symbol, ApplySelection, AcceptAndClose, compact: true));
        DockPanel.SetDock(recentLabel, Dock.Left);
        recentPanel.Children.Add(recentLabel);
        recentPanel.Children.Add(new Border
        {
            BorderBrush = Brush(205, 205, 205),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = recentGrid,
        });

        var chooser = CreateSymbolChooserGrid(fontBox, subsetBox, searchBox);
        var symbolsPanel = new DockPanel();
        DockPanel.SetDock(chooser, Dock.Top);
        DockPanel.SetDock(resultCount, Dock.Top);
        DockPanel.SetDock(recentPanel, Dock.Bottom);
        symbolsPanel.Children.Add(chooser);
        symbolsPanel.Children.Add(resultCount);
        symbolsPanel.Children.Add(recentPanel);
        symbolsPanel.Children.Add(symbolListHost);

        var specialPanel = CreateSpecialCharactersPanel(ApplySelection, AcceptAndClose);
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Symbols", Content = symbolsPanel },
                new TabItem { Header = "Special Characters", Content = specialPanel },
            },
        };
        ApplyClassicTabChrome(tabs);

        var details = CreateSymbolDetailsPanel(preview, selectedName, selectedSubset, selectedCode, ApplySelection);
        // Right details column: 210px to comfortably fit "Inverted Exclamation Mark" in SemiBold on Linux/Skia.
        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,210"),
        };
        Grid.SetColumn(details, 1);
        contentGrid.Children.Add(tabs);
        contentGrid.Children.Add(details);

        var insert = new Button
        {
            Content = "Insert",
            MinWidth = 84,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(0, 120, 215),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsDefault = true,
        };
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { insert, cancel },
        };

        insert.Click += (_, _) =>
        {
            AcceptAndClose();
        };
        cancel.Click += (_, _) => dialog.Close();

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(contentGrid);
        dialog.Content = root;
        dialog.Opened += (_, _) =>
        {
            ApplySelection(selectedSymbol);
            symbolGrid.Focus();
        };

        await dialog.ShowDialog(this);
        if (!accepted || string.IsNullOrEmpty(selectedSymbol))
            return;

        var selection = SymbolPickerSelectionPlanner.CreateSelection(selectedSymbol);
        var current = FormatEditText(_session.ActiveSheet.GetCell(_session.ActiveCell), _session.ActiveCell);
        var result = _session.CommitCellText(current + selection.Symbol);
        RefreshShell(result.Success
            ? $"Inserted {selection.Symbol} into {FormatCellReference(_session.ActiveCell)}"
            : result.ErrorMessage ?? "Could not insert the symbol.");
    }

    private static IReadOnlyList<string> CreateLatinSupplementSymbols() =>
        Enumerable
            .Range(0x00A1, 0x00FF - 0x00A1 + 1)
            .Where(static codePoint => codePoint != 0x00AD)
            .Select(static codePoint => char.ConvertFromUtf32(codePoint))
            .ToArray();

    private static Grid CreateSymbolChooserGrid(ComboBox fontBox, ComboBox subsetBox, TextBox searchBox)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto,*"),
        };
        AddSymbolChooserField(grid, 0, "Font:", fontBox);
        AddSymbolChooserField(grid, 2, "Subset:", subsetBox);
        AddSymbolChooserField(grid, 4, "Search:", searchBox);
        return grid;
    }

    private static void AddSymbolChooserField(Grid grid, int column, string label, Control control)
    {
        var labelControl = new TextBlock
        {
            Text = StripDisplayMnemonic(label),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(column == 0 ? 0 : 10, 0, 6, 0),
        };
        Grid.SetColumn(labelControl, column);
        Grid.SetColumn(control, column + 1);
        grid.Children.Add(labelControl);
        grid.Children.Add(control);
    }

    private static Button CreateSymbolCell(
        string symbol,
        Action<string> select,
        Action close,
        bool compact = false)
    {
        var button = new Button
        {
            Content = symbol,
            Width = compact ? 30 : 31,
            Height = compact ? 28 : 30,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            FontSize = compact ? 14 : 15,
            FontFamily = new FontFamily("Segoe UI Symbol"),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => select(symbol);
        button.DoubleTapped += (_, _) =>
        {
            select(symbol);
            close();
        };
        return button;
    }

    private static Border CreateSymbolDetailsPanel(
        TextBlock preview,
        TextBlock selectedName,
        TextBlock selectedSubset,
        TextBox selectedCode,
        Action<string> select)
    {
        var codeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new TextBlock { Text = "Character code:", FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var goButton = new Button
        {
            Content = "Go",
            MinWidth = 64,
            Height = 24,
            MinHeight = 24,
            MaxHeight = 24,
            Padding = new Thickness(4, 1),
            Background = Brushes.White,
            BorderBrush = Brush(112, 112, 112),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        goButton.Click += (_, _) =>
        {
            if (TryParseSymbolCode(selectedCode.Text, out var symbol))
                select(symbol);
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            Children =
            {
                new Border
                {
                    BorderBrush = Brush(205, 205, 205),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Width = 116,
                    Height = 94,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = preview,
                },
                selectedName,
                new TextBlock { Text = "Subset:", FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = Brush(96, 96, 96), Margin = new Thickness(0, 14, 0, 0) },
                selectedSubset,
                codeRow,
                selectedCode,
                new TextBlock { Text = "from: Unicode (hex)", FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = Brush(96, 96, 96), Margin = new Thickness(0, 6, 0, 0) },
                goButton,
            },
        };

        return new Border { Child = panel };
    }

    private static Border CreateSpecialCharactersPanel(Action<string> select, Action close)
    {
        var list = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
        foreach (var (name, symbol) in new[]
                 {
                     ("Em Dash", "\u2014"),
                     ("Nonbreaking Space", "\u00a0"),
                     ("Copyright", "\u00a9"),
                     ("Registered", "\u00ae"),
                     ("Trademark", "\u2122"),
                     ("Section", "\u00a7"),
                     ("Paragraph", "\u00b6"),
                     ("Ellipsis", "\u2026"),
                     ("Degree", "\u00b0"),
                     ("Check Mark", "\u2713"),
                 })
        {
            var button = new Button
            {
                Content = $"{name}    {SymbolPickerSelectionPlanner.CreateSelection(symbol).CodeText}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) => select(symbol);
            button.DoubleTapped += (_, _) =>
            {
                select(symbol);
                close();
            };
            list.Children.Add(button);
        }

        return new Border
        {
            BorderBrush = Brush(205, 205, 205),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = list,
        };
    }

    private static bool TryParseSymbolCode(string? text, out string symbol)
    {
        symbol = "";
        var normalized = text?.Trim() ?? "";
        if (normalized.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];

        if (!int.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out var codePoint))
            return false;
        if (codePoint < 0 || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
            return false;

        symbol = char.ConvertFromUtf32(codePoint);
        return true;
    }
}
