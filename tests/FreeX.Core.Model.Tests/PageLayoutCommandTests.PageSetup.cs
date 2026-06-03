using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed partial class PageLayoutCommandTests
{
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
}
