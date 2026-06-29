using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record PageSetupRangeSelectionRequest(
    PageSetupRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public partial class PageSetupDialog : Window
{
    private readonly Sheet _sourceSheet;
    private readonly SheetId _sheetId;
    private readonly GridRange? _currentSelection;
    private readonly Action<PageSetupRangeSelectionRequest>? _requestRangeSelection;
    private readonly PageSetupInitialFocusTarget _initialFocusTarget;

    public PageSetupDialogFields Fields { get; private set; }
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
        _sourceSheet = sheet;
        _requestRangeSelection = requestRangeSelection;
        _initialFocusTarget = initialFocusTarget;
        _sheetId = sheet.Id;
        _currentSelection = currentSelection is { } selection &&
                            selection.Start.Sheet == sheet.Id &&
                            selection.End.Sheet == sheet.Id
            ? selection
            : null;
        Fields = PageSetupDialogModel.FromSheet(sheet);
        Header = Fields.Header;
        Footer = Fields.Footer;
        FirstPageHeader = Fields.FirstPageHeader;
        FirstPageFooter = Fields.FirstPageFooter;
        EvenPageHeader = Fields.EvenPageHeader;
        EvenPageFooter = Fields.EvenPageFooter;
        HeaderPictures = Fields.HeaderPictures.DeepClone();
        FooterPictures = Fields.FooterPictures.DeepClone();
        FirstPageHeaderPictures = Fields.FirstPageHeaderPictures.DeepClone();
        FirstPageFooterPictures = Fields.FirstPageFooterPictures.DeepClone();
        EvenPageHeaderPictures = Fields.EvenPageHeaderPictures.DeepClone();
        EvenPageFooterPictures = Fields.EvenPageFooterPictures.DeepClone();
        DifferentFirstPage = Fields.DifferentFirstPage;
        DifferentOddEvenPages = Fields.DifferentOddEvenPages;
        ScaleHeaderFooterWithDocument = Fields.ScaleHeaderFooterWithDocument;
        AlignHeaderFooterWithMargins = Fields.AlignHeaderFooterWithMargins;
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
        var fields = ReadFields();
        var submission = PageSetupSubmissionPlanner.TryBuild(_sourceSheet, fields, requestedAction);
        if (!submission.Success)
        {
            var validation = submission.Validation!;
            DialogMessageHelper.ShowWarning(
                this,
                validation.Message.Resolve(UiText.Get),
                UiText.Get(PageSetupSubmissionPlanner.DefaultCaptionResourceKey));
            FocusValidationTarget(validation.Target);
            return;
        }

        Fields = fields;
        DifferentFirstPage = DifferentFirstPageBox.IsChecked == true;
        DifferentOddEvenPages = DifferentOddEvenBox.IsChecked == true;
        ScaleHeaderFooterWithDocument = ScaleWithDocumentBox.IsChecked == true;
        AlignHeaderFooterWithMargins = AlignWithMarginsBox.IsChecked == true;
        RequestedAction = submission.Submission!.RequestedAction;
        DialogResult = true;
        Close();
    }

    private PageSetupDialogFields ReadFields()
    {
        var marginsText = string.Join(",",
            LeftMarginBox.Text,
            RightMarginBox.Text,
            TopMarginBox.Text,
            BottomMarginBox.Text);

        return Fields with
        {
            Orientation = SelectedOrientation(),
            PaperSize = SelectedPaperSize(),
            MarginsText = marginsText,
            HeaderMarginText = HeaderMarginBox.Text,
            FooterMarginText = FooterMarginBox.Text,
            CenterHorizontally = CenterHorizontallyBox.IsChecked == true,
            CenterVertically = CenterVerticallyBox.IsChecked == true,
            ScalingMode = FitToRadioButton.IsChecked == true
                ? PageSetupScalingMode.FitToPages
                : PageSetupScalingMode.AdjustToPercent,
            ScalePercentText = ScalePercentBox.Text,
            FitToWideText = FitPagesWideBox.Text,
            FitToTallText = FitPagesTallBox.Text,
            FirstPageNumberText = FirstPageNumberBox.Text,
            PrintQualityDpiText = PrintQualityBox.Text,
            PrintAreaText = PrintAreaBox.Text,
            RepeatRowsText = RowsRepeatBox.Text,
            RepeatColumnsText = ColumnsRepeatBox.Text,
            PrintGridlines = PrintGridlinesBox.IsChecked == true,
            PrintHeadings = PrintHeadingsBox.IsChecked == true,
            PrintBlackAndWhite = PrintBlackAndWhiteBox.IsChecked == true,
            PrintDraftQuality = PrintDraftQualityBox.IsChecked == true,
            PrintErrorValue = SelectedPrintErrorValue(),
            PrintComments = SelectedPrintComments(),
            PageOrder = SelectedPageOrder(),
            Header = Header,
            Footer = Footer,
            FirstPageHeader = FirstPageHeader,
            FirstPageFooter = FirstPageFooter,
            EvenPageHeader = EvenPageHeader,
            EvenPageFooter = EvenPageFooter,
            HeaderPictures = HeaderPictures.DeepClone(),
            FooterPictures = FooterPictures.DeepClone(),
            FirstPageHeaderPictures = FirstPageHeaderPictures.DeepClone(),
            FirstPageFooterPictures = FirstPageFooterPictures.DeepClone(),
            EvenPageHeaderPictures = EvenPageHeaderPictures.DeepClone(),
            EvenPageFooterPictures = EvenPageFooterPictures.DeepClone(),
            DifferentFirstPage = DifferentFirstPageBox.IsChecked == true,
            DifferentOddEvenPages = DifferentOddEvenBox.IsChecked == true,
            ScaleHeaderFooterWithDocument = ScaleWithDocumentBox.IsChecked == true,
            AlignHeaderFooterWithMargins = AlignWithMarginsBox.IsChecked == true
        };
    }

    private void FocusValidationTarget(PageSetupValidationTarget? target)
    {
        var route = PageSetupDialogModel.GetValidationRoute(target);
        switch (route.Field)
        {
            case PageSetupDialogField.Margins:
                FocusInvalidMarginInput();
                break;
            case PageSetupDialogField.HeaderMargin:
            case PageSetupDialogField.FooterMargin:
                FocusInvalidHeaderFooterMargin();
                break;
            case PageSetupDialogField.Scaling:
                FocusInvalidScalingInput();
                break;
            case PageSetupDialogField.FirstPageNumber:
                FocusInvalidPageTabNumber(FirstPageNumberBox);
                break;
            case PageSetupDialogField.PrintQuality:
                FocusInvalidPageTabNumber(PrintQualityBox);
                break;
            case PageSetupDialogField.PrintArea:
                FocusInvalidPrintArea();
                break;
            case PageSetupDialogField.RepeatRows:
            case PageSetupDialogField.RepeatColumns:
                FocusInvalidPrintTitles();
                break;
            case PageSetupDialogField.PaperSize:
                PageSetupTabs.SelectedItem = PageTab;
                PaperSizeBox.Focus();
                break;
            case PageSetupDialogField.PageOrder:
                PageSetupTabs.SelectedItem = SheetTab;
                PageOrderBox.Focus();
                break;
            case PageSetupDialogField.PrintErrorValue:
                PageSetupTabs.SelectedItem = SheetTab;
                PrintErrorValueBox.Focus();
                break;
            case PageSetupDialogField.PrintComments:
                PageSetupTabs.SelectedItem = SheetTab;
                PrintCommentsBox.Focus();
                break;
            default:
                PageSetupTabs.SelectedItem = PageTab;
                OrientationBox.Focus();
                break;
        }
    }

    private WorksheetPageOrientation SelectedOrientation() =>
        PageSetupDialogPlanner.OrientationChoices.ValueAt(OrientationBox.SelectedIndex);

    private WorksheetPaperSize SelectedPaperSize() =>
        PageSetupDialogPlanner.PaperSizeChoices.ValueAt(PaperSizeBox.SelectedIndex);

    private WorksheetPageOrder SelectedPageOrder() =>
        PageSetupDialogPlanner.PageOrderChoices.ValueAt(PageOrderBox.SelectedIndex);

    private WorksheetPrintErrorValue SelectedPrintErrorValue() =>
        PageSetupDialogPlanner.PrintErrorValueChoices.ValueAt(PrintErrorValueBox.SelectedIndex);

    private WorksheetPrintComments SelectedPrintComments() =>
        PageSetupDialogPlanner.PrintCommentChoices.ValueAt(PrintCommentsBox.SelectedIndex);
}
