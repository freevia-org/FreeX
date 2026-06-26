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
/// Pre-populated from <see cref="DocumentView.GetSelectionFormatting"/> on open; on OK the changed
/// properties are applied in sequence via the <see cref="DocumentView"/> formatting methods,
/// wrapped in a single undo group (BZ4).
/// </para>
///
/// <para>
/// The apply path is exposed as <see cref="ApplyResult"/> so tests can call it without displaying
/// a window.
/// </para>
///
/// BZ1: the family + size combos are editable; <see cref="OnOk"/> reads <c>.Text</c> (not
/// <c>SelectedItem</c>) so a typed value that is not in the preset list is preserved. The dialog
/// constructor seeds <c>_sizeBox.Text</c> so a non-ladder size (e.g. 13pt) shows correctly.
///
/// BZ3: when the selection has mixed bold/italic/underline/strikethrough the checkboxes are shown
/// with <c>IsChecked = null</c> (three-state). Mixed family/size leaves the combo text blank. On
/// OK, a null checkbox or a blank combo box means "user did not change this field" — ApplyResult
/// skips those fields so mixed runs are not clobbered.
///
/// BZ4: <see cref="ApplyResult"/> wraps every individual editor call in a single undo group so
/// one Ctrl+Z reverts the whole dialog OK.
///
/// BZ5 (collapsed caret): handled in <see cref="DocumentView.ApplyRunFormatting"/> and
/// <see cref="DocumentView.ToggleRunFlag"/> — they store a pending format for the next typed
/// character instead of reformatting the whole paragraph. The dialog code is unchanged; the fix
/// is in the editor layer.
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

    // Maximum allowed font size in points (Word clamps at 1638; we use 409 = the dialog
    // input limit in Word's UI which rejects values above 1638 but the size box only shows 3 digits).
    private const double MinFontSizePt = 1;
    private const double MaxFontSizePt = 1638;

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
    // BZ3: indeterminate flags set from SelectionFormatting at dialog-open.
    private readonly bool _boldIndeterminate;
    private readonly bool _italicIndeterminate;
    private readonly bool _underlineIndeterminate;
    private readonly bool _strikeIndeterminate;
    private readonly bool _familyIndeterminate;
    private readonly bool _sizeIndeterminate;

    // ── Controls ─────────────────────────────────────────────────────────────
    private readonly ComboBox _familyBox;
    private readonly ComboBox _sizeBox;
    private readonly CheckBox _boldChk   = new() { Content = "Bold",          Margin = new Thickness(0, 4, 12, 0), IsThreeState = true };
    private readonly CheckBox _italicChk = new() { Content = "Italic",        Margin = new Thickness(0, 4, 12, 0), IsThreeState = true };
    private readonly CheckBox _underlineChk = new() { Content = "Underline",  Margin = new Thickness(0, 4, 12, 0), IsThreeState = true };
    private readonly CheckBox _strikeChk    = new() { Content = "Strikethrough", Margin = new Thickness(0, 4, 12, 0), IsThreeState = true };
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

    /// <summary>
    /// Creates the dialog pre-populated from <paramref name="current"/>. Use the overload that
    /// takes <see cref="DocumentView.SelectionFormatting"/> when opening from the editor so
    /// mixed-selection indeterminate state is preserved.
    /// </summary>
    public FontDialog(RunFormatting current)
        : this(new DocumentView.SelectionFormatting(current, ParagraphFormatting.Default))
    {
    }

    /// <summary>
    /// Creates the dialog pre-populated from <paramref name="sel"/>, respecting indeterminate
    /// flags from a mixed selection (BZ3).
    /// </summary>
    public FontDialog(DocumentView.SelectionFormatting sel)
    {
        var current = sel.Run;
        _original = current;
        _boldIndeterminate       = sel.BoldIndeterminate;
        _italicIndeterminate     = sel.ItalicIndeterminate;
        _underlineIndeterminate  = sel.UnderlineIndeterminate;
        _strikeIndeterminate     = sel.StrikethroughIndeterminate;
        _familyIndeterminate     = sel.FamilyIndeterminate;
        _sizeIndeterminate       = sel.SizeIndeterminate;

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
        if (_familyIndeterminate)
        {
            // BZ3: mixed family → show blank; SelectedItem/Text stays empty.
            _familyBox.SelectedIndex = -1;
        }
        else if (!string.IsNullOrEmpty(current.FontFamily))
        {
            // BZ1: seed Text so a non-preset family (e.g. "Cambria") is visible.
            _familyBox.SelectedItem = current.FontFamily;
            if (_familyBox.SelectedItem is null)
                _familyBox.Text = current.FontFamily; // not in preset list
        }
        else
        {
            _familyBox.SelectedIndex = 0; // Calibri
        }

        // ── Size combo ───────────────────────────────────────────────────────
        _sizeBox = new ComboBox
        {
            MinWidth = 100,
            IsEditable = true,
        };
        _sizeBox.ItemsSource = SizeLadder;
        if (_sizeIndeterminate)
        {
            // BZ3: mixed size → blank.
            _sizeBox.SelectedIndex = -1;
        }
        else if (current.FontSizePt.HasValue)
        {
            var currentSizeStr = current.FontSizePt.Value.ToString("G", CultureInfo.InvariantCulture);
            _sizeBox.SelectedItem = currentSizeStr;
            if (_sizeBox.SelectedItem is null)
            {
                // BZ1: size not in the standard ladder (e.g. 13pt) — seed Text directly so
                // the combo shows "13" instead of blank.
                _sizeBox.Text = currentSizeStr;
            }
        }
        else
        {
            _sizeBox.SelectedIndex = -1; // no font size set
        }

        // ── Style checkboxes ─────────────────────────────────────────────────
        // BZ3: null IsChecked = indeterminate (three-state).
        _boldChk.IsChecked        = _boldIndeterminate       ? null : current.Bold;
        _italicChk.IsChecked      = _italicIndeterminate     ? null : current.Italic;
        _underlineChk.IsChecked   = _underlineIndeterminate  ? null : current.Underline;
        _strikeChk.IsChecked      = _strikeIndeterminate     ? null : current.Strikethrough;
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

    /// <summary>
    /// Result record produced by the dialog on OK.
    ///
    /// BZ3: <c>Bold</c>, <c>Italic</c>, <c>Underline</c>, <c>Strikethrough</c> are <c>bool?</c>:
    /// <c>null</c> means the user left the field indeterminate (mixed selection, unchanged) — the
    /// apply step skips those fields so mixed runs are not clobbered.
    ///
    /// Similarly <c>Family</c> and <c>SizePt</c> being <c>null</c> can mean either "no value" or
    /// "indeterminate/unchanged" — the apply step uses <c>FamilyChanged</c> / <c>SizeChanged</c>
    /// to distinguish an explicit clear from an indeterminate skip.
    /// </summary>
    public sealed record FontDialogResult(
        string? Family,
        double? SizePt,
        bool? Bold, bool? Italic, bool? Underline, bool? Strikethrough,
        VerticalAlign VerticalAlign,
        bool SmallCaps, bool AllCaps,
        string? ColorHex,
        string? HighlightHex,
        // BZ3: true when the user explicitly changed the family/size field (false = indeterminate, skip).
        bool FamilyChanged = true,
        bool SizeChanged   = true);

    // ── OK handler ────────────────────────────────────────────────────────────

    private void OnOk()
    {
        _status.IsVisible = false;

        // BZ1: read .Text (not .SelectedItem) so a typed value that is not in the list is captured.
        var sizeText = (_sizeBox.Text ?? (_sizeBox.SelectedItem as string) ?? string.Empty).Trim();
        double? sizePt = null;
        var sizeChanged = true;
        if (_sizeIndeterminate && string.IsNullOrEmpty(sizeText))
        {
            // User left the size blank in an indeterminate combo → do not apply size.
            sizeChanged = false;
        }
        else if (!string.IsNullOrEmpty(sizeText))
        {
            if (!double.TryParse(sizeText, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                || parsed < MinFontSizePt || parsed > MaxFontSizePt)
            {
                _status.Text = $"Invalid font size: \"{sizeText}\". Enter a number between {MinFontSizePt} and {MaxFontSizePt}.";
                _status.IsVisible = true;
                return;
            }
            sizePt = Math.Clamp(parsed, MinFontSizePt, MaxFontSizePt);
        }

        // BZ1: read family from .Text; fall back to SelectedItem for backwards compat.
        var familyText = (_familyBox.Text ?? (_familyBox.SelectedItem as string) ?? string.Empty).Trim();
        var family = string.IsNullOrWhiteSpace(familyText) ? null : familyText;
        var familyChanged = true;
        if (_familyIndeterminate && string.IsNullOrWhiteSpace(familyText))
        {
            // User left family blank in an indeterminate combo → do not apply family.
            familyChanged = false;
        }

        // BZ3: a checkbox left at null (indeterminate by user) means "do not apply".
        bool? boldResult       = _boldChk.IsChecked;
        bool? italicResult     = _italicChk.IsChecked;
        bool? underlineResult  = _underlineChk.IsChecked;
        bool? strikeResult     = _strikeChk.IsChecked;

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
            Bold:         boldResult,
            Italic:       italicResult,
            Underline:    underlineResult,
            Strikethrough: strikeResult,
            VerticalAlign: va,
            SmallCaps:    _smallCapsChk.IsChecked == true,
            AllCaps:      _allCapsChk.IsChecked   == true,
            ColorHex:     colorHex,
            HighlightHex: highlightHex,
            FamilyChanged: familyChanged,
            SizeChanged:  sizeChanged);

        Close(result);
    }

    // ── Static apply ──────────────────────────────────────────────────────────

    /// <summary>
    /// Apply <paramref name="result"/> to <paramref name="editor"/>, changing only properties that
    /// differ from <paramref name="original"/>. Safe to call without showing the window (used by tests).
    ///
    /// BZ4: all individual editor calls are wrapped in a single undo group so OK = one undo step.
    /// BZ3: null Bool? fields and indeterminate family/size are skipped, preserving mixed runs.
    /// </summary>
    public static void ApplyResult(DocumentView editor, FontDialogResult result, RunFormatting original)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        // BZ4: wrap all changes in a single undo group.
        editor.BeginFontUndoGroup();
        try
        {
            // Font family — BZ3: skip when indeterminate (FamilyChanged = false).
            if (result.FamilyChanged && result.Family != original.FontFamily)
            {
                editor.SetSelectionFontFamily(result.Family ?? string.Empty);
            }

            // Font size — BZ3: skip when indeterminate (SizeChanged = false).
            if (result.SizeChanged && result.SizePt != original.FontSizePt)
            {
                if (result.SizePt.HasValue)
                    editor.SetSelectionFontSize(result.SizePt.Value);
            }

            // Bold — BZ3: skip when null (indeterminate).
            if (result.Bold.HasValue && result.Bold.Value != original.Bold)
                editor.ToggleBold();

            // Italic — BZ3: skip when null.
            if (result.Italic.HasValue && result.Italic.Value != original.Italic)
                editor.ToggleItalic();

            // Underline — BZ3: skip when null.
            if (result.Underline.HasValue && result.Underline.Value != original.Underline)
                editor.ToggleUnderline();

            // Strikethrough — BZ3: skip when null.
            if (result.Strikethrough.HasValue && result.Strikethrough.Value != original.Strikethrough)
                editor.ToggleStrikethrough();

            // Vertical alignment (super/subscript)
            if (result.VerticalAlign != original.VerticalAlign)
            {
                switch (result.VerticalAlign)
                {
                    case VerticalAlign.Superscript:
                        if (original.VerticalAlign != VerticalAlign.Superscript)
                            editor.ToggleSuperscript();
                        break;
                    case VerticalAlign.Subscript:
                        if (original.VerticalAlign != VerticalAlign.Subscript)
                            editor.ToggleSubscript();
                        break;
                    case VerticalAlign.Baseline:
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

            // SmallCaps / AllCaps: DocumentView does not yet expose ToggleSmallCaps /
            // ToggleAllCaps. These flags round-trip through the model but the dialog can
            // already read them. Applying them requires adding methods; we leave this for
            // the next wave (deferred) — marked below.
            // TODO(deferred): editor.ToggleSmallCaps() / editor.ToggleAllCaps() when added.
        }
        finally
        {
            editor.CommitFontUndoGroup("Font");
        }
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

        // BZ3: use GetSelectionFormatting to detect mixed bools and blank family/size.
        var sel = editor.GetSelectionFormatting();
        var dialog = new FontDialog(sel);
        var result = await dialog.ShowDialog<FontDialogResult?>(owner);
        if (result is null) return;
        ApplyResult(editor, result, sel.Run);
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
