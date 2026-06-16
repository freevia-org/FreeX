using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

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
}
