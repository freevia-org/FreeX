using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A two-tab Font dialog matching the Home > Font dialog-launcher in Word. The <b>Font</b> tab
/// covers family, size, style (bold/italic/underline/strikethrough), colour, and character effects
/// (small caps, all caps, superscript, subscript). The <b>Advanced</b> tab surfaces the OpenType
/// and character-spacing fields already on <see cref="RunFormatting"/>:
/// character spacing, kerning threshold, raised/lowered position, ligatures, stylistic set, number
/// form, and number spacing. Applies to the selection via the command bus through
/// <see cref="DocumentView.ApplyFontFormatting"/>.
/// </summary>
internal static class FontDialog
{
    // Named colour palette matching the Home > Font text-colour picker.
    private static readonly (string Label, string? Hex)[] Colors =
    [
        ("Automatic",    null),
        ("Black",        "#000000"),
        ("Dark Red",     "#C00000"),
        ("Red",          "#FF0000"),
        ("Blue accent",  "#2F5496"),
        ("Blue",         "#0070C0"),
        ("Green",        "#00B050"),
        ("Purple",       "#7030A0"),
        ("Grey",         "#7F7F7F"),
    ];

    private static readonly (string Label, double Size)[] Sizes =
    [
        ("8", 8), ("9", 9), ("10", 10), ("11", 11), ("12", 12),
        ("14", 14), ("16", 16), ("18", 18), ("24", 24), ("28", 28),
        ("36", 36), ("48", 48), ("72", 72),
    ];

    private static readonly (string Label, LigatureMode Mode)[] LigatureModes =
    [
        ("(None)",                    LigatureMode.None),
        ("None (explicit)",           LigatureMode.NoneExplicit),
        ("Standard",                  LigatureMode.Standard),
        ("Contextual",                LigatureMode.Contextual),
        ("Standard and Contextual",   LigatureMode.StandardContextual),
        ("Historical",                LigatureMode.Historical),
        ("Discretional",              LigatureMode.Discretional),
        ("All",                       LigatureMode.All),
    ];

    private static readonly (string Label, NumberForm Form)[] NumberForms =
    [
        ("(Default)",  NumberForm.Default),
        ("Lining",     NumberForm.Lining),
        ("Old-Style",  NumberForm.OldStyle),
    ];

    private static readonly (string Label, NumberSpacing Spacing)[] NumberSpacings =
    [
        ("(Default)",    NumberSpacing.Default),
        ("Proportional", NumberSpacing.Proportional),
        ("Tabular",      NumberSpacing.Tabular),
    ];

