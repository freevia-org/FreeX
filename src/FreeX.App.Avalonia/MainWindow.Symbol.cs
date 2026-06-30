using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task ShowSymbolPickerAsync()
    {
        var selectedSymbol = SymbolPickerCatalogPlanner.CreateDefaultSelection().Symbol;
        var recentSymbols = SymbolPickerCatalogPlanner.DefaultRecentSymbols.ToList();
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
            recentSymbols = SymbolPickerCatalogPlanner
                .PromoteRecentSymbol(
                    recentSymbols,
                    selectedSymbol,
                    SymbolPickerCatalogPlanner.DefaultRecentSymbolCapacity)
                .ToList();
            accepted = true;
            dialog.Close();
        }

        var symbolCells = new List<(Button Cell, string Symbol)>();
        var selectedCellBrush = Brush(0, 120, 215);
        void HighlightSelectedCell(string symbol)
        {
            foreach (var (cell, cellSymbol) in symbolCells)
            {
                var isSelected = string.Equals(cellSymbol, symbol, StringComparison.Ordinal);
                cell.Background = isSelected ? Brush(204, 232, 255) : Brushes.Transparent;
                cell.BorderBrush = isSelected ? selectedCellBrush : Brushes.Transparent;
            }
        }

        void ApplySelection(string symbol, string? name = null, string? subset = null, string? codeText = null)
        {
            var selection = SymbolPickerCatalogPlanner.CreateSelection(symbol);
            var entry = SymbolPickerCatalogPlanner.CreateSymbolEntry(
                selection.Symbol,
                subset ?? SymbolPickerCatalogPlanner.DefaultSubsetName);
            selectedSymbol = selection.Symbol;
            preview.Text = SymbolPickerCatalogPlanner.CreateDisplaySymbol(selection.Symbol);
            selectedCode.Text = string.IsNullOrEmpty(codeText) ? selection.CodeText : codeText;
            selectedName.Text = string.IsNullOrEmpty(name) ? entry.Name : name;
            selectedSubset.Text = string.IsNullOrEmpty(subset) ? entry.Subset : subset;
            HighlightSelectedCell(selection.Symbol);
        }

        void SelectCatalogEntry(SymbolPickerCatalogEntry entry) =>
            ApplySelection(entry.Symbol, entry.Name, entry.Subset, entry.CodeText);

        void SelectSpecialCharacter(SymbolPickerSpecialCharacter special) =>
            ApplySelection(
                special.Symbol,
                special.Name,
                "Special Characters",
                special.CodeText);

        var fontBox = new ComboBox
        {
            ItemsSource = SymbolPickerCatalogPlanner.GetPreferredFontChoices(),
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
            ItemsSource = SymbolPickerCatalogPlanner.GetSubsetNames(),
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
        void PopulateRecentSymbols()
        {
            recentGrid.Children.Clear();
            foreach (var symbol in recentSymbols)
            {
                var entry = SymbolPickerCatalogPlanner.CreateSymbolEntry(symbol, "Recently used symbols");
                recentGrid.Children.Add(CreateSymbolCell(entry, SelectCatalogEntry, AcceptAndClose, compact: true));
            }
        }

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

        void RefreshSymbols()
        {
            var plan = SymbolPickerCatalogPlanner.PlanSymbolList(
                subsetBox.SelectedItem as string,
                searchBox.Text,
                selectedSymbol);

            symbolCells.Clear();
            symbolGrid.Children.Clear();
            foreach (var entry in plan.Entries)
            {
                var cell = CreateSymbolCell(entry, SelectCatalogEntry, AcceptAndClose);
                symbolCells.Add((cell, entry.Symbol));
                symbolGrid.Children.Add(cell);
            }

            resultCount.Text = $"Symbols shown: {plan.Entries.Count}";
            if (plan.SelectedEntry is { } selectedEntry)
                SelectCatalogEntry(selectedEntry);
            else
                HighlightSelectedCell(selectedSymbol);
        }

        subsetBox.SelectionChanged += (_, _) => RefreshSymbols();
        searchBox.TextChanged += (_, _) => RefreshSymbols();

        var specialPanel = CreateSpecialCharactersPanel(SelectSpecialCharacter, AcceptAndClose);
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Symbols", Content = symbolsPanel },
                new TabItem { Header = "Special Characters", Content = specialPanel },
            },
        };
        ApplyClassicTabChrome(tabs);

        var details = CreateSymbolDetailsPanel(preview, selectedName, selectedSubset, selectedCode, symbol => ApplySelection(symbol));
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
        PopulateRecentSymbols();
        RefreshSymbols();
        dialog.Opened += (_, _) =>
        {
            ApplySelection(selectedSymbol);
            symbolGrid.Focus();
        };

        await dialog.ShowDialog(this);
        if (!accepted || string.IsNullOrEmpty(selectedSymbol))
            return;

        var selection = SymbolPickerCatalogPlanner.CreateSelection(selectedSymbol);
        var current = FormatEditText(_session.ActiveSheet.GetCell(_session.ActiveCell), _session.ActiveCell);
        var result = _session.CommitCellText(current + selection.Symbol);
        RefreshShell(result.Success
            ? $"Inserted {selection.Symbol} into {FormatCellReference(_session.ActiveCell)}"
            : result.ErrorMessage ?? "Could not insert the symbol.");
    }

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
        SymbolPickerCatalogEntry entry,
        Action<SymbolPickerCatalogEntry> select,
        Action close,
        bool compact = false)
    {
        var button = new Button
        {
            Content = SymbolPickerCatalogPlanner.CreateDisplaySymbol(entry.Symbol),
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
        button.Click += (_, _) => select(entry);
        button.DoubleTapped += (_, _) =>
        {
            select(entry);
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
            if (SymbolPickerCatalogPlanner.TryParseCharacterCode(selectedCode.Text, out var symbol))
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

    private static Border CreateSpecialCharactersPanel(Action<SymbolPickerSpecialCharacter> select, Action close)
    {
        var list = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
        foreach (var special in SymbolPickerCatalogPlanner.GetSpecialCharacters())
        {
            var button = new Button
            {
                Content = $"{special.Name}    {special.CodeText}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) => select(special);
            button.DoubleTapped += (_, _) =>
            {
                select(special);
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
}
