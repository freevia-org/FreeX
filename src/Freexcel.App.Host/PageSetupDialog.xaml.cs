using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class PageSetupDialog : Window
{
    public WorksheetPageOrientation Orientation { get; private set; }
    public WorksheetPaperSize PaperSize { get; private set; }
    public WorksheetPageMargins Margins { get; private set; }
    public double HeaderMargin { get; private set; }
    public double FooterMargin { get; private set; }
    public bool PrintGridlines { get; private set; }
    public bool PrintHeadings { get; private set; }
    public WorksheetScaleToFit ScaleToFit { get; private set; }
    public WorksheetRepeatRange? PrintTitleRows { get; private set; }
    public WorksheetRepeatRange? PrintTitleColumns { get; private set; }
    public bool CenterHorizontally { get; private set; }
    public bool CenterVertically { get; private set; }
    public WorksheetPageOrder PageOrder { get; private set; }
    public int? FirstPageNumber { get; private set; }
    public bool PrintBlackAndWhite { get; private set; }
    public bool PrintDraftQuality { get; private set; }
    public int? PrintQualityDpi { get; private set; }
    public WorksheetPrintErrorValue PrintErrorValue { get; private set; }
    public WorksheetPrintComments PrintComments { get; private set; }

    public PageSetupDialog(Sheet sheet)
    {
        InitializeComponent();
        Orientation = sheet.PageOrientation;
        PaperSize = sheet.PaperSize;
        Margins = sheet.PageMargins;
        HeaderMargin = sheet.HeaderMargin;
        FooterMargin = sheet.FooterMargin;
        PrintGridlines = sheet.PrintGridlines;
        PrintHeadings = sheet.PrintHeadings;
        ScaleToFit = sheet.ScaleToFit;
        PrintTitleRows = sheet.PrintTitleRows;
        PrintTitleColumns = sheet.PrintTitleColumns;
        CenterHorizontally = sheet.CenterHorizontallyOnPage;
        CenterVertically = sheet.CenterVerticallyOnPage;
        PageOrder = sheet.PageOrder;
        FirstPageNumber = sheet.FirstPageNumber;
        PrintBlackAndWhite = sheet.PrintBlackAndWhite;
        PrintDraftQuality = sheet.PrintDraftQuality;
        PrintQualityDpi = sheet.PrintQualityDpi;
        PrintErrorValue = sheet.PrintErrorValue;
        PrintComments = sheet.PrintComments;
        PopulateFields();
    }

    private void PopulateFields()
    {
        OrientationBox.SelectedIndex = Orientation == WorksheetPageOrientation.Landscape ? 1 : 0;
        PaperSizeBox.SelectedIndex = PaperSize switch
        {
            WorksheetPaperSize.Letter => 0,
            WorksheetPaperSize.Legal => 2,
            _ => 1
        };
        LeftMarginBox.Text = Margins.Left.ToString(CultureInfo.InvariantCulture);
        RightMarginBox.Text = Margins.Right.ToString(CultureInfo.InvariantCulture);
        TopMarginBox.Text = Margins.Top.ToString(CultureInfo.InvariantCulture);
        BottomMarginBox.Text = Margins.Bottom.ToString(CultureInfo.InvariantCulture);
        HeaderMarginBox.Text = HeaderMargin.ToString(CultureInfo.InvariantCulture);
        FooterMarginBox.Text = FooterMargin.ToString(CultureInfo.InvariantCulture);
        CenterHorizontallyBox.IsChecked = CenterHorizontally;
        CenterVerticallyBox.IsChecked = CenterVertically;
        ScaleBox.Text = PageLayoutInputParser.FormatScaleToFit(ScaleToFit);
        FirstPageNumberBox.Text = FirstPageNumber?.ToString(CultureInfo.InvariantCulture) ?? "";
        PrintQualityBox.Text = PrintQualityDpi?.ToString(CultureInfo.InvariantCulture) ?? "";
        RowsRepeatBox.Text = PrintTitleRows is { } rows ? $"{rows.Start}:{rows.End}" : "";
        ColumnsRepeatBox.Text = PrintTitleColumns is { } cols
            ? $"{CellAddress.NumberToColumnName(cols.Start)}:{CellAddress.NumberToColumnName(cols.End)}"
            : "";
        PrintGridlinesBox.IsChecked = PrintGridlines;
        PrintHeadingsBox.IsChecked = PrintHeadings;
        PageOrderBox.SelectedIndex = PageOrder == WorksheetPageOrder.OverThenDown ? 1 : 0;
        PrintBlackAndWhiteBox.IsChecked = PrintBlackAndWhite;
        PrintDraftQualityBox.IsChecked = PrintDraftQuality;
        PrintErrorValueBox.SelectedIndex = PrintErrorValue switch
        {
            WorksheetPrintErrorValue.Blank => 1,
            WorksheetPrintErrorValue.Dash => 2,
            WorksheetPrintErrorValue.NotAvailable => 3,
            _ => 0
        };
        PrintCommentsBox.SelectedIndex = PrintComments switch
        {
            WorksheetPrintComments.AtEnd => 1,
            WorksheetPrintComments.AsDisplayed => 2,
            _ => 0
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var marginsText = string.Join(",",
            LeftMarginBox.Text,
            RightMarginBox.Text,
            TopMarginBox.Text,
            BottomMarginBox.Text);
        if (!PageMarginInputParser.TryParse(marginsText, out var margins, out var marginError))
        {
            MessageBox.Show(this, marginError, "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!PageLayoutInputParser.TryParseMarginDistance(HeaderMarginBox.Text, out var headerMargin) ||
            !PageLayoutInputParser.TryParseMarginDistance(FooterMarginBox.Text, out var footerMargin))
        {
            MessageBox.Show(this, "Enter non-negative header and footer margins in inches.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!PageLayoutInputParser.TryParseScaleToFit(ScaleBox.Text, out var scaleToFit))
        {
            MessageBox.Show(this, "Enter scaling as percent 10-400 or pages wide x tall, for example 1x1.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!PageLayoutInputParser.TryParseOptionalFirstPageNumber(FirstPageNumberBox.Text, out var firstPageNumber))
        {
            MessageBox.Show(this, "Enter a non-zero first page number, or leave it blank for Automatic.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!PageLayoutInputParser.TryParseOptionalPrintQuality(PrintQualityBox.Text, out var printQualityDpi))
        {
            MessageBox.Show(this, "Enter a positive print quality DPI value, or leave it blank for printer default.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!PageLayoutInputParser.TryParseRepeatRows(RowsRepeatBox.Text, out var repeatRows) ||
            !PageLayoutInputParser.TryParseRepeatColumns(ColumnsRepeatBox.Text, out var repeatColumns))
        {
            MessageBox.Show(this, "Enter print titles as rows like 1:2 and columns like A:C, or leave blank.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Orientation = ((OrientationBox.SelectedItem as ComboBoxItem)?.Tag as string) == "Landscape"
            ? WorksheetPageOrientation.Landscape
            : WorksheetPageOrientation.Portrait;
        PaperSize = ((PaperSizeBox.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "Letter" => WorksheetPaperSize.Letter,
            "Legal" => WorksheetPaperSize.Legal,
            _ => WorksheetPaperSize.A4
        };
        Margins = margins;
        HeaderMargin = headerMargin;
        FooterMargin = footerMargin;
        ScaleToFit = scaleToFit;
        PrintGridlines = PrintGridlinesBox.IsChecked == true;
        PrintHeadings = PrintHeadingsBox.IsChecked == true;
        PrintTitleRows = repeatRows;
        PrintTitleColumns = repeatColumns;
        CenterHorizontally = CenterHorizontallyBox.IsChecked == true;
        CenterVertically = CenterVerticallyBox.IsChecked == true;
        PageOrder = ((PageOrderBox.SelectedItem as ComboBoxItem)?.Tag as string) == "OverThenDown"
            ? WorksheetPageOrder.OverThenDown
            : WorksheetPageOrder.DownThenOver;
        FirstPageNumber = firstPageNumber;
        PrintQualityDpi = printQualityDpi;
        PrintBlackAndWhite = PrintBlackAndWhiteBox.IsChecked == true;
        PrintDraftQuality = PrintDraftQualityBox.IsChecked == true;
        PrintErrorValue = ((PrintErrorValueBox.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "Blank" => WorksheetPrintErrorValue.Blank,
            "Dash" => WorksheetPrintErrorValue.Dash,
            "NotAvailable" => WorksheetPrintErrorValue.NotAvailable,
            _ => WorksheetPrintErrorValue.Displayed
        };
        PrintComments = ((PrintCommentsBox.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "AtEnd" => WorksheetPrintComments.AtEnd,
            "AsDisplayed" => WorksheetPrintComments.AsDisplayed,
            _ => WorksheetPrintComments.None
        };
        DialogResult = true;
        Close();
    }

}