    /// <summary>
    /// Show the Font dialog seeded from <paramref name="current"/>. Returns the edited
    /// <see cref="RunFormatting"/>, or null if cancelled.
    /// </summary>
    public static RunFormatting? Prompt(Window? owner, RunFormatting current)
    {
        RunFormatting? result = null;

        var dialog = new Window
        {
            Title = "Font",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        // ── Font tab ────────────────────────────────────────────────────────
        var familyBox = new TextBox
        {
            Text = current.FontFamily ?? string.Empty,
            MinWidth = 200,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var sizeBox = new ComboBox { MinWidth = 80, IsEditable = true, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (lbl, _) in Sizes)
            sizeBox.Items.Add(lbl);
        sizeBox.Text = current.FontSizePt.HasValue
            ? current.FontSizePt.Value.ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;

        var boldCheck      = new CheckBox { Content = "Bold",             IsChecked = current.Bold,          Margin = new Thickness(0, 0, 12, 4) };
        var italicCheck    = new CheckBox { Content = "Italic",           IsChecked = current.Italic,        Margin = new Thickness(0, 0, 12, 4) };
        var underlineCheck = new CheckBox { Content = "Underline",        IsChecked = current.Underline,     Margin = new Thickness(0, 0, 12, 4) };
        var strikeCheck    = new CheckBox { Content = "Strikethrough",    IsChecked = current.Strikethrough, Margin = new Thickness(0, 0, 12, 4) };
        var smallCapsCheck = new CheckBox { Content = "Small Caps",       IsChecked = current.SmallCaps,     Margin = new Thickness(0, 0, 12, 4) };
        var allCapsCheck   = new CheckBox { Content = "All Caps",         IsChecked = current.AllCaps,       Margin = new Thickness(0, 0, 12, 4) };
        var superCheck     = new CheckBox { Content = "Superscript",      IsChecked = current.VerticalAlign == VerticalAlign.Superscript, Margin = new Thickness(0, 0, 12, 4) };
        var subCheck       = new CheckBox { Content = "Subscript",        IsChecked = current.VerticalAlign == VerticalAlign.Subscript,   Margin = new Thickness(0, 0, 0, 4) };

        var colorBox = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (lbl, _) in Colors)
            colorBox.Items.Add(lbl);
        colorBox.SelectedIndex = IndexOfColor(current.ColorHex);

        var fontPanel = new StackPanel { Margin = new Thickness(10) };
        FontRow(fontPanel, "Font family:", familyBox);
        FontRow(fontPanel, "Size (pt):",   sizeBox);
        FontRow(fontPanel, "Color:",       colorBox);
        fontPanel.Children.Add(new TextBlock { Text = "Style:", Margin = new Thickness(0, 4, 0, 2) });
        var effectsWrap = new WrapPanel();
        foreach (var cb in new[] { boldCheck, italicCheck, underlineCheck, strikeCheck, smallCapsCheck, allCapsCheck, superCheck, subCheck })
            effectsWrap.Children.Add(cb);
        fontPanel.Children.Add(effectsWrap);

        // ── Advanced tab ─────────────────────────────────────────────────────
        var spacingBox = NumberTextBox(current.CharacterSpacingPt);
        var kerningBox = new TextBox
        {
            Text = current.KerningMinSizePt.HasValue
                ? current.KerningMinSizePt.Value.ToString("0.##", CultureInfo.CurrentCulture)
                : string.Empty,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var positionBox = NumberTextBox(current.PositionPt);

        var ligatureBox = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (lbl, _) in LigatureModes)
            ligatureBox.Items.Add(lbl);
        ligatureBox.SelectedIndex = IndexOfLigature(current.Ligatures);

        var stylisticBox = new TextBox
        {
            Text = current.StylisticSet.HasValue
                ? current.StylisticSet.Value.ToString(CultureInfo.CurrentCulture)
                : string.Empty,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "OpenType stylistic set id (1–20), or blank for none",
        };

        var numberFormBox = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (lbl, _) in NumberForms)
            numberFormBox.Items.Add(lbl);
        numberFormBox.SelectedIndex = IndexOfNumberForm(current.NumberForm);

        var numberSpacingBox = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (lbl, _) in NumberSpacings)
            numberSpacingBox.Items.Add(lbl);
        numberSpacingBox.SelectedIndex = IndexOfNumberSpacing(current.NumberSpacing);

        var advPanel = new StackPanel { Margin = new Thickness(10) };
        FontRow(advPanel, "Character spacing (pt):", spacingBox);
        FontRow(advPanel, "Kerning min size (pt):",  kerningBox);
        FontRow(advPanel, "Position (pt):",          positionBox);
        FontRow(advPanel, "Ligatures:",              ligatureBox);
        FontRow(advPanel, "Stylistic set (1–20):",   stylisticBox);
        FontRow(advPanel, "Number form:",            numberFormBox);
        FontRow(advPanel, "Number spacing:",         numberSpacingBox);

