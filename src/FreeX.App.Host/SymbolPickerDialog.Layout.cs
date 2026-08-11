using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Host;

public sealed partial class SymbolPickerDialog
{
    private const double SymbolCellSize = 32;
    private const double RecentSymbolCellSize = 30;

    private UIElement CreateDialogContent()
    {
        var recentSymbols = SymbolPickerCatalogPlanner.DefaultRecentSymbols.ToList();
        var symbolItems = new ObservableCollection<SymbolPickerCatalogEntry>();
        var recentItems = new ObservableCollection<SymbolPickerCatalogEntry>();
        var selectedCode = new TextBox { Width = 96, Text = SymbolPickerCatalogPlanner.FormatCodeText(SelectedSymbol) };
        var preview = new TextBlock
        {
            FontSize = 44,
            Width = 96,
            Height = 82,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = CreateVisibleSymbolText(SelectedSymbol)
        };
        var selectedName = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var selectedSubset = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var resultCount = new TextBlock { Margin = new Thickness(0, 0, 0, 6), Foreground = Brushes.DimGray };
        var noResults = new TextBlock
        {
            Text = UiText.Get("SymbolPicker_NoSymbolsFound"),
            Visibility = Visibility.Collapsed,
            Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var fontBox = new ComboBox { ItemsSource = FontChoices, SelectedIndex = 0, MinWidth = 160 };
        var subsetBox = new ComboBox { ItemsSource = SubsetChoices, SelectedIndex = 0, MinWidth = 190 };
        var searchBox = new TextBox { MinWidth = 160 };
        AutomationProperties.SetName(fontBox, UiText.Get("SymbolPicker_FontAutomationName"));
        AutomationProperties.SetHelpText(fontBox, UiText.Get("SymbolPicker_FontHelpText"));
        AutomationProperties.SetName(subsetBox, UiText.Get("SymbolPicker_SubsetAutomationName"));
        AutomationProperties.SetHelpText(subsetBox, UiText.Get("SymbolPicker_SubsetHelpText"));
        AutomationProperties.SetName(searchBox, UiText.Get("SymbolPicker_SearchAutomationName"));
        AutomationProperties.SetHelpText(searchBox, UiText.Get("SymbolPicker_SearchHelpText"));
        AutomationProperties.SetName(selectedCode, UiText.Get("SymbolPicker_CharacterCodeAutomationName"));
        AutomationProperties.SetHelpText(selectedCode, UiText.Get("SymbolPicker_CharacterCodeHelpText"));
        AutomationProperties.SetName(preview, UiText.Get("SymbolPicker_SelectedSymbolPreviewAutomationName"));
        AutomationProperties.SetHelpText(preview, UiText.Get("SymbolPicker_SelectedSymbolPreviewHelpText"));

        var symbolList = CreateSymbolList(symbolItems, SymbolCellSize, 18);
        AutomationProperties.SetName(symbolList, UiText.Get("SymbolPicker_SymbolsAutomationName"));
        var recentList = CreateSymbolList(recentItems, RecentSymbolCellSize, 16);
        AutomationProperties.SetName(recentList, UiText.Get("SymbolPicker_RecentlyUsedSymbols"));

        void SelectSymbolText(string value, string? name = null, string? subset = null, string? codeText = null)
        {
            var selection = SymbolPickerCatalogPlanner.CreateSelection(value);
            var entry = CreateSymbolEntry(selection.Symbol, subset ?? "");
            ApplySelection(selection);
            preview.Text = CreateVisibleSymbolText(selection.Symbol);
            selectedCode.Text = string.IsNullOrEmpty(codeText) ? selection.CodeText : codeText;
            selectedName.Text = string.IsNullOrEmpty(name) ? entry.Name : name;
            selectedSubset.Text = string.IsNullOrEmpty(subset) ? entry.Subset : subset;
        }

        void SelectCatalogEntry(SymbolPickerCatalogEntry entry) =>
            SelectSymbolText(entry.Symbol, entry.Name, entry.Subset, entry.CodeText);

        void SelectSpecialCharacter(SymbolPickerSpecialCharacter special) =>
            SelectSymbolText(special.Symbol, special.Name, UiText.Get("SymbolPicker_SpecialCharactersTab"), special.CodeText);

        void PopulateRecent()
        {
            recentItems.Clear();
            foreach (var symbol in recentSymbols)
                recentItems.Add(CreateSymbolEntry(symbol, UiText.Get("SymbolPicker_RecentlyUsedSymbols")));
        }

        void AcceptSelectedSymbol()
        {
            if (string.IsNullOrEmpty(SelectedSymbol))
                return;

            recentSymbols = PromoteRecentSymbol(recentSymbols, SelectedSymbol, 12).ToList();
            PopulateRecent();
            DialogResult = true;
        }

        void SelectVisibleSymbol(string symbol)
        {
            foreach (var entry in symbolItems)
            {
                if (!string.Equals(entry.Symbol, symbol, StringComparison.Ordinal))
                    continue;

                symbolList.SelectedItem = entry;
                symbolList.ScrollIntoView(entry);
                return;
            }
        }

        void RefreshSymbols()
        {
            var plan = SymbolPickerCatalogPlanner.PlanSymbolList(
                subsetBox.SelectedItem as string,
                searchBox.Text,
                SelectedSymbol,
                fontBox.SelectedItem as string);

            symbolItems.Clear();
            foreach (var entry in plan.Entries)
                symbolItems.Add(entry);

            resultCount.Text = UiText.Format("SymbolPicker_SearchResultCountFormat", symbolItems.Count);
            noResults.Visibility = symbolItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (plan.SelectedEntry is { } selectedEntry)
                SelectVisibleSymbol(selectedEntry.Symbol);
        }

        void ApplySymbolFont(string fontName)
        {
            var fontFamily = new FontFamily(fontName);
            preview.FontFamily = fontFamily;
            symbolList.FontFamily = fontFamily;
            recentList.FontFamily = fontFamily;
        }

        fontBox.SelectionChanged += (_, _) =>
        {
            if (fontBox.SelectedItem is string fontName)
                ApplySymbolFont(fontName);

            // R91-commands-insert-object-5-3: picking a Symbol-charset font (Wingdings/Webdings/etc.)
            // must swap the catalog itself to that font's glyph set, not just re-font the existing
            // fixed Unicode table (which produced garbage glyphs, not the chosen dingbat icons).
            RefreshSymbols();
        };
        subsetBox.SelectionChanged += (_, _) => RefreshSymbols();
        searchBox.TextChanged += (_, _) => RefreshSymbols();
        symbolList.SelectionChanged += (_, _) =>
        {
            if (symbolList.SelectedItem is SymbolPickerCatalogEntry entry)
                SelectCatalogEntry(entry);
        };
        symbolList.MouseDoubleClick += (_, e) =>
        {
            if (symbolList.SelectedItem is SymbolPickerCatalogEntry)
            {
                AcceptSelectedSymbol();
                e.Handled = true;
            }
        };
        symbolList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AcceptSelectedSymbol();
                e.Handled = true;
            }
        };
        recentList.SelectionChanged += (_, _) =>
        {
            if (recentList.SelectedItem is SymbolPickerCatalogEntry entry)
                SelectCatalogEntry(entry);
        };
        recentList.MouseDoubleClick += (_, e) =>
        {
            if (recentList.SelectedItem is SymbolPickerCatalogEntry)
            {
                AcceptSelectedSymbol();
                e.Handled = true;
            }
        };
        recentList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AcceptSelectedSymbol();
                e.Handled = true;
            }
        };

        var topGrid = CreateChooserGrid(fontBox, subsetBox, searchBox);
        var symbolHost = new Grid();
        symbolHost.Children.Add(symbolList);
        symbolHost.Children.Add(noResults);

        var recentPanel = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };
        var recentLabel = new TextBlock
        {
            Text = UiText.Get("SymbolPicker_RecentlyUsedSymbols"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 150
        };
        DockPanel.SetDock(recentLabel, Dock.Left);
        recentPanel.Children.Add(recentLabel);
        recentPanel.Children.Add(recentList);

        var symbolsPanel = new DockPanel();
        DockPanel.SetDock(topGrid, Dock.Top);
        DockPanel.SetDock(resultCount, Dock.Top);
        DockPanel.SetDock(recentPanel, Dock.Bottom);
        symbolsPanel.Children.Add(topGrid);
        symbolsPanel.Children.Add(resultCount);
        symbolsPanel.Children.Add(recentPanel);
        symbolsPanel.Children.Add(symbolHost);

        var specialList = CreateSpecialCharacterList(SelectSpecialCharacter, AcceptSelectedSymbol);
        var specialPanel = new DockPanel();
        specialPanel.Children.Add(specialList);

        var tabControl = new TabControl();
        tabControl.Items.Add(new TabItem { Header = UiText.Get("SymbolPicker_SymbolsTab"), Content = symbolsPanel });
        tabControl.Items.Add(new TabItem { Header = UiText.Get("SymbolPicker_SpecialCharactersTab"), Content = specialPanel });

        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = CreateButtonRow(AcceptSelectedSymbol);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mainGrid.Children.Add(tabControl);
        var details = CreateDetailsPanel(preview, selectedName, selectedSubset, selectedCode, SelectSymbolText);
        Grid.SetColumn(details, 1);
        mainGrid.Children.Add(details);
        root.Children.Add(mainGrid);

        PopulateRecent();
        RefreshSymbols();
        if (fontBox.SelectedItem is string initialFontName)
            ApplySymbolFont(initialFontName);

        Loaded += (_, _) => FocusInitialKeyboardTarget(symbolList);
        return root;
    }

    private static Grid CreateChooserGrid(ComboBox fontBox, ComboBox subsetBox, TextBox searchBox)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddLabeledControl(grid, 0, UiText.Get("SymbolPicker_FontLabel"), fontBox);
        AddLabeledControl(grid, 2, UiText.Get("SymbolPicker_SubsetLabel"), subsetBox);
        AddLabeledControl(grid, 4, UiText.Get("SymbolPicker_SearchLabel"), searchBox);
        return grid;
    }

    private static void AddLabeledControl(Grid grid, int labelColumn, string label, Control control)
    {
        var labelElement = new Label
        {
            Content = label,
            Target = control,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(labelColumn == 0 ? 0 : 10, 0, 6, 0)
        };
        Grid.SetColumn(labelElement, labelColumn);
        grid.Children.Add(labelElement);
        Grid.SetColumn(control, labelColumn + 1);
        grid.Children.Add(control);
    }

    private static ListBox CreateSymbolList(ObservableCollection<SymbolPickerCatalogEntry> items, double cellSize, double fontSize)
    {
        var list = new ListBox
        {
            ItemsSource = items,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = CreateSymbolTemplate(fontSize),
            ItemContainerStyle = CreateSymbolItemStyle(cellSize),
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Padding = new Thickness(3)
        };
        list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        list.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        list.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Contained);
        list.ItemsPanel = CreateSymbolItemsPanel(cellSize);
        return list;
    }

    private static ItemsPanelTemplate CreateSymbolItemsPanel(double cellSize)
    {
        var panelFactory = new FrameworkElementFactory(typeof(WrapPanel));
        panelFactory.SetValue(WrapPanel.ItemWidthProperty, cellSize);
        panelFactory.SetValue(WrapPanel.ItemHeightProperty, cellSize);
        return new ItemsPanelTemplate(panelFactory);
    }

    private static DataTemplate CreateSymbolTemplate(double fontSize)
    {
        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(SymbolPickerCatalogEntry.Symbol)));
        textFactory.SetValue(TextBlock.FontSizeProperty, fontSize);
        textFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        return new DataTemplate { VisualTree = textFactory };
    }

    private static Style CreateSymbolItemStyle(double cellSize)
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, cellSize));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, cellSize));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        style.Setters.Add(new Setter(
            AutomationProperties.NameProperty,
            new Binding(nameof(SymbolPickerCatalogEntry.Symbol)) { Converter = SymbolAutomationNameConverter.Instance }));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(SymbolPickerCatalogEntry.ToolTipText))));
        return style;
    }

    private ListView CreateSpecialCharacterList(Action<SymbolPickerSpecialCharacter> selectSpecialCharacter, Action acceptSelectedSymbol)
    {
        var specialList = new ListView
        {
            ItemsSource = GetSpecialCharacters(),
            SelectionMode = SelectionMode.Single,
            ItemContainerStyle = CreateSpecialCharacterItemStyle()
        };
        specialList.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        specialList.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(specialList, UiText.Get("SymbolPicker_SpecialCharactersAutomationName"));
        specialList.View = new GridView
        {
            Columns =
            {
                new GridViewColumn { Header = UiText.Get("SymbolPicker_NameLabel"), Width = 220, DisplayMemberBinding = new Binding(nameof(SymbolPickerSpecialCharacter.Name)) },
                new GridViewColumn { Header = UiText.Get("SymbolPicker_Symbol"), Width = 90, DisplayMemberBinding = new Binding(nameof(SymbolPickerSpecialCharacter.DisplaySymbol)) },
                new GridViewColumn { Header = UiText.Get("SymbolPicker_CharacterCodeLabel"), Width = 110, DisplayMemberBinding = new Binding(nameof(SymbolPickerSpecialCharacter.CodeText)) }
            }
        };
        specialList.SelectionChanged += (_, _) =>
        {
            if (specialList.SelectedItem is SymbolPickerSpecialCharacter special)
                selectSpecialCharacter(special);
        };
        specialList.MouseDoubleClick += (_, e) =>
        {
            if (specialList.SelectedItem is SymbolPickerSpecialCharacter)
            {
                acceptSelectedSymbol();
                e.Handled = true;
            }
        };
        specialList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                acceptSelectedSymbol();
                e.Handled = true;
            }
        };
        return specialList;
    }

    private static Style CreateSpecialCharacterItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(
            AutomationProperties.NameProperty,
            new Binding(".") { Converter = SpecialCharacterAutomationNameConverter.Instance }));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(SymbolPickerSpecialCharacter.SearchText))));
        return style;
    }

    private DockPanel CreateDetailsPanel(
        TextBlock preview,
        TextBlock selectedName,
        TextBlock selectedSubset,
        TextBox selectedCode,
        Action<string, string?, string?, string?> selectSymbolText)
    {
        var panel = new DockPanel
        {
            Width = 190,
            Margin = new Thickness(12, 0, 0, 0),
            LastChildFill = false
        };

        var stack = new StackPanel();
        panel.Children.Add(stack);
        stack.Children.Add(new Border
        {
            Width = 116,
            Height = 96,
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = preview
        });
        stack.Children.Add(selectedName);
        stack.Children.Add(new TextBlock
        {
            Text = UiText.Get("SymbolPicker_SubsetLabel"),
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 12, 0, 0)
        });
        stack.Children.Add(selectedSubset);
        stack.Children.Add(CreateCharacterCodeRow(selectedCode, selectSymbolText));
        return panel;
    }

    private StackPanel CreateCharacterCodeRow(TextBox selectedCode, Action<string, string?, string?, string?> selectSymbolText)
    {
        var codePanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        codePanel.Children.Add(new Label
        {
            Content = UiText.Get("SymbolPicker_CharacterCodeLabel"),
            Target = selectedCode,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        });
        codePanel.Children.Add(selectedCode);
        codePanel.Children.Add(new TextBlock
        {
            Text = UiText.Get("SymbolPicker_FromUnicodeHex"),
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        var codeSelect = new Button
        {
            Content = UiText.Get("SymbolPicker_GoButton"),
            Width = 64,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        AutomationProperties.SetName(codeSelect, UiText.Get("SymbolPicker_GoToCharacterCodeAutomationName"));
        AutomationProperties.SetHelpText(codeSelect, UiText.Get("SymbolPicker_GoToCharacterCodeHelpText"));
        codeSelect.Click += (_, _) =>
        {
            if (TryParseCharacterCode(selectedCode.Text, out var symbol))
            {
                var entry = CreateSymbolEntry(symbol, "");
                selectSymbolText(entry.Symbol, entry.Name, entry.Subset, entry.CodeText);
            }
            else
            {
                ShowInvalidCharacterCodeWarning(selectedCode);
                return;
            }

            selectedCode.Focus();
            selectedCode.SelectAll();
            Keyboard.Focus(selectedCode);
        };
        codePanel.Children.Add(codeSelect);
        return codePanel;
    }

    private static StackPanel CreateButtonRow(Action acceptSelectedSymbol)
    {
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var insert = new Button { Content = UiText.Get("SymbolPicker_InsertButton"), MinWidth = 84, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(insert, UiText.Get("SymbolPicker_InsertSelectedSymbolAutomationName"));
        AutomationProperties.SetHelpText(insert, UiText.Get("SymbolPicker_InsertSelectedSymbolHelpText"));
        insert.Click += (_, _) => acceptSelectedSymbol();
        var cancel = new Button { Content = UiText.Cancel, MinWidth = 84, IsCancel = true };
        AutomationProperties.SetName(cancel, UiText.Get("SymbolPicker_CancelAutomationName"));
        AutomationProperties.SetHelpText(cancel, UiText.Get("SymbolPicker_CancelHelpText"));
        btnRow.Children.Add(insert);
        btnRow.Children.Add(cancel);
        return btnRow;
    }

    private static void FocusInitialKeyboardTarget(ListBox symbolList)
    {
        if (symbolList.Items.Count > 0 && symbolList.SelectedIndex < 0)
            symbolList.SelectedIndex = 0;

        symbolList.Focus();
        Keyboard.Focus(symbolList);
    }

    private void ShowInvalidCharacterCodeWarning(TextBox selectedCode)
    {
        DialogMessageHelper.ShowWarning(this, UiText.Get("SymbolPicker_InvalidCharacterCodeMessage"), Title);
        selectedCode.Focus();
        selectedCode.SelectAll();
        Keyboard.Focus(selectedCode);
    }

    private static string CreateVisibleSymbolText(string value) =>
        value switch
        {
            "\u00a0" => "NBSP",
            "\u00ad" => "SHY",
            _ => value
        };

    private static string CreateSymbolAutomationName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return UiText.Get("SymbolPicker_SymbolAutomationName");

        var rune = default(Rune);
        foreach (var candidate in value.EnumerateRunes())
        {
            rune = candidate;
            break;
        }

        return rune == default
            ? UiText.Get("SymbolPicker_SymbolAutomationName")
            : UiText.Format("SymbolPicker_SymbolCodeAutomationNameFormat", rune.Value);
    }

    private sealed class SymbolAutomationNameConverter : IValueConverter
    {
        public static SymbolAutomationNameConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            CreateSymbolAutomationName(value as string ?? string.Empty);

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
    }

    private sealed class SpecialCharacterAutomationNameConverter : IValueConverter
    {
        public static SpecialCharacterAutomationNameConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is SymbolPickerSpecialCharacter special
                ? UiText.Format(
                    "SymbolPicker_SpecialCharacterAutomationNameFormat",
                    special.Name,
                    CreateSymbolAutomationName(special.Symbol))
                : UiText.Get("SymbolPicker_SymbolAutomationName");

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
    }
}
