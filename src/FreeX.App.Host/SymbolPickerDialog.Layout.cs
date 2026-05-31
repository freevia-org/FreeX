using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FreeX.App.Host;

public sealed partial class SymbolPickerDialog
{
    private UIElement CreateDialogContent()
    {
        var recentSymbols = CommonSymbols.ToList();
        var outer = new DockPanel { Margin = new Thickness(12) };
        var selectedCode = new TextBox { Width = 88, Text = SymbolPickerSelectionPlanner.FormatCodeText(SelectedSymbol) };
        var preview = new TextBlock
        {
            FontSize = 32,
            Width = 60,
            Height = 60,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 0, 0, 0),
            Text = SelectedChar.ToString()
        };

        var tabControl = new TabControl();
        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fontBox = new ComboBox { ItemsSource = FontChoices, SelectedIndex = 0, MinWidth = 160 };
        var subsetBox = new ComboBox { ItemsSource = SubsetChoices, SelectedIndex = 0, MinWidth = 160 };
        AutomationProperties.SetName(fontBox, UiText.Get("SymbolPicker_FontAutomationName"));
        AutomationProperties.SetHelpText(fontBox, UiText.Get("SymbolPicker_FontHelpText"));
        AutomationProperties.SetName(subsetBox, UiText.Get("SymbolPicker_SubsetAutomationName"));
        AutomationProperties.SetHelpText(subsetBox, UiText.Get("SymbolPicker_SubsetHelpText"));
        AutomationProperties.SetName(selectedCode, UiText.Get("SymbolPicker_CharacterCodeAutomationName"));
        AutomationProperties.SetHelpText(selectedCode, UiText.Get("SymbolPicker_CharacterCodeHelpText"));
        AutomationProperties.SetName(preview, UiText.Get("SymbolPicker_SelectedSymbolPreviewAutomationName"));
        AutomationProperties.SetHelpText(preview, UiText.Get("SymbolPicker_SelectedSymbolPreviewHelpText"));
        topGrid.Children.Add(new Label { Content = UiText.Get("SymbolPicker_FontLabel"), Target = fontBox, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(fontBox, 1);
        topGrid.Children.Add(fontBox);
        var subsetLabel = new Label { Content = UiText.Get("SymbolPicker_SubsetLabel"), Target = subsetBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(subsetLabel, 2);
        topGrid.Children.Add(subsetLabel);
        Grid.SetColumn(subsetBox, 3);
        topGrid.Children.Add(subsetBox);

        var grid = new UniformGrid { Columns = 10, Width = 360 };
        AutomationProperties.SetName(grid, UiText.Get("SymbolPicker_SymbolsAutomationName"));
        var recent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

        void SelectSymbol(char value)
        {
            SelectSymbolText(value.ToString());
        }

        void SelectSymbolText(string value)
        {
            var selection = SymbolPickerSelectionPlanner.CreateSelection(value);
            ApplySelection(selection);
            preview.Text = selection.Symbol;
            selectedCode.Text = selection.CodeText;
        }

        void AcceptSelectedSymbol()
        {
            recentSymbols = PromoteRecentSymbol(recentSymbols, SelectedSymbol, 8).ToList();
            PopulateRecent();
            DialogResult = true;
        }

        Button CreateSymbolButton(string value, double width = 34, double height = 34, double fontSize = 16)
        {
            var button = new Button
            {
                Content = value,
                Width = width,
                Height = height,
                FontSize = fontSize,
                Margin = new Thickness(1),
                Tag = value,
                FontFamily = preview.FontFamily
            };
            AutomationProperties.SetName(button, CreateSymbolAutomationName(value));
            button.Click += (s, _) =>
            {
                if (s is Button { Tag: string symbol })
                    SelectSymbolText(symbol);
            };
            button.MouseDoubleClick += (_, _) => AcceptSelectedSymbol();
            return button;
        }

        void PopulateGrid(string subset)
        {
            grid.Children.Clear();
            foreach (var ch in GetSymbolsForSubset(subset))
                grid.Children.Add(CreateSymbolButton(ch.ToString()));
        }

        void PopulateRecent()
        {
            recent.Children.Clear();
            recent.Children.Add(new TextBlock { Text = UiText.Get("SymbolPicker_RecentlyUsedSymbols"), VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            foreach (var symbol in recentSymbols)
                recent.Children.Add(CreateSymbolButton(symbol, 28, 28, 14));
        }

        void ApplySymbolFont(string fontName)
        {
            var fontFamily = new FontFamily(fontName);
            preview.FontFamily = fontFamily;
            foreach (var button in grid.Children.OfType<Button>().Concat(recent.Children.OfType<Button>()))
                button.FontFamily = fontFamily;
        }

        fontBox.SelectionChanged += (_, _) =>
        {
            if (fontBox.SelectedItem is string fontName)
                ApplySymbolFont(fontName);
        };
        subsetBox.SelectionChanged += (_, _) =>
        {
            if (subsetBox.SelectedItem is string subset)
            {
                var symbols = GetSymbolsForSubset(subset);
                SelectSymbol(symbols[0]);
                PopulateGrid(subset);
            }
        };
        PopulateGrid(SubsetChoices[0]);
        PopulateRecent();
        if (fontBox.SelectedItem is string initialFontName)
            ApplySymbolFont(initialFontName);

        var scroll = new ScrollViewer
        {
            Content = grid,
            Height = 246,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var specialList = CreateSpecialCharacterList(SelectSymbolText, AcceptSelectedSymbol);
        var codeRow = CreateCharacterCodeRow(selectedCode, SelectSymbolText);
        var btnRow = CreateButtonRow(AcceptSelectedSymbol);

        var symbolsPanel = new StackPanel();
        symbolsPanel.Children.Add(topGrid);
        symbolsPanel.Children.Add(scroll);
        symbolsPanel.Children.Add(recent);

        var specialPanel = new StackPanel();
        specialPanel.Children.Add(specialList);

        tabControl.Items.Add(new TabItem { Header = UiText.Get("SymbolPicker_SymbolsTab"), Content = symbolsPanel });
        tabControl.Items.Add(new TabItem { Header = UiText.Get("SymbolPicker_SpecialCharactersTab"), Content = specialPanel });

        var leftPanel = new StackPanel();
        leftPanel.Children.Add(tabControl);
        leftPanel.Children.Add(codeRow);
        leftPanel.Children.Add(btnRow);

        DockPanel.SetDock(preview, Dock.Right);
        outer.Children.Add(preview);
        outer.Children.Add(leftPanel);

        Loaded += (_, _) => FocusInitialKeyboardTarget(grid);
        return outer;
    }

    private ListBox CreateSpecialCharacterList(Action<string> selectSymbolText, Action acceptSelectedSymbol)
    {
        var specialList = new ListBox { Height = 292, MinWidth = 360 };
        AutomationProperties.SetName(specialList, UiText.Get("SymbolPicker_SpecialCharactersAutomationName"));
        foreach (var special in GetSpecialCharacters())
        {
            var item = new ListBoxItem
            {
                Content = $"{special.Name}    {special.Symbol}",
                Tag = special.Symbol,
                FontSize = 14
            };
            AutomationProperties.SetName(item, UiText.Format("SymbolPicker_SpecialCharacterAutomationNameFormat", special.Name, CreateSymbolAutomationName(special.Symbol)));
            specialList.Items.Add(item);
        }
        specialList.SelectionChanged += (_, _) =>
        {
            if (specialList.SelectedItem is ListBoxItem { Tag: string symbol })
                selectSymbolText(symbol);
        };
        specialList.MouseDoubleClick += (_, _) => acceptSelectedSymbol();
        return specialList;
    }

    private StackPanel CreateCharacterCodeRow(TextBox selectedCode, Action<string> selectSymbolText)
    {
        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        codeRow.Children.Add(new Label { Content = UiText.Get("SymbolPicker_CharacterCodeLabel"), Target = selectedCode, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        codeRow.Children.Add(selectedCode);
        codeRow.Children.Add(new TextBlock { Text = UiText.Get("SymbolPicker_FromUnicodeHex"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        var codeSelect = new Button { Content = UiText.Get("SymbolPicker_GoButton"), Width = 52, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(codeSelect, UiText.Get("SymbolPicker_GoToCharacterCodeAutomationName"));
        AutomationProperties.SetHelpText(codeSelect, UiText.Get("SymbolPicker_GoToCharacterCodeHelpText"));
        codeSelect.Click += (_, _) =>
        {
            if (TryParseCharacterCode(selectedCode.Text, out var symbol))
            {
                selectSymbolText(symbol);
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
        codeRow.Children.Add(codeSelect);
        return codeRow;
    }

    private static StackPanel CreateButtonRow(Action acceptSelectedSymbol)
    {
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var insert = new Button { Content = UiText.Get("SymbolPicker_InsertButton"), Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(insert, UiText.Get("SymbolPicker_InsertSelectedSymbolAutomationName"));
        AutomationProperties.SetHelpText(insert, UiText.Get("SymbolPicker_InsertSelectedSymbolHelpText"));
        insert.Click += (_, _) => acceptSelectedSymbol();
        var cancel = new Button { Content = UiText.Cancel, Width = 80, IsCancel = true };
        AutomationProperties.SetName(cancel, UiText.Get("SymbolPicker_CancelAutomationName"));
        AutomationProperties.SetHelpText(cancel, UiText.Get("SymbolPicker_CancelHelpText"));
        btnRow.Children.Add(insert);
        btnRow.Children.Add(cancel);
        return btnRow;
    }

    private static void FocusInitialKeyboardTarget(UniformGrid grid)
    {
        if (grid.Children.OfType<Button>().FirstOrDefault() is not { } firstSymbol)
            return;

        firstSymbol.Focus();
        Keyboard.Focus(firstSymbol);
    }

    private void ShowInvalidCharacterCodeWarning(TextBox selectedCode)
    {
        DialogMessageHelper.ShowWarning(this, UiText.Get("SymbolPicker_InvalidCharacterCodeMessage"), Title);
        selectedCode.Focus();
        selectedCode.SelectAll();
        Keyboard.Focus(selectedCode);
    }

    private static string CreateSymbolAutomationName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return UiText.Get("SymbolPicker_SymbolAutomationName");

        var rune = value.EnumerateRunes().FirstOrDefault();
        return rune == default
            ? UiText.Get("SymbolPicker_SymbolAutomationName")
            : UiText.Format("SymbolPicker_SymbolCodeAutomationNameFormat", rune.Value);
    }
}
