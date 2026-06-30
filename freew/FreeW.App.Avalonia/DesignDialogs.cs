using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Free.Shared.Shell.Avalonia;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-DESIGN: Page Borders dialog (Design &gt; Page Background &gt; Page Borders). Collects a line style,
/// colour and width and returns a <see cref="PageBorder"/> on OK, or signals removal. Mirrors the existing
/// Avalonia dialog pattern (non-resizable, owner-centred, result via public properties awaited by the shell).
/// Edge-selection (top/bottom/left/right) is modelled by Word but FreeW's <see cref="PageBorder"/> is a
/// uniform box, so the dialog applies to all four edges (the most common case); per-edge selection is deferred.
/// </summary>
public sealed class PageBordersDialog : Window
{
    private static readonly (string Label, BorderLineStyle Style)[] BorderStyles =
    [
        ("Single", BorderLineStyle.Single),
        ("Dotted", BorderLineStyle.Dotted),
        ("Dashed", BorderLineStyle.Dashed),
        ("Double", BorderLineStyle.Double),
        ("Thick",  BorderLineStyle.Thick),
    ];

    private static readonly (string Label, string Hex)[] Colors =
    [
        ("Black",     "#000000"),
        ("Dark Blue", "#1F3864"),
        ("Blue",      "#2F5496"),
        ("Red",       "#C00000"),
        ("Green",     "#548235"),
        ("Gray",      "#808080"),
    ];

    private readonly ComboBox _style = new() { MinWidth = 180, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _color = new() { MinWidth = 180, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _width = new() { MinWidth = 180, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>The border the user chose to apply (OK), or null when cancelled / Remove was clicked.</summary>
    public PageBorder? Result { get; private set; }

    /// <summary>True when the user clicked "None" (remove the page border).</summary>
    public bool RemoveRequested { get; private set; }

    public PageBordersDialog(PageBorder? current)
    {
        Title = "Page Borders";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _style.ItemsSource = BorderStyles.Select(s => s.Label).ToList();
        _style.SelectedIndex = Math.Max(0, Array.FindIndex(BorderStyles, s => s.Style == (current?.LineStyle ?? BorderLineStyle.Single)));

        _color.ItemsSource = Colors.Select(c => c.Label).ToList();
        _color.SelectedIndex = Math.Max(0, Array.FindIndex(Colors,
            c => string.Equals(c.Hex, current?.ColorHex, StringComparison.OrdinalIgnoreCase)));

        _width.ItemsSource = new[] { "0.5 pt", "1 pt", "1.5 pt", "2.25 pt", "3 pt", "4.5 pt", "6 pt" };
        _width.SelectedIndex = current is null ? 1 : ClosestWidthIndex(current.WidthPt);
        AvaloniaCompactDialogChrome.ApplyComboBox(_style, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_color, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_width, InsertDialogLayout.ChromeStyle);

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, "Style:", _style);
        InsertDialogLayout.AddLabeledRow(grid, 1, "Color:", _color);
        InsertDialogLayout.AddLabeledRow(grid, 2, "Width:", _width);

        var okButton = InsertDialogLayout.MakeButton("OK", (_, _) =>
        {
            var style = BorderStyles[Math.Max(0, _style.SelectedIndex)].Style;
            var hex = Colors[Math.Max(0, _color.SelectedIndex)].Hex;
            var widthPt = WidthPtForIndex(Math.Max(0, _width.SelectedIndex));
            Result = new PageBorder(hex, widthPt) { LineStyle = style };
            Close();
        });
        var noneButton = InsertDialogLayout.MakeButton("None", (_, _) =>
        {
            RemoveRequested = true;
            Close();
        });
        var cancelButton = InsertDialogLayout.MakeButton("Cancel", (_, _) => Close());
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, noneButton, cancelButton], new Thickness(14, 12, 14, 12));

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(btnRow);
        Content = outer;
    }

    private static readonly double[] WidthValues = [0.5, 1.0, 1.5, 2.25, 3.0, 4.5, 6.0];

    private static int ClosestWidthIndex(double pt)
    {
        var best = 1;
        var bestDelta = double.MaxValue;
        for (var i = 0; i < WidthValues.Length; i++)
        {
            var d = Math.Abs(WidthValues[i] - pt);
            if (d < bestDelta) { bestDelta = d; best = i; }
        }
        return best;
    }

