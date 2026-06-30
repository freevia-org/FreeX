using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstagePaneProjectionPlannerTests
{
    [Fact]
    public void BuildInfoDialog_ProjectsDialogSectionsRowsActionsAndNotes()
    {
        var pane = FreeXBackstageInfoPanePlanner.Build(
            FreeXBackstageInfoSurface.AvaloniaInfoDialog,
            new FreeXBackstageInfoPaneRequest(
                "Budget.xlsx",
                @"C:\Work\Budget.xlsx",
                "3",
                ".xlsx",
                "12 KB",
                "6/30/2026 10:00 AM",
                SharingStatus: string.Empty,
                ExportStatus: string.Empty,
                WorkbookProtectionSummary: "Workbook protected",
                ActiveSheetProtectionSummary: "Sheet unprotected",
                StatisticsSummary: "3 sheets",
                AccessibilitySummary: string.Empty,
                FormulaErrorSummary: string.Empty,
                UnsavedChangesNote: "Unsaved changes"));

        var projection = FreeXBackstagePaneProjectionPlanner.BuildInfoDialog(pane);

        projection.Elements.Select(element => element.GetType()).Should().Equal(
            typeof(FreeXBackstageSectionHeaderProjectionElement),
            typeof(FreeXBackstageDetailRowsProjectionElement),
            typeof(FreeXBackstageNoteProjectionElement),
            typeof(FreeXBackstageSectionHeaderProjectionElement),
            typeof(FreeXBackstageNoteProjectionElement),
            typeof(FreeXBackstageNoteProjectionElement),
            typeof(FreeXBackstageInfoActionRowProjectionElement),
            typeof(FreeXBackstageSectionHeaderProjectionElement),
            typeof(FreeXBackstageNoteProjectionElement));

        var details = projection.Elements.OfType<FreeXBackstageDetailRowsProjectionElement>().Single();
        details.Rows.Select(row => row.ValueAutomationId).Should().Equal(
            "BackstageInfoName",
            "BackstageInfoPath",
            "BackstageInfoFormat",
            "BackstageInfoSize",
            "BackstageInfoModified",
            "BackstageInfoSheets");

        projection.Elements.OfType<FreeXBackstageInfoActionRowProjectionElement>()
            .Single()
            .Actions.Select(action => action.Id)
            .Should()
            .Equal(
                FreeXBackstageInfoActionId.ProtectSheet,
                FreeXBackstageInfoActionId.ProtectWorkbook,
                FreeXBackstageInfoActionId.InspectWorkbook);
    }

    [Fact]
    public void BuildExportDialog_ProjectsUnavailableNoteAndPortableRadioGroups()
    {
        var pane = FreeXBackstageExportPanePlanner.Build(new FreeXBackstageExportPaneRequest(
            [
                new(FreeXBackstageExportScopeId.SelectedRange, IsAvailable: false, IsDefault: false),
                new(FreeXBackstageExportScopeId.ActiveSheet, IsAvailable: true, IsDefault: true),
            ],
            [new(FreeXBackstageExportOutputKindId.Pdf, IsDefault: true)],
            CanExport: false));

        var projection = FreeXBackstagePaneProjectionPlanner.BuildExportDialog(pane);

        projection.Elements.Select(element => element.GetType()).Should().Equal(
            typeof(FreeXBackstageNoteProjectionElement),
            typeof(FreeXBackstageSectionHeaderProjectionElement),
            typeof(FreeXBackstageExportRadioGroupProjectionElement),
            typeof(FreeXBackstageSectionHeaderProjectionElement),
            typeof(FreeXBackstageExportRadioGroupProjectionElement));

        var note = projection.Elements.OfType<FreeXBackstageNoteProjectionElement>().Single();
        note.Text.TextKey.Should().Be("Backstage_Export_Unavailable");
        note.AutomationId.Should().Be("BackstageExportUnavailable");

        var groups = projection.Elements.OfType<FreeXBackstageExportRadioGroupProjectionElement>().ToArray();
        groups[0].GroupAutomationId.Should().Be("BackstageExportScope");
        groups[0].Options.OfType<FreeXBackstageExportScopeRadioOptionProjection>()
            .Select(option => (option.Scope, option.IsEnabled, option.IsDefault))
            .Should()
            .Equal(
                (FreeXBackstageExportScopeId.SelectedRange, false, false),
                (FreeXBackstageExportScopeId.ActiveSheet, true, true));
        groups[1].GroupAutomationId.Should().Be("BackstageExportFormat");
        groups[1].Options.OfType<FreeXBackstageExportOutputKindRadioOptionProjection>()
            .Single()
            .OutputKind.Should().Be(FreeXBackstageExportOutputKindId.Pdf);
    }

    [Fact]
    public void BuildAccountDialog_ProjectsHeadingRowsActionsAndNotices()
    {
        var pane = FreeXBackstageAccountPanePlanner.Build(new FreeXBackstageAccountPaneRequest(
            UserName: "quinn",
            DeviceName: "FREEX-PC",
            VersionText: "Version 1.2.3",
            OptionsAvailable: true,
            CurrentWorkbookPath: @"C:\Workbooks\Budget.xlsx",
            CurrentWorkbookName: null,
            TrademarkNotice: "Trademark",
            LicenseNotice: "License",
            PrivacyNotice: "Privacy"));

        var projection = FreeXBackstagePaneProjectionPlanner.BuildAccountDialog(pane);

        projection.Elements.Select(element => element.GetType()).Should().Equal(
            typeof(FreeXBackstageHeadingProjectionElement),
            typeof(FreeXBackstageSectionHeaderProjectionElement),
            typeof(FreeXBackstageDetailRowsProjectionElement),
            typeof(FreeXBackstageAccountActionRowProjectionElement),
            typeof(FreeXBackstageSectionHeaderProjectionElement),
            typeof(FreeXBackstageNoteProjectionElement),
            typeof(FreeXBackstageNoteProjectionElement),
            typeof(FreeXBackstageNoteProjectionElement));

        var heading = projection.Elements.OfType<FreeXBackstageHeadingProjectionElement>().Single();
        heading.TextKey.Should().Be("Backstage_Account_Title");

        projection.Elements.OfType<FreeXBackstageAccountActionRowProjectionElement>()
            .Single()
            .Actions.Select(action => action.Id)
            .Should()
            .Equal(
                FreeXBackstageAccountActionId.Options,
                FreeXBackstageAccountActionId.LegalNotices);

        projection.Elements.OfType<FreeXBackstageNoteProjectionElement>()
            .Select(note => note.AutomationId)
            .Should()
            .Equal(
                "BackstageAccountTrademark",
                "BackstageAccountLicense",
                "BackstageAccountPrivacy");
    }
}
