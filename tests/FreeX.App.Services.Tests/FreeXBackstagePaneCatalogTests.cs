using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class FreeXBackstagePaneCatalogTests
{
    [Fact]
    public void BuildInfoActions_PinsSurfaceSpecificOrderingAndMetadata()
    {
        var wpfActions = FreeXBackstagePaneCatalog.BuildInfoActions(FreeXBackstageInfoSurface.WpfInfoPane);
        var avaloniaActions = FreeXBackstagePaneCatalog.BuildInfoActions(FreeXBackstageInfoSurface.AvaloniaInfoDialog);

        wpfActions.Select(action => action.Id).Should().Equal(
            FreeXBackstageInfoActionId.ProtectWorkbook,
            FreeXBackstageInfoActionId.CheckAccessibility,
            FreeXBackstageInfoActionId.WorkbookStatistics,
            FreeXBackstageInfoActionId.ErrorChecking);
        wpfActions.Select(action => action.AutomationId).Should().Equal(
            "BackstageInfoProtectWorkbookButton",
            "BackstageInfoCheckAccessibilityButton",
            "BackstageInfoWorkbookStatisticsButton",
            "BackstageInfoErrorCheckingButton");
        wpfActions.Select(action => action.KeyTip).Should().Equal("PW", "CA", "W", "EC");

        avaloniaActions.Select(action => action.Id).Should().Equal(
            FreeXBackstageInfoActionId.ProtectSheet,
            FreeXBackstageInfoActionId.ProtectWorkbook,
            FreeXBackstageInfoActionId.InspectWorkbook);
        avaloniaActions.Select(action => action.LabelKey).Should().Equal(
            "Backstage_Info_ProtectSheetAction",
            "Backstage_Info_ProtectWorkbookAction",
            "Backstage_Info_InspectAction");
    }

    [Fact]
    public void BuildInfoDetails_PinsAvaloniaAndParityDetailRows()
    {
        FreeXBackstagePaneCatalog.BuildInfoDetails(FreeXBackstageInfoSurface.AvaloniaInfoDialog)
            .Select(detail => (detail.Id, detail.LabelKey, detail.ValueAutomationId))
            .Should().Equal(
                (FreeXBackstageInfoDetailId.WorkbookName, "Backstage_Info_NameLabel", "BackstageInfoName"),
                (FreeXBackstageInfoDetailId.FilePath, "Backstage_Info_PathLabel", "BackstageInfoPath"),
                (FreeXBackstageInfoDetailId.Format, "Backstage_Info_FormatLabel", "BackstageInfoFormat"),
                (FreeXBackstageInfoDetailId.FileSize, "Backstage_Info_SizeLabel", "BackstageInfoSize"),
                (FreeXBackstageInfoDetailId.LastModified, "Backstage_Info_ModifiedLabel", "BackstageInfoModified"),
                (FreeXBackstageInfoDetailId.SheetCount, "Backstage_Info_SheetsLabel", "BackstageInfoSheets"));

        FreeXBackstagePaneCatalog.BuildInfoDetails(FreeXBackstageInfoSurface.ParityCapture)
            .Select(detail => detail.LabelKey)
            .Should().Equal(
                "MainWindow_Text_WorkbookName",
                "MainWindow_Text_FilePath",
                "MainWindow_Text_Sheets",
                "MainWindow_Text_Format",
                "MainWindow_Text_FileSize",
                "MainWindow_Text_LastModified",
                "MainWindow_Text_Share",
                "MainWindow_Text_Export",
                "MainWindow_Text_WorkbookProtection");
    }

    [Fact]
    public void ExportCatalog_MapsLabelsAndAutomationIds()
    {
        FreeXBackstagePaneCatalog.GetExportScopeLabelKey(
                WorkbookExportPrintScope.SelectedRange,
                isAvailable: true)
            .Should().Be("Backstage_Export_ScopeSelection");
        FreeXBackstagePaneCatalog.GetExportScopeLabelKey(
                WorkbookExportPrintScope.SelectedRange,
                isAvailable: false)
            .Should().Be("Backstage_Export_ScopeSelectionUnavailable");
        FreeXBackstagePaneCatalog.GetExportScopeLabelKey(
                WorkbookExportPrintScope.ActiveSheet,
                isAvailable: true)
            .Should().Be("Backstage_Export_ScopeActiveSheet");
        FreeXBackstagePaneCatalog.GetExportScopeLabelKey(
                WorkbookExportPrintScope.VisibleWorkbook,
                isAvailable: true)
            .Should().Be("Backstage_Export_ScopeWorkbook");

        FreeXBackstagePaneCatalog.GetExportScopeAutomationId(WorkbookExportPrintScope.ActiveSheet)
            .Should().Be("BackstageExportScope_ActiveSheet");
        FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(WorkbookExportPrintOutputKind.Pdf)
            .Should().Be("Backstage_Export_FormatPdf");
        FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(WorkbookExportPrintOutputKind.Xps)
            .Should().Be("Backstage_Export_FormatXps");
        FreeXBackstagePaneCatalog.GetExportOutputKindAutomationId(WorkbookExportPrintOutputKind.Pdf)
            .Should().Be("BackstageExportFormat_Pdf");
    }

    [Fact]
    public void AccountCatalog_PinsDetailsActionsAndNotices()
    {
        FreeXBackstagePaneCatalog.BuildAccountDetails()
            .Select(detail => (detail.Id, detail.LabelKey, detail.ValueAutomationId))
            .Should().Equal(
                (FreeXBackstageAccountDetailId.Product, "Backstage_Account_ProductLabel", "BackstageAccountProduct"),
                (FreeXBackstageAccountDetailId.Version, "Backstage_Account_VersionLabel", "BackstageAccountVersion"),
                (FreeXBackstageAccountDetailId.Device, "Backstage_Account_DeviceLabel", "BackstageAccountDevice"),
                (FreeXBackstageAccountDetailId.User, "Backstage_Account_UserLabel", "BackstageAccountUser"));

        FreeXBackstagePaneCatalog.BuildAccountActions(optionsAvailable: true)
            .Select(action => action.Id)
            .Should().Equal(FreeXBackstageAccountActionId.Options, FreeXBackstageAccountActionId.LegalNotices);
        FreeXBackstagePaneCatalog.BuildAccountActions(optionsAvailable: false)
            .Select(action => action.Id)
            .Should().Equal(FreeXBackstageAccountActionId.LegalNotices);

        FreeXBackstagePaneCatalog.BuildAccountNotices()
            .Select(notice => notice.AutomationId)
            .Should().Equal("BackstageAccountTrademark", "BackstageAccountLicense", "BackstageAccountPrivacy");
    }
}
