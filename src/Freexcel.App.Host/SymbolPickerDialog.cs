using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public sealed class SymbolPickerDialog : Window
{
    public char SelectedChar { get; private set; }

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
        Width = 460; Height = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var outer = new DockPanel { Margin = new Thickness(12) };

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

        var wrap = new WrapPanel { Width = 340 };
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
                if (s is Button b && b.Tag is char c) preview.Text = c.ToString();
            };
            btn.Click += (s, _) =>
            {
                if (s is Button b && b.Tag is char c)
                {
                    SelectedChar = c;
                    DialogResult = true;
                }
            };
            wrap.Children.Add(btn);
        }

        var scroll = new ScrollViewer { Content = wrap, Height = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        btnRow.Children.Add(cancel);

        var leftPanel = new StackPanel();
        leftPanel.Children.Add(scroll);
        leftPanel.Children.Add(btnRow);

        DockPanel.SetDock(preview, Dock.Right);
        outer.Children.Add(preview);
        outer.Children.Add(leftPanel);

        Content = outer;
    }
}
