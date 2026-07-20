using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
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
public sealed class FontDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

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

    private readonly TextBlock _status = new();

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
        var state = FontDialogPlanner.BuildBasicInitialState(
            current,
            CultureInfo.InvariantCulture,
            _familyIndeterminate,
            _sizeIndeterminate);

        // ── Family combo ──────────────────────────────────────────────────────
        _familyBox = new ComboBox
        {
            MinWidth = 260,
            IsEditable = true,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(_familyBox, DialogChromeStyle);
        _familyBox.ItemsSource = FontDialogPlanner.BasicFamilyChoices;
        if (_familyIndeterminate)
        {
            // BZ3: mixed family → show blank; SelectedItem/Text stays empty.
            _familyBox.SelectedIndex = -1;
        }
        else if (!string.IsNullOrEmpty(state.FontFamilyText))
        {
            // BZ1: seed Text so a non-preset family (e.g. "Cambria") is visible.
            _familyBox.SelectedItem = state.FontFamilyText;
            if (_familyBox.SelectedItem is null)
                _familyBox.Text = state.FontFamilyText; // not in preset list
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
        AvaloniaCompactDialogChrome.ApplyComboBox(_sizeBox, DialogChromeStyle);
        _sizeBox.ItemsSource = FontDialogPlanner.BasicSizeChoices.Select(size => size.Label).ToArray();
        if (_sizeIndeterminate)
        {
            // BZ3: mixed size → blank.
            _sizeBox.SelectedIndex = -1;
        }
        else if (!string.IsNullOrEmpty(state.FontSizeText))
        {
            _sizeBox.SelectedItem = state.FontSizeText;
            if (_sizeBox.SelectedItem is null)
            {
                // BZ1: size not in the standard ladder (e.g. 13pt) — seed Text directly so
                // the combo shows "13" instead of blank.
                _sizeBox.Text = state.FontSizeText;
            }
        }
        else
        {
            _sizeBox.SelectedIndex = -1; // no font size set
        }

        // ── Style checkboxes ─────────────────────────────────────────────────
        // BZ3: null IsChecked = indeterminate (three-state).
        _boldChk.IsChecked        = _boldIndeterminate       ? null : state.Bold;
        _italicChk.IsChecked      = _italicIndeterminate     ? null : state.Italic;
        _underlineChk.IsChecked   = _underlineIndeterminate  ? null : state.Underline;
        _strikeChk.IsChecked      = _strikeIndeterminate     ? null : state.Strikethrough;
        _superChk.IsChecked       = state.Superscript;
        _subChk.IsChecked         = state.Subscript;
        _smallCapsChk.IsChecked   = state.SmallCaps;
        _allCapsChk.IsChecked     = state.AllCaps;
        ApplyCheckBoxChrome();
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(0, 6, 0, 0));

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
        _colorBox     = BuildPaletteCombo(FontDialogPlanner.BasicColorChoices, state.ColorIndex);
        _highlightBox = BuildPaletteCombo(FontDialogPlanner.HighlightColorChoices, state.HighlightColorIndex);

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
        var ok     = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 84, isDefault: true);
        var cancel = new Button { Content = "Cancel", IsCancel = true  };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 84);
        ok.Click     += (_, _) => OnOk();
        cancel.Click += (_, _) => Close(null);

        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 14, 0, 0));

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

        var input = new FontDialogBasicInput(
            FontFamilyText: _familyBox.Text ?? (_familyBox.SelectedItem as string) ?? string.Empty,
            FontSizeText: _sizeBox.Text ?? (_sizeBox.SelectedItem as string) ?? string.Empty,
            FamilyIndeterminate: _familyIndeterminate,
            SizeIndeterminate: _sizeIndeterminate,
            ColorIndex: _colorBox.SelectedIndex,
            HighlightColorIndex: _highlightBox.SelectedIndex,
            Bold: _boldChk.IsChecked,
            Italic: _italicChk.IsChecked,
            Underline: _underlineChk.IsChecked,
            Strikethrough: _strikeChk.IsChecked,
            SmallCaps: _smallCapsChk.IsChecked == true,
            AllCaps: _allCapsChk.IsChecked == true,
            Superscript: _superChk.IsChecked == true,
            Subscript: _subChk.IsChecked == true);

        if (!FontDialogPlanner.TryBuildBasicResult(
                input,
                CultureInfo.InvariantCulture,
                out var plannedResult,
                out var errorMessage))
        {
            _status.Text = errorMessage ?? FontDialogPlanner.BuildBasicFontSizeValidationMessage(
                input.FontSizeText,
                CultureInfo.InvariantCulture);
            _status.IsVisible = true;
            return;
        }

        Close(ToDialogResult(plannedResult!));
    }

    private static FontDialogResult ToDialogResult(FontDialogBasicResult result) => new(
        Family: result.Family,
        SizePt: result.SizePt,
        Bold: result.Bold,
        Italic: result.Italic,
        Underline: result.Underline,
        Strikethrough: result.Strikethrough,
        VerticalAlign: result.VerticalAlign,
        SmallCaps: result.SmallCaps,
        AllCaps: result.AllCaps,
        ColorHex: result.ColorHex,
        HighlightHex: result.HighlightHex,
        FamilyChanged: result.FamilyChanged,
        SizeChanged: result.SizeChanged);

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

            if (result.SmallCaps != original.SmallCaps)
                editor.ToggleSmallCaps();
            if (result.AllCaps != original.AllCaps)
                editor.ToggleAllCaps();
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

    private static ComboBox BuildPaletteCombo(IReadOnlyList<FontDialogColorChoice> palette, int selectedIndex)
    {
        var cb = new ComboBox { MinWidth = 150 };
        AvaloniaCompactDialogChrome.ApplyComboBox(cb, DialogChromeStyle);
        cb.ItemsSource = palette.Select(p => p.Label).ToArray();
        cb.SelectedIndex = selectedIndex < 0 || selectedIndex >= palette.Count ? 0 : selectedIndex;

        return cb;
    }

    private void ApplyCheckBoxChrome()
    {
        foreach (var checkBox in new[]
        {
            _boldChk,
            _italicChk,
            _underlineChk,
            _strikeChk,
            _superChk,
            _subChk,
            _smallCapsChk,
            _allCapsChk,
        })
        {
            AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, DialogChromeStyle);
        }
    }
}
