using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Borders and Shading" dialog (Home / Design &gt; Borders &gt; Borders and Shading…). Edits the
/// paragraph box border, the whole-page border (w:pgBorders) and paragraph shading from one place, mirroring
/// Word's three-tab layout:
/// <list type="bullet">
/// <item>Borders — a setting (None / Box / Shadow / 3-D / Custom), a line style, colour and width, and the
/// per-edge top/bottom/left/right toggles.</item>
/// <item>Page Border — the same controls for the page border (the model already carries a
/// <see cref="PageBorder"/>).</item>
/// <item>Shading — a fill colour and a pattern (Clear / Solid / 10% / 25% / 50%).</item>
/// </list>
///
/// <para>
/// The dialog only produces a <see cref="Result"/>; the apply path (the ribbon command) routes the chosen
/// paragraph border/shading through <see cref="FreeW.App.Host.Editing.DocumentView.SetParagraphBorder"/> /
/// <see cref="FreeW.App.Host.Editing.DocumentView.SetParagraphShading"/> (the undo/redo bus) and the page
/// border through <see cref="FreeW.App.Host.Editing.DocumentView.ApplyPageSettings"/>. Everything round-trips
/// through the existing w:pBdr / w:pgBorders / w:shd writers. The "Shadow / 3-D" settings have no distinct
/// OOXML representation in FreeW's model, so they map to a Box (all four edges) — the setting is informational,
/// matching how Word collapses them when a document is reopened.
/// </para>
/// </summary>
internal sealed class BordersAndShadingDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>
    /// The settings the dialog produces. <see cref="ParagraphBorder"/> is null to clear the paragraph border
    /// (the None setting); <see cref="PageBorder"/> is null to clear the page border; <see cref="ShadingHex"/>
    /// is null to clear shading.
    /// </summary>
    internal sealed record Result(ParagraphBorder? ParagraphBorder, PageBorder? PageBorder, string? ShadingHex, ShadingPattern ShadingPattern);

    // Setting presets shown on the Borders / Page Border tabs. Box/Shadow/3-D are all-edges boxes (Shadow/3-D
    // have no separate FreeW model state); None clears; Custom honours the per-edge toggles as the user sets them.
    private static readonly string[] Settings = ["None", "Box", "Shadow", "3-D", "Custom"];
    private static readonly string[] LineStyleNames = ["Single", "Dotted", "Dashed", "Double", "Thick", "Wave"];
    private static readonly BorderLineStyle[] LineStyleValues =
        [BorderLineStyle.Single, BorderLineStyle.Dotted, BorderLineStyle.Dashed, BorderLineStyle.Double, BorderLineStyle.Thick, BorderLineStyle.Wave];

    private static readonly string[] PatternNames = ["Clear (none)", "Solid (100%)", "10%", "25%", "50%"];
    private static readonly ShadingPattern[] PatternValues =
        [ShadingPattern.Clear, ShadingPattern.Solid, ShadingPattern.Pct10, ShadingPattern.Pct25, ShadingPattern.Pct50];

    // A small swatch palette shared by the border-colour and shading-colour pickers.
    private static readonly string[] Palette =
    [
        "#000000", "#808080", "#C00000", "#FF0000", "#FFC000", "#FFFF00",
        "#92D050", "#00B050", "#00B0F0", "#0070C0", "#7030A0", "#FFFFFF",
    ];

    // Borders tab.
    private readonly ComboBox _setting;
    private readonly ComboBox _lineStyle;
    private readonly ComboBox _color;
    private readonly TextBox _width;
    private readonly CheckBox _top;
    private readonly CheckBox _left;
    private readonly CheckBox _bottom;
    private readonly CheckBox _right;

    // Page Border tab.
    private readonly ComboBox _pageSetting;
    private readonly ComboBox _pageLineStyle;
    private readonly ComboBox _pageColor;
    private readonly TextBox _pageWidth;

    // Shading tab.
    private readonly ComboBox _shadingColor;
    private readonly ComboBox _shadingPattern;

    private Result? _result;

    private BordersAndShadingDialog(Window? owner, ParagraphFormatting paragraph, PageBorder? pageBorder)
    {
        Owner = owner;
        Title = "Borders and Shading";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var border = paragraph.Border;

        _setting = Combo(Settings, SettingIndexFor(border));
        _lineStyle = Combo(LineStyleNames, IndexOf(LineStyleValues, border?.LineStyle ?? BorderLineStyle.Single));
        _color = ColorCombo(border?.ColorHex ?? "#000000");
        _width = NumberBox(border?.WidthPt ?? 0.5);
        _top = EdgeBox("Top", border?.Top ?? true);
        _left = EdgeBox("Left", border?.Left ?? true);
        _bottom = EdgeBox("Bottom", border?.Bottom ?? true);
        _right = EdgeBox("Right", border?.Right ?? true);
        _setting.SelectionChanged += (_, _) => ApplyParagraphSetting();

        _pageSetting = Combo(Settings, pageBorder is null ? 0 : 1);
        _pageLineStyle = Combo(LineStyleNames, IndexOf(LineStyleValues, pageBorder?.LineStyle ?? BorderLineStyle.Single));
        _pageColor = ColorCombo(pageBorder?.ColorHex ?? "#000000");
        _pageWidth = NumberBox(pageBorder?.WidthPt ?? 1.0);

        _shadingColor = ColorCombo(paragraph.ShadingColorHex ?? "#FFFFFF", includeNone: true,
            selectNone: string.IsNullOrEmpty(paragraph.ShadingColorHex));
        _shadingPattern = Combo(PatternNames, IndexOf(PatternValues, paragraph.ShadingPattern));

        var tabs = new TabControl { Margin = new Thickness(14, 14, 14, 0) };
        tabs.Items.Add(new TabItem { Header = "Borders", Content = BuildBordersTab() });
        tabs.Items.Add(new TabItem { Header = "Page Border", Content = BuildPageBorderTab() });
        tabs.Items.Add(new TabItem { Header = "Shading", Content = BuildShadingTab() });

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(14, 12, 14, 12));

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;

        DialogFocus.FocusAndSelect(_width);
    }

    // --- tab builders -------------------------------------------------------

    private Grid BuildBordersTab()
    {
        var grid = TwoColumnGrid(7);
        AddRow(grid, 0, "Setting:", _setting);
        AddRow(grid, 1, "Style:", _lineStyle);
        AddRow(grid, 2, "Colour:", _color);
        AddRow(grid, 3, "Width (pt):", _width);
        AddRow(grid, 4, "Edges:", EdgeRow(_top, _bottom));
        AddRow(grid, 5, string.Empty, EdgeRow(_left, _right));
        return grid;
    }

    private Grid BuildPageBorderTab()
    {
        var grid = TwoColumnGrid(4);
        AddRow(grid, 0, "Setting:", _pageSetting);
        AddRow(grid, 1, "Style:", _pageLineStyle);
        AddRow(grid, 2, "Colour:", _pageColor);
        AddRow(grid, 3, "Width (pt):", _pageWidth);
        return grid;
    }

    private Grid BuildShadingTab()
    {
        var grid = TwoColumnGrid(2);
        AddRow(grid, 0, "Fill:", _shadingColor);
        AddRow(grid, 1, "Pattern:", _shadingPattern);
        return grid;
    }

    // --- helpers ------------------------------------------------------------

    private static Grid TwoColumnGrid(int rows)
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static StackPanel EdgeRow(CheckBox a, CheckBox b)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        a.Margin = new Thickness(0, 0, 16, 0);
        panel.Children.Add(a);
        panel.Children.Add(b);
        return panel;
    }

    private static ComboBox Combo(string[] items, int selected)
    {
        var combo = new ComboBox { MinWidth = 160 };
        foreach (var item in items)
            combo.Items.Add(item);
        combo.SelectedIndex = System.Math.Clamp(selected, 0, items.Length - 1);
        return combo;
    }

    // A colour combo whose items are coloured swatches (plus an optional "No Colour" entry). The first item is
    // selected to match the seed colour; selecting "No Colour" yields a null hex via SelectedColor below.
    private static ComboBox ColorCombo(string seedHex, bool includeNone = false, bool selectNone = false)
    {
        var combo = new ComboBox { MinWidth = 160 };
        var selectedIndex = 0;
        if (includeNone)
            combo.Items.Add(new ComboBoxItem { Content = "No Colour", Tag = (string?)null });
        var offset = combo.Items.Count;
        for (var i = 0; i < Palette.Length; i++)
        {
            var hex = Palette[i];
            combo.Items.Add(SwatchItem(hex));
            if (string.Equals(hex, seedHex, StringComparison.OrdinalIgnoreCase))
                selectedIndex = offset + i;
        }
        combo.SelectedIndex = includeNone && selectNone ? 0 : selectedIndex;
        return combo;
    }

    private static ComboBoxItem SwatchItem(string hex)
    {
        var swatch = new System.Windows.Shapes.Rectangle
        {
            Width = 28,
            Height = 12,
            Stroke = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            StrokeThickness = 1,
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            Margin = new Thickness(0, 0, 6, 0)
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(swatch);
        panel.Children.Add(new TextBlock { Text = hex, VerticalAlignment = VerticalAlignment.Center });
        return new ComboBoxItem { Content = panel, Tag = hex };
    }

    // The hex carried by the selected swatch item, or null for the "No Colour" entry.
    private static string? SelectedColor(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: string hex } ? hex : null;

    private static TextBox NumberBox(double value) => new()
    {
        Text = value.ToString("0.##", CultureInfo.CurrentCulture),
        MinWidth = 160
    };

    private static CheckBox EdgeBox(string label, bool isChecked) => new()
    {
        Content = label,
        IsChecked = isChecked,
        VerticalAlignment = VerticalAlignment.Center
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

        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static int IndexOf<T>(T[] values, T value)
    {
        var index = System.Array.IndexOf(values, value);
        return index < 0 ? 0 : index;
    }

    // Maps an existing paragraph border back to a setting selection so reopening shows the current state.
    private static int SettingIndexFor(ParagraphBorder? border)
    {
        if (border is null)
            return 0; // None
        var fullBox = border is { Top: true, Left: true, Bottom: true, Right: true } && !border.BottomOnly;
        return fullBox ? 1 : 4; // Box, else Custom
    }

    // The None setting disables the per-edge toggles; Box/Shadow/3-D force all four on; Custom leaves them
    // editable. Mirrors Word's setting → edge behaviour.
    private void ApplyParagraphSetting()
    {
        switch (_setting.SelectedIndex)
        {
            case 0: // None
                SetEdges(false);
                SetEdgesEnabled(false);
                break;
            case 4: // Custom
                SetEdgesEnabled(true);
                break;
            default: // Box / Shadow / 3-D
                SetEdges(true);
                SetEdgesEnabled(false);
                break;
        }
    }

    private void SetEdges(bool value)
    {
        _top.IsChecked = value;
        _left.IsChecked = value;
        _bottom.IsChecked = value;
        _right.IsChecked = value;
    }

    private void SetEdgesEnabled(bool enabled)
    {
        _top.IsEnabled = enabled;
        _left.IsEnabled = enabled;
        _bottom.IsEnabled = enabled;
        _right.IsEnabled = enabled;
    }

    private void Accept()
    {
        if (!TryParseDouble(_width.Text, out var width) || width <= 0 || width > 12
            || !TryParseDouble(_pageWidth.Text, out var pageWidth) || pageWidth <= 0 || pageWidth > 12)
        {
            DialogMessageHelper.ShowWarning(this, "Enter a border width between 0 and 12 points.");
            return;
        }

        var paragraphBorder = BuildParagraphBorder(width);
        var pageBorderResult = BuildPageBorderResult(pageWidth);
        var shadingHex = SelectedColor(_shadingColor);
        var pattern = PatternValues[System.Math.Clamp(_shadingPattern.SelectedIndex, 0, PatternValues.Length - 1)];

        _result = new Result(paragraphBorder, pageBorderResult, shadingHex, pattern);
        Close();
    }

    private ParagraphBorder? BuildParagraphBorder(double width)
    {
        if (_setting.SelectedIndex == 0) // None
            return null;
        var top = _top.IsChecked == true;
        var left = _left.IsChecked == true;
        var bottom = _bottom.IsChecked == true;
        var right = _right.IsChecked == true;
        if (!top && !left && !bottom && !right)
            return null; // every edge off ≡ no border
        return new ParagraphBorder(SelectedColor(_color) ?? "#000000", width)
        {
            LineStyle = LineStyleValues[System.Math.Clamp(_lineStyle.SelectedIndex, 0, LineStyleValues.Length - 1)],
            Top = top,
            Left = left,
            Bottom = bottom,
            Right = right,
        };
    }

    private PageBorder? BuildPageBorderResult(double pageWidth)
    {
        if (_pageSetting.SelectedIndex == 0) // None
            return null;
        return new PageBorder(SelectedColor(_pageColor) ?? "#000000", pageWidth)
        {
            LineStyle = LineStyleValues[System.Math.Clamp(_pageLineStyle.SelectedIndex, 0, LineStyleValues.Length - 1)],
        };
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    /// <summary>
    /// Show the dialog seeded with the current paragraph border/shading and page border; returns the chosen
    /// settings, or null if cancelled.
    /// </summary>
    public static Result? Prompt(Window? owner, ParagraphFormatting paragraph, PageBorder? pageBorder)
    {
        var dialog = new BordersAndShadingDialog(owner, paragraph, pageBorder);
        dialog.ApplyParagraphSetting();
        dialog.ShowDialog();
        return dialog._result;
    }
}