    private static double WidthPtForIndex(int index) =>
        index >= 0 && index < WidthValues.Length ? WidthValues[index] : 1.0;
}

/// <summary>
/// AV-DESIGN: Custom Watermark dialog (Design &gt; Page Background &gt; Watermark &gt; Custom Watermark). Collects
/// text, font, colour, layout (diagonal / horizontal) and a semitransparent flag, and returns a
/// <see cref="WatermarkOptions"/> on OK (or null to remove). Picture watermarks are deferred (text only).
/// </summary>
public sealed class WatermarkDialog : Window
{
    private static readonly (string Label, string Hex)[] Colors =
    [
        ("Gray",      "#808080"),
        ("Light Gray","#BFBFBF"),
        ("Red",       "#C00000"),
        ("Blue",      "#2F5496"),
        ("Green",     "#548235"),
        ("Black",     "#000000"),
    ];

    private readonly TextBox _text = new()
    {
        MinWidth = 220,
        PlaceholderText = "Watermark text",
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly ComboBox _font = new() { MinWidth = 220, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _color = new() { MinWidth = 220, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _layout = new() { MinWidth = 220, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _semitransparent = new()
    {
        Content = "Semitransparent",
        IsChecked = true,
        Margin = new Thickness(0, 8, 0, 0),
    };

    /// <summary>The watermark to apply (OK), or null when cancelled / Remove was clicked.</summary>
    public WatermarkOptions? Result { get; private set; }

    /// <summary>True when the user chose "No watermark" (remove).</summary>
    public bool RemoveRequested { get; private set; }

    public WatermarkDialog(WatermarkOptions? current)
    {
        Title = "Custom Watermark";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _text.Text = current?.Text ?? string.Empty;

        _font.ItemsSource = FreeW.App.Avalonia.Ribbon.FreeWRibbon.FontFamilies;
        _font.SelectedItem = current?.FontFamily ?? "Calibri";
        if (_font.SelectedItem is null)
            _font.SelectedIndex = 0;

        _color.ItemsSource = Colors.Select(c => c.Label).ToList();
        _color.SelectedIndex = Math.Max(0, Array.FindIndex(Colors,
            c => string.Equals(c.Hex, current?.FontColorHex, StringComparison.OrdinalIgnoreCase)));

        _layout.ItemsSource = new[] { "Diagonal", "Horizontal" };
        _layout.SelectedIndex = current?.Layout == WatermarkLayout.Horizontal ? 1 : 0;

        _semitransparent.IsChecked = current is null || current.Opacity < 0.999;
        AvaloniaCompactDialogChrome.ApplyTextBox(_text, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_font, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_color, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_layout, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_semitransparent, InsertDialogLayout.ChromeStyle);

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, "Text:",   _text);
        InsertDialogLayout.AddLabeledRow(grid, 1, "Font:",   _font);
        InsertDialogLayout.AddLabeledRow(grid, 2, "Color:",  _color);
        InsertDialogLayout.AddLabeledRow(grid, 3, "Layout:", _layout);

        var checkRow = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
        checkRow.Children.Add(_semitransparent);

        var okButton = InsertDialogLayout.MakeButton("OK", (_, _) =>
        {
            var text = _text.Text?.Trim();
            if (string.IsNullOrEmpty(text))
                return; // text is required; keep the dialog open
            Result = new WatermarkOptions(text)
            {
                FontFamily = _font.SelectedItem as string ?? "Calibri",
                FontColorHex = Colors[Math.Max(0, _color.SelectedIndex)].Hex,
                Layout = _layout.SelectedIndex == 1 ? WatermarkLayout.Horizontal : WatermarkLayout.Diagonal,
                Opacity = _semitransparent.IsChecked == true ? 0.3 : 1.0,
            };
            Close();
        });
        var noWatermarkButton = InsertDialogLayout.MakeButton("No Watermark", (_, _) =>
        {
            RemoveRequested = true;
            Close();
        });
        var cancelButton = InsertDialogLayout.MakeButton("Cancel", (_, _) => Close());
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, noWatermarkButton, cancelButton], new Thickness(14, 12, 14, 12));

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(checkRow);
        outer.Children.Add(btnRow);
        Content = outer;
    }
}
