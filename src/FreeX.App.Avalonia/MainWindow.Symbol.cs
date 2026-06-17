using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Insert ▸ Symbol (parity gap: the button was a no-op). A picker of common symbols; choosing one
    // appends it to the active cell's text via WorkbookSession.CommitCellText (undo/redo).

    private static readonly string[] CommonSymbols =
    [
        "€", "£", "¥", "¢", "$", "©", "®", "™", "°", "±", "×", "÷", "µ", "½", "¼", "¾",
        "≈", "≠", "≤", "≥", "→", "←", "↑", "↓", "•", "…", "—", "§", "¶", "√", "∞", "π",
        "α", "β", "γ", "δ", "θ", "λ", "Σ", "Ω", "Δ", "✓", "✗", "★", "☆", "→",
    ];

    private async Task ShowSymbolPickerAsync()
    {
        string? picked = null;
        var grid = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 360 };

        var dialog = new Window
        {
            Title = "Symbol",
            Width = 400,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        foreach (var symbol in CommonSymbols)
        {
            var local = symbol;
            var button = new Button
            {
                Content = symbol,
                Width = 40,
                Height = 36,
                FontSize = 16,
                Margin = new Thickness(2),
            };
            button.Click += (_, _) => { picked = local; dialog.Close(); };
            grid.Children.Add(button);
        }

        dialog.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10),
            Content = grid,
        };

        await dialog.ShowDialog(this);
        if (picked is null)
            return;

        var current = FormatEditText(_session.ActiveSheet.GetCell(_session.ActiveCell), _session.ActiveCell);
        var result = _session.CommitCellText(current + picked);
        RefreshShell(result.Success
            ? $"Inserted {picked} into {FormatCellReference(_session.ActiveCell)}"
            : result.ErrorMessage ?? "Could not insert the symbol.");
    }
}
