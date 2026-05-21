using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Freexcel.App.Host;

public sealed class SymbolPickerDialog : Window
{
    private static readonly string[] FontChoices = ["Segoe UI Symbol", "Calibri", "Arial", "Times New Roman"];
    private static readonly string[] SubsetChoices = ["Currency Symbols", "Greek and Coptic", "Arrows", "Mathematical Operators", "Geometric Shapes"];
    private static readonly char[] RecentSymbols = ['\u20ac', '\u00a3', '\u00a5', '\u00a9', '\u00ae', '\u2122', '\u00b0', '\u00b1'];

    private static readonly IReadOnlyDictionary<string, char[]> SymbolsBySubset = new Dictionary<string, char[]>
    {
        ["Currency Symbols"] = ['\u20ac', '\u00a3', '\u00a5', '\u00a2', '\u00a4', '\u20a9', '\u20aa', '\u20ab', '\u20ac', '\u20ad', '\u20ae', '\u20af'],
        ["Greek and Coptic"] = ['\u03b1', '\u03b2', '\u03b3', '\u03b4', '\u03b5', '\u03b6', '\u03b7', '\u03b8', '\u03b9', '\u03ba', '\u03bb', '\u03bc', '\u03bd', '\u03be', '\u03bf', '\u03c0', '\u03c1', '\u03c3', '\u03c4', '\u03c5', '\u03c6', '\u03c7', '\u03c8', '\u03c9'],
        ["Arrows"] = ['\u2190', '\u2191', '\u2192', '\u2193', '\u2194', '\u2195', '\u2196', '\u2197', '\u2198', '\u2199', '\u21d0', '\u21d2', '\u21d4'],
        ["Mathematical Operators"] = ['\u00b1', '\u00d7', '\u00f7', '\u2212', '\u221e', '\u2211', '\u221a', '\u222b', '\u2248', '\u2260', '\u2264', '\u2265'],
        ["Geometric Shapes"] = ['\u25cf', '\u25cb', '\u25a0', '\u25a1', '\u25b2', '\u25b3', '\u25bc', '\u25bd', '\u2605', '\u2606', '\u25c6', '\u25c7']
    };

    public char SelectedChar { get; private set; }

    public SymbolPickerDialog()
    {
        Title = "Symbol";
        Width = 560;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        SelectedChar = GetSymbolsForSubset(SubsetChoices[0])[0];

        var outer = new DockPanel { Margin = new Thickness(12) };
        var selectedCode = new TextBox { Width = 72, IsReadOnly = true, Text = ToCodeText(SelectedChar) };
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

        void SelectSymbol(char value)
        {
            SelectedChar = value;
            preview.Text = value.ToString();
            selectedCode.Text = ToCodeText(value);
        }

        void PopulateGrid(string subset)
        {
            grid.Children.Clear();
            foreach (var ch in GetSymbolsForSubset(subset))
            {
                var button = new Button
                {
                    Content = ch.ToString(),
                    Width = 34,
                    Height = 34,
                    FontSize = 16,
                    Margin = new Thickness(1),
                    Tag = ch,
                    FontFamily = preview.FontFamily
                };
                button.Click += (s, _) =>
                {
                    if (s is Button { Tag: char value })
                        SelectSymbol(value);
                };
                grid.Children.Add(button);
            }
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

        var scroll = new ScrollViewer
        {
            Content = grid,
            Height = 246,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var recent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        recent.Children.Add(new TextBlock { Text = "Recently used symbols", VerticalAlignment = VerticalAlignment.Center, Width = 140 });
        foreach (var ch in RecentSymbols)
        {
            var button = new Button { Content = ch.ToString(), Width = 28, Height = 28, FontSize = 14, Margin = new Thickness(1), Tag = ch };
            button.Click += (s, _) =>
            {
                if (s is Button { Tag: char value })
                    SelectSymbol(value);
            };
            recent.Children.Add(button);
        }

        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        codeRow.Children.Add(new TextBlock { Text = "Character code:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        codeRow.Children.Add(selectedCode);
        codeRow.Children.Add(new TextBlock { Text = "from: Unicode (hex)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var insert = new Button { Content = "_Insert", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        insert.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "_Cancel", Width = 80, IsCancel = true };
        btnRow.Children.Add(insert);
        btnRow.Children.Add(cancel);

        var leftPanel = new StackPanel();
        leftPanel.Children.Add(topGrid);
        leftPanel.Children.Add(scroll);
        leftPanel.Children.Add(recent);
        leftPanel.Children.Add(codeRow);
        leftPanel.Children.Add(btnRow);

        DockPanel.SetDock(preview, Dock.Right);
        outer.Children.Add(preview);
        outer.Children.Add(leftPanel);

        Content = outer;
    }

    public static IReadOnlyList<char> GetSymbolsForSubset(string subset) =>
        SymbolsBySubset.TryGetValue(subset, out var symbols)
            ? symbols
            : SymbolsBySubset[SubsetChoices[0]];

    private static string ToCodeText(char value) =>
        ((int)value).ToString("X4");
}
