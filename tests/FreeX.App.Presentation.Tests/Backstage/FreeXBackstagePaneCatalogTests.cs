using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

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
                (FreeXBackstageInfoDetailId.SheetCount, "Backstage_Info_SheetsLabel", "BackstageInfoSheets"),
                // R129-model-avalonia-info-formula-issues-1: File > Info now surfaces formula
                // issues/circular references on this shell too, matching the WPF host.
                (FreeXBackstageInfoDetailId.FormulaErrors, "Backstage_Info_FormulaErrorsLabel", "BackstageInfoFormulaErrors"));

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
                "MainWindow_Text_WorkbookProtection",
                "MainWindow_Text_ActiveSheetProtection");
    }

    [Fact]
    public void ExportCatalog_MapsLabelsAndAutomationIds()
    {
        FreeXBackstagePaneCatalog.GetExportScopeLabelKey(
                FreeXBackstageExportScopeId.SelectedRange,
                isAvailable: true)
            .Should().Be("Backstage_Export_ScopeSelection");
        FreeXBackstagePaneCatalog.GetExportScopeLabelKey(
                FreeXBackstageExportScopeId.SelectedRange,
                isAvailable: false)
            .Should().Be("Backstage_Export_ScopeSelectionUnavailable");
        FreeXBackstagePaneCatalog.GetExportScopeLabelKey(
                FreeXBackstageExportScopeId.ActiveSheet,
                isAvailable: true)
            .Should().Be("Backstage_Export_ScopeActiveSheet");
        FreeXBackstagePaneCatalog.GetExportScopeLabelKey(
                FreeXBackstageExportScopeId.VisibleWorkbook,
                isAvailable: true)
            .Should().Be("Backstage_Export_ScopeWorkbook");

        FreeXBackstagePaneCatalog.GetExportScopeAutomationId(FreeXBackstageExportScopeId.ActiveSheet)
            .Should().Be("BackstageExportScope_ActiveSheet");
        FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(FreeXBackstageExportOutputKindId.Pdf)
            .Should().Be("Backstage_Export_FormatPdf");
        FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(FreeXBackstageExportOutputKindId.Xps)
            .Should().Be("Backstage_Export_FormatXps");
        FreeXBackstagePaneCatalog.GetExportOutputKindAutomationId(FreeXBackstageExportOutputKindId.Pdf)
            .Should().Be("BackstageExportFormat_Pdf");
    }

    [Fact]
    public void AccountCatalog_PinsDetailsActionsAndNotices()
    {
        FreeXBackstagePaneCatalog.BuildAccountDetails()
            .Select(detail => (detail.Id, detail.LabelKey, detail.ValueAutomationId))
            .Should().Equal(
                (FreeXBackstageAccountDetailId.FreeXUserName, "Backstage_Account_FreeXUserNameLabel", "BackstageAccountFreeXUserName"),
                (FreeXBackstageAccountDetailId.LocalOsAccount, "Backstage_Account_LocalOSAccountLabel", "BackstageAccountLocalOsAccount"),
                (FreeXBackstageAccountDetailId.Device, "Backstage_Account_DeviceRowLabel", "BackstageAccountDevice"),
                (FreeXBackstageAccountDetailId.AppVersion, "Backstage_Account_AppVersionLabel", "BackstageAccountAppVersion"),
                (FreeXBackstageAccountDetailId.OptionsFile, "Backstage_Account_OptionsFileLabel", "BackstageAccountOptionsFile"),
                (FreeXBackstageAccountDetailId.CurrentWorkbook, "Backstage_Account_CurrentWorkbookLabel", "BackstageAccountCurrentWorkbook"),
                (FreeXBackstageAccountDetailId.Sharing, "Backstage_Account_SharingLabel", "BackstageAccountSharing"),
                (FreeXBackstageAccountDetailId.Export, "Backstage_Account_ExportLabel", "BackstageAccountExport"));

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
