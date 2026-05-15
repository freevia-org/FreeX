using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Model.Tests;

public sealed class PageLayoutCommandTests
{
    [Fact]
    public void SetPrintAreaCommand_SetsPrintAreaAndUndoRestoresPreviousArea()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var previous = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        var next = new GridRange(
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 4, 4));
        sheet.PrintArea = previous;

        var command = new SetPrintAreaCommand(sheet.Id, next);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintArea.Should().Be(next);

        command.Revert(ctx);

        sheet.PrintArea.Should().Be(previous);
    }

    [Fact]
    public void ClearPrintAreaCommand_ClearsPrintAreaAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var previous = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        sheet.PrintArea = previous;

        var command = new ClearPrintAreaCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintArea.Should().BeNull();

        command.Revert(ctx);

        sheet.PrintArea.Should().Be(previous);
    }

    [Fact]
    public void SetPageOrientationCommand_SetsOrientationAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;

        var command = new SetPageOrientationCommand(sheet.Id, WorksheetPageOrientation.Portrait);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Portrait);

        command.Revert(ctx);

        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
    }

    [Fact]
    public void SetPageOrientationCommand_RejectsInvalidOrientation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;

        var outcome = new SetPageOrientationCommand(sheet.Id, (WorksheetPageOrientation)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Portrait);
    }

    [Fact]
    public void SetPaperSizeCommand_SetsPaperSizeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PaperSize = WorksheetPaperSize.A4;

        var command = new SetPaperSizeCommand(sheet.Id, WorksheetPaperSize.Legal);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PaperSize.Should().Be(WorksheetPaperSize.Legal);

        command.Revert(ctx);

        sheet.PaperSize.Should().Be(WorksheetPaperSize.A4);
    }

    [Fact]
    public void SetPaperSizeCommand_RejectsInvalidPaperSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PaperSize = WorksheetPaperSize.Letter;

        var outcome = new SetPaperSizeCommand(sheet.Id, (WorksheetPaperSize)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.PaperSize.Should().Be(WorksheetPaperSize.Letter);
    }

    [Fact]
    public void SetPageMarginsCommand_SetsMarginsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageMargins = new WorksheetPageMargins(1, 1, 1, 1);
        var narrow = new WorksheetPageMargins(0.5, 0.5, 0.5, 0.5);

        var command = new SetPageMarginsCommand(sheet.Id, narrow);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PageMargins.Should().Be(narrow);

        command.Revert(ctx);

        sheet.PageMargins.Should().Be(new WorksheetPageMargins(1, 1, 1, 1));
    }

    [Fact]
    public void WorksheetPageLayout_GetPageSizeInches_AppliesLandscapeOrientation()
    {
        var size = WorksheetPageLayout.GetPageSizeInches(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Landscape);

        size.Width.Should().Be(11.0);
        size.Height.Should().Be(8.5);
    }

    [Fact]
    public void WorksheetPageLayout_GetMarginGuideFractions_ConvertsMarginsToPageFractions()
    {
        var guide = WorksheetPageLayout.GetMarginGuideFractions(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            new WorksheetPageMargins(1.0, 0.5, 2.0, 1.0));

        guide.Left.Should().BeApproximately(1.0 / 8.5, 0.0001);
        guide.Right.Should().BeApproximately(1.0 - (0.5 / 8.5), 0.0001);
        guide.Top.Should().BeApproximately(2.0 / 11.0, 0.0001);
        guide.Bottom.Should().BeApproximately(1.0 - (1.0 / 11.0), 0.0001);
    }

    [Fact]
    public void WorksheetPageLayout_GetMarginsFromGuideFraction_ConvertsDraggedGuidesToMargins()
    {
        var margins = new WorksheetPageMargins(1, 1, 1, 1);

        var left = WorksheetPageLayout.GetMarginsFromGuideFraction(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            WorksheetPageMarginEdge.Left,
            2.0 / 8.5);
        var right = WorksheetPageLayout.GetMarginsFromGuideFraction(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            WorksheetPageMarginEdge.Right,
            7.0 / 8.5);
        var top = WorksheetPageLayout.GetMarginsFromGuideFraction(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            WorksheetPageMarginEdge.Top,
            1.5 / 11.0);
        var bottom = WorksheetPageLayout.GetMarginsFromGuideFraction(
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            margins,
            WorksheetPageMarginEdge.Bottom,
            9.5 / 11.0);

        left.Left.Should().BeApproximately(2.0, 0.0001);
        right.Right.Should().BeApproximately(1.5, 0.0001);
        top.Top.Should().BeApproximately(1.5, 0.0001);
        bottom.Bottom.Should().BeApproximately(1.5, 0.0001);
    }

    [Fact]
    public void WorksheetPageLayout_GetDisplayedCommentOverlays_ReturnsOnlyCommentsOnPrintedPage()
    {
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var c2 = new CellAddress(sheetId, 2, 3);
        var outside = new CellAddress(sheetId, 9, 9);
        var comments = new Dictionary<CellAddress, string>
        {
            [outside] = "outside",
            [c2] = "review total",
            [a1] = "check header"
        };

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            pageRows: [1, 2],
            pageColumns: [1, 3]);

        overlays.Should().Equal(
            new WorksheetDisplayedComment(a1, "check header", 0, 0),
            new WorksheetDisplayedComment(c2, "review total", 1, 1));
    }

    [Fact]
    public void PageMarginInputParser_ParsesFourCommaSeparatedInchValues()
    {
        PageMarginInputParser.TryParse("0.7, 0.8, 0.9, 1.1", out var margins, out var error)
            .Should().BeTrue();

        margins.Should().Be(new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1));
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("0.5,0.5,0.5")]
    [InlineData("0.5,-0.5,0.5,0.5")]
    [InlineData("0.5,nope,0.5,0.5")]
    public void PageMarginInputParser_RejectsInvalidCustomMarginInput(string input)
    {
        PageMarginInputParser.TryParse(input, out _, out var error).Should().BeFalse();

        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SetPrintOptionsCommand_SetsOptionsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PrintGridlines = false;
        sheet.PrintHeadings = true;

        var command = new SetPrintOptionsCommand(sheet.Id, printGridlines: true, printHeadings: false);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.PrintHeadings.Should().BeFalse();

        command.Revert(ctx);

        sheet.PrintGridlines.Should().BeFalse();
        sheet.PrintHeadings.Should().BeTrue();
    }

    [Fact]
    public void SetScaleToFitCommand_SetsScaleAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.ScaleToFit = new WorksheetScaleToFit(ScalePercent: 100, FitToPagesWide: null, FitToPagesTall: null);
        var next = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: 1);

        var command = new SetScaleToFitCommand(sheet.Id, next);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ScaleToFit.Should().Be(next);

        command.Revert(ctx);

        sheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(ScalePercent: 100, FitToPagesWide: null, FitToPagesTall: null));
    }

    [Fact]
    public void SetPrintTitlesCommand_SetsRowsAndColumnsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);

        var command = new SetPrintTitlesCommand(
            sheet.Id,
            rows: new WorksheetRepeatRange(2, 3),
            columns: new WorksheetRepeatRange(1, 2));

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(2, 3));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 2));

        command.Revert(ctx);

        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 1));
        sheet.PrintTitleColumns.Should().BeNull();
    }

    [Fact]
    public void SetPageBreaksCommand_ReplacesManualBreaksAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.RowPageBreaks.Add(10);

        var command = new SetPageBreaksCommand(sheet.Id, rowBreaks: [20, 30], columnBreaks: [4]);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.RowPageBreaks.Should().Equal(20u, 30u);
        sheet.ColumnPageBreaks.Should().Equal(4u);

        command.Revert(ctx);

        sheet.RowPageBreaks.Should().Equal(10u);
        sheet.ColumnPageBreaks.Should().BeEmpty();
    }

    [Fact]
    public void SetWorksheetBackgroundCommand_SetsBackgroundAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.BackgroundImage = new WorksheetBackgroundImage([1, 2, 3], "image/png", "old.png");
        var next = new WorksheetBackgroundImage([9, 8, 7], "image/jpeg", "new.jpg");

        var command = new SetWorksheetBackgroundCommand(sheet.Id, next);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.BackgroundImage.Should().Be(next);

        command.Revert(ctx);

        sheet.BackgroundImage.Should().NotBeNull();
        sheet.BackgroundImage!.ImageBytes.Should().Equal(1, 2, 3);
        sheet.BackgroundImage.ContentType.Should().Be("image/png");
        sheet.BackgroundImage.FileName.Should().Be("old.png");
    }

    [Fact]
    public void ClearWorksheetBackgroundCommand_ClearsBackgroundAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.BackgroundImage = new WorksheetBackgroundImage([1, 2, 3], "image/png", "background.png");

        var command = new ClearWorksheetBackgroundCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.BackgroundImage.Should().BeNull();

        command.Revert(ctx);

        sheet.BackgroundImage.Should().NotBeNull();
        sheet.BackgroundImage!.FileName.Should().Be("background.png");
    }

    [Fact]
    public void SetPageSetupCommand_AppliesDialogSettingsAsOneUndoableOperation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PaperSize = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.HeaderMargin = 0.3;
        sheet.FooterMargin = 0.3;
        sheet.PrintGridlines = false;
        sheet.PrintHeadings = false;
        sheet.ScaleToFit = WorksheetScaleToFit.Default;
        sheet.CenterHorizontallyOnPage = false;
        sheet.CenterVerticallyOnPage = false;
        sheet.PageOrder = WorksheetPageOrder.DownThenOver;
        sheet.FirstPageNumber = null;
        sheet.PrintBlackAndWhite = false;
        sheet.PrintDraftQuality = false;
        sheet.PrintQualityDpi = null;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Displayed;
        sheet.PrintComments = WorksheetPrintComments.None;

        var command = new SetPageSetupCommand(
            sheet.Id,
            WorksheetPageOrientation.Landscape,
            WorksheetPaperSize.Legal,
            WorksheetPageMargins.Wide,
            printGridlines: true,
            printHeadings: true,
            new WorksheetScaleToFit(null, 1, 2),
            new WorksheetRepeatRange(1, 2),
            new WorksheetRepeatRange(1, 1),
            centerHorizontally: true,
            centerVertically: true,
            pageOrder: WorksheetPageOrder.OverThenDown,
            firstPageNumber: 5,
            headerMargin: 0.4,
            footerMargin: 0.6,
            printBlackAndWhite: true,
            printDraftQuality: true,
            printQualityDpi: 600,
            printErrorValue: WorksheetPrintErrorValue.Blank,
            printComments: WorksheetPrintComments.AtEnd);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        sheet.PaperSize.Should().Be(WorksheetPaperSize.Legal);
        sheet.PageMargins.Should().Be(WorksheetPageMargins.Wide);
        sheet.HeaderMargin.Should().Be(0.4);
        sheet.FooterMargin.Should().Be(0.6);
        sheet.PrintGridlines.Should().BeTrue();
        sheet.PrintHeadings.Should().BeTrue();
        sheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 1, 2));
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 1));
        sheet.CenterHorizontallyOnPage.Should().BeTrue();
        sheet.CenterVerticallyOnPage.Should().BeTrue();
        sheet.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        sheet.FirstPageNumber.Should().Be(5);
        sheet.PrintBlackAndWhite.Should().BeTrue();
        sheet.PrintDraftQuality.Should().BeTrue();
        sheet.PrintQualityDpi.Should().Be(600);
        sheet.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Blank);
        sheet.PrintComments.Should().Be(WorksheetPrintComments.AtEnd);

        command.Revert(ctx);

        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Portrait);
        sheet.PaperSize.Should().Be(WorksheetPaperSize.A4);
        sheet.PageMargins.Should().Be(WorksheetPageMargins.Narrow);
        sheet.HeaderMargin.Should().Be(0.3);
        sheet.FooterMargin.Should().Be(0.3);
        sheet.PrintGridlines.Should().BeFalse();
        sheet.PrintHeadings.Should().BeFalse();
        sheet.ScaleToFit.Should().Be(WorksheetScaleToFit.Default);
        sheet.PrintTitleRows.Should().BeNull();
        sheet.PrintTitleColumns.Should().BeNull();
        sheet.CenterHorizontallyOnPage.Should().BeFalse();
        sheet.CenterVerticallyOnPage.Should().BeFalse();
        sheet.PageOrder.Should().Be(WorksheetPageOrder.DownThenOver);
        sheet.FirstPageNumber.Should().BeNull();
        sheet.PrintBlackAndWhite.Should().BeFalse();
        sheet.PrintDraftQuality.Should().BeFalse();
        sheet.PrintQualityDpi.Should().BeNull();
        sheet.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Displayed);
        sheet.PrintComments.Should().Be(WorksheetPrintComments.None);
    }

    [Fact]
    public void SetPageSetupCommand_RejectsInvalidChoiceValues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrder = WorksheetPageOrder.DownThenOver;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Displayed;
        sheet.PrintComments = WorksheetPrintComments.None;

        var command = new SetPageSetupCommand(
            sheet.Id,
            (WorksheetPageOrientation)99,
            (WorksheetPaperSize)99,
            WorksheetPageMargins.Normal,
            printGridlines: false,
            printHeadings: false,
            WorksheetScaleToFit.Default,
            printTitleRows: null,
            printTitleColumns: null,
            pageOrder: (WorksheetPageOrder)99,
            printErrorValue: (WorksheetPrintErrorValue)99,
            printComments: (WorksheetPrintComments)99);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Portrait);
        sheet.PaperSize.Should().Be(WorksheetPaperSize.Letter);
        sheet.PageOrder.Should().Be(WorksheetPageOrder.DownThenOver);
        sheet.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Displayed);
        sheet.PrintComments.Should().Be(WorksheetPrintComments.None);
    }

    [Fact]
    public void SetHeaderFooterCommand_SetsHeaderFooterAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageHeader = new WorksheetHeaderFooter("Old left", "", "Old right");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &[Page]", "");
        sheet.FirstPageHeader = new WorksheetHeaderFooter("Old first", "", "");
        sheet.FirstPageFooter = new WorksheetHeaderFooter("", "Old first footer", "");
        sheet.EvenPageHeader = new WorksheetHeaderFooter("Old even", "", "");
        sheet.EvenPageFooter = new WorksheetHeaderFooter("", "Old even footer", "");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.DifferentOddEvenHeaderFooter = true;
        sheet.HeaderFooterScaleWithDocument = false;
        sheet.HeaderFooterAlignWithMargins = false;

        var command = new SetHeaderFooterCommand(
            sheet.Id,
            new WorksheetHeaderFooter("Left", "Center", "Right"),
            new WorksheetHeaderFooter("Footer left", "Footer center", "Footer right"),
            firstPageHeader: new WorksheetHeaderFooter("First left", "First center", "First right"),
            firstPageFooter: new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right"),
            evenPageHeader: new WorksheetHeaderFooter("Even left", "Even center", "Even right"),
            evenPageFooter: new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right"),
            differentFirstPage: true,
            differentOddEvenPages: true,
            scaleWithDocument: true,
            alignWithMargins: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Left", "Center", "Right"));
        sheet.PageFooter.Should().Be(new WorksheetHeaderFooter("Footer left", "Footer center", "Footer right"));
        sheet.FirstPageHeader.Should().Be(new WorksheetHeaderFooter("First left", "First center", "First right"));
        sheet.FirstPageFooter.Should().Be(new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right"));
        sheet.EvenPageHeader.Should().Be(new WorksheetHeaderFooter("Even left", "Even center", "Even right"));
        sheet.EvenPageFooter.Should().Be(new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right"));
        sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        sheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        sheet.HeaderFooterScaleWithDocument.Should().BeTrue();
        sheet.HeaderFooterAlignWithMargins.Should().BeTrue();

        command.Revert(ctx);

        sheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Old left", "", "Old right"));
        sheet.PageFooter.Should().Be(new WorksheetHeaderFooter("", "Page &[Page]", ""));
        sheet.FirstPageHeader.Should().Be(new WorksheetHeaderFooter("Old first", "", ""));
        sheet.FirstPageFooter.Should().Be(new WorksheetHeaderFooter("", "Old first footer", ""));
        sheet.EvenPageHeader.Should().Be(new WorksheetHeaderFooter("Old even", "", ""));
        sheet.EvenPageFooter.Should().Be(new WorksheetHeaderFooter("", "Old even footer", ""));
        sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        sheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        sheet.HeaderFooterScaleWithDocument.Should().BeFalse();
        sheet.HeaderFooterAlignWithMargins.Should().BeFalse();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