        // ── Tab control ──────────────────────────────────────────────────────
        var tabs = new TabControl { Margin = new Thickness(0) };
        tabs.Items.Add(new TabItem { Header = "Font",     Content = new ScrollViewer { Content = fontPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = "Advanced", Content = new ScrollViewer { Content = advPanel,  VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        // ── Buttons ──────────────────────────────────────────────────────────
        void Accept()
        {
            // Font tab parsing
            var family = familyBox.Text.Trim();
            double? sizePt = null;
            var sizeText = sizeBox.Text.Trim();
            if (sizeText.Length > 0)
            {
                if (!double.TryParse(sizeText, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) || parsed <= 0)
                {
                    DialogMessageHelper.ShowWarning(dialog, "Enter a positive font size in points.");
                    return;
                }
                sizePt = parsed;
            }

            var colorHex = Colors[Math.Max(0, colorBox.SelectedIndex)].Hex;
            var vertAlign = (superCheck.IsChecked == true) ? VerticalAlign.Superscript
                          : (subCheck.IsChecked   == true) ? VerticalAlign.Subscript
                          : VerticalAlign.Baseline;

            // Advanced tab parsing
            if (!TryParseDouble(spacingBox.Text, out var spacingPt))
            {
                DialogMessageHelper.ShowWarning(dialog, "Enter a valid character spacing in points.");
                return;
            }

            double? kerningPt = null;
            if (kerningBox.Text.Trim().Length > 0)
            {
                if (!TryParseDouble(kerningBox.Text, out var kp) || kp < 0)
                {
                    DialogMessageHelper.ShowWarning(dialog, "Enter a non-negative kerning threshold in points, or leave blank.");
                    return;
                }
                kerningPt = kp;
            }

            if (!TryParseDouble(positionBox.Text, out var positionPt))
            {
                DialogMessageHelper.ShowWarning(dialog, "Enter a valid position offset in points.");
                return;
            }

            int? stylisticSet = null;
            if (stylisticBox.Text.Trim().Length > 0)
            {
                if (!int.TryParse(stylisticBox.Text.Trim(), out var ss) || ss < 1 || ss > 20)
                {
                    DialogMessageHelper.ShowWarning(dialog, "Stylistic set must be a number from 1 to 20, or blank.");
                    return;
                }
                stylisticSet = ss;
            }

            var ligatureMode   = LigatureModes[Math.Max(0, ligatureBox.SelectedIndex)].Mode;
            var numberForm     = NumberForms[Math.Max(0, numberFormBox.SelectedIndex)].Form;
            var numberSpacing  = NumberSpacings[Math.Max(0, numberSpacingBox.SelectedIndex)].Spacing;

            result = current with
            {
                FontFamily   = family.Length > 0 ? family : null,
                FontSizePt   = sizePt,
                Bold         = boldCheck.IsChecked == true,
                Italic       = italicCheck.IsChecked == true,
                Underline    = underlineCheck.IsChecked == true,
                Strikethrough= strikeCheck.IsChecked == true,
                SmallCaps    = smallCapsCheck.IsChecked == true,
                AllCaps      = allCapsCheck.IsChecked == true,
                VerticalAlign= vertAlign,
                ColorHex     = colorHex,
                // Advanced
                CharacterSpacingPt = spacingPt,
                KerningMinSizePt   = kerningPt,
                PositionPt         = positionPt,
                Ligatures          = ligatureMode,
                StylisticSet       = stylisticSet,
                NumberForm         = numberForm,
                NumberSpacing      = numberSpacing,
            };
            dialog.DialogResult = true;
        }

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 10, 0, 0));

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(tabs);
        root.Children.Add(buttons);
        dialog.Content = root;

        familyBox.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }

    private static void FontRow(Panel panel, string label, UIElement control)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 2),
        });
        if (control is FrameworkElement fe)
            fe.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(control);
    }

    private static TextBox NumberTextBox(double value) => new()
    {
        Text = value.ToString("0.##", CultureInfo.CurrentCulture),
        MinWidth = 100,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static int IndexOfColor(string? hex)
    {
        if (hex is null) return 0;
        for (var i = 0; i < Colors.Length; i++)
            if (string.Equals(Colors[i].Hex, hex, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }

    private static int IndexOfLigature(LigatureMode mode)
    {
        for (var i = 0; i < LigatureModes.Length; i++)
            if (LigatureModes[i].Mode == mode) return i;
        return 0;
    }

    private static int IndexOfNumberForm(NumberForm form)
    {
        for (var i = 0; i < NumberForms.Length; i++)
            if (NumberForms[i].Form == form) return i;
        return 0;
    }

    private static int IndexOfNumberSpacing(NumberSpacing spacing)
    {
        for (var i = 0; i < NumberSpacings.Length; i++)
            if (NumberSpacings[i].Spacing == spacing) return i;
        return 0;
    }
}
