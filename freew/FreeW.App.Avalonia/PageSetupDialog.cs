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
/// FreeW Avalonia Page Setup dialog: a modal <see cref="Window"/> that lets the user inspect and
/// change the document's page geometry (size, orientation, margins).
///
/// <para>
/// Pre-populated from <see cref="DocumentView.Document"/>'s <see cref="TextDocument.Page"/> on
/// open; on OK the changes are applied via <see cref="DocumentView.SetPageSettings"/> (one
/// undoable step). Cancel makes no change.
/// </para>
///
/// <para>
/// The apply path is exposed as <see cref="ApplyResult"/> so tests can call it without displaying
/// a window.
/// </para>
///
/// Sections covered:
/// <list type="bullet">
///   <item>Margins — top, bottom, left, right (points)</item>
///   <item>Orientation — Portrait / Landscape radio buttons</item>
///   <item>Paper size — standard preset dropdown (Letter, Legal, A4, A3, Custom) plus explicit
///     width/height boxes when Custom is selected</item>
/// </list>
///
/// Deferred: Gutter, header/footer distance, mirror margins, columns, paper source, page borders,
/// line numbers, apply-to scope (section vs. whole document).
/// </summary>
public sealed class PageSetupDialog : Window
{
    // ── Paper size presets ────────────────────────────────────────────────────
    // Width × Height in points. Custom = 0 × 0 (sentinel).
    private static readonly (string Label, double WidthPt, double HeightPt)[] PaperSizes =
    [
        ("Letter (8.5 × 11 in)",  612,   792),
        ("Legal (8.5 × 14 in)",   612,  1008),
        ("A4 (210 × 297 mm)",     595.3, 841.9),
        ("A3 (297 × 420 mm)",     841.9, 1190.6),
        ("A5 (148 × 210 mm)",     419.5, 595.3),
        ("Executive (7.25 × 10.5 in)", 522, 756),
        ("Custom",                0,    0),
    ];

    private const int CustomIndex = 6; // index of "Custom" in PaperSizes

