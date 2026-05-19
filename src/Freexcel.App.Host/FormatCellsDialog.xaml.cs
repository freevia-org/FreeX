using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;

namespace Freexcel.App.Host;

public partial class FormatCellsDialog : Window
{
    public StyleDiff? ResultDiff { get; private set; }

    private static readonly string[] NumberFormatCodes =
        ["General", "0.00", "$#,##0.00", "0%", "yyyy-MM-dd", "HH:mm:ss", "@"];

    private static readonly string[] NumberFormatLabels =
        ["General", "Number (0.00)", "Currency ($#,##0.00)", "Percentage (0%)",
         "Date (yyyy-MM-dd)", "Time (HH:mm:ss)", "Text (@)"];

    public FormatCellsDialog(CellStyle current, FormatCellsDialogTab initialTab = FormatCellsDialogTab.Number)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Populate(current);
            Tabs.SelectedIndex = (int)initialTab;
        };
    }

    private void Populate(CellStyle s)
    {
        NumberFormatCombo.ItemsSource = NumberFormatLabels;
        var idx = Array.IndexOf(NumberFormatCodes, s.NumberFormat);
        NumberFormatCombo.SelectedIndex = idx >= 0 ? idx : 0;
        if (idx < 0) NumberFormatCombo.Text = s.NumberFormat;

        DlgFontNameBox.ItemsSource  = new[] { "Calibri", "Arial", "Times New Roman", "Courier New", "Segoe UI", "Verdana" };
        DlgFontNameBox.SelectedItem = s.FontName;
        DlgFontSizeBox.ItemsSource  = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36" };
        DlgFontSizeBox.Text         = s.FontSize.ToString("0.#");
        DlgBoldCheck.IsChecked      = s.Bold;
        DlgItalicCheck.IsChecked    = s.Italic;
        DlgUnderlineCheck.IsChecked = s.Underline;
        DlgStrikeCheck.IsChecked    = s.Strikethrough;
        DlgFontColorBox.Text        = $"{s.FontColor.R},{s.FontColor.G},{s.FontColor.B}";

        DlgFillColorBox.Text = s.FillColor.HasValue
            ? $"{s.FillColor.Value.R},{s.FillColor.Value.G},{s.FillColor.Value.B}"
            : "";

        DlgHAlignBox.ItemsSource  = Enum.GetNames(typeof(CellHAlign));
        DlgHAlignBox.SelectedItem = s.HorizontalAlignment.ToString();
        DlgVAlignBox.ItemsSource  = Enum.GetNames(typeof(CellVAlign));
        DlgVAlignBox.SelectedItem = s.VerticalAlignment.ToString();
        DlgWrapTextCheck.IsChecked = s.WrapText;
    }

    private void NumberFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        CellColor? fontColor = TryParseColor(DlgFontColorBox.Text);
        CellColor? fillColor = TryParseColor(DlgFillColorBox.Text);

        string? numFmt = null;
        if (NumberFormatCombo.SelectedIndex >= 0 && NumberFormatCombo.SelectedIndex < NumberFormatCodes.Length)
            numFmt = NumberFormatCodes[NumberFormatCombo.SelectedIndex];
        else if (!string.IsNullOrWhiteSpace(NumberFormatCombo.Text))
            numFmt = NumberFormatCombo.Text;

        double? fontSize = null;
        if (double.TryParse(DlgFontSizeBox.Text, out var fs) && fs > 0) fontSize = fs;

        CellHAlign? hAlign = null;
        if (DlgHAlignBox.SelectedItem is string ha && Enum.TryParse(ha, out CellHAlign h)) hAlign = h;
        CellVAlign? vAlign = null;
        if (DlgVAlignBox.SelectedItem is string va && Enum.TryParse(va, out CellVAlign v)) vAlign = v;

        ResultDiff = new StyleDiff(
            Bold:          DlgBoldCheck.IsChecked,
            Italic:        DlgItalicCheck.IsChecked,
            Underline:     DlgUnderlineCheck.IsChecked,
            Strikethrough: DlgStrikeCheck.IsChecked,
            FontName:      DlgFontNameBox.SelectedItem as string,
            FontSize:      fontSize,
            FontColor:     fontColor,
            FillColor:     fillColor,
            HAlign:        hAlign,
            VAlign:        vAlign,
            WrapText:      DlgWrapTextCheck.IsChecked,
            NumberFormat:  numFmt
        );

        DialogResult = true;
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
    Font,
    Fill,
    Alignment
}
