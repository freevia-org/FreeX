using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using TextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW Avalonia Paragraph dialog: a modal <see cref="Window"/> that lets the user inspect and
/// change paragraph-level formatting of the paragraph containing the caret.
///
/// <para>
/// Pre-populated from <see cref="DocumentView.GetCaretFormatting"/> on open; on OK the changed
/// properties are applied via <see cref="DocumentView"/> paragraph methods. Cancel makes no changes.
/// </para>
///
/// <para>
/// The apply path is exposed as <see cref="ApplyResult"/> so tests can call it without displaying
/// a window.
/// </para>
///
/// Fields covered:
/// <list type="bullet">
///   <item>Alignment (Left / Center / Right / Justify)</item>
///   <item>Indent Left, Indent Right, First Line (hanging = negative first-line)</item>
///   <item>Space Before, Space After (points)</item>
///   <item>Line spacing: Single / 1.5 Lines / Double / Multiple / At Least / Exactly</item>
///   <item>Line spacing value (for At Least / Exactly / Multiple when not a preset)</item>
/// </list>
///
/// Deferred: Page-break-before, keep-with-next, keep-lines-together, widow-control,
/// paragraph borders — these are low-frequency options for a subsequent dialog wave.
/// </summary>
public sealed class ParagraphDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    // ── Alignment items ───────────────────────────────────────────────────────
    private static readonly (string Label, TextAlignment Value)[] AlignmentItems =
    [
        ("Left",    TextAlignment.Left),
        ("Centered",TextAlignment.Center),
        ("Right",   TextAlignment.Right),
        ("Justified",TextAlignment.Justify),
    ];

    // ── Line-spacing presets ──────────────────────────────────────────────────
    private enum LineSpacingPreset { Single, OneAndHalf, Double, Multiple, AtLeast, Exactly }

    private static readonly (string Label, LineSpacingPreset Preset)[] SpacingItems =
    [
        ("Single (1×)",           LineSpacingPreset.Single),
        ("1.5 Lines",             LineSpacingPreset.OneAndHalf),
        ("Double (2×)",           LineSpacingPreset.Double),
        ("Multiple",              LineSpacingPreset.Multiple),
        ("At Least (pt)",         LineSpacingPreset.AtLeast),
        ("Exactly (pt)",          LineSpacingPreset.Exactly),
    ];

    // ── Snapshot ──────────────────────────────────────────────────────────────
    private readonly ParagraphFormatting _original;

    // ── Controls ──────────────────────────────────────────────────────────────
    private readonly ComboBox  _alignBox;
    private readonly TextBox   _leftBox   = MakeNumericBox();
    private readonly TextBox   _rightBox  = MakeNumericBox();
    private readonly TextBox   _firstBox  = MakeNumericBox();   // positive = first-line, negative = hanging
    private readonly TextBox   _beforeBox = MakeNumericBox();
    private readonly TextBox   _afterBox  = MakeNumericBox();
    private readonly ComboBox  _lineSpacingBox;
    private readonly TextBox   _lineValueBox = MakeNumericBox(); // pt value for AtLeast / Exactly / Multiple
    private readonly TextBlock _lineValueLabel = new()
    {
        Text = "At:",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 6, 4, 0),
    };

    private readonly TextBlock _status = new();

    // ── Construction ──────────────────────────────────────────────────────────

    public ParagraphDialog(ParagraphFormatting current)
    {
        _original = current;

        Title = "Paragraph";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        // ── Alignment combo ───────────────────────────────────────────────────
        _alignBox = new ComboBox { MinWidth = 180 };
        AvaloniaCompactDialogChrome.ApplyComboBox(_alignBox, DialogChromeStyle);
        _alignBox.ItemsSource = AlignmentItems.Select(a => a.Label).ToArray();
        var alignIdx = Array.FindIndex(AlignmentItems, a => a.Value == current.Alignment);
        _alignBox.SelectedIndex = alignIdx >= 0 ? alignIdx : 0;

        // ── Indent boxes ──────────────────────────────────────────────────────
        _leftBox.Text  = Pt(current.IndentLeftPt);
        _rightBox.Text = Pt(current.IndentRightPt);
        // FirstLineIndentPt: positive = indent, negative = hanging.
        _firstBox.Text = Pt(current.FirstLineIndentPt);

        // ── Spacing boxes ─────────────────────────────────────────────────────
        _beforeBox.Text = Pt(current.SpaceBeforePt);
        _afterBox.Text  = Pt(current.SpaceAfterPt);

        // ── Line-spacing combo ────────────────────────────────────────────────
        _lineSpacingBox = new ComboBox { MinWidth = 180 };
        AvaloniaCompactDialogChrome.ApplyComboBox(_lineSpacingBox, DialogChromeStyle);
        _lineSpacingBox.ItemsSource = SpacingItems.Select(s => s.Label).ToArray();

        var (initPreset, initValue) = ToPreset(current);
        var presetIdx = Array.FindIndex(SpacingItems, s => s.Preset == initPreset);
        _lineSpacingBox.SelectedIndex = presetIdx >= 0 ? presetIdx : 0;
        _lineValueBox.Text = Pt(initValue);

        // Show/hide the "At:" value box based on selected preset.
        _lineSpacingBox.SelectionChanged += (_, _) => UpdateLineValueVisibility();
        UpdateLineValueVisibility();
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(0, 6, 0, 0));

        // ── Layout ────────────────────────────────────────────────────────────
        var content = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };

        // --- Alignment ---
        content.Children.Add(SectionLabel("Alignment"));
        content.Children.Add(Labeled("Alignment:", _alignBox));

        // --- Indentation ---
        content.Children.Add(SectionLabel("Indentation"));
        var indentGrid = new Grid();
        indentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        indentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
        indentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        indentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
        indentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        indentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddLabeledCell(indentGrid, "Left (pt):",       _leftBox,  0, 0);
        AddLabeledCell(indentGrid, "Right (pt):",      _rightBox, 0, 2);
        AddLabeledCell(indentGrid, "First Line (pt):", _firstBox, 0, 4);
        content.Children.Add(indentGrid);
        content.Children.Add(IndentHint());

        // --- Spacing ---
        content.Children.Add(SectionLabel("Spacing"));
        var spacingGrid = new Grid();
        spacingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        spacingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
        spacingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        spacingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddLabeledCell(spacingGrid, "Before (pt):", _beforeBox, 0, 0);
        AddLabeledCell(spacingGrid, "After (pt):",  _afterBox,  0, 2);
        content.Children.Add(spacingGrid);

        // --- Line spacing ---
        content.Children.Add(SectionLabel("Line Spacing"));
        var lsRow = new StackPanel { Orientation = Orientation.Horizontal };
        lsRow.Children.Add(_lineSpacingBox);
        lsRow.Children.Add(_lineValueLabel);
        lsRow.Children.Add(_lineValueBox);
        content.Children.Add(lsRow);

        // --- Status + buttons ---
        content.Children.Add(_status);
        var ok     = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 84, isDefault: true);
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 84);
        ok.Click     += (_, _) => OnOk();
        cancel.Click += (_, _) => Close(null);
        content.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 14, 0, 0)));

        Content = content;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
        };
    }

    // ── Result record ─────────────────────────────────────────────────────────

    /// <summary>Result produced by the dialog on OK.</summary>
    public sealed record ParagraphDialogResult(
        TextAlignment Alignment,
        double IndentLeftPt,
        double IndentRightPt,
        double FirstLineIndentPt,
        double SpaceBeforePt,
        double SpaceAfterPt,
        LineSpacingRule LineRule,
        double LineSpacingValue);   // Multiple multiplier or pt value for Exact/AtLeast.

    // ── OK handler ────────────────────────────────────────────────────────────

    private void OnOk()
    {
        _status.IsVisible = false;

        // Parse all numeric fields.
        if (!TryParseNonNeg(_leftBox.Text,  "Left indent",   out var left))  return;
        if (!TryParseNonNeg(_rightBox.Text, "Right indent",  out var right)) return;
        if (!TryParseAny   (_firstBox.Text, "First line",    out var first)) return;
        if (!TryParseNonNeg(_beforeBox.Text,"Space before",  out var before))return;
        if (!TryParseNonNeg(_afterBox.Text, "Space after",   out var after)) return;

        // Line spacing.
        var presetIdx = _lineSpacingBox.SelectedIndex;
        var preset = presetIdx >= 0 ? SpacingItems[presetIdx].Preset : LineSpacingPreset.Single;

        LineSpacingRule lsRule;
        double lsValue;

        switch (preset)
        {
            case LineSpacingPreset.Single:
                lsRule = LineSpacingRule.Multiple; lsValue = 1.0; break;
            case LineSpacingPreset.OneAndHalf:
                lsRule = LineSpacingRule.Multiple; lsValue = 1.5; break;
            case LineSpacingPreset.Double:
                lsRule = LineSpacingRule.Multiple; lsValue = 2.0; break;
            case LineSpacingPreset.Multiple:
                if (!TryParsePos(_lineValueBox.Text, "Line spacing multiplier", out lsValue)) return;
                lsRule = LineSpacingRule.Multiple;
                break;
            case LineSpacingPreset.AtLeast:
                if (!TryParsePos(_lineValueBox.Text, "At least (pt)", out lsValue)) return;
                lsRule = LineSpacingRule.AtLeast;
                break;
            case LineSpacingPreset.Exactly:
                if (!TryParsePos(_lineValueBox.Text, "Exactly (pt)", out lsValue)) return;
                lsRule = LineSpacingRule.Exact;
                break;
            default:
                lsRule = LineSpacingRule.Multiple; lsValue = 1.0; break;
        }

        // Alignment.
        var alignIdx = _alignBox.SelectedIndex;
        var align = alignIdx >= 0 ? AlignmentItems[alignIdx].Value : TextAlignment.Left;

        Close(new ParagraphDialogResult(
            Alignment:         align,
            IndentLeftPt:      left,
            IndentRightPt:     right,
            FirstLineIndentPt: first,
            SpaceBeforePt:     before,
            SpaceAfterPt:      after,
            LineRule:          lsRule,
            LineSpacingValue:  lsValue));
    }

    // ── Static apply ─────────────────────────────────────────────────────────

    /// <summary>
    /// Apply <paramref name="result"/> to <paramref name="editor"/> across ALL paragraphs spanned
    /// by the current selection (or just the caret paragraph when there is no selection). All
    /// changes are issued as a single undoable action. Safe to call without showing the window
    /// (used by tests).
    /// </summary>
    public static void ApplyResult(DocumentView editor, ParagraphDialogResult result, ParagraphFormatting original)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        // Delegate to the multi-paragraph apply on the editor.  It enumerates every paragraph
        // block spanned by the selection and wraps the mutations in a single CompositeDocumentCommand
        // so that a single Undo reverts all paragraphs at once — mirroring WPF's
        // ApplyParagraphDialogFormatting / FormatSelectedModelParagraphs.
        editor.ApplyParagraphDialogFormatting(
            result.Alignment,
            result.IndentLeftPt, result.IndentRightPt, result.FirstLineIndentPt,
            result.SpaceBeforePt, result.SpaceAfterPt,
            result.LineRule, result.LineSpacingValue);
    }

    // ── Static factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the paragraph dialog modally and, on OK, applies the changes to <paramref name="editor"/>.
    /// Must be called from the UI thread.
    /// </summary>
    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var (_, paraFmt) = editor.GetCaretFormatting();
        var dialog = new ParagraphDialog(paraFmt);
        var result = await dialog.ShowDialog<ParagraphDialogResult?>(owner);
        if (result is null) return;
        ApplyResult(editor, result, paraFmt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateLineValueVisibility()
    {
        var presetIdx = _lineSpacingBox.SelectedIndex;
        var preset = presetIdx >= 0 ? SpacingItems[presetIdx].Preset : LineSpacingPreset.Single;
        var needsValue = preset is LineSpacingPreset.Multiple or LineSpacingPreset.AtLeast or LineSpacingPreset.Exactly;
        _lineValueLabel.IsVisible = needsValue;
        _lineValueBox.IsVisible   = needsValue;
    }

    private static (LineSpacingPreset Preset, double Value) ToPreset(ParagraphFormatting f)
    {
        if (f.LineRule == LineSpacingRule.AtLeast)
            return (LineSpacingPreset.AtLeast, f.LineHeightPt > 0 ? f.LineHeightPt : 12);
        if (f.LineRule == LineSpacingRule.Exact)
            return (LineSpacingPreset.Exactly, f.LineHeightPt > 0 ? f.LineHeightPt : 12);

        // Multiple: check against standard presets.
        var mult = f.LineSpacing;
        if (Math.Abs(mult - 1.0) <= 0.001) return (LineSpacingPreset.Single,     1.0);
        if (Math.Abs(mult - 1.5) <= 0.001) return (LineSpacingPreset.OneAndHalf, 1.5);
        if (Math.Abs(mult - 2.0) <= 0.001) return (LineSpacingPreset.Double,     2.0);
        return (LineSpacingPreset.Multiple, mult);
    }

    private bool TryParseNonNeg(string? text, string field, out double value)
    {
        value = 0;
        var t = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t)) return true; // treat blank as 0
        if (!double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value) || value < 0)
        {
            ShowError($"Invalid value for {field}: \"{t}\". Enter a non-negative number.");
            return false;
        }
        return true;
    }

    private bool TryParseAny(string? text, string field, out double value)
    {
        value = 0;
        var t = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t)) return true;
        if (!double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            ShowError($"Invalid value for {field}: \"{t}\". Enter a number (negative for hanging).");
            return false;
        }
        return true;
    }

    private bool TryParsePos(string? text, string field, out double value)
    {
        value = 1.0;
        var t = (text ?? string.Empty).Trim();
        if (!double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            ShowError($"Invalid value for {field}: \"{t}\". Enter a positive number.");
            return false;
        }
        return true;
    }

    private void ShowError(string msg)
    {
        _status.Text = msg;
        _status.IsVisible = true;
    }

    private static string Pt(double v) => v == 0 ? "0" : v.ToString("G", CultureInfo.InvariantCulture);

    private static TextBox MakeNumericBox()
    {
        var box = new TextBox { Width = 80, Margin = new Thickness(0, 6, 0, 0) };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 10, 0, 2),
    };

    private static Control Labeled(string label, Control ctrl)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 0) },
                ctrl,
            },
        };
    }

    private static TextBlock IndentHint() => new()
    {
        Text = "Hint: First Line positive = indent, negative = hanging.",
        FontSize = 10,
        Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
        Margin = new Thickness(0, 2, 0, 0),
    };

    private static void AddLabeledCell(Grid grid, string label, Control ctrl, int row, int col)
    {
        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 2),
        };
        var cell = new StackPanel { Children = { lbl, ctrl } };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, col);
        grid.Children.Add(cell);
    }
}
