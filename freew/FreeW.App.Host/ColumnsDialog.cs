using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Columns" dialog (Layout &gt; Page Setup &gt; Columns &gt; More Columns…). Lets the user pick a
/// preset — One / Two / Three / Left / Right — or a custom number of columns, with a column spacing and a
/// "line between" toggle. Returns the chosen column settings to apply to <see cref="PageSettings"/>, or
/// null if cancelled.
///
/// <para>
/// "Left" and "Right" are the classic two-column unequal presets: a narrow column beside a wide one (Word
/// uses roughly a 1.5"/4.5" split of the content area). They are returned as explicit per-column widths
/// (see <see cref="PageSettings.ColumnWidthsPt"/>); the equal presets leave widths null so the layout is
/// derived from the count + spacing.
/// </para>
/// </summary>
internal sealed class ColumnsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>The settings the dialog produces, mapped onto a <see cref="PageSettings"/> on apply.</summary>
    internal sealed record Result(int Count, double SpacingPt, bool LineBetween, IReadOnlyList<double>? WidthsPt);

    private readonly ComboBox _presetBox;
    private readonly TextBox _countBox;
    private readonly TextBox _spacingBox;
    private readonly CheckBox _lineBetween;
    private readonly double _contentWidthPt;
    private Result? _result;

    // Preset order shown in the drop-down. Indexes map to ApplyPreset below.
    private static readonly string[] Presets = ["One", "Two", "Three", "Left", "Right"];

    private ColumnsDialog(Window? owner, PageSettings page)
    {
        Owner = owner;
        Title = "Columns";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _contentWidthPt = System.Math.Max(72, page.WidthPt - page.MarginLeftPt - page.MarginRightPt);

        _countBox = NumberBox(System.Math.Max(1, page.ColumnCount));
        _spacingBox = NumberBox(page.ColumnSpacingPt);
        _lineBetween = new CheckBox { Content = "Line between", IsChecked = page.ColumnsLineBetween, Margin = new Thickness(0, 6, 0, 0) };

        _presetBox = new ComboBox { MinWidth = 140 };
        foreach (var preset in Presets)
            _presetBox.Items.Add(preset);
        _presetBox.SelectedIndex = PresetIndexFor(page);
        _presetBox.SelectionChanged += (_, _) => ApplyPreset(_presetBox.SelectedIndex);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Presets:", _presetBox);
        AddRow(grid, 1, "Number of columns:", _countBox);
        AddRow(grid, 2, "Spacing (pt):", _spacingBox);

        Grid.SetRow(_lineBetween, 3);
        Grid.SetColumn(_lineBetween, 1);
        grid.Children.Add(_lineBetween);

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 4);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_countBox);
    }

    private static TextBox NumberBox(double value) => new()
    {
        Text = value.ToString("0.##", CultureInfo.CurrentCulture),
        MinWidth = 120
    };

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    // Maps an existing page back to a preset selection so reopening the dialog shows the current state.
    private static int PresetIndexFor(PageSettings page)
    {
        if (page.ColumnWidthsPt is { Count: 2 } widths)
            return widths[0] < widths[1] ? 3 : 4; // narrow-first → Left, wide-first → Right
        return System.Math.Clamp(page.ColumnCount - 1, 0, 2); // 1/2/3 → One/Two/Three
    }

    // Fills the count box (and clears any unequal-width intent) from a preset choice.
    private void ApplyPreset(int index)
    {
        var count = index switch { 0 => 1, 1 or 3 or 4 => 2, _ => 3 };
        _countBox.Text = count.ToString(CultureInfo.CurrentCulture);
    }

    // Word's Left/Right split: a ~1.5" narrow column next to the rest of the content area.
    private IReadOnlyList<double>? UnequalWidths()
    {
        var spacing = ParseOr(_spacingBox.Text, 36);
        const double narrowPt = 108; // 1.5 inch
        var widePt = System.Math.Max(36, _contentWidthPt - spacing - narrowPt);
        return _presetBox.SelectedIndex switch
        {
            3 => [narrowPt, widePt], // Left: narrow column on the left
            4 => [widePt, narrowPt], // Right: narrow column on the right
            _ => null
        };
    }

    private void Accept()
    {
        if (!TryParseInt(_countBox.Text, out var count) || count < 1 || count > 12
            || !TryParseDouble(_spacingBox.Text, out var spacing) || spacing < 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter 1–12 columns and a non-negative spacing in points.");
            return;
        }

        var widths = UnequalWidths();
        // An unequal preset forces a 2-column count regardless of what's in the count box.
        if (widths is not null)
            count = widths.Count;

        _result = new Result(count, spacing, _lineBetween.IsChecked == true, widths);
        Close();
    }

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static double ParseOr(string text, double fallback) =>
        TryParseDouble(text, out var v) ? v : fallback;

    /// <summary>
    /// Show the dialog seeded with the current page columns; returns the chosen settings, or null if
    /// cancelled.
    /// </summary>
    public static Result? Prompt(Window? owner, PageSettings page)
    {
        var dialog = new ColumnsDialog(owner, page);
        dialog.ShowDialog();
        return dialog._result;
    }
}
