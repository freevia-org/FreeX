using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Freexcel.App.Host;

public sealed partial class SymbolPickerDialog : Window
{
    private static readonly string[] FontChoices = ["Segoe UI Symbol", "Calibri", "Arial", "Times New Roman"];

    public char SelectedChar { get; private set; }
    public string SelectedSymbol { get; private set; } = "";

    public readonly record struct SpecialCharacter(string Name, string Symbol);

    public SymbolPickerDialog()
    {
        Title = "Symbol";
        Width = 620;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        SelectedChar = GetSymbolsForSubset(SubsetChoices[0])[0];
        SelectedSymbol = SelectedChar.ToString();

        var recentSymbols = CommonSymbols.ToList();
        var outer = new DockPanel { Margin = new Thickness(12) };
        var selectedCode = new TextBox { Width = 88, Text = ToCodeText(SelectedSymbol) };
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
        topGrid.Children.Add(new Label { Content = "_Font:", Target = fontBox, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(fontBox, 1);
        topGrid.Children.Add(fontBox);
        var subsetLabel = new Label { Content = "_Subset:", Target = subsetBox, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(subsetLabel, 2);
        topGrid.Children.Add(subsetLabel);
        Grid.SetColumn(subsetBox, 3);
        topGrid.Children.Add(subsetBox);

        var grid = new UniformGrid { Columns = 10, Width = 360 };
        var recent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

        void SelectSymbol(char value)
        {
            SelectedChar = value;
            SelectSymbolText(value.ToString());
        }

        void SelectSymbolText(string value)
        {
            SelectedSymbol = value;
            SelectedChar = value.Length == 1 ? value[0] : '\0';
            preview.Text = value;
            selectedCode.Text = ToCodeText(value);
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
            button.Click += (s, _) =>
            {
                if (s is Button { Tag: string symbol })
                    SelectSymbolText(symbol);
            };
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
            recent.Children.Add(new TextBlock { Text = "Recently used symbols", VerticalAlignment = VerticalAlignment.Center, Width = 140 });
            foreach (var symbol in recentSymbols)
                recent.Children.Add(CreateSymbolButton(symbol, 28, 28, 14));
        }

        fontBox.SelectionChanged += (_, _) =>
        {
            if (fontBox.SelectedItem is string fontName)
                preview.FontFamily = new FontFamily(fontName);
            foreach (var button in grid.Children.OfType<Button>())
                button.FontFamily = preview.FontFamily;
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

        var scroll = new ScrollViewer
        {
            Content = grid,
            Height = 246,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var specialList = new ListBox { Height = 292, MinWidth = 360 };
        foreach (var special in GetSpecialCharacters())
        {
            var item = new ListBoxItem
            {
                Content = $"{special.Name}    {special.Symbol}",
                Tag = special.Symbol,
                FontSize = 14
            };
            specialList.Items.Add(item);
        }
        specialList.SelectionChanged += (_, _) =>
        {
            if (specialList.SelectedItem is ListBoxItem { Tag: string symbol })
                SelectSymbolText(symbol);
        };
        specialList.SelectedIndex = 0;

        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        codeRow.Children.Add(new TextBlock { Text = "Character code:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        codeRow.Children.Add(selectedCode);
        codeRow.Children.Add(new TextBlock { Text = "from: Unicode (hex)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        var codeSelect = new Button { Content = "_Go", Width = 52, Margin = new Thickness(8, 0, 0, 0) };
        codeSelect.Click += (_, _) =>
        {
            if (TryParseCharacterCode(selectedCode.Text, out var symbol))
                SelectSymbolText(symbol);
        };
        codeRow.Children.Add(codeSelect);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var insert = new Button { Content = "_Insert", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        insert.Click += (_, _) =>
        {
            recentSymbols = PromoteRecentSymbol(recentSymbols, SelectedSymbol, 8).ToList();
            PopulateRecent();
            DialogResult = true;
        };
        var cancel = new Button { Content = "_Cancel", Width = 80, IsCancel = true };
        btnRow.Children.Add(insert);
        btnRow.Children.Add(cancel);

        var symbolsPanel = new StackPanel();
        symbolsPanel.Children.Add(topGrid);
        symbolsPanel.Children.Add(scroll);
        symbolsPanel.Children.Add(recent);

        var specialPanel = new StackPanel();
        specialPanel.Children.Add(specialList);

        tabControl.Items.Add(new TabItem { Header = "Symbols", Content = symbolsPanel });
        tabControl.Items.Add(new TabItem { Header = "Special Characters", Content = specialPanel });

        var leftPanel = new StackPanel();
        leftPanel.Children.Add(tabControl);
        leftPanel.Children.Add(codeRow);
        leftPanel.Children.Add(btnRow);

        DockPanel.SetDock(preview, Dock.Right);
        outer.Children.Add(preview);
        outer.Children.Add(leftPanel);

        Content = outer;
    }

}
