using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintPreviewSettingsPanelPlannerTests
{
    [Fact]
    public void Build_ProjectsSheetAndCurrentPreviewSettingsIntoOptionPlan()
    {
        var sheet = CreateSheet();
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Legal;
        sheet.PageMargins = WorksheetPageMargins.Wide;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, null);
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.PrintGridlines = true;

        var plan = PrintPreviewSettingsPanelPlanner.Build(
            sheet,
            new PrintPreviewSettings(
                Copies: 2,
                PrintWhat: PrintWhat.EntireWorkbook,
                Sides: PrintPreviewSidesMode.TwoSidedShortEdge,
                Collated: false),
            hasSelection: false,
            canUpdatePrintPreviewSettings: true);

        plan.Copies.Should().Be(2);
        plan.PrintWhatSelectedIndex.Should().Be(1);
        plan.PrintWhatOptions.Select(option => option.Value).Should().Equal(
            PrintWhat.ActiveSheets,
            PrintWhat.EntireWorkbook,
            PrintWhat.Selection);
        plan.PrintWhatOptions[2].IsEnabled.Should().BeFalse();
        plan.SidesSelectedIndex.Should().Be(2);
        plan.CollationSelectedIndex.Should().Be(1);
        plan.OrientationSelectedIndex.Should().Be(1);
        plan.PaperSizeSelectedIndex.Should().Be(2);
        plan.MarginsSelectedIndex.Should().Be(2);
        plan.ScalingSelectedIndex.Should().Be(2);
        plan.IgnorePrintAreaEnabled.Should().BeTrue();
        plan.PrintGridlines.Should().BeTrue();
        plan.PrintHeadings.Should().BeFalse();
    }

    [Fact]
    public void Build_ClampsCopiesAndFallsBackToActiveSheetsWhenSelectionIsUnavailable()
    {
        var plan = PrintPreviewSettingsPanelPlanner.Build(
            CreateSheet(),
            new PrintPreviewSettings(Copies: 2000, PrintWhat: PrintWhat.Selection),
            hasSelection: false,
            canUpdatePrintPreviewSettings: false);

        plan.Copies.Should().Be(999);
        plan.PrintWhatSelectedIndex.Should().Be((int)PrintWhat.ActiveSheets);
        plan.IgnorePrintAreaEnabled.Should().BeFalse();
    }

    [Fact]
    public void CreateMarginAndScalingOptions_MarkCustomEntriesAsPlaceholders()
    {
        var margins = PrintPreviewSettingsPanelPlanner.CreateMarginOptions();
        var scaling = PrintPreviewSettingsPanelPlanner.CreateScalingOptions();

        margins.Select(option => option.Value).Should().Equal(
            WorksheetPageMargins.Narrow,
            WorksheetPageMargins.Normal,
            WorksheetPageMargins.Wide,
            WorksheetPageMargins.Narrow);
        margins[PrintPreviewSettingsPanelPlanner.CustomMarginsOptionIndex].IsPlaceholder.Should().BeTrue();

        scaling[0].Value.Should().Be(WorksheetScaleToFit.Default);
        scaling[1].Value.Should().Be(new WorksheetScaleToFit(null, 1, 1));
        scaling[2].Value.Should().Be(new WorksheetScaleToFit(null, 1, null));
        scaling[3].Value.Should().Be(new WorksheetScaleToFit(null, null, 1));
        scaling[PrintPreviewSettingsPanelPlanner.CustomScalingOptionIndex].IsPlaceholder.Should().BeTrue();
    }

    [Fact]
    public void Build_UsesTextResolverForOptionLabels()
    {
        var resolver = new PrintSettingsTextResolver(
            key => "[" + key + "]",
            (_, _) => "");

        var plan = PrintPreviewSettingsPanelPlanner.Build(
            CreateSheet(),
            new PrintPreviewSettings(),
            hasSelection: true,
            canUpdatePrintPreviewSettings: true,
            resolver);

        plan.PrintWhatOptions[0].Text.Should().Be("[PrintPreview_PrintWhatActiveSheets]");
        plan.OrientationOptions[1].Text.Should().Be("[PageSetup_Landscape]");
        plan.PaperSizeOptions[2].Text.Should().Be("[MainWindow_Header_Legal]");
        plan.MarginOptions[3].Text.Should().Be("[PrintPreview_CustomMarginsOption]");
        plan.ScalingOptions[4].Text.Should().Be("[PrintPreview_ScaleCustomOptions]");
    }

    [Theory]
    [InlineData("2", "5", 2, 5)]
    [InlineData(" 3 ", "", 3, null)]
    [InlineData("abc", "9", null, 9)]
    [InlineData(null, "   ", null, null)]
    public void CreatePageRangePlan_ParsesOptionalPageRangeFields(
        string? fromText,
        string? toText,
        int? expectedFrom,
        int? expectedTo)
    {
        var plan = PrintPreviewSettingsPanelPlanner.CreatePageRangePlan(fromText, toText);

        plan.FromPage.Should().Be(expectedFrom);
        plan.ToPage.Should().Be(expectedTo);
    }

    private static Sheet CreateSheet() =>
        new Workbook("Book1").AddSheet("Sheet1");
}
