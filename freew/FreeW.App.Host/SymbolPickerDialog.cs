using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FreeW.App.Host;

/// <summary>
/// A tiny modal picker showing a grid of common glyphs (symbols, punctuation, currency, Greek/math).
/// Clicking a glyph closes the dialog and returns it; the caller inserts it at the caret as plain text.
/// Returns the chosen glyph, or null if the user cancels.
/// </summary>
internal sealed class SymbolPickerDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // A reasonable spread of frequently-needed glyphs: legal/typographic marks, dashes/ellipsis,
    // math/comparison operators, arrows, currency, fractions, and a few Greek letters.
    private static readonly string[] Glyphs =
    [
        "©", "®", "™", "§", "¶", "•", // © ® ™ § ¶ •
        "–", "—", "…", "°", "±", "×", // – — … ° ± ×
        "÷", "≤", "≥", "≠", "≈", "∞", // ÷ ≤ ≥ ≠ ≈ ∞
        "→", "←", "↑", "↓", "€", "£", // → ← ↑ ↓ € £
        "¥", "¢", "½", "¼", "¾", "‰", // ¥ ¢ ½ ¼ ¾ ‰
        "α", "β", "γ", "π", "Σ", "Ω", // α β γ π Σ Ω
    ];

    private string? _result;

    private SymbolPickerDialog(Window? owner)
    {
        Owner = owner;
        Title = "Symbol";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(8) };
        var grid = new UniformGrid { Columns = 6 };
        foreach (var glyph in Glyphs)
        {
            var button = new Button
            {
                Content = glyph,
                Width = 36,
                Height = 36,
                FontSize = 18,
                Margin = new Thickness(2),
                ToolTip = $"U+{char.ConvertToUtf32(glyph, 0):X4}"
            };
            button.Click += (_, _) => { _result = glyph; DialogResult = true; };
            grid.Children.Add(button);
        }
        panel.Children.Add(grid);

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(2, 8, 2, 0),
            Padding = new Thickness(8, 2, 8, 2)
        };
        panel.Children.Add(cancel);

        Content = panel;
    }

    /// <summary>Show the picker; returns the chosen glyph, or null if cancelled.</summary>
    public static string? Prompt(Window? owner)
    {
        var dialog = new SymbolPickerDialog(owner);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }
}
