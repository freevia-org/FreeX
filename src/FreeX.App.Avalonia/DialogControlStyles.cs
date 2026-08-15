using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

namespace FreeX.App.Avalonia;

/// <summary>
/// Registers low-priority application styles for code-built dialogs. Shared shell owns control
/// metrics, templates, and selectors; FreeX supplies only its product-theme brushes and the one
/// Options-page disabled-opacity exception.
/// </summary>
internal static class DialogControlStyles
{
    private static readonly IBrush BorderBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(0xAB, 0xAB, 0xAB));

    private static readonly IBrush SelectionBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0x0F, 0x6D, 0x8C));

    private static readonly IBrush SelectionForegroundBrush =
        AvaloniaThemeResourceResolver.Find<IBrush>(ProductThemeResourceProfiles.FreeX.Brush("Text"))
        ?? new ImmutableSolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));

    private static readonly IBrush TextSelectionBrush =
        new ImmutableSolidColorBrush(Color.FromRgb(173, 214, 255));

    public static IEnumerable<IStyle> Build()
    {
        var options = new AvaloniaCompactDialogFallbackStyleOptions(
            AvaloniaCompactDialogChrome.WindowsStyle,
            BorderBrush,
            SelectionBrush,
            SelectionForegroundBrush,
            TextSelectionBrush);

        foreach (var style in AvaloniaCompactDialogFallbackStyles.Create(options))
            yield return style;

        yield return new Style(x => x
            .OfType<CheckBox>()
            .Class("free-options-ease-checkbox")
            .Class(":disabled"))
        {
            // WPF keeps disabled labels on this Options page at full contrast while changing
            // the glyph fill, border, and mark through shared compact-toggle chrome.
            Setters = { new Setter(Visual.OpacityProperty, 1d) },
        };
    }
}
