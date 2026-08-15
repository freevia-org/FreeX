using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;

namespace FreeX.App.Avalonia;

/// <summary>
/// App-wide compact Avalonia control styles for dialog windows, matching the WPF shell's look.
/// Applied once in <see cref="App.OnFrameworkInitializationCompleted"/> after <c>FluentTheme</c> so
/// they serve as fallbacks for every dialog and panel that does not carry its own local styles.
///
/// Scoping: the styles are registered at the <c>Application</c> level (lowest priority in the
/// Avalonia style resolution order).  The ribbon's own <c>TabControl.Styles</c> block
/// (<see cref="Free.Shared.Ribbon.Avalonia.AvaloniaRibbonRenderer.ApplyRibbonTheme"/>) is a
/// <em>local</em> style collection and always wins over application-level styles, so the ribbon
/// is unaffected by anything defined here.  Any control that sets an explicit property value also
/// wins, so existing per-dialog overrides are preserved.
///
/// Design targets (matched from WPF screenshots):
/// <list type="bullet">
/// <item>CheckBox / RadioButton — ~14 px glyph, FontSize 12, tight padding</item>
/// <item>TabControl / TabItem   — compact header strip (FontSize 12, ~26 px height)</item>
/// <item>ListBox / ListBoxItem  — compact rows (~22 px), FontSize 12, 1 px border, subtle selection</item>
/// </list>
/// </summary>
internal static class DialogControlStyles
{
    // ── Sizing constants (WPF ground-truth) ─────────────────────────────────────────────────────────
    private const double DialogFontSize = 12d;
    private const double TabHeaderHeight = 24d;  // WPF tab header ~24-26 px
    private const double ListItemMinHeight = 22d;

    // ── Colors (shared with the ribbon palette) ──────────────────────────────────────────────────────
    // WS-G divergence: BorderBrush (#ABABAB) has no matching FreeX token role — left as literal.
    private static readonly IBrush BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xAB, 0xAB, 0xAB));
    // WS-G note: SelectionBrush is derived (AccentSoft @ alpha 0x40) — no standalone token role; left as literal.
    private static readonly IBrush SelectionBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0x0F, 0x6D, 0x8C));
    // WS-G token: FreeXTextBrush (#1F1F1F) — byte-identical to the literal; falls back when no app.
    private static readonly IBrush SelectionForegroundBrush =
        AvaloniaThemeResourceResolver.Find<IBrush>(ProductThemeResourceProfiles.FreeX.Brush("Text"))
        ?? new ImmutableSolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));

    /// <summary>
    /// Builds the <see cref="Styles"/> collection to add to <see cref="Application.Styles"/>.
    /// </summary>
    public static IEnumerable<IStyle> Build()
    {
        // ── CheckBox ────────────────────────────────────────────────────────────────────────────────
        // Avalonia Fluent defaults are much larger than WPF dialog toggles. Keep only the
        // application-level selector here; shared compact chrome owns the actual template.
        yield return new Style(x => x.OfType<CheckBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, DialogFontSize),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(
                    TemplatedControl.TemplateProperty,
                    AvaloniaCompactDialogChrome.CreateCompactCheckBoxTemplate(
                        AvaloniaCompactDialogChrome.WindowsStyle)),
            },
        };

        yield return new Style(x => x.OfType<CheckBox>().Class(":disabled"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.45d) },
        };

        yield return new Style(x => x.OfType<CheckBox>().Class("free-options-ease-checkbox").Class(":disabled"))
        {
            // WPF keeps this Options page's disabled labels at full contrast and changes the glyph
            // fill/check instead. Other dialogs retain the existing shared disabled opacity.
            Setters = { new Setter(Visual.OpacityProperty, 1d) },
        };

        // ── RadioButton ─────────────────────────────────────────────────────────────────────────────
        yield return new Style(x => x.OfType<RadioButton>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, DialogFontSize),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(
                    TemplatedControl.TemplateProperty,
                    AvaloniaCompactDialogChrome.CreateCompactRadioButtonTemplate(
                        AvaloniaCompactDialogChrome.WindowsStyle)),
            },
        };

        yield return new Style(x => x.OfType<RadioButton>().Class(":disabled"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.45d) },
        };

        // ── TabControl ──────────────────────────────────────────────────────────────────────────────
        // No template swap needed for TabControl itself — just remove the default padding so the
        // header strip does not add extra space around the compact tab items.
        yield return new Style(x => x.OfType<TabControl>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, DialogFontSize),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
            },
        };

        // ── TabItem ─────────────────────────────────────────────────────────────────────────────────
        // Avalonia Fluent default tab header height is ~48 px; WPF's is ~24-26 px.
        // Use the same compact height, FontSize 12, flat borders approach as the ribbon does locally.
        yield return new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, DialogFontSize),
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(Layoutable.HeightProperty, TabHeaderHeight),
                new Setter(Layoutable.MaxHeightProperty, TabHeaderHeight),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 2, 6, 2)),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1, 1, 1, 0)),
            },
        };

        // Selected tab — match WPF: slightly distinct border bottom so it looks like a physical tab.
        yield return new Style(x => x.OfType<TabItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xAB, 0xAB, 0xAB))),
                new Setter(TemplatedControl.FontSizeProperty, DialogFontSize),
            },
        };

        // ── ListBox ─────────────────────────────────────────────────────────────────────────────────
        // WPF ListBox: 1 px border, white background, no extra padding.
        yield return new Style(x => x.OfType<ListBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, DialogFontSize),
                new Setter(TemplatedControl.BorderBrushProperty, BorderBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.White),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
            },
        };

        // ── ListBoxItem ─────────────────────────────────────────────────────────────────────────────
        // Avalonia Fluent default item height is large; WPF rows are ~22 px.
        yield return new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, DialogFontSize),
                new Setter(Layoutable.MinHeightProperty, ListItemMinHeight),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 1, 4, 1)),
                new Setter(Layoutable.VerticalAlignmentProperty, VerticalAlignment.Center),
            },
        };

        // Selected list item — subtle accent tint, same text color (WPF uses SystemColors.HighlightBrush
        // which is a blue but in non-system-theme it's a soft highlight).
        yield return new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, SelectionBrush),
                new Setter(TemplatedControl.ForegroundProperty, SelectionForegroundBrush),
            },
        };

        // Focused + selected — keep the same compact look.
        yield return new Style(x => x.OfType<ListBoxItem>().Class(":selected").Class(":focus"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, SelectionBrush),
                new Setter(TemplatedControl.ForegroundProperty, SelectionForegroundBrush),
            },
        };

        // Readable text selection everywhere: Avalonia Fluent's default TextBox selection is the dark
        // accent, which makes selected black text hard to read (e.g. the auto-selected range in
        // CreateTable). Use a light blue so selected text stays legible, matching Windows. Single
        // app-wide seam — applies to every dialog/formula TextBox.
        yield return new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.SelectionBrushProperty, TextSelectionBrush),
            },
        };
    }

    /// <summary>Light-blue text-selection highlight that keeps black text readable (Windows-like).</summary>
    private static readonly IBrush TextSelectionBrush = new ImmutableSolidColorBrush(Color.FromRgb(173, 214, 255));
}
