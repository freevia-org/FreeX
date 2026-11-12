using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle SymbolDialogChromeStyle => new(FormulaBarFontFamily);

    private static void ApplySymbolButtonChrome(Button button, double minWidth, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, SymbolDialogChromeStyle, minWidth, isDefault);

    private static void ApplySymbolTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, SymbolDialogChromeStyle);

    private static void ApplySymbolComboBoxChrome(ComboBox comboBox)
        => AvaloniaCompactDialogChrome.ApplyComboBox(
            comboBox,
            SymbolDialogChromeStyle with { ComboBoxPadding = new Thickness(6, 1) });

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
            // WPF SymbolPickerDialog.Layout.cs: the character-code box is 96 wide.
            Width = 96,
        };
        ApplySymbolTextBoxChrome(selectedCode);
        var preview = new TextBlock
        {
            // WPF SymbolPickerDialog.Layout.cs: 96x82 preview text inside the 116x96 frame.
            FontSize = 44,
            Width = 96,
            Height = 82,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // Fallback faces with a short ascent draw marks above the line box; do not crop them.
            ClipToBounds = false,
        };

        var dialog = new Window
        {
            Title = UiText.Get("SymbolPicker_Symbol"),
            // WPF SymbolPickerDialog is a resizable DialogWidth x DialogHeight window (min 760x540).
            // Those are OUTER sizes; the client area the content is laid out in drops the frame.
            Width = SymbolPickerCatalogPlanner.DialogWidth - WpfResizableFrameWidth,
            Height = SymbolPickerCatalogPlanner.DialogHeight - WpfResizableFrameHeight,
            MinWidth = 760 - WpfResizableFrameWidth,
            MinHeight = 540 - WpfResizableFrameHeight,
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
                UiText.Get("SymbolPicker_SpecialCharactersAutomationName"),
                special.CodeText);

        var fontBox = new ComboBox
        {
            ItemsSource = SymbolPickerCatalogPlanner.GetPreferredFontChoices(),
            SelectedIndex = 0,
            // WPF stretches the chooser fields across their star columns.
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ApplySymbolComboBoxChrome(fontBox);
        var subsetBox = new ComboBox
        {
            ItemsSource = SymbolPickerCatalogPlanner.GetSubsetNames(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ApplySymbolComboBoxChrome(subsetBox);
        var searchBox = new TextBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // Small right margin keeps the field inside the tab pane border.
            Margin = new Thickness(0, 0, 2, 0),
        };
        ApplySymbolTextBoxChrome(searchBox);
        var resultCount = new TextBlock
        {
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = Brush(96, 96, 96),
            Margin = new Thickness(0, 0, 0, 6),
        };

        var symbolGrid = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            // WPF SymbolPickerDialog.Layout.cs: SymbolCellSize = 32 slots inside 3px list padding.
            ItemWidth = 32,
            ItemHeight = 32,
            Margin = new Thickness(3),
            Focusable = true,
        };
        KeyboardNavigation.SetIsTabStop(symbolGrid, true);
        AutomationProperties.SetAutomationId(symbolGrid, "SymbolPickerSymbolsList");

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
            Text = UiText.Get("SymbolPicker_RecentlyUsedSymbols"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 150,
        };
        var recentGrid = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            // WPF SymbolPickerDialog.Layout.cs: RecentSymbolCellSize = 30 slots inside 3px list padding.
            ItemWidth = 30,
            ItemHeight = 30,
            Margin = new Thickness(3),
        };
        void PopulateRecentSymbols()
        {
            recentGrid.Children.Clear();
            foreach (var symbol in recentSymbols)
            {
                var entry = SymbolPickerCatalogPlanner.CreateSymbolEntry(
                    symbol,
                    UiText.Get("SymbolPicker_RecentlyUsedSymbols"));
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
            var selectedFontName = fontBox.SelectedItem as string;
            var plan = SymbolPickerCatalogPlanner.PlanSymbolList(
                subsetBox.SelectedItem as string,
                searchBox.Text,
                selectedSymbol,
                selectedFontName);

            // R91-commands-insert-object-5-3: Wingdings/Webdings/etc. glyphs live in the font's own
            // Private Use Area mapping, not in "Segoe UI Symbol" -- the grid and the big preview
            // must render in the chosen dingbat font itself or the correct codepoints show as tofu.
            var cellFontFamily = SymbolPickerCatalogPlanner.IsSymbolFont(selectedFontName)
                ? new FontFamily(selectedFontName!)
                : new FontFamily("Segoe UI Symbol");
            preview.FontFamily = cellFontFamily;

            symbolCells.Clear();
            symbolGrid.Children.Clear();
            foreach (var entry in plan.Entries)
            {
                var cell = CreateSymbolCell(entry, SelectCatalogEntry, AcceptAndClose, fontFamily: cellFontFamily);
                symbolCells.Add((cell, entry.Symbol));
                symbolGrid.Children.Add(cell);
            }

            resultCount.Text = UiText.Format("SymbolPicker_SearchResultCountFormat", plan.Entries.Count);
            if (plan.SelectedEntry is { } selectedEntry)
                SelectCatalogEntry(selectedEntry);
            else
                HighlightSelectedCell(selectedSymbol);
        }

        fontBox.SelectionChanged += (_, _) => RefreshSymbols();
        subsetBox.SelectionChanged += (_, _) => RefreshSymbols();
        searchBox.TextChanged += (_, _) => RefreshSymbols();

        var specialPanel = CreateSpecialCharactersPanel(SelectSpecialCharacter, AcceptAndClose);
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("SymbolPicker_SymbolsTab")), Content = symbolsPanel },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("SymbolPicker_SpecialCharactersTab")), Content = specialPanel },
            },
        };
        AutomationProperties.SetAutomationId(tabs, "SymbolPickerTabs");
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs);

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
            Content = StripDisplayMnemonic(UiText.Get("SymbolPicker_InsertButton")),
            MinWidth = 84,
            IsDefault = true,
        };
        ApplySymbolButtonChrome(insert, 84, isDefault: true);
        var cancel = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")),
            MinWidth = 84,
            IsCancel = true,
        };
        ApplySymbolButtonChrome(cancel, 84);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([insert, cancel], new Thickness(0, 12, 0, 0));

        insert.Click += (_, _) =>
        {
            AcceptAndClose();
        };
        cancel.Click += (_, _) => dialog.Close();

        var root = new DockPanel { Margin = new Thickness(12) };
        ConfigureDialogTabCycle(dialog, root);
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
            ? UiText.Format(
                "SymbolPicker_InsertedIntoCellFormat",
                selection.Symbol,
                FormatCellReference(_session.ActiveCell))
            : result.ErrorMessage ?? UiText.Get("SymbolPicker_CouldNotInsertSymbol"));
    }

    private static Grid CreateSymbolChooserGrid(ComboBox fontBox, ComboBox subsetBox, TextBox searchBox)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8),
            // WPF CreateChooserGrid: Auto,*,Auto,1.1*,Auto,*.
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,1.1*,Auto,*"),
        };
        AddSymbolChooserField(
            grid,
            0,
            StripDisplayMnemonic(UiText.Get("SymbolPicker_FontLabel")),
            fontBox);
        AddSymbolChooserField(
            grid,
            2,
            StripDisplayMnemonic(UiText.Get("SymbolPicker_SubsetLabel")),
            subsetBox);
        AddSymbolChooserField(
            grid,
            4,
            StripDisplayMnemonic(UiText.Get("SymbolPicker_SearchLabel")),
            searchBox);
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
        bool compact = false,
        FontFamily? fontFamily = null)
    {
        // WPF SymbolPickerDialog.Layout.cs draws 18px glyphs in 32px cells and 16px glyphs in the
        // 30px recent cells, each cell inset by a 1px item margin.
        var cellFontFamily = fontFamily ?? new FontFamily("Segoe UI Symbol");
        var cellFontSize = compact ? 16d : 18d;
        var button = new Button
        {
            // An explicit, unclipped text block: the default presenter text clips to its line box,
            // which cut the marks above capital letters when a fallback face with a short ascent
            // resolved the glyph.
            Content = new TextBlock
            {
                Text = SymbolPickerCatalogPlanner.CreateDisplaySymbol(entry.Symbol),
                FontSize = cellFontSize,
                FontFamily = cellFontFamily,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, 3),
                ClipToBounds = false,
            },
            Width = compact ? 28 : 30,
            Height = compact ? 28 : 30,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            ClipToBounds = false,
            FontSize = cellFontSize,
            FontFamily = cellFontFamily,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        // The glyph is no longer plain string content, so publish it as the accessible name.
        AutomationProperties.SetName(button, SymbolPickerCatalogPlanner.CreateDisplaySymbol(entry.Symbol));
        button.Click += (_, _) => select(entry);
        button.DoubleTapped += (_, _) =>
        {
            select(entry);
            close();
        };
        KeyboardNavigation.SetIsTabStop(button, false);
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
            Margin = new Thickness(0, 12, 0, 4),
            Children =
            {
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("SymbolPicker_CharacterCodeLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var goButton = new Button
        {
            Content = StripDisplayMnemonic(UiText.Get("SymbolPicker_GoButton")),
            MinWidth = 64,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };
        ApplySymbolButtonChrome(goButton, 64);
        // WPF SymbolPickerDialog.Layout.cs: an auto-height 64px button (20px for one 12px line),
        // not the 24px dialog command-button height.
        goButton.Height = 20;
        goButton.MinHeight = 20;
        goButton.MaxHeight = 20;
        goButton.Padding = new Thickness(goButton.Padding.Left, 0, goButton.Padding.Right, 0);
        goButton.VerticalContentAlignment = VerticalAlignment.Center;
        goButton.Click += (_, _) =>
        {
            if (SymbolPickerCatalogPlanner.TryParseCharacterCode(selectedCode.Text, out var symbol))
                select(symbol);
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            // WPF docks the details stack at its natural width, so the 96px code box centres
            // under the widest detail line instead of the whole column.
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                new Border
                {
                    BorderBrush = Brush(205, 205, 205),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Width = 116,
                    Height = 96,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = preview,
                },
                selectedName,
                new TextBlock { Text = StripDisplayMnemonic(UiText.Get("SymbolPicker_SubsetLabel")), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = Brush(96, 96, 96), Margin = new Thickness(0, 12, 0, 0) },
                selectedSubset,
                codeRow,
                selectedCode,
                new TextBlock { Text = UiText.Get("SymbolPicker_FromUnicodeHex"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = Brush(96, 96, 96), Margin = new Thickness(0, 4, 0, 0) },
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
            KeyboardNavigation.SetIsTabStop(button, false);
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
            Focusable = true,
            BorderBrush = Brush(205, 205, 205),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = list,
        };
    }
}
