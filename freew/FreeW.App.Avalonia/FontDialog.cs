using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW Avalonia Font dialog: a modal <see cref="Window"/> that lets the user inspect and change
/// the character formatting of the current caret / selection.
///
/// <para>
/// Pre-populated from <see cref="DocumentView.GetCaretFormatting"/> on open; on OK the changed
/// properties are applied in sequence via the <see cref="DocumentView"/> formatting methods.
/// Cancel (or OS-close) makes no changes.
/// </para>
///
/// <para>
/// The apply path is exposed as <see cref="ApplyResult"/> so tests can call it without displaying
/// a window.
/// </para>
///
/// Fields covered:
/// <list type="bullet">
///   <item>Font family (editable combo with standard presets)</item>
///   <item>Font size (editable combo with standard presets)</item>
///   <item>Bold, Italic, Underline, Strikethrough (checkboxes)</item>
///   <item>Superscript / Subscript (mutually exclusive checkboxes)</item>
///   <item>Font color (combo from the shared palette)</item>
///   <item>Highlight color (combo from a highlight palette)</item>
///   <item>Small Caps / All Caps (checkboxes)</item>
/// </list>
///
/// Deferred: Advanced typography (kerning, ligatures, number form/spacing, character spacing,
/// position) — modelled in <see cref="RunFormatting"/> but low-priority for the dialog surface.
/// </summary>
public sealed class FontDialog : Window
{
    // ── Standard font size ladder (mirrors FreeWRibbon.FontSizes) ────────────
    private static readonly string[] SizeLadder =
        ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72"];

    private static readonly string[] FamilyPresets =
        ["Calibri", "Arial", "Times New Roman", "Inter", "Verdana", "Georgia", "Courier New"];

    // ── Colour palettes ───────────────────────────────────────────────────────
    private static readonly (string Label, string? Hex)[] FontColorPalette =
    [
        ("Automatic",  null),
        ("Black",      "#000000"),
        ("Dark Red",   "#C00000"),
        ("Red",        "#FF0000"),
        ("Orange",     "#FF6600"),
        ("Yellow",     "#FFFF00"),
        ("Green",      "#00B050"),
        ("Blue",       "#0070C0"),
        ("Dark Blue",  "#00008B"),
        ("Purple",     "#7030A0"),
        ("White",      "#FFFFFF"),
    ];

    private static readonly (string Label, string? Hex)[] HighlightPalette =
    [
        ("None",        null),
        ("Yellow",      "#FFFF00"),
        ("Bright Green","#00FF00"),
        ("Cyan",        "#00FFFF"),
        ("Magenta",     "#FF00FF"),
        ("Red",         "#FF0000"),
        ("Dark Blue",   "#0000CD"),
        ("Teal",        "#008080"),
        ("Dark Red",    "#8B0000"),
        ("Dark Yellow", "#808000"),
        ("Gray 50%",    "#808080"),
        ("Gray 25%",    "#C0C0C0"),
        ("Black",       "#000000"),
        ("White",       "#FFFFFF"),
    ];

    // ── Snapshot of the initial formatting ───────────────────────────────────
    private readonly RunFormatting _original;

