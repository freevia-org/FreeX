using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Unit tests for the non-UI glue backing the Avalonia Page Setup dialog: mapping the sheet's
/// page-setup model into dialog fields, resolving the adjust-to / fit-to scaling choice, parsing the
/// free-text margin/print-area/print-title inputs, and building the persisted command. No running UI.
/// </summary>
public sealed class PageSetupDialogModelTests
{
    private static Sheet CreateSheet()
    {
        var workbook = new Workbook("Book");
        return workbook.AddSheet("Sheet1");
    }

    [Fact]
    public void FromSheet_MapsOrientationPaperSizeAndMargins()
    {
        var sheet = CreateSheet();
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Legal;
        sheet.PageMargins = new WorksheetPageMargins(0.75, 0.8, 1.0, 1.1);

        var fields = PageSetupDialogModel.FromSheet(sheet);

        fields.Orientation.Should().Be(WorksheetPageOrientation.Landscape);
        fields.PaperSize.Should().Be(WorksheetPaperSize.Legal);
        fields.MarginsText.Should().Be("0.75, 0.8, 1, 1.1");
    }

    [Fact]
    public void FromSheet_PercentScaleSelectsAdjustToMode()
    {
        var sheet = CreateSheet();
        sheet.ScaleToFit = new WorksheetScaleToFit(85, null, null);

        var fields = PageSetupDialogModel.FromSheet(sheet);

        fields.ScalingMode.Should().Be(PageSetupScalingMode.AdjustToPercent);
        fields.ScalePercentText.Should().Be("85");
    }

    [Fact]
    public void FromSheet_FitToPagesSelectsFitMode()
    {
        var sheet = CreateSheet();
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 2, 3);

        var fields = PageSetupDialogModel.FromSheet(sheet);

