using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageSetupDialogPlannerTests
{
    [Fact]
    public void ChoicePlans_WrapSharedCatalogsWithFallbacks()
    {
        PageSetupDialogPlanner.OrientationChoices.Choices
            .Should()
            .Equal(PageSetupDialogModel.OrientationChoices);
        PageSetupDialogPlanner.PaperSizeChoices.Choices
            .Should()
            .Equal(PageSetupDialogModel.PaperSizeChoices);

        PageSetupDialogPlanner.PaperSizeChoices.IndexOf(WorksheetPaperSize.B4).Should().Be(7);
        PageSetupDialogPlanner.PaperSizeChoices.IndexOf(WorksheetPaperSize.B5).Should().Be(8);
        PageSetupDialogPlanner.PaperSizeChoices.ValueAt(99).Should().Be(WorksheetPaperSize.A4);
        PageSetupDialogPlanner.PrintErrorValueChoices.ValueAt(2).Should().Be(WorksheetPrintErrorValue.Dash);
    }

    [Fact]
    public void ResolveChoiceLabels_UsesCatalogResourceKeysInOrder()
    {
        PageSetupDialogPlanner.ResolveChoiceLabels(PageSetupDialogPlanner.OrientationChoices, key => $"loc:{key}")
            .Should()
            .Equal("loc:PageSetup_Portrait", "loc:PageSetup_Landscape");
    }

    [Fact]
    public void SurfaceMetadata_CentralizesSharedDialogContract()
    {
        PageSetupDialogPlanner.DialogAutomationId.Should().Be("PageSetupDialog");
        PageSetupDialogPlanner.TabsAutomationId.Should().Be("PageSetupTabs");
        PageSetupDialogPlanner.WindowWidth.Should().Be(600);
        PageSetupDialogPlanner.WindowHeight.Should().Be(560);
        PageSetupDialogPlanner.FieldMinWidth.Should().Be(220);
        PageSetupDialogPlanner.PrintPreviewButtonAutomationId.Should().Be("PageSetupPrintPreviewButton");
    }

    [Fact]
    public void PlanSurface_ProjectsSheetStateToRendererNeutralDisplayState()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Legal;
        sheet.PageMargins = new WorksheetPageMargins(0.75, 0.8, 1.0, 1.1);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 2, null);
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 8, 4));
        sheet.PrintTitleRows = new WorksheetRepeatRange(2, 4);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(2, 4);
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Dash;
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.PageHeader = new WorksheetHeaderFooter("", "Confidential, Page &[Page]", "");
        sheet.PageFooter = new WorksheetHeaderFooter("", "&[Time]", "");

        var plan = PageSetupDialogPlanner.PlanSurface(sheet);

        plan.ChoiceIndexes.Orientation.Should().Be(1);
        plan.ChoiceIndexes.PaperSize.Should().Be(2);
        plan.ChoiceIndexes.PageOrder.Should().Be(1);
        plan.ChoiceIndexes.PrintErrorValue.Should().Be(2);
        plan.ChoiceIndexes.PrintComments.Should().Be(1);
        plan.ChoiceIndexes.HeaderPreset.Should().Be(7);
        plan.ChoiceIndexes.FooterPreset.Should().Be(8);
        plan.Margins.Should().Be(new PageSetupMarginTextFields("0.75", "0.8", "1", "1.1"));
        plan.Scaling.Should().Be(new PageSetupDialogScalingSurface
        {
            Mode = PageSetupScalingMode.FitToPages,
            ScalePercentText = "100",
            FitToWideText = "2",
            FitToTallText = "",
        });
        plan.PrintAreaText.Should().Be("$B$2:$D$8");
        plan.RepeatRowsText.Should().Be("$2:$4");
        plan.RepeatColumnsText.Should().Be("$B:$D");
    }

    [Fact]
    public void BuildFields_MapsSurfaceInputThroughSharedChoicePlans()
    {
        var initial = new PageSetupDialogFields();
        var fields = PageSetupDialogPlanner.BuildFields(
            initial,
            new PageSetupDialogSurfaceInput
            {
                OrientationIndex = 1,
                PaperSizeIndex = 2,
                MarginsText = PageSetupDialogPlanner.BuildMarginsText(
                    new PageSetupMarginTextFields("0.7", "0.8", "0.9", "1")),
                HeaderMarginText = "0.25",
                FooterMarginText = "0.35",
                CenterHorizontally = true,
                CenterVertically = true,
                ScalingMode = PageSetupScalingMode.FitToPages,
                ScalePercentText = "100",
                FitToWideText = "1",
                FitToTallText = "",
                FirstPageNumberText = "3",
                PrintQualityDpiText = "600",
                PrintAreaText = "$A$1:$B$5",
                RepeatRowsText = "$1:$2",
                RepeatColumnsText = "$A:$B",
                PrintGridlines = true,
                PrintHeadings = true,
                PrintBlackAndWhite = true,
                PrintDraftQuality = true,
                PrintErrorValueIndex = 3,
                PrintCommentsIndex = 2,
                PageOrderIndex = 1,
                Header = new WorksheetHeaderFooter("L", "H", "R"),
                Footer = new WorksheetHeaderFooter("L", "F", "R"),
                DifferentFirstPage = true,
                DifferentOddEvenPages = true,
                ScaleHeaderFooterWithDocument = false,
                AlignHeaderFooterWithMargins = false,
            });

        fields.Orientation.Should().Be(WorksheetPageOrientation.Landscape);
        fields.PaperSize.Should().Be(WorksheetPaperSize.Legal);
        fields.MarginsText.Should().Be("0.7,0.8,0.9,1");
        fields.CenterHorizontally.Should().BeTrue();
        fields.ScalingMode.Should().Be(PageSetupScalingMode.FitToPages);
        fields.FitToTallText.Should().BeEmpty();
        fields.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.NotAvailable);
        fields.PrintComments.Should().Be(WorksheetPrintComments.AsDisplayed);
        fields.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        fields.Header.Center.Should().Be("H");
        fields.Footer.Center.Should().Be("F");
        fields.DifferentFirstPage.Should().BeTrue();
        fields.ScaleHeaderFooterWithDocument.Should().BeFalse();
    }

    [Fact]
    public void BuildFields_ComposesSeparateMarginFieldsInWpfOrder()
    {
        var fields = PageSetupDialogPlanner.BuildFields(
            new PageSetupDialogFields(),
            new PageSetupDialogSurfaceInput
            {
                LeftMarginText = "0.7",
                RightMarginText = "0.8",
                TopMarginText = "0.9",
                BottomMarginText = "1.0",
            });

        fields.MarginsText.Should().Be("0.7,0.8,0.9,1.0");
    }

    [Fact]
    public void BuildFields_PreservesLegacyCombinedMarginsWhenSeparateFieldsAreUnavailable()
    {
        var fields = PageSetupDialogPlanner.BuildFields(
            new PageSetupDialogFields(),
            new PageSetupDialogSurfaceInput { MarginsText = "0.7,0.8,0.9,1" });

        fields.MarginsText.Should().Be("0.7,0.8,0.9,1");
    }

    [Fact]
    public void PresetHelpers_ApplyHeaderAndFooterCenterChoices()
    {
        PageSetupDialogPlanner.ApplyHeaderPreset(
                new WorksheetHeaderFooter("L", "custom", "R"),
                selectedIndex: 7)
            .Should()
            .Be(new WorksheetHeaderFooter("L", "Confidential, Page &[Page]", "R"));

        PageSetupDialogPlanner.ApplyFooterPreset(
                new WorksheetHeaderFooter("L", "custom", "R"),
                selectedIndex: 8)
            .Should()
            .Be(new WorksheetHeaderFooter("L", "&[Time]", "R"));
    }

    [Fact]
    public void FocusPlans_SelectRendererNeutralTargets()
    {
        PageSetupDialogPlanner.PlanInitialFocus(
                PageSetupDialogPlanner.PlanOpen(PageSetupInitialFocusTarget.ScaleToFit),
                PageSetupScalingMode.FitToPages)
            .Should()
            .Be(new PageSetupDialogFocusPlan(
                new PageSetupValidationRoute(PageSetupDialogTab.Page, PageSetupDialogField.Scaling),
                PageSetupDialogFocusTarget.FitPagesWide));

        PageSetupDialogPlanner.PlanValidationFocus(
                PageSetupValidationTarget.Margins,
                new PageSetupDialogValidationFocusState
                {
                    HasSeparateMarginFields = true,
                    LeftMarginText = "0.5",
                    RightMarginText = "bad",
                    TopMarginText = "0.5",
                    BottomMarginText = "0.5",
                })
            .Target
            .Should()
            .Be(PageSetupDialogFocusTarget.RightMargin);

        PageSetupDialogPlanner.PlanValidationFocus(
                PageSetupValidationTarget.RepeatColumns,
                new PageSetupDialogValidationFocusState { RepeatRowsText = "$1:$2" })
            .Target
            .Should()
            .Be(PageSetupDialogFocusTarget.RepeatColumns);
    }

    [Theory]
    [InlineData(PageLayoutPageSetupOpenSource.DialogButton, PageSetupInitialFocusTarget.PageOrientation, PageSetupDialogTab.Page, PageSetupDialogField.Orientation)]
    [InlineData(PageLayoutPageSetupOpenSource.CustomMargins, PageSetupInitialFocusTarget.Margins, PageSetupDialogTab.Margins, PageSetupDialogField.Margins)]
    [InlineData(PageLayoutPageSetupOpenSource.ExtendedPaperSize, PageSetupInitialFocusTarget.PaperSize, PageSetupDialogTab.Page, PageSetupDialogField.PaperSize)]
    [InlineData(PageLayoutPageSetupOpenSource.ScaleToFit, PageSetupInitialFocusTarget.ScaleToFit, PageSetupDialogTab.Page, PageSetupDialogField.Scaling)]
    [InlineData(PageLayoutPageSetupOpenSource.PrintArea, PageSetupInitialFocusTarget.PrintArea, PageSetupDialogTab.Sheet, PageSetupDialogField.PrintArea)]
    [InlineData(PageLayoutPageSetupOpenSource.PrintTitles, PageSetupInitialFocusTarget.RepeatRows, PageSetupDialogTab.Sheet, PageSetupDialogField.RepeatRows)]
    public void PlanOpen_MapsRibbonSourcesToSharedInitialFocusRoutes(
        PageLayoutPageSetupOpenSource source,
        PageSetupInitialFocusTarget expectedFocus,
        PageSetupDialogTab expectedTab,
        PageSetupDialogField expectedField)
    {
        var plan = PageSetupDialogPlanner.PlanOpen(source);

        plan.InitialFocusTarget.Should().Be(expectedFocus);
        plan.InitialRoute.Should().Be(new PageSetupValidationRoute(expectedTab, expectedField));
    }
}
