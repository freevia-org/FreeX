using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

public partial class FormatCellsDialog : Window
{
    public StyleDiff? ResultDiff { get; private set; }

    private readonly CellStyle _current;

    private static readonly string[] NumberFormatCodes =
        ["General", "0.00", "$#,##0.00", "0%", "yyyy-MM-dd", "HH:mm:ss", "@"];

    private static readonly string[] NumberFormatLabels =
        ["General", "Number (0.00)", "Currency ($#,##0.00)", "Percentage (0%)",
         "Date (yyyy-MM-dd)", "Time (HH:mm:ss)", "Text (@)"];

    public FormatCellsDialog(CellStyle current, FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)
    {
        _current = current.Clone();
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Populate(_current);
            Tabs.SelectedIndex = (int)initialTab;
        };
    }

    private void Populate(CellStyle s)
    {
        NumberFormatCombo.ItemsSource = NumberFormatLabels;
        var idx = Array.IndexOf(NumberFormatCodes, s.NumberFormat);
        if (idx >= 0)
        {
            NumberFormatCombo.SelectedIndex = idx;
        }
        else
        {
            NumberFormatCombo.SelectedIndex = -1;
            NumberFormatCombo.Text = s.NumberFormat;
        }

        DlgFontNameBox.ItemsSource  = new[] { "Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana" };
        DlgFontNameBox.SelectedItem = s.FontName;
        DlgFontSizeBox.ItemsSource  = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36" };
        DlgFontSizeBox.Text         = s.FontSize.ToString("0.#");
        DlgBoldCheck.IsChecked      = s.Bold;
        DlgItalicCheck.IsChecked    = s.Italic;
        DlgUnderlineCheck.IsChecked = s.Underline;
        DlgDoubleUnderlineCheck.IsChecked = s.DoubleUnderline;
        DlgStrikeCheck.IsChecked    = s.Strikethrough;
        DlgSuperscriptCheck.IsChecked = s.Superscript;
        DlgSubscriptCheck.IsChecked = s.Subscript;
        DlgFontColorBox.Text        = $"{s.FontColor.R},{s.FontColor.G},{s.FontColor.B}";

        DlgFillColorBox.Text = s.FillColor.HasValue
            ? $"{s.FillColor.Value.R},{s.FillColor.Value.G},{s.FillColor.Value.B}"
            : "";
        DlgClearFillCheck.IsChecked = false;

        DlgHAlignBox.ItemsSource  = Enum.GetNames(typeof(CellHAlign));
        DlgHAlignBox.SelectedItem = s.HorizontalAlignment.ToString();
        DlgVAlignBox.ItemsSource  = Enum.GetNames(typeof(CellVAlign));
        DlgVAlignBox.SelectedItem = s.VerticalAlignment.ToString();
        DlgWrapTextCheck.IsChecked = s.WrapText;
        DlgShrinkToFitCheck.IsChecked = s.ShrinkToFit;
        DlgIndentLevelBox.Text = s.IndentLevel.ToString();
        DlgTextRotationBox.Text = s.TextRotation.ToString();

        PopulateBorder(DlgBorderTopStyleBox, DlgBorderTopColorBox, s.BorderTop);
        PopulateBorder(DlgBorderRightStyleBox, DlgBorderRightColorBox, s.BorderRight);
        PopulateBorder(DlgBorderBottomStyleBox, DlgBorderBottomColorBox, s.BorderBottom);
        PopulateBorder(DlgBorderLeftStyleBox, DlgBorderLeftColorBox, s.BorderLeft);

        DlgLockedCheck.IsChecked = s.Locked;
    }

    private void NumberFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    public static string? ResolveNumberFormat(string text, int selectedIndex)
    {
        var trimmedText = text.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedText)
            && (selectedIndex < 0
                || selectedIndex >= NumberFormatCodes.Length
                || (trimmedText != NumberFormatLabels[selectedIndex]
                    && trimmedText != NumberFormatCodes[selectedIndex])))
        {
            return trimmedText;
        }

        if (selectedIndex >= 0 && selectedIndex < NumberFormatCodes.Length)
            return NumberFormatCodes[selectedIndex];

        return string.IsNullOrWhiteSpace(trimmedText) ? null : trimmedText;
    }

    public static int? TryParseSupportedTextRotation(string text)
    {
        if (!int.TryParse(text, out var rotation))
            return null;

        return rotation == 255 || rotation is >= -90 and <= 90
            ? rotation
            : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        CellColor? fontColor = TryParseColor(DlgFontColorBox.Text);
        CellColor? fillColor = TryParseColor(DlgFillColorBox.Text);
        bool clearFill = DlgClearFillCheck.IsChecked == true;

        string? numFmt = ResolveNumberFormat(NumberFormatCombo.Text, NumberFormatCombo.SelectedIndex);

        double? fontSize = null;
        if (double.TryParse(DlgFontSizeBox.Text, out var fs) && fs > 0) fontSize = fs;

        CellHAlign? hAlign = null;
        if (DlgHAlignBox.SelectedItem is string ha && Enum.TryParse(ha, out CellHAlign h)) hAlign = h;
        CellVAlign? vAlign = null;
        if (DlgVAlignBox.SelectedItem is string va && Enum.TryParse(va, out CellVAlign v)) vAlign = v;

        int? indentLevel = null;
        if (int.TryParse(DlgIndentLevelBox.Text, out var indent))
            indentLevel = Math.Clamp(indent, 0, 15);

        int? textRotation = TryParseSupportedTextRotation(DlgTextRotationBox.Text);

        CellBorder borderTop = ParseBorder(DlgBorderTopStyleBox, DlgBorderTopColorBox, _current.BorderTop);
        CellBorder borderRight = ParseBorder(DlgBorderRightStyleBox, DlgBorderRightColorBox, _current.BorderRight);
        CellBorder borderBottom = ParseBorder(DlgBorderBottomStyleBox, DlgBorderBottomColorBox, _current.BorderBottom);
        CellBorder borderLeft = ParseBorder(DlgBorderLeftStyleBox, DlgBorderLeftColorBox, _current.BorderLeft);

        ResultDiff = new StyleDiff(
            Bold:            DlgBoldCheck.IsChecked,
            Italic:          DlgItalicCheck.IsChecked,
            Underline:       DlgUnderlineCheck.IsChecked,
            Strikethrough:   DlgStrikeCheck.IsChecked,
            Superscript:     DlgSuperscriptCheck.IsChecked,
            Subscript:       DlgSubscriptCheck.IsChecked,
            FontName:        DlgFontNameBox.SelectedItem as string,
            FontSize:        fontSize,
            FontColor:       fontColor,
            FillColor:       clearFill ? null : fillColor,
            HAlign:          hAlign,
            VAlign:          vAlign,
            WrapText:        DlgWrapTextCheck.IsChecked,
            ShrinkToFit:     DlgShrinkToFitCheck.IsChecked,
            NumberFormat:    numFmt,
            DoubleUnderline: DlgDoubleUnderlineCheck.IsChecked,
            IndentLevel:     indentLevel,
            TextRotation:    textRotation,
            BorderTop:       borderTop,
            BorderRight:     borderRight,
            BorderBottom:    borderBottom,
            BorderLeft:      borderLeft,
            Locked:          DlgLockedCheck.IsChecked,
            ClearFill:       clearFill ? true : null
        );

        DialogResult = true;
    }

    private static void PopulateBorder(ComboBox styleBox, TextBox colorBox, CellBorder border)
    {
        styleBox.ItemsSource = Enum.GetNames(typeof(BorderStyle));
        styleBox.SelectedItem = border.Style.ToString();
        colorBox.Text = FormatColor(border.Color);
    }

    private static CellBorder ParseBorder(ComboBox styleBox, TextBox colorBox, CellBorder current)
    {
        var style = current.Style;
        if (styleBox.SelectedItem is string selectedStyle
            && Enum.TryParse(selectedStyle, out BorderStyle parsedStyle)
            && Enum.IsDefined(parsedStyle))
        {
            style = parsedStyle;
        }

        var color = TryParseColor(colorBox.Text) ?? current.Color;
        return new CellBorder(style, color);
    }

    private static string FormatColor(CellColor color) => $"{color.R},{color.G},{color.B}";

    private void DlgFontColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFontColorBox, allowNoColor: false);

    private void DlgFillColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgFillColorBox, allowNoColor: true);

    private void DlgBorderTopColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderTopColorBox, allowNoColor: false);

    private void DlgBorderRightColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderRightColorBox, allowNoColor: false);

    private void DlgBorderBottomColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderBottomColorBox, allowNoColor: false);

    private void DlgBorderLeftColorPickerButton_Click(object sender, RoutedEventArgs e) =>
        PickColorInto(DlgBorderLeftColorBox, allowNoColor: false);

    private void PickColorInto(TextBox target, bool allowNoColor)
    {
        var initial = TryParseColor(target.Text);
        var dialog = new ColorPickerDialog(initial, allowNoColor) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        target.Text = dialog.SelectedColor is { } color ? FormatColor(color) : "";
    }

    private static CellColor? TryParseColor(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(',');
        if (parts.Length == 3
            && byte.TryParse(parts[0].Trim(), out var r)
            && byte.TryParse(parts[1].Trim(), out var g)
            && byte.TryParse(parts[2].Trim(), out var b))
            return new CellColor(r, g, b);
        return null;
    }
}

public enum FormatCellsDialogTab
{
    Number,
    Alignment,
    Font,
    Fill,
    Border,
    Protection
}