        fields.ScalingMode.Should().Be(PageSetupScalingMode.FitToPages);
        fields.FitToWideText.Should().Be("2");
        fields.FitToTallText.Should().Be("3");
    }

    [Fact]
    public void FromSheet_RoundTripsPrintAreaAndPrintTitles()
    {
        var sheet = CreateSheet();
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20, 4));
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 2);

        var fields = PageSetupDialogModel.FromSheet(sheet);

        fields.PrintAreaText.Should().Be("A1:D20");
        fields.RepeatRowsText.Should().Be("1:2");
        fields.RepeatColumnsText.Should().Be("A:B");
    }

    [Fact]
    public void TryResolveScaleToFit_AdjustToPercentProducesExplicitPercent()
    {
        var fields = new PageSetupDialogFields
        {
            ScalingMode = PageSetupScalingMode.AdjustToPercent,
            ScalePercentText = "120",
        };

        PageSetupDialogModel.TryResolveScaleToFit(fields, out var scale, out var error).Should().BeTrue();
        error.Should().BeNull();
        scale.Should().Be(new WorksheetScaleToFit(120, null, null));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("500")]
    [InlineData("abc")]
    public void TryResolveScaleToFit_RejectsOutOfRangePercent(string percentText)
    {
        var fields = new PageSetupDialogFields
        {
            ScalingMode = PageSetupScalingMode.AdjustToPercent,
            ScalePercentText = percentText,
        };

        PageSetupDialogModel.TryResolveScaleToFit(fields, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryResolveScaleToFit_FitToBlankAxisMapsToNull()
    {
        var fields = new PageSetupDialogFields
        {
            ScalingMode = PageSetupScalingMode.FitToPages,
            FitToWideText = "1",
            FitToTallText = "",
        };

        PageSetupDialogModel.TryResolveScaleToFit(fields, out var scale, out var error).Should().BeTrue();
        error.Should().BeNull();
        scale.Should().Be(new WorksheetScaleToFit(null, 1, null));
    }

    [Fact]
    public void TryResolveScaleToFit_FitToBothBlankIsRejected()
    {
        var fields = new PageSetupDialogFields
        {
            ScalingMode = PageSetupScalingMode.FitToPages,
            FitToWideText = "",
            FitToTallText = "auto",
        };

        PageSetupDialogModel.TryResolveScaleToFit(fields, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryBuildCommand_BuildsCommandFromValidFields()
    {
        var sheet = CreateSheet();
        var fields = new PageSetupDialogFields
        {
            Orientation = WorksheetPageOrientation.Landscape,
            PaperSize = WorksheetPaperSize.Letter,
            MarginsText = "0.5, 0.5, 0.7, 0.7",
            ScalingMode = PageSetupScalingMode.FitToPages,
            FitToWideText = "1",
            FitToTallText = "2",
            PrintAreaText = "A1:C10",
            RepeatRowsText = "1",
            RepeatColumnsText = "A",
            PrintGridlines = true,
            PrintHeadings = true,
            PageOrder = WorksheetPageOrder.OverThenDown,
        };

        var result = PageSetupDialogModel.TryBuildCommand(sheet, fields);

        result.Success.Should().BeTrue();
        result.Command.Should().NotBeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void TryBuildCommand_InvalidMarginsReportsError()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { MarginsText = "1, 2, 3" };

        var result = PageSetupDialogModel.TryBuildCommand(sheet, fields);

        result.Success.Should().BeFalse();
        result.Command.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryBuildCommand_InvalidPrintTitleReportsError()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { RepeatRowsText = "abc" };

        var result = PageSetupDialogModel.TryBuildCommand(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryBuildCommand_ProducesUndoableLabeledCommand()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with
        {
            Orientation = WorksheetPageOrientation.Landscape,
            ScalingMode = PageSetupScalingMode.AdjustToPercent,
            ScalePercentText = "75",
        };

        var result = PageSetupDialogModel.TryBuildCommand(sheet, fields);

        result.Success.Should().BeTrue();
        result.Command!.Label.Should().Be("Page Setup");
    }

    [Fact]
    public void TryParsePrintArea_BlankClearsArea()
    {
        var sheet = CreateSheet();

        PageSetupDialogModel.TryParsePrintArea("", sheet.Id, out var printArea).Should().BeTrue();
        printArea.Should().BeNull();
    }

    [Fact]
    public void TryParsePrintArea_ParsesRange()
    {
        var sheet = CreateSheet();

        PageSetupDialogModel.TryParsePrintArea("B2:E9", sheet.Id, out var printArea).Should().BeTrue();
        printArea.Should().NotBeNull();
        printArea!.Value.Start.Col.Should().Be(2u);
        printArea.Value.End.Row.Should().Be(9u);
    }

    [Fact]
    public void FromSheet_MapsAdvancedFields()
    {
        var sheet = CreateSheet();
        sheet.HeaderMargin = 0.4;
        sheet.FooterMargin = 0.45;
        sheet.CenterHorizontallyOnPage = true;
        sheet.CenterVerticallyOnPage = true;
        sheet.FirstPageNumber = 7;
        sheet.PrintQualityDpi = 600;
        sheet.PrintBlackAndWhite = true;
        sheet.PrintDraftQuality = true;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Dash;
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.PageHeader = new WorksheetHeaderFooter("L", "&[Page]", "R");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.HeaderFooterScaleWithDocument = false;

        var fields = PageSetupDialogModel.FromSheet(sheet);

        fields.HeaderMarginText.Should().Be("0.4");
        fields.FooterMarginText.Should().Be("0.45");
        fields.CenterHorizontally.Should().BeTrue();
        fields.CenterVertically.Should().BeTrue();
        fields.FirstPageNumberText.Should().Be("7");
        fields.PrintQualityDpiText.Should().Be("600");
        fields.PrintBlackAndWhite.Should().BeTrue();
        fields.PrintDraftQuality.Should().BeTrue();
        fields.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Dash);
        fields.PrintComments.Should().Be(WorksheetPrintComments.AtEnd);
        fields.Header.Center.Should().Be("&[Page]");
        fields.DifferentFirstPage.Should().BeTrue();
        fields.ScaleHeaderFooterWithDocument.Should().BeFalse();
    }

    [Fact]
    public void BuildCommand_AppliedAndRevertedRoundTripsAdvancedFields()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new PageSetupTestCommandContext(workbook);

        var fields = PageSetupDialogModel.FromSheet(sheet) with
        {
            HeaderMarginText = "0.6",
            FooterMarginText = "0.7",
            CenterHorizontally = true,
            CenterVertically = true,
            FirstPageNumberText = "5",
            PrintQualityDpiText = "300",
            PrintBlackAndWhite = true,
            PrintDraftQuality = true,
            PrintErrorValue = WorksheetPrintErrorValue.NotAvailable,
            PrintComments = WorksheetPrintComments.AsDisplayed,
        };

        var build = PageSetupDialogModel.TryBuildCommand(sheet, fields);
        build.Success.Should().BeTrue();

        build.Command!.Apply(ctx).Success.Should().BeTrue();
        sheet.HeaderMargin.Should().Be(0.6);
        sheet.FooterMargin.Should().Be(0.7);
        sheet.CenterHorizontallyOnPage.Should().BeTrue();
        sheet.CenterVerticallyOnPage.Should().BeTrue();
        sheet.FirstPageNumber.Should().Be(5);
        sheet.PrintQualityDpi.Should().Be(300);
        sheet.PrintBlackAndWhite.Should().BeTrue();
        sheet.PrintDraftQuality.Should().BeTrue();
        sheet.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.NotAvailable);
        sheet.PrintComments.Should().Be(WorksheetPrintComments.AsDisplayed);

        build.Command.Revert(ctx);
        sheet.CenterHorizontallyOnPage.Should().BeFalse();
        sheet.PrintBlackAndWhite.Should().BeFalse();
        sheet.FirstPageNumber.Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("x")]
    public void TryBuildCommand_RejectsInvalidFirstPageNumber(string text)
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { FirstPageNumberText = text };

        var result = PageSetupDialogModel.TryBuildCommand(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-50")]
    [InlineData("abc")]
    public void TryBuildCommand_RejectsInvalidPrintQuality(string text)
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { PrintQualityDpiText = text };

        var result = PageSetupDialogModel.TryBuildCommand(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryBuildCommand_RejectsNegativeHeaderMargin()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { HeaderMarginText = "-1" };

        var result = PageSetupDialogModel.TryBuildCommand(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BuildHeaderFooterCommand_AppliesHeaderFooterText()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new PageSetupTestCommandContext(workbook);

        var fields = PageSetupDialogModel.FromSheet(sheet) with
        {
            Header = new WorksheetHeaderFooter("", "Page &[Page] of &[Pages]", ""),
            Footer = new WorksheetHeaderFooter("&[File]", "", "&[Date]"),
            DifferentOddEvenPages = true,
            AlignHeaderFooterWithMargins = false,
        };

        var command = PageSetupDialogModel.BuildHeaderFooterCommand(sheet, fields);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.PageHeader.Center.Should().Be("Page &[Page] of &[Pages]");
        sheet.PageFooter.Left.Should().Be("&[File]");
        sheet.PageFooter.Right.Should().Be("&[Date]");
        sheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        sheet.HeaderFooterAlignWithMargins.Should().BeFalse();

        command.Revert(ctx);
        sheet.PageHeader.Center.Should().BeEmpty();
        sheet.DifferentOddEvenHeaderFooter.Should().BeFalse();
    }

    private sealed class PageSetupTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
