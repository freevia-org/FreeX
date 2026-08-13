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
    public void ChoiceLists_DefineRendererNeutralPageSetupComboOrder()
    {
        PageSetupDialogModel.OrientationChoices.Select(choice => choice.Value)
            .Should()
            .Equal(WorksheetPageOrientation.Portrait, WorksheetPageOrientation.Landscape);
        PageSetupDialogModel.PaperSizeChoices.Select(choice => choice.Value)
            .Should()
            .Equal(
                WorksheetPaperSize.Letter,
                WorksheetPaperSize.A4,
                WorksheetPaperSize.Legal,
                WorksheetPaperSize.Tabloid,
                WorksheetPaperSize.Executive,
                WorksheetPaperSize.A3,
                WorksheetPaperSize.A5,
                WorksheetPaperSize.B4,
                WorksheetPaperSize.B5);
        PageSetupDialogModel.PaperSizes.Should().Equal(
            PageSetupDialogModel.PaperSizeChoices.Select(choice => choice.Value));
        PageSetupDialogModel.PageOrderChoices.Select(choice => choice.Value)
            .Should()
            .Equal(WorksheetPageOrder.DownThenOver, WorksheetPageOrder.OverThenDown);
        PageSetupDialogModel.PrintErrorValueChoices.Select(choice => choice.Value)
            .Should()
            .Equal(
                WorksheetPrintErrorValue.Displayed,
                WorksheetPrintErrorValue.Blank,
                WorksheetPrintErrorValue.Dash,
                WorksheetPrintErrorValue.NotAvailable);
        PageSetupDialogModel.PrintCommentChoices.Select(choice => choice.Value)
            .Should()
            .Equal(WorksheetPrintComments.None, WorksheetPrintComments.AtEnd, WorksheetPrintComments.AsDisplayed);
        PageSetupDialogModel.PrintErrorValueChoices
            .Should()
            .OnlyContain(choice => !string.IsNullOrWhiteSpace(choice.LabelResourceKey));
    }

    [Fact]
    public void HeaderFooterPresetChoices_DefineRendererNeutralPresetCatalogs()
    {
        PageSetupDialogModel.HeaderPresetChoices.Select(choice => choice.LabelResourceKey)
            .Should()
            .ContainInOrder(
                "PageSetup_None",
                "PageSetup_Page1",
                "PageSetup_Page1Of",
                "PageSetup_Sheet1",
                "PageSetup_Book1",
                "PageSetup_Book1Xlsx",
                "PageSetup_Book1XlsxSheet1",
                "PageSetup_ConfidentialPage1",
                "PageSetup_DatePage1",
                "PageSetup_SheetName",
                "PageSetup_FileName",
                "PageSetup_FilePath");
        PageSetupDialogModel.FooterPresetChoices.Select(choice => choice.LabelResourceKey)
            .Should()
            .ContainInOrder(
                "PageSetup_None",
                "PageSetup_Page1",
                "PageSetup_Page1Of",
                "PageSetup_Sheet1",
                "PageSetup_Book1",
                "PageSetup_Book1Xlsx",
                "PageSetup_Book1XlsxSheet1",
                "PageSetup_Date",
                "PageSetup_Time",
                "PageSetup_DatePage1",
                "PageSetup_FilePath",
                "PageSetup_FileName");
        PageSetupDialogModel.HeaderFooterPresetChoices.Select(choice => choice.Value)
            .Should()
            .Equal(PageSetupDialogModel.HeaderFooterPresets);
    }

    [Fact]
    public void ChoiceHelpers_FallbackUnknownValuesAndIndexes()
    {
        PageSetupDialogModel.ChoiceIndex(
                PageSetupDialogModel.PrintErrorValueChoices,
                WorksheetPrintErrorValue.Dash,
                WorksheetPrintErrorValue.Displayed)
            .Should()
            .Be(2);
        PageSetupDialogModel.ChoiceIndex(
                PageSetupDialogModel.PrintErrorValueChoices,
                (WorksheetPrintErrorValue)999,
                WorksheetPrintErrorValue.Displayed)
            .Should()
            .Be(0);
        PageSetupDialogModel.ChoiceValue(
                PageSetupDialogModel.PrintCommentChoices,
                selectedIndex: 1,
                WorksheetPrintComments.None)
            .Should()
            .Be(WorksheetPrintComments.AtEnd);
        PageSetupDialogModel.ChoiceValue(
                PageSetupDialogModel.PrintCommentChoices,
                selectedIndex: 99,
                WorksheetPrintComments.None)
            .Should()
            .Be(WorksheetPrintComments.None);
        PageSetupDialogModel.ChoiceValue(
                Array.Empty<PageSetupChoice<WorksheetPageOrder>>(),
                selectedIndex: 0,
                WorksheetPageOrder.DownThenOver)
            .Should()
            .Be(WorksheetPageOrder.DownThenOver);
    }

    [Fact]
    public void HeaderFooterPresetHelpers_SelectValuesAndBuildPreviewText()
    {
        PageSetupDialogModel.HeaderFooterPresetIndex(
                PageSetupDialogModel.HeaderPresetChoices,
                "Confidential, Page &[Page]")
            .Should()
            .Be(7);
        PageSetupDialogModel.HeaderFooterPresetValue(
                PageSetupDialogModel.FooterPresetChoices,
                selectedIndex: 8)
            .Should()
            .Be("&[Time]");
        PageSetupDialogModel.HeaderFooterPresetValue(
                PageSetupDialogModel.FooterPresetChoices,
                selectedIndex: 99)
            .Should()
            .BeEmpty();
        PageSetupDialogModel.HeaderFooterPresetExactIndex(
                PageSetupDialogModel.HeaderPresetChoices,
                "Confidential, Page &[Page]")
            .Should()
            .Be(7);
        PageSetupDialogModel.HeaderFooterPresetExactIndex(
                PageSetupDialogModel.HeaderPresetChoices,
                "Custom center text")
            .Should()
            .Be(-1);

        PageSetupDialogModel.BuildHeaderFooterPreview(new WorksheetHeaderFooter("&[File]", "", "&[Date]"), "(none)")
            .Should()
            .Be("&[File] | &[Date]");
        PageSetupDialogModel.BuildHeaderFooterPreview(new WorksheetHeaderFooter("", "", ""), "(none)")
            .Should()
            .Be("(none)");
    }

    [Theory]
    [InlineData(null, PageSetupDialogTab.Page, PageSetupDialogField.Orientation)]
    [InlineData(PageSetupValidationTarget.Orientation, PageSetupDialogTab.Page, PageSetupDialogField.Orientation)]
    [InlineData(PageSetupValidationTarget.PaperSize, PageSetupDialogTab.Page, PageSetupDialogField.PaperSize)]
    [InlineData(PageSetupValidationTarget.Scaling, PageSetupDialogTab.Page, PageSetupDialogField.Scaling)]
    [InlineData(PageSetupValidationTarget.HeaderMargin, PageSetupDialogTab.Margins, PageSetupDialogField.HeaderMargin)]
    [InlineData(PageSetupValidationTarget.FooterMargin, PageSetupDialogTab.Margins, PageSetupDialogField.FooterMargin)]
    [InlineData(PageSetupValidationTarget.RepeatColumns, PageSetupDialogTab.Sheet, PageSetupDialogField.RepeatColumns)]
    [InlineData(PageSetupValidationTarget.PrintErrorValue, PageSetupDialogTab.Sheet, PageSetupDialogField.PrintErrorValue)]
    public void GetValidationRoute_MapsValidationTargetsToNeutralDialogFields(
        PageSetupValidationTarget? target,
        PageSetupDialogTab expectedTab,
        PageSetupDialogField expectedField)
    {
        PageSetupDialogModel.GetValidationRoute(target)
            .Should()
            .Be(new PageSetupValidationRoute(expectedTab, expectedField));
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
    public void TryBuildCommandPlan_BuildsCommandFromValidFields()
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

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Plan!.PageSetupCommand.Should().NotBeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void TryBuildCommandPlan_InvalidMarginsReportsError()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { MarginsText = "1, 2, 3" };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
        result.Target.Should().Be(PageSetupValidationTarget.Margins);
    }

    [Fact]
    public void TryBuildCommandPlan_InvalidPrintTitleReportsError()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { RepeatRowsText = "abc" };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Target.Should().Be(PageSetupValidationTarget.RepeatRows);
    }

    [Fact]
    public void TryBuildCommandPlan_ProducesUndoableLabeledCommand()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with
        {
            Orientation = WorksheetPageOrientation.Landscape,
            ScalingMode = PageSetupScalingMode.AdjustToPercent,
            ScalePercentText = "75",
        };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeTrue();
        result.Plan!.PageSetupCommand.Label.Should().Be("Page Setup");
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
        fields.HeaderFooter.Header.Center.Should().Be("&[Page]");
        fields.HeaderFooter.DifferentFirstPage.Should().BeTrue();
        fields.HeaderFooter.ScaleWithDocument.Should().BeFalse();
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

        var build = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);
        build.Success.Should().BeTrue();

        build.Plan!.PageSetupCommand.Apply(ctx).Success.Should().BeTrue();
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

        build.Plan!.PageSetupCommand.Revert(ctx);
        sheet.CenterHorizontallyOnPage.Should().BeFalse();
        sheet.PrintBlackAndWhite.Should().BeFalse();
        sheet.FirstPageNumber.Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("x")]
    public void TryBuildCommandPlan_RejectsInvalidFirstPageNumber(string text)
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { FirstPageNumberText = text };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Target.Should().Be(PageSetupValidationTarget.FirstPageNumber);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-50")]
    [InlineData("abc")]
    public void TryBuildCommandPlan_RejectsInvalidPrintQuality(string text)
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { PrintQualityDpiText = text };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Target.Should().Be(PageSetupValidationTarget.PrintQuality);
    }

    [Fact]
    public void TryBuildCommandPlan_RejectsNegativeHeaderMargin()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { HeaderMarginText = "-1" };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Target.Should().Be(PageSetupValidationTarget.HeaderMargin);
    }

    [Fact]
    public void TryBuildCommandPlan_InvalidPrintAreaReportsTarget()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { PrintAreaText = "not a range" };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Target.Should().Be(PageSetupValidationTarget.PrintArea);
    }

    [Fact]
    public void TryBuildCommandPlan_InvalidEnumReportsTarget()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with
        {
            PageOrder = (WorksheetPageOrder)999,
        };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Target.Should().Be(PageSetupValidationTarget.PageOrder);
    }

    [Fact]
    public void BuildHeaderFooterCommand_AppliesHeaderFooterText()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new PageSetupTestCommandContext(workbook);

        var initial = PageSetupDialogModel.FromSheet(sheet);
        var fields = initial with
        {
            HeaderFooter = initial.HeaderFooter with
            {
                Header = new WorksheetHeaderFooter("", "Page &[Page] of &[Pages]", ""),
                Footer = new WorksheetHeaderFooter("&[File]", "", "&[Date]"),
                DifferentOddEvenPages = true,
                AlignWithMargins = false,
            }
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

    [Fact]
    public void BuildHeaderFooterCommand_RoundTripsAllScopesAndPictureSets()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new PageSetupTestCommandContext(workbook);
        var initial = PageSetupDialogModel.FromSheet(sheet);
        var fields = initial with
        {
            HeaderFooter = initial.HeaderFooter with
            {
                Header = new WorksheetHeaderFooter("Header left", "Header center &[Picture]", "Header right"),
                Footer = new WorksheetHeaderFooter("Footer left", "Footer center", "Footer right &[Picture]"),
                FirstPageHeader = new WorksheetHeaderFooter("First header", "", ""),
                FirstPageFooter = new WorksheetHeaderFooter("", "First footer", ""),
                EvenPageHeader = new WorksheetHeaderFooter("", "Even header", ""),
                EvenPageFooter = new WorksheetHeaderFooter("", "", "Even footer"),
                HeaderPictures = new WorksheetHeaderFooterPictureSet(Picture("header-left.png"), Picture("header-center.png"), null),
                FooterPictures = new WorksheetHeaderFooterPictureSet(null, null, Picture("footer-right.png")),
                FirstPageHeaderPictures = new WorksheetHeaderFooterPictureSet(Picture("first-header.png"), null, null),
                FirstPageFooterPictures = new WorksheetHeaderFooterPictureSet(null, Picture("first-footer.png"), null),
                EvenPageHeaderPictures = new WorksheetHeaderFooterPictureSet(null, Picture("even-header.png"), null),
                EvenPageFooterPictures = new WorksheetHeaderFooterPictureSet(null, null, Picture("even-footer.png")),
                DifferentFirstPage = true,
                DifferentOddEvenPages = true,
                ScaleWithDocument = false,
                AlignWithMargins = false,
            }
        };

        var command = PageSetupDialogModel.BuildHeaderFooterCommand(sheet, fields);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.PageHeader.Should().Be(fields.HeaderFooter.Header);
        sheet.PageFooter.Should().Be(fields.HeaderFooter.Footer);
        sheet.FirstPageHeader.Should().Be(fields.HeaderFooter.FirstPageHeader);
        sheet.FirstPageFooter.Should().Be(fields.HeaderFooter.FirstPageFooter);
        sheet.EvenPageHeader.Should().Be(fields.HeaderFooter.EvenPageHeader);
        sheet.EvenPageFooter.Should().Be(fields.HeaderFooter.EvenPageFooter);
        sheet.PageHeaderPictures.Center!.FileName.Should().Be("header-center.png");
        sheet.PageFooterPictures.Right!.FileName.Should().Be("footer-right.png");
        sheet.FirstPageHeaderPictures.Left!.FileName.Should().Be("first-header.png");
        sheet.FirstPageFooterPictures.Center!.FileName.Should().Be("first-footer.png");
        sheet.EvenPageHeaderPictures.Center!.FileName.Should().Be("even-header.png");
        sheet.EvenPageFooterPictures.Right!.FileName.Should().Be("even-footer.png");
        sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        sheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        sheet.HeaderFooterScaleWithDocument.Should().BeFalse();
        sheet.HeaderFooterAlignWithMargins.Should().BeFalse();

        command.Revert(ctx);

        sheet.PageHeader.Should().Be(new WorksheetHeaderFooter("", "", ""));
        sheet.PageFooter.Should().Be(new WorksheetHeaderFooter("", "", ""));
        sheet.FirstPageHeaderPictures.Should().Be(WorksheetHeaderFooterPictureSet.Empty);
        sheet.FirstPageFooterPictures.Should().Be(WorksheetHeaderFooterPictureSet.Empty);
        sheet.EvenPageHeaderPictures.Should().Be(WorksheetHeaderFooterPictureSet.Empty);
        sheet.EvenPageFooterPictures.Should().Be(WorksheetHeaderFooterPictureSet.Empty);
        sheet.DifferentFirstPageHeaderFooter.Should().BeFalse();
        sheet.DifferentOddEvenHeaderFooter.Should().BeFalse();
        sheet.HeaderFooterScaleWithDocument.Should().BeTrue();
        sheet.HeaderFooterAlignWithMargins.Should().BeTrue();
    }

    [Fact]
    public void TryBuildCommandPlan_RemapsPrintAreaToTargetSheet()
    {
        var workbook = new Workbook("Book");
        var source = workbook.AddSheet("Sheet1");
        var target = workbook.AddSheet("Sheet2");
        var ctx = new PageSetupTestCommandContext(workbook);

        var fields = PageSetupDialogModel.FromSheet(source) with
        {
            Orientation = WorksheetPageOrientation.Landscape,
            PrintAreaText = "B2:D5",
        };

        var result = PageSetupDialogModel.TryBuildCommandPlan(source, fields, target.Id);

        result.Success.Should().BeTrue(result.Error);
        var plan = result.Plan!;
        plan.PrintArea.Should().NotBeNull();
        plan.PrintArea!.Value.Start.Sheet.Should().Be(target.Id);
        plan.PrintArea.Value.Start.Row.Should().Be(2u);
        plan.PrintArea.Value.Start.Col.Should().Be(2u);
        plan.PrintArea.Value.End.Row.Should().Be(5u);
        plan.PrintArea.Value.End.Col.Should().Be(4u);

        plan.PageSetupCommand.Apply(ctx).Success.Should().BeTrue();
        plan.PrintAreaCommand.Apply(ctx).Success.Should().BeTrue();
        target.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        target.PrintArea.Should().Be(plan.PrintArea);
    }

    [Fact]
    public void SubmissionPlanner_BuildsRequestedActionAndGroupedTargetCommand()
    {
        var workbook = new Workbook("Book");
        var source = workbook.AddSheet("Sheet1");
        var target = workbook.AddSheet("Sheet2");
        var ctx = new PageSetupTestCommandContext(workbook);

        var initial = PageSetupDialogModel.FromSheet(source);
        var fields = initial with
        {
            Orientation = WorksheetPageOrientation.Landscape,
            PrintAreaText = "B2:D5",
            HeaderFooter = initial.HeaderFooter with
            {
                Header = new WorksheetHeaderFooter("", "Report", "")
            },
        };

        var submission = PageSetupSubmissionPlanner.TryBuild(source, fields, PageSetupDialogAction.PrintPreview);

        submission.Success.Should().BeTrue();
        submission.Validation.Should().BeNull();
        submission.Submission!.RequestedAction.Should().Be(PageSetupDialogAction.PrintPreview);

        var targetCommand = submission.Submission.TryBuildCompositeCommandForTarget(source, target.Id);
        targetCommand.Success.Should().BeTrue();
        targetCommand.Validation.Should().BeNull();

        var command = targetCommand.Command!;
        command.Label.Should().Be(PageSetupSubmissionPlanner.DefaultCommandLabel);
        command.Apply(ctx).Success.Should().BeTrue();

        target.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        target.PrintArea.Should().Be(new GridRange(
            new CellAddress(target.Id, 2, 2),
            new CellAddress(target.Id, 5, 4)));
        target.PageHeader.Center.Should().Be("Report");
    }

    [Fact]
    public void SubmissionPlanner_BuildsCompositeCommandForGroupedTargetsAndRoutesFollowUp()
    {
        var workbook = new Workbook("Book");
        var source = workbook.AddSheet("Sheet1");
        var target = workbook.AddSheet("Sheet2");
        var ctx = new PageSetupTestCommandContext(workbook);

        var initial = PageSetupDialogModel.FromSheet(source);
        var fields = initial with
        {
            Orientation = WorksheetPageOrientation.Landscape,
            PrintAreaText = "A1:B4",
            HeaderFooter = initial.HeaderFooter with
            {
                Header = new WorksheetHeaderFooter("", "Grouped", "")
            },
        };

        var submission = PageSetupSubmissionPlanner.TryBuild(source, fields, PageSetupDialogAction.Options);
        var commandBuild = submission.Submission!.TryBuildCompositeCommandForTargets(
            source,
            [source.Id, target.Id]);

        submission.Success.Should().BeTrue();
        submission.Submission.FollowUpAction.Should().Be(PageSetupDialogFollowUpAction.ShowPrinterOptions);
        PageSetupSubmissionPlanner.ResolveFollowUp(PageSetupDialogAction.PrintPreview)
            .Should()
            .Be(PageSetupDialogFollowUpAction.PrintPreview);
        commandBuild.Success.Should().BeTrue(commandBuild.Validation?.Message.FallbackText);
        commandBuild.Command!.Label.Should().Be(PageSetupSubmissionPlanner.DefaultCommandLabel);

        commandBuild.Command.Apply(ctx).Success.Should().BeTrue();

        source.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        target.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        source.PrintArea.Should().Be(new GridRange(
            new CellAddress(source.Id, 1, 1),
            new CellAddress(source.Id, 4, 2)));
        target.PrintArea.Should().Be(new GridRange(
            new CellAddress(target.Id, 1, 1),
            new CellAddress(target.Id, 4, 2)));
        source.PageHeader.Center.Should().Be("Grouped");
        target.PageHeader.Center.Should().Be("Grouped");
    }

    [Fact]
    public void SubmissionPlanner_InvalidFieldsReturnSharedValidationRouteAndMessageKey()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with { HeaderMarginText = "-1" };

        var submission = PageSetupSubmissionPlanner.TryBuild(sheet, fields);

        submission.Success.Should().BeFalse();
        submission.Submission.Should().BeNull();
        submission.Validation.Should().NotBeNull();
        submission.Validation!.Target.Should().Be(PageSetupValidationTarget.HeaderMargin);
        submission.Validation.Route.Should().Be(new PageSetupValidationRoute(
            PageSetupDialogTab.Margins,
            PageSetupDialogField.HeaderMargin));
        submission.Validation.Message.ResourceKey.Should().Be("PageSetup_InvalidHeaderFooterMarginsMessage");
        submission.Validation.Message.Resolve(key => $"[{key}]")
            .Should()
            .Be("[PageSetup_InvalidHeaderFooterMarginsMessage]");
    }

    [Fact]
    public void SubmissionPlanner_UnknownValidationMessageFallsBackToModelError()
    {
        var sheet = CreateSheet();
        var fields = PageSetupDialogModel.FromSheet(sheet) with
        {
            Orientation = (WorksheetPageOrientation)999,
        };

        var submission = PageSetupSubmissionPlanner.TryBuild(sheet, fields);

        submission.Success.Should().BeFalse();
        submission.Validation!.Target.Should().Be(PageSetupValidationTarget.Orientation);
        submission.Validation.Message.ResourceKey.Should().BeNull();
        submission.Validation.Message.Resolve(key => $"[{key}]")
            .Should()
            .Be("Choose a page orientation.");
    }

    [Fact]
    public void CommandFactory_CompositePreservesAdvancedHeaderFooterPictures()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new PageSetupTestCommandContext(workbook);
        var picture = new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "logo.png", 120, 80);

        var request = new PageSetupCommandRequest
        {
            HeaderFooter = new HeaderFooterEditorState
            {
                Header = new WorksheetHeaderFooter("", "Main", ""),
                FirstPageHeader = new WorksheetHeaderFooter("First", "", ""),
                EvenPageFooter = new WorksheetHeaderFooter("", "", "Even"),
                HeaderPictures = new WorksheetHeaderFooterPictureSet(null, picture, null),
                DifferentFirstPage = true,
                DifferentOddEvenPages = true,
            }
        };

        var command = PageSetupCommandFactory.Build(sheet.Id, request).ToComposite();

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PageHeader.Center.Should().Be("Main");
        sheet.FirstPageHeader.Left.Should().Be("First");
        sheet.EvenPageFooter.Right.Should().Be("Even");
        sheet.PageHeaderPictures.Center.Should().NotBeNull();
        sheet.PageHeaderPictures.Center!.FileName.Should().Be("logo.png");
        sheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        sheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
    }

    private sealed class PageSetupTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static WorksheetHeaderFooterPicture Picture(string fileName) =>
        new([1, 2, 3], "image/png", fileName, 120, 48);
}
