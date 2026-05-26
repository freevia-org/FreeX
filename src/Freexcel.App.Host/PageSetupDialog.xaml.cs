using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum PageSetupRangeSelectionTarget
{
    PrintArea,
    RepeatRows,
    RepeatColumns
}

public enum PageSetupInitialFocusTarget
{
    PageOrientation,
    RepeatRows,
    ScaleToFit
}

public sealed record PageSetupRangeSelectionRequest(
    PageSetupRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public partial class PageSetupDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly GridRange? _currentSelection;
    private readonly Action<PageSetupRangeSelectionRequest>? _requestRangeSelection;
    private readonly PageSetupInitialFocusTarget _initialFocusTarget;

    public WorksheetPageOrientation Orientation { get; private set; }
    public WorksheetPaperSize PaperSize { get; private set; }
    public WorksheetPageMargins Margins { get; private set; }
    public double HeaderMargin { get; private set; }
    public double FooterMargin { get; private set; }
    public bool PrintGridlines { get; private set; }
    public bool PrintHeadings { get; private set; }
    public GridRange? PrintArea { get; private set; }
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
    public WorksheetHeaderFooter Header { get; private set; }
    public WorksheetHeaderFooter Footer { get; private set; }
    public WorksheetHeaderFooter FirstPageHeader { get; private set; }
    public WorksheetHeaderFooter FirstPageFooter { get; private set; }
    public WorksheetHeaderFooter EvenPageHeader { get; private set; }
    public WorksheetHeaderFooter EvenPageFooter { get; private set; }
    public WorksheetHeaderFooterPictureSet HeaderPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet FooterPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet FirstPageHeaderPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet FirstPageFooterPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet EvenPageHeaderPictures { get; private set; }
    public WorksheetHeaderFooterPictureSet EvenPageFooterPictures { get; private set; }
    public bool DifferentFirstPage { get; private set; }
    public bool DifferentOddEvenPages { get; private set; }
    public bool ScaleHeaderFooterWithDocument { get; private set; }
    public bool AlignHeaderFooterWithMargins { get; private set; }
    public PageSetupDialogAction RequestedAction { get; private set; } = PageSetupDialogAction.Ok;
    public PageSetupRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public PageSetupDialog(
        Sheet sheet,
        GridRange? currentSelection = null,
        Action<PageSetupRangeSelectionRequest>? requestRangeSelection = null,
        PageSetupInitialFocusTarget initialFocusTarget = PageSetupInitialFocusTarget.PageOrientation)
    {
        InitializeComponent();
        _requestRangeSelection = requestRangeSelection;
        _initialFocusTarget = initialFocusTarget;
        _sheetId = sheet.Id;
        _currentSelection = currentSelection is { } selection &&
                            selection.Start.Sheet == sheet.Id &&
                            selection.End.Sheet == sheet.Id
            ? selection
            : null;
        Orientation = sheet.PageOrientation;
        PaperSize = sheet.PaperSize;
        Margins = sheet.PageMargins;
        HeaderMargin = sheet.HeaderMargin;
        FooterMargin = sheet.FooterMargin;
        PrintGridlines = sheet.PrintGridlines;
        PrintHeadings = sheet.PrintHeadings;
        PrintArea = sheet.PrintArea;
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
        Header = sheet.PageHeader;
        Footer = sheet.PageFooter;
        FirstPageHeader = sheet.FirstPageHeader;
        FirstPageFooter = sheet.FirstPageFooter;
        EvenPageHeader = sheet.EvenPageHeader;
        EvenPageFooter = sheet.EvenPageFooter;
        HeaderPictures = sheet.PageHeaderPictures.DeepClone();
        FooterPictures = sheet.PageFooterPictures.DeepClone();
        FirstPageHeaderPictures = sheet.FirstPageHeaderPictures.DeepClone();
        FirstPageFooterPictures = sheet.FirstPageFooterPictures.DeepClone();
        EvenPageHeaderPictures = sheet.EvenPageHeaderPictures.DeepClone();
        EvenPageFooterPictures = sheet.EvenPageFooterPictures.DeepClone();
        DifferentFirstPage = sheet.DifferentFirstPageHeaderFooter;
        DifferentOddEvenPages = sheet.DifferentOddEvenHeaderFooter;
        ScaleHeaderFooterWithDocument = sheet.HeaderFooterScaleWithDocument;
        AlignHeaderFooterWithMargins = sheet.HeaderFooterAlignWithMargins;
        PopulateFields();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) =>
        Accept(PageSetupDialogAction.Ok);

    private void PrintButton_Click(object sender, RoutedEventArgs e) =>
        Accept(PageSetupDialogAction.Print);

    private void PrintPreviewButton_Click(object sender, RoutedEventArgs e) =>
        Accept(PageSetupDialogAction.PrintPreview);

    private void OptionsButton_Click(object sender, RoutedEventArgs e) =>
        Accept(PageSetupDialogAction.Options);

    private void Accept(PageSetupDialogAction requestedAction)
    {
        var marginsText = string.Join(",",
            LeftMarginBox.Text,
            RightMarginBox.Text,
            TopMarginBox.Text,
            BottomMarginBox.Text);
        if (!PageMarginInputParser.TryParse(marginsText, out var margins, out var marginError))
        {
            MessageBox.Show(this, marginError, "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidMarginInput();
            return;
        }

        if (!PageLayoutInputParser.TryParseMarginDistance(HeaderMarginBox.Text, out var headerMargin) ||
            !PageLayoutInputParser.TryParseMarginDistance(FooterMarginBox.Text, out var footerMargin))
        {
            MessageBox.Show(this, "Enter non-negative header and footer margins in inches.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidHeaderFooterMargin();
            return;
        }

        var scaleText = FitToRadioButton.IsChecked == true
            ? $"{FitPagesWideBox.Text}x{FitPagesTallBox.Text}"
            : ScalePercentBox.Text;
        if (!PageLayoutInputParser.TryParseScaleToFit(scaleText, out var scaleToFit))
        {
            MessageBox.Show(this, "Enter scaling as percent 10-400 or pages wide x tall, for example 1x1.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidScalingInput();
            return;
        }

        if (!PageLayoutInputParser.TryParseOptionalFirstPageNumber(FirstPageNumberBox.Text, out var firstPageNumber))
        {
            MessageBox.Show(this, "Enter a non-zero first page number, or leave it blank for Automatic.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidPageTabNumber(FirstPageNumberBox);
            return;
        }

        if (!PageLayoutInputParser.TryParseOptionalPrintQuality(PrintQualityBox.Text, out var printQualityDpi))
        {
            MessageBox.Show(this, "Enter a positive print quality DPI value, or leave it blank for printer default.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidPageTabNumber(PrintQualityBox);
            return;
        }

        if (!TryParseOptionalPrintArea(PrintAreaBox.Text, out var printArea))
        {
            MessageBox.Show(this, "Enter print area as a cell range like A1:C10, or leave it blank.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidPrintArea();
            return;
        }

        var validRepeatRows = PageLayoutInputParser.TryParseRepeatRows(RowsRepeatBox.Text, out var repeatRows);
        var validRepeatColumns = PageLayoutInputParser.TryParseRepeatColumns(ColumnsRepeatBox.Text, out var repeatColumns);
        if (!validRepeatRows || !validRepeatColumns)
        {
            MessageBox.Show(this, "Enter print titles as rows like 1:2 and columns like A:C, or leave blank.",
                "Page Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInvalidPrintTitles();
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
        PrintArea = printArea;
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
        DifferentFirstPage = DifferentFirstPageBox.IsChecked == true;
        DifferentOddEvenPages = DifferentOddEvenBox.IsChecked == true;
        ScaleHeaderFooterWithDocument = ScaleWithDocumentBox.IsChecked == true;
        AlignHeaderFooterWithMargins = AlignWithMarginsBox.IsChecked == true;
        RequestedAction = requestedAction;
        DialogResult = true;
        Close();
    }

    private bool TryParseOptionalPrintArea(string input, out GridRange? printArea)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            printArea = null;
            return true;
        }

        try
        {
            printArea = GridRange.Parse(trimmed, _sheetId);
            return true;
        }
        catch (FormatException)
        {
            printArea = null;
            return false;
        }
    }

}

public enum PageSetupDialogAction
{
    Ok,
    Print,
    PrintPreview,
    Options
}