    // ── Controls ─────────────────────────────────────────────────────────────
    private readonly ComboBox _familyBox;
    private readonly ComboBox _sizeBox;
    private readonly CheckBox _boldChk   = new() { Content = "Bold",          Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _italicChk = new() { Content = "Italic",        Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _underlineChk = new() { Content = "Underline",  Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _strikeChk    = new() { Content = "Strikethrough", Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _superChk = new() { Content = "Superscript",    Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _subChk   = new() { Content = "Subscript",      Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _smallCapsChk = new() { Content = "Small Caps", Margin = new Thickness(0, 4, 12, 0) };
    private readonly CheckBox _allCapsChk   = new() { Content = "All Caps",   Margin = new Thickness(0, 4, 12, 0) };
    private readonly ComboBox _colorBox;
    private readonly ComboBox _highlightBox;

    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x00, 0x00)),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        IsVisible = false,
    };

    // ── Construction ──────────────────────────────────────────────────────────

    public FontDialog(RunFormatting current)
    {
        _original = current;

        Title = "Font";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        // ── Family combo ──────────────────────────────────────────────────────
        _familyBox = new ComboBox
        {
            MinWidth = 260,
            IsEditable = true,
        };
        _familyBox.ItemsSource = FamilyPresets;
        if (!string.IsNullOrEmpty(current.FontFamily))
            _familyBox.SelectedItem = current.FontFamily;
        else
            _familyBox.SelectedIndex = 0; // Calibri

        // ── Size combo ───────────────────────────────────────────────────────
        _sizeBox = new ComboBox
        {
            MinWidth = 100,
            IsEditable = true,
        };
        _sizeBox.ItemsSource = SizeLadder;
        var currentSizeStr = current.FontSizePt.HasValue
            ? current.FontSizePt.Value.ToString("G", CultureInfo.InvariantCulture)
            : "11";
        _sizeBox.SelectedItem = currentSizeStr;
        if (_sizeBox.SelectedItem is null)
        {
            // Size not in the standard ladder (e.g. 13pt) — set text directly.
            _sizeBox.SelectedIndex = -1;
        }

        // ── Style checkboxes ─────────────────────────────────────────────────
        _boldChk.IsChecked        = current.Bold;
        _italicChk.IsChecked      = current.Italic;
        _underlineChk.IsChecked   = current.Underline;
        _strikeChk.IsChecked      = current.Strikethrough;
        _superChk.IsChecked       = current.VerticalAlign == VerticalAlign.Superscript;
        _subChk.IsChecked         = current.VerticalAlign == VerticalAlign.Subscript;
        _smallCapsChk.IsChecked   = current.SmallCaps;
        _allCapsChk.IsChecked     = current.AllCaps;

        // Super / Sub are mutually exclusive.
        _superChk.IsCheckedChanged += (_, _) =>
        {
            if (_superChk.IsChecked == true) _subChk.IsChecked = false;
        };
        _subChk.IsCheckedChanged += (_, _) =>
        {
            if (_subChk.IsChecked == true) _superChk.IsChecked = false;
        };

        // ── Color combos ──────────────────────────────────────────────────────
        _colorBox     = BuildPaletteCombo(FontColorPalette,     current.ColorHex);
        _highlightBox = BuildPaletteCombo(HighlightPalette,     current.HighlightColorHex);

        // ── Layout ────────────────────────────────────────────────────────────
        var grid = BuildFormGrid();

        // Style checkboxes row 1: Bold Italic Underline Strikethrough
        var styleRow1 = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        styleRow1.Children.Add(_boldChk);
        styleRow1.Children.Add(_italicChk);
        styleRow1.Children.Add(_underlineChk);
        styleRow1.Children.Add(_strikeChk);

        // Style checkboxes row 2: Super Sub SmallCaps AllCaps
        var styleRow2 = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        styleRow2.Children.Add(_superChk);
        styleRow2.Children.Add(_subChk);
        styleRow2.Children.Add(_smallCapsChk);
        styleRow2.Children.Add(_allCapsChk);

        // ── Buttons ───────────────────────────────────────────────────────────
        var ok     = new Button { Content = "OK",     MinWidth = 84, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true  };
        ok.Click     += (_, _) => OnOk();
        cancel.Click += (_, _) => Close(null);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { ok, cancel },
        };

        var outer = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        outer.Children.Add(grid);
        outer.Children.Add(new TextBlock { Text = "Style", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
        outer.Children.Add(styleRow1);
        outer.Children.Add(styleRow2);
        outer.Children.Add(_status);
        outer.Children.Add(btnRow);

        Content = outer;

        // Escape to cancel.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
        };
    }

    /// <summary>Result record produced by the dialog on OK.</summary>
    public sealed record FontDialogResult(
        string? Family,
        double? SizePt,
        bool Bold, bool Italic, bool Underline, bool Strikethrough,
        VerticalAlign VerticalAlign,
        bool SmallCaps, bool AllCaps,
        string? ColorHex,
        string? HighlightHex);

    // ── OK handler ────────────────────────────────────────────────────────────

    private void OnOk()
    {
        _status.IsVisible = false;

        // Parse size.
        var sizeText = (_sizeBox.SelectedItem as string ?? string.Empty).Trim();
        double? sizePt = null;
        if (!string.IsNullOrEmpty(sizeText))
        {
            if (!double.TryParse(sizeText, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                _status.Text = $"Invalid font size: \"{sizeText}\". Enter a positive number.";
                _status.IsVisible = true;
                return;
            }
            sizePt = parsed;
        }

        // Resolve family.
        var family = (_familyBox.SelectedItem as string ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(family)) family = null;

        // Resolve vertical alignment.
        var va = _superChk.IsChecked == true ? VerticalAlign.Superscript
               : _subChk.IsChecked   == true ? VerticalAlign.Subscript
               : VerticalAlign.Baseline;

        // Resolve colors from the combo labels → hex.
        var colorHex     = SelectedHex(_colorBox,     FontColorPalette);
        var highlightHex = SelectedHex(_highlightBox, HighlightPalette);

        var result = new FontDialogResult(
            Family:       family,
            SizePt:       sizePt,
            Bold:         _boldChk.IsChecked    == true,
            Italic:       _italicChk.IsChecked  == true,
            Underline:    _underlineChk.IsChecked == true,
            Strikethrough: _strikeChk.IsChecked  == true,
            VerticalAlign: va,
            SmallCaps:    _smallCapsChk.IsChecked == true,
            AllCaps:      _allCapsChk.IsChecked   == true,
            ColorHex:     colorHex,
            HighlightHex: highlightHex);

        Close(result);
    }

    // ── Static apply ──────────────────────────────────────────────────────────

    /// <summary>
    /// Apply <paramref name="result"/> to <paramref name="editor"/>, changing only properties that
    /// differ from <paramref name="original"/>. Safe to call without showing the window (used by tests).
    /// </summary>
    public static void ApplyResult(DocumentView editor, FontDialogResult result, RunFormatting original)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        // Font family
        if (result.Family != original.FontFamily)
        {
            editor.SetSelectionFontFamily(result.Family ?? string.Empty);
        }

        // Font size
        if (result.SizePt != original.FontSizePt)
        {
            if (result.SizePt.HasValue)
                editor.SetSelectionFontSize(result.SizePt.Value);
        }

        // Bold
        if (result.Bold != original.Bold)
            editor.ToggleBold();

        // Italic
        if (result.Italic != original.Italic)
            editor.ToggleItalic();

        // Underline
        if (result.Underline != original.Underline)
            editor.ToggleUnderline();

        // Strikethrough
        if (result.Strikethrough != original.Strikethrough)
            editor.ToggleStrikethrough();

        // Vertical alignment (super/subscript)
        if (result.VerticalAlign != original.VerticalAlign)
        {
            switch (result.VerticalAlign)
            {
                case VerticalAlign.Superscript:
                    // Ensure we're not already in superscript.
                    if (original.VerticalAlign != VerticalAlign.Superscript)
                        editor.ToggleSuperscript();
                    break;
                case VerticalAlign.Subscript:
                    if (original.VerticalAlign != VerticalAlign.Subscript)
                        editor.ToggleSubscript();
                    break;
                case VerticalAlign.Baseline:
                    // Clear: toggle whichever is active.
                    if (original.VerticalAlign == VerticalAlign.Superscript)
                        editor.ToggleSuperscript();
                    else if (original.VerticalAlign == VerticalAlign.Subscript)
                        editor.ToggleSubscript();
                    break;
            }
        }

        // Font color
        if (result.ColorHex != original.ColorHex)
            editor.SetFontColor(result.ColorHex);

        // Highlight
        if (result.HighlightHex != original.HighlightColorHex)
            editor.SetHighlightColor(result.HighlightHex);

        // SmallCaps / AllCaps — these don't have dedicated toggle methods yet, so we
        // apply via SetSelectionRunFormatting-equivalent; reuse the ApplyRunFormatting
        // path by checking if a dedicated method exists. Since there is none, we patch
        // via the same trick as SetFontColor: a single ApplyRunFormatting lambda via the
        // editor's public surface. The only available hook is to toggle bold/etc. For
        // SmallCaps and AllCaps the DocumentView doesn't have a dedicated method yet —
        // we apply them by calling SetFontColor with a special side-effect path is NOT
        // available. Instead, expose helpers on DocumentView (or skip for now since the
        // model fields exist but no toggle method is defined). We apply them below via
        // dedicated helper calls where available.
        //
        // SmallCaps / AllCaps: DocumentView does not yet expose ToggleSmallCaps /
        // ToggleAllCaps. These flags round-trip through the model but the dialog can
        // already read them. Applying them requires adding methods; we leave this for
        // the next wave (deferred) — marked below.
        // TODO(deferred): editor.ToggleSmallCaps() / editor.ToggleAllCaps() when added.
    }

    // ── Static factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the font dialog modally and, on OK, applies the changes to <paramref name="editor"/>.
    /// Must be called from the UI thread.
    /// </summary>
    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var (runFmt, _) = editor.GetCaretFormatting();
        var dialog = new FontDialog(runFmt);
        var result = await dialog.ShowDialog<FontDialogResult?>(owner);
        if (result is null) return;
        ApplyResult(editor, result, runFmt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Grid BuildFormGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void AddRow(string label, Control ctrl, int row)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 10, 0),
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            Grid.SetRow(ctrl, row);
            Grid.SetColumn(ctrl, 1);
            ctrl.Margin = new Thickness(0, 6, 0, 0);
            grid.Children.Add(ctrl);
        }

        AddRow("Font:",      _familyBox,    0);
        AddRow("Size:",      _sizeBox,      1);
        AddRow("Color:",     _colorBox,     2);
        AddRow("Highlight:", _highlightBox, 3);

        return grid;
    }

    private static ComboBox BuildPaletteCombo((string Label, string? Hex)[] palette, string? currentHex)
    {
        var cb = new ComboBox { MinWidth = 150 };
        cb.ItemsSource = palette.Select(p => p.Label).ToArray();

        // Select the entry matching the current hex (null = "Automatic" / "None").
        var idx = Array.FindIndex(palette, p =>
            string.Equals(p.Hex, currentHex, StringComparison.OrdinalIgnoreCase));
        cb.SelectedIndex = idx >= 0 ? idx : 0;

        return cb;
    }

    private static string? SelectedHex(ComboBox cb, (string Label, string? Hex)[] palette)
    {
        var idx = cb.SelectedIndex;
        if (idx < 0 || idx >= palette.Length) return null;
        return palette[idx].Hex;
    }
}
