using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
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

    [Fact]
    public void SelectionActions_PlanPreviewSettingUpdates()
    {
        var panelPlan = PrintPreviewSettingsPanelPlanner.Build(
            CreateSheet(),
            new PrintPreviewSettings(),
            hasSelection: true,
            canUpdatePrintPreviewSettings: true);
        var currentSettings = new PrintPreviewSettings();

        PrintPreviewSettingsPanelPlanner.CreateCopiesAction(currentSettings, "4")
            .Settings.Should().Be(currentSettings with { Copies = 4 });
        PrintPreviewSettingsPanelPlanner.CreateCopiesAction(currentSettings, "1000")
            .Kind.Should().Be(PrintPreviewSettingsPanelActionKind.None);
        PrintPreviewSettingsPanelPlanner.CreatePrintWhatAction(panelPlan, currentSettings, 2)
            .Settings.Should().Be(currentSettings with { PrintWhat = PrintWhat.Selection });
        PrintPreviewSettingsPanelPlanner.CreateSidesAction(panelPlan, currentSettings, 1)
            .Settings.Should().Be(currentSettings with { Sides = PrintPreviewSidesMode.TwoSidedLongEdge });
        PrintPreviewSettingsPanelPlanner.CreateCollationAction(panelPlan, currentSettings, 1)
            .Settings.Should().Be(currentSettings with { Collated = false });
        PrintPreviewSettingsPanelPlanner.CreateIgnorePrintAreaAction(currentSettings, true)
            .Settings.Should().Be(currentSettings with { IgnorePrintArea = true });
        PrintPreviewSettingsPanelPlanner.CreatePageRangeAction(currentSettings, "2", "5")
            .Settings.Should().Be(currentSettings with { PageFrom = 2, PageTo = 5 });
    }

    [Fact]
    public void SelectionActions_PlanPageLayoutCommandsAndPlaceholderDialogs()
    {
        var sheet = CreateSheet();
        var panelPlan = PrintPreviewSettingsPanelPlanner.Build(
            sheet,
            new PrintPreviewSettings(),
            hasSelection: false,
            canUpdatePrintPreviewSettings: true);

        PrintPreviewSettingsPanelPlanner.CreateOrientationAction(sheet.Id, panelPlan, 1)
            .Command.Should().BeOfType<SetPageOrientationCommand>();
        PrintPreviewSettingsPanelPlanner.CreatePaperSizeAction(sheet.Id, panelPlan, 2)
            .Command.Should().BeOfType<SetPaperSizeCommand>();
        PrintPreviewSettingsPanelPlanner.CreateMarginsAction(sheet.Id, panelPlan, 1)
            .Command.Should().BeOfType<SetPageMarginsCommand>();
        PrintPreviewSettingsPanelPlanner.CreateScalingAction(sheet.Id, panelPlan, 1)
            .Command.Should().BeOfType<SetScaleToFitCommand>();
        PrintPreviewSettingsPanelPlanner.CreatePrintOptionsAction(sheet.Id, printGridlines: true, printHeadings: false)
            .Command.Should().BeOfType<SetPrintOptionsCommand>();

        var customMargins = PrintPreviewSettingsPanelPlanner.CreateMarginsAction(
            sheet.Id,
            panelPlan,
            PrintPreviewSettingsPanelPlanner.CustomMarginsOptionIndex);
        customMargins.Kind.Should().Be(PrintPreviewSettingsPanelActionKind.OpenCustomMargins);
        customMargins.ResetSelection.Should().BeTrue();
        customMargins.Command.Should().BeNull();

        var customScaling = PrintPreviewSettingsPanelPlanner.CreateScalingAction(
            sheet.Id,
            panelPlan,
            PrintPreviewSettingsPanelPlanner.CustomScalingOptionIndex);
        customScaling.Kind.Should().Be(PrintPreviewSettingsPanelActionKind.OpenPageSetup);
        customScaling.ResetSelection.Should().BeTrue();
        customScaling.Command.Should().BeNull();
    }

    private static Sheet CreateSheet() =>
        new Workbook("Book1").AddSheet("Sheet1");
}
