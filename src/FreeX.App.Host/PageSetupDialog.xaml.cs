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
        Fields = PageSetupDialogPlanner.PlanSurface(sheet).Fields;
        var headerFooter = Fields.HeaderFooter.DeepClone();
        Header = headerFooter.Header;
        Footer = headerFooter.Footer;
        FirstPageHeader = headerFooter.FirstPageHeader;
        FirstPageFooter = headerFooter.FirstPageFooter;
        EvenPageHeader = headerFooter.EvenPageHeader;
        EvenPageFooter = headerFooter.EvenPageFooter;
        HeaderPictures = headerFooter.HeaderPictures;
        FooterPictures = headerFooter.FooterPictures;
        FirstPageHeaderPictures = headerFooter.FirstPageHeaderPictures;
        FirstPageFooterPictures = headerFooter.FirstPageFooterPictures;
        EvenPageHeaderPictures = headerFooter.EvenPageHeaderPictures;
        EvenPageFooterPictures = headerFooter.EvenPageFooterPictures;
        DifferentFirstPage = headerFooter.DifferentFirstPage;
        DifferentOddEvenPages = headerFooter.DifferentOddEvenPages;
        ScaleHeaderFooterWithDocument = headerFooter.ScaleWithDocument;
        AlignHeaderFooterWithMargins = headerFooter.AlignWithMargins;
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
        var margins = new PageSetupMarginTextFields(
            LeftMarginBox.Text,
            RightMarginBox.Text,
            TopMarginBox.Text,
            BottomMarginBox.Text);

        return PageSetupDialogPlanner.BuildFields(Fields, new PageSetupDialogSurfaceInput
        {
            OrientationIndex = OrientationBox.SelectedIndex,
            PaperSizeIndex = PaperSizeBox.SelectedIndex,
            MarginsText = PageSetupDialogPlanner.BuildMarginsText(margins),
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
            PrintErrorValueIndex = PrintErrorValueBox.SelectedIndex,
            PrintCommentsIndex = PrintCommentsBox.SelectedIndex,
            PageOrderIndex = PageOrderBox.SelectedIndex,
            HeaderFooter = new HeaderFooterEditorState(
                Header,
                Footer,
                FirstPageHeader,
                FirstPageFooter,
                EvenPageHeader,
                EvenPageFooter,
                HeaderPictures.DeepClone(),
                FooterPictures.DeepClone(),
                FirstPageHeaderPictures.DeepClone(),
                FirstPageFooterPictures.DeepClone(),
                EvenPageHeaderPictures.DeepClone(),
                EvenPageFooterPictures.DeepClone(),
                DifferentFirstPageBox.IsChecked == true,
                DifferentOddEvenBox.IsChecked == true,
                ScaleWithDocumentBox.IsChecked == true,
                AlignWithMarginsBox.IsChecked == true)
        });
    }

    private void FocusValidationTarget(PageSetupValidationTarget? target)
    {
        FocusDialogTarget(
            PageSetupDialogPlanner.PlanValidationFocus(
                target,
                new PageSetupDialogValidationFocusState
                {
                    HasSeparateMarginFields = true,
                    LeftMarginText = LeftMarginBox.Text,
                    RightMarginText = RightMarginBox.Text,
                    TopMarginText = TopMarginBox.Text,
                    BottomMarginText = BottomMarginBox.Text,
                    HeaderMarginText = HeaderMarginBox.Text,
                    FooterMarginText = FooterMarginBox.Text,
                    ScalingMode = FitToRadioButton.IsChecked == true
                        ? PageSetupScalingMode.FitToPages
                        : PageSetupScalingMode.AdjustToPercent,
                    FitToWideText = FitPagesWideBox.Text,
                    RepeatRowsText = RowsRepeatBox.Text
                }));
    }
}