    // ── Controls ──────────────────────────────────────────────────────────────
    private readonly TextBox _topBox    = MakeNumericBox();
    private readonly TextBox _bottomBox = MakeNumericBox();
    private readonly TextBox _leftBox   = MakeNumericBox();
    private readonly TextBox _rightBox  = MakeNumericBox();
    private readonly RadioButton _portraitRadio  = new() { Content = "Portrait",  IsChecked = true,  Margin = new Thickness(0, 4, 12, 0), GroupName = "Orientation" };
    private readonly RadioButton _landscapeRadio = new() { Content = "Landscape", IsChecked = false, Margin = new Thickness(0, 4, 12, 0), GroupName = "Orientation" };
    private readonly ComboBox _paperSizeBox;
    private readonly TextBox _paperWidthBox  = MakeNumericBox();
    private readonly TextBox _paperHeightBox = MakeNumericBox();
    private readonly StackPanel _customSizePanel;
    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x00, 0x00)),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        IsVisible = false,
    };

    // ── Construction ──────────────────────────────────────────────────────────

    public PageSetupDialog(PageSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        Title = "Page Setup";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        // ── Margins ──────────────────────────────────────────────────────────
        _topBox.Text    = Pt(current.MarginTopPt);
        _bottomBox.Text = Pt(current.MarginBottomPt);
        _leftBox.Text   = Pt(current.MarginLeftPt);
        _rightBox.Text  = Pt(current.MarginRightPt);

        // ── Orientation ───────────────────────────────────────────────────────
        // Landscape = width > height in the stored model.
        var isLandscape = current.Landscape || current.WidthPt > current.HeightPt;
        _portraitRadio.IsChecked  = !isLandscape;
        _landscapeRadio.IsChecked = isLandscape;

        // ── Paper size combo ──────────────────────────────────────────────────
        _paperSizeBox = new ComboBox { MinWidth = 200 };
        _paperSizeBox.ItemsSource = PaperSizes.Select(p => p.Label).ToArray();

        // Match the current page size to a preset (within 1pt tolerance).
        // Compare against the portrait dimensions (shorter side × longer side) so landscape
        // documents still match the correct preset.
        var shortPt = Math.Min(current.WidthPt, current.HeightPt);
        var longPt  = Math.Max(current.WidthPt, current.HeightPt);
        var presetIdx = Array.FindIndex(PaperSizes, p =>
            p.WidthPt > 0 && Math.Abs(Math.Min(p.WidthPt, p.HeightPt) - shortPt) < 1.5
                          && Math.Abs(Math.Max(p.WidthPt, p.HeightPt) - longPt)  < 1.5);
        if (presetIdx < 0) presetIdx = CustomIndex;
        _paperSizeBox.SelectedIndex = presetIdx;

        // Custom size boxes: show the actual stored width × height.
        _paperWidthBox.Text  = Pt(current.WidthPt);
        _paperHeightBox.Text = Pt(current.HeightPt);

        _customSizePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _customSizePanel.Children.Add(new TextBlock { Text = "Width (pt):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _customSizePanel.Children.Add(_paperWidthBox);
        _customSizePanel.Children.Add(new TextBlock { Text = "  Height (pt):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        _customSizePanel.Children.Add(_paperHeightBox);
        _customSizePanel.IsVisible = presetIdx == CustomIndex;

        _paperSizeBox.SelectionChanged += (_, _) =>
        {
            _customSizePanel.IsVisible = _paperSizeBox.SelectedIndex == CustomIndex;
        };

        // ── Layout ────────────────────────────────────────────────────────────
        var content = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };

        // Margins section
        content.Children.Add(SectionLabel("Margins (points)"));
        var marginGrid = new Grid();
        marginGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        marginGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        marginGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        marginGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        marginGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddLabeledCell(marginGrid, "Top:",    _topBox,    0, 0);
        AddLabeledCell(marginGrid, "Bottom:", _bottomBox, 0, 2);
        AddLabeledCell(marginGrid, "Left:",   _leftBox,   1, 0);
        AddLabeledCell(marginGrid, "Right:",  _rightBox,  1, 2);
        content.Children.Add(marginGrid);

        // Orientation section
        content.Children.Add(SectionLabel("Orientation"));
        var orientRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        orientRow.Children.Add(_portraitRadio);
        orientRow.Children.Add(_landscapeRadio);
        content.Children.Add(orientRow);

        // Paper section
        content.Children.Add(SectionLabel("Paper Size"));
        content.Children.Add(_paperSizeBox);
        content.Children.Add(_customSizePanel);

        // Status + buttons
        content.Children.Add(_status);
        var ok     = new Button { Content = "OK",     MinWidth = 84, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel  = true };
        ok.Click     += (_, _) => OnOk();
        cancel.Click += (_, _) => Close(null);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { ok, cancel },
        });

        Content = content;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(null); e.Handled = true; }
        };
    }

    // ── Result record ─────────────────────────────────────────────────────────

    /// <summary>Result produced by the dialog on OK.</summary>
    public sealed record PageSetupDialogResult(
        double MarginTopPt,
        double MarginBottomPt,
        double MarginLeftPt,
        double MarginRightPt,
        bool Landscape,
        double WidthPt,
        double HeightPt);

    // ── OK handler ────────────────────────────────────────────────────────────

    private void OnOk()
    {
        _status.IsVisible = false;

        // Parse margins.
        if (!TryParseNonNeg(_topBox.Text,    "Top margin",    out var top))    return;
        if (!TryParseNonNeg(_bottomBox.Text, "Bottom margin", out var bottom)) return;
        if (!TryParseNonNeg(_leftBox.Text,   "Left margin",   out var left))   return;
        if (!TryParseNonNeg(_rightBox.Text,  "Right margin",  out var right))  return;

        // Orientation.
        var landscape = _landscapeRadio.IsChecked == true;

        // Paper size.
        double widthPt, heightPt;
        var sizeIdx = _paperSizeBox.SelectedIndex;
        if (sizeIdx == CustomIndex || sizeIdx < 0)
        {
            if (!TryParsePos(_paperWidthBox.Text,  "Paper width",  out widthPt))  return;
            if (!TryParsePos(_paperHeightBox.Text, "Paper height", out heightPt)) return;
        }
        else
        {
            var preset = PaperSizes[sizeIdx];
            // Store in portrait orientation (shorter × longer); the Landscape flag toggles display.
            widthPt  = Math.Min(preset.WidthPt, preset.HeightPt);
            heightPt = Math.Max(preset.WidthPt, preset.HeightPt);
        }

        // When landscape is selected, swap so width > height in the model (matches Word).
        if (landscape && widthPt < heightPt)
            (widthPt, heightPt) = (heightPt, widthPt);
        else if (!landscape && widthPt > heightPt)
            (widthPt, heightPt) = (heightPt, widthPt);

        Close(new PageSetupDialogResult(
            MarginTopPt:    top,
            MarginBottomPt: bottom,
            MarginLeftPt:   left,
            MarginRightPt:  right,
            Landscape:      landscape,
            WidthPt:        widthPt,
            HeightPt:       heightPt));
    }

    // ── Static apply ─────────────────────────────────────────────────────────

    /// <summary>
    /// Build a <see cref="PageSettings"/> from <paramref name="result"/>, copying it over the
    /// current <see cref="TextDocument.Page"/> via <see cref="DocumentView.SetPageSettings"/>.
    /// Safe to call without showing the dialog window (used by tests and quick commands).
    /// </summary>
    public static void ApplyResult(DocumentView editor, PageSetupDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(result);

        // Clone the current page settings so we don't mutate the live model before the command runs.
        var settings = editor.Document.Page.Clone();
        settings.MarginTopPt    = result.MarginTopPt;
        settings.MarginBottomPt = result.MarginBottomPt;
        settings.MarginLeftPt   = result.MarginLeftPt;
        settings.MarginRightPt  = result.MarginRightPt;
        settings.Landscape      = result.Landscape;
        settings.WidthPt        = result.WidthPt;
        settings.HeightPt       = result.HeightPt;

        editor.SetPageSettings(settings);
    }

    // ── Static factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the Page Setup dialog modally and, on OK, applies the changes to the document.
    /// Must be called from the UI thread.
    /// </summary>
    public static async Task ShowAndApplyAsync(Window owner, DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);

        var current = editor.Document.Page;
        var dialog  = new PageSetupDialog(current);
        var result  = await dialog.ShowDialog<PageSetupDialogResult?>(owner);
        if (result is null) return;
        ApplyResult(editor, result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool TryParseNonNeg(string? text, string field, out double value)
    {
        value = 0;
        var t = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t)) return true; // blank = 0
        if (!double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out value) || value < 0)
        {
            ShowError($"Invalid value for {field}: \"{t}\". Enter a non-negative number.");
            return false;
        }
        return true;
    }

    private bool TryParsePos(string? text, string field, out double value)
    {
        value = 1;
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

    private static string Pt(double v) =>
        v == 0 ? "0" : v.ToString("G5", CultureInfo.InvariantCulture);

    private static TextBox MakeNumericBox() =>
        new() { Width = 80, Margin = new Thickness(0, 4, 0, 0) };

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 10, 0, 2),
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
