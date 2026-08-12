using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageInfoPanePlannerTests
{
    [Fact]
    public void Build_WpfPaneMapsAllLiveInfoRowsToValues()
    {
        var plan = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.WpfInfoPane,
            Request());

        plan.TitleKey.Should().Be("MainWindow_Text_Info");
        plan.ActionsHeadingKey.Should().Be("MainWindow_Text_WorkbookActions");
        plan.PropertiesHeadingKey.Should().Be("MainWindow_Text_Properties");
        plan.Actions.Select(action => action.Id).Should().Equal(
            FreeXBackstageInfoActionId.ProtectWorkbook,
            FreeXBackstageInfoActionId.CheckAccessibility,
            FreeXBackstageInfoActionId.WorkbookStatistics,
            FreeXBackstageInfoActionId.ErrorChecking);
        plan.Actions.Single(action => action.Id == FreeXBackstageInfoActionId.ProtectWorkbook)
            .Detail!.TextKey.Should().Be("MainWindow_Text_ControlWhatTypesOfChangesOthersCanMake");

        plan.Details.Select(detail => detail.Id).Should().Equal(
            FreeXBackstageInfoDetailId.WorkbookName,
            FreeXBackstageInfoDetailId.FilePath,
            FreeXBackstageInfoDetailId.SheetCount,
            FreeXBackstageInfoDetailId.Format,
            FreeXBackstageInfoDetailId.FileSize,
            FreeXBackstageInfoDetailId.LastModified,
            FreeXBackstageInfoDetailId.Share,
            FreeXBackstageInfoDetailId.Export,
            FreeXBackstageInfoDetailId.WorkbookProtection,
            FreeXBackstageInfoDetailId.ActiveSheetProtection,
            FreeXBackstageInfoDetailId.WorkbookStatistics,
            FreeXBackstageInfoDetailId.Accessibility,
            FreeXBackstageInfoDetailId.FormulaErrors);
        TextFor(plan, FreeXBackstageInfoDetailId.WorkbookName).Should().Be("Budget.xlsx");
        TextFor(plan, FreeXBackstageInfoDetailId.Share).Should().Be("Save before sharing");
        TextFor(plan, FreeXBackstageInfoDetailId.Export).Should().Be("Ready for PDF");
        TextFor(plan, FreeXBackstageInfoDetailId.WorkbookStatistics).Should().Be("3 sheets");
        TextFor(plan, FreeXBackstageInfoDetailId.Accessibility).Should().Be("No issues");
        TextFor(plan, FreeXBackstageInfoDetailId.FormulaErrors).Should().Be("1 issue found");
    }

    [Fact]
    public void Build_AvaloniaPaneKeepsDialogRowsAndSharedSectionValues()
    {
        var plan = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.AvaloniaInfoDialog,
            Request(unsavedChangesNote: "Unsaved changes"));

        plan.FileSectionHeaderKey.Should().Be("Backstage_Info_FileSectionHeader");
        plan.ProtectionSectionHeaderKey.Should().Be("Backstage_Info_ProtectionSectionHeader");
        plan.StatisticsSectionHeaderKey.Should().Be("Backstage_Info_StatisticsSectionHeader");
        plan.Details.Select(detail => detail.Id).Should().Equal(
            FreeXBackstageInfoDetailId.WorkbookName,
            FreeXBackstageInfoDetailId.FilePath,
            FreeXBackstageInfoDetailId.Format,
            FreeXBackstageInfoDetailId.FileSize,
            FreeXBackstageInfoDetailId.LastModified,
            FreeXBackstageInfoDetailId.SheetCount,
            // R129-model-avalonia-info-formula-issues-1: the Avalonia/macOS Info dialog previously
            // had no row surfacing formula issues/circular references at all -- see
            // FreeXBackstagePaneCatalog.AvaloniaInfoDetails.
            FreeXBackstageInfoDetailId.FormulaErrors);
        plan.Actions.Select(action => action.Id).Should().Equal(
            FreeXBackstageInfoActionId.ProtectSheet,
            FreeXBackstageInfoActionId.ProtectWorkbook,
            FreeXBackstageInfoActionId.InspectWorkbook);
        plan.UnsavedChangesNote!.Text.Should().Be("Unsaved changes");
        plan.WorkbookProtectionSummary.Text.Should().Be("Workbook protected");
        plan.ActiveSheetProtectionSummary.Text.Should().Be("Sheet unprotected");
        plan.StatisticsSummary.Text.Should().Be("3 sheets");
        TextFor(plan, FreeXBackstageInfoDetailId.FormulaErrors).Should().Be("1 issue found",
            "the Avalonia Info dialog must report formula issues/circular references with the same wording as the WPF host");
    }

    [Fact]
    public void Build_AvaloniaLivePaneOwnsItsExactRendererLabelsAndValues()
    {
        var plan = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.AvaloniaLivePane,
            Request(unsavedChangesNote: "Unsaved changes"));

        plan.Actions.Should().BeEmpty();
        plan.ProtectionSectionHeaderKey.Should().Be("Backstage_LiveInfo_ProtectionSectionHeader");
        plan.StatisticsSectionHeaderKey.Should().Be("Backstage_LiveInfo_StatisticsSectionHeader");
        plan.Details.Select(detail => (detail.Id, detail.LabelKey)).Should().Equal(
            (FreeXBackstageInfoDetailId.WorkbookName, "Backstage_LiveInfo_WorkbookLabel"),
            (FreeXBackstageInfoDetailId.FilePath, "Backstage_LiveInfo_LocationLabel"),
            (FreeXBackstageInfoDetailId.Format, "Backstage_LiveInfo_FormatLabel"),
            (FreeXBackstageInfoDetailId.FileSize, "Backstage_LiveInfo_SizeLabel"),
            (FreeXBackstageInfoDetailId.LastModified, "Backstage_LiveInfo_LastModifiedLabel"),
            (FreeXBackstageInfoDetailId.SheetCount, "Backstage_LiveInfo_SheetsLabel"));
        TextFor(plan, FreeXBackstageInfoDetailId.WorkbookName).Should().Be("Budget.xlsx");
        plan.WorkbookProtectionSummary.Text.Should().Be("Workbook protected");
        plan.ActiveSheetProtectionSummary.Text.Should().Be("Sheet unprotected");
        plan.StatisticsSummary.Text.Should().Be("3 sheets");
    }

    [Fact]
    public void Build_ParityCaptureKeepsWindowsPropertySubset()
    {
        var plan = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.ParityCapture,
            Request());

        plan.Details.Select(detail => detail.Id).Should().Equal(
            FreeXBackstageInfoDetailId.WorkbookName,
            FreeXBackstageInfoDetailId.FilePath,
            FreeXBackstageInfoDetailId.SheetCount,
            FreeXBackstageInfoDetailId.Format,
            FreeXBackstageInfoDetailId.FileSize,
            FreeXBackstageInfoDetailId.LastModified,
            FreeXBackstageInfoDetailId.Share,
            FreeXBackstageInfoDetailId.Export,
            FreeXBackstageInfoDetailId.WorkbookProtection,
            FreeXBackstageInfoDetailId.ActiveSheetProtection);
    }

    private static FreeXBackstageInfoPaneRequest Request(string? unsavedChangesNote = null) =>
        new(
            WorkbookName: "Budget.xlsx",
            FilePath: @"C:\Work\Budget.xlsx",
            SheetCount: "3",
            Format: ".xlsx",
            FileSize: "12 KB",
            LastModified: "6/30/2026 10:00 AM",
            SharingStatus: "Save before sharing",
            ExportStatus: "Ready for PDF",
            WorkbookProtectionSummary: "Workbook protected",
            ActiveSheetProtectionSummary: "Sheet unprotected",
            StatisticsSummary: "3 sheets",
            AccessibilitySummary: "No issues",
            FormulaErrorSummary: "1 issue found",
            UnsavedChangesNote: unsavedChangesNote);

    private static string? TextFor(
        FreeXBackstageInfoPanePlan plan,
        FreeXBackstageInfoDetailId id) =>
        plan.Details.Single(detail => detail.Id == id).Value.Text;
}
