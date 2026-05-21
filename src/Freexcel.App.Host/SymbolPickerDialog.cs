using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Freexcel.App.Host;

public sealed class SymbolPickerDialog : Window
{
    public char SelectedChar { get; private set; }

    private static readonly string[] FontChoices = ["Segoe UI Symbol", "Calibri", "Arial", "Times New Roman"];
    private static readonly string[] SubsetChoices = ["Currency Symbols", "Greek and Coptic", "Arrows", "Mathematical Operators", "Geometric Shapes"];
    private static readonly char[] RecentSymbols = ['€', '£', '¥', '©', '®', '™', '°', '±'];

    private static readonly char[] CommonSymbols =
    [
        '©','®','™','°','±','²','³','µ','¶','·','¼','½','¾',
        'À','Á','Â','Ã','Ä','Å','Æ','Ç','È','É','Ê','Ë',
        '€','£','¥','¢','¤','§','¨','ª','«','»','¬','¯',
        'α','β','γ','δ','ε','ζ','η','θ','ι','κ','λ','μ',
        'ν','ξ','ο','π','ρ','σ','τ','υ','φ','χ','ψ','ω',
        '←','→','↑','↓','↔','↕','⇒','⇐','⇔',
        '∞','∑','√','∫','≈','≠','≤','≥','÷','×','−',
        '●','○','■','□','▲','△','▼','▽','★','☆','♦','◆',
        '♠','♣','♥','♦','♪','♫','☺','☻','✓','✗','✔','✘'
    ];

    public SymbolPickerDialog()
    {
        Title = "Symbol";
        Width = 560; Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var outer = new DockPanel { Margin = new Thickness(12) };
        var selectedCode = new TextBox { Width = 72, IsReadOnly = true, Text = ToCodeText(CommonSymbols[0]) };

        var preview = new TextBlock
        {
            FontSize = 32,
            Width = 60,
            Height = 60,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 0, 0, 0),
            Text = CommonSymbols[0].ToString()
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
        foreach (var ch in CommonSymbols)
        {
            var btn = new Button
            {
                Content = ch.ToString(),
                Width = 34, Height = 34,
                FontSize = 16,
                Margin = new Thickness(1),
                Tag = ch
            };
            btn.MouseEnter += (s, _) =>
            {
                if (s is Button b && b.Tag is char c)
                {
                    preview.Text = c.ToString();
                    selectedCode.Text = ToCodeText(c);
                }
            };
            btn.Click += (s, _) =>
            {
                if (s is Button b && b.Tag is char c)
                {
                    SelectedChar = c;
                    DialogResult = true;
                }
            };
            grid.Children.Add(btn);
        }

        var scroll = new ScrollViewer { Content = grid, Height = 246,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var recent = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        recent.Children.Add(new TextBlock { Text = "Recently used symbols", VerticalAlignment = VerticalAlignment.Center, Width = 140 });
        foreach (var ch in RecentSymbols)
        {
            var btn = new Button { Content = ch.ToString(), Width = 28, Height = 28, FontSize = 14, Margin = new Thickness(1), Tag = ch };
            btn.Click += (s, _) =>
            {
                if (s is Button b && b.Tag is char c)
                {
                    SelectedChar = c;
                    DialogResult = true;
                }
            };
            recent.Children.Add(btn);
        }

        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        codeRow.Children.Add(new TextBlock { Text = "Character code:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        codeRow.Children.Add(selectedCode);
        codeRow.Children.Add(new TextBlock { Text = "from: Unicode (hex)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = "_Cancel", Width = 80, IsCancel = true };
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

    private static string ToCodeText(char value) =>
        ((int)value).ToString("X4");
}
