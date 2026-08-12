using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class LocalAccountBackstagePlanTests
{
    [Fact]
    public void CanonicalPlan_BuildsLocalizedIdentityStorageAndReadinessRows()
    {
        var workbook = new Workbook("Budget.xlsx");
        workbook.AddSheet("Sheet1");

        var (info, pane) = Create(
            userName: "Analyst",
            currentFilePath: @"C:\Work\Budget.xlsx",
            workbookName: "Budget.xlsx",
            fileExists: path => path == @"C:\Work\Budget.xlsx",
            workbook: workbook,
            hasSelection: true);

        info.WorkbookStatus.Should().Be(@"Budget.xlsx (C:\Work\Budget.xlsx)");
        info.SharingStatus.Should().Be(@"Ready for Windows Share from C:\Work\Budget.xlsx.");
        info.ExportStatus.Should().Contain("selected range");
        Resolve(pane, FreeXBackstageAccountDetailId.FreeXUserName).Should().Be("Analyst");
        Resolve(pane, FreeXBackstageAccountDetailId.LocalOsAccount).Should().Be(@"DESKTOP\quinn");
        Resolve(pane, FreeXBackstageAccountDetailId.Device).Should().Be("FREEX-PC");
        Resolve(pane, FreeXBackstageAccountDetailId.AppVersion).Should().StartWith("Version ");
        Resolve(pane, FreeXBackstageAccountDetailId.OptionsFile)
            .Should().Be(@"C:\Users\quinn\AppData\Roaming\FreeX\options.json");
        Resolve(pane, FreeXBackstageAccountDetailId.Export)
            .Should().Contain("Ready for local PDF/XPS export");
        pane.Details.Select(detail => UiText.Get(detail.LabelKey)).Should().Equal(
            FreeXBackstagePaneCatalog.BuildAccountDetails().Select(detail => UiText.Get(detail.LabelKey)));
    }

    [Fact]
    public void CanonicalPlan_ReportsUnsavedMissingAndInvalidWorkbookPaths()
    {
        var (unsaved, unsavedPane) = Create(
            userName: string.Empty,
            currentFilePath: null,
            workbookName: "Book1",
            fileExists: _ => false,
            localOsDomain: string.Empty);

        unsaved.UserName.Should().Be("quinn");
        unsaved.WorkbookStatus.Should().Be("Book1 (not saved yet)");
        unsaved.SharingStatus.Should().Be(
            "Save As is required before Windows Share can send the workbook because it has not been saved yet.");
        Resolve(unsavedPane, FreeXBackstageAccountDetailId.FreeXUserName).Should().Be("quinn");

        var (missing, _) = Create(
            userName: "Analyst",
            currentFilePath: @"C:\Missing\Book1.xlsx",
            workbookName: "Book1",
            fileExists: _ => false,
            localOsDomain: string.Empty);
        missing.WorkbookStatus.Should().Be(@"Book1 (saved path missing: C:\Missing\Book1.xlsx)");
        missing.SharingStatus.Should().Be(
            @"Save As is required before Windows Share can send the workbook because the saved path is missing: C:\Missing\Book1.xlsx.");

        var (invalid, _) = Create(
            userName: "Analyst",
            currentFilePath: "bad\0path.xlsx",
            workbookName: "Book1",
            fileExists: _ => throw new InvalidOperationException("invalid paths must not be probed"),
            localOsDomain: string.Empty);
        invalid.WorkbookStatus.Should().Be(
            "Book1 (saved path is not a valid local file path: bad\0path.xlsx)");
        invalid.SharingStatus.Should().Be(
            "Save As is required before Windows Share can send the workbook because the saved path is not a valid local file path.");
    }

    [Fact]
    public void FormatMessageBody_PreservesLocalOnlyBoundaryAndAllRows()
    {
        var (_, pane) = Create(
            userName: "Analyst",
            currentFilePath: @"C:\Work\Budget.xlsx",
            workbookName: "Budget.xlsx",
            fileExists: _ => true);

        var body = FreeXBackstageAccountPanePlanner.FormatMessageBody(
            pane,
            UiText.Get("DeferredCommand_LocalAccount_Body"),
            UiText.Get);

        body.Should().Contain("Microsoft account integration is not implemented");
        body.Should().Contain("FreeX user name: Analyst");
        body.Should().Contain(@"Local OS account: DESKTOP\quinn");
        body.Should().Contain("Sharing: Ready for Windows Share");
        body.Should().Contain("Export: Ready for local PDF/XPS export");
        body.Should().NotContain("Microsoft 365 services");
    }

    private static (LocalAccountInfoPlan Info, FreeXBackstageAccountPanePlan Pane) Create(
        string userName,
        string? currentFilePath,
        string workbookName,
        Func<string, bool> fileExists,
        Workbook? workbook = null,
        bool hasSelection = false,
        string localOsDomain = "DESKTOP")
    {
        var info = LocalAccountInfoPlanner.Build(new LocalAccountInfoRequest(
            typeof(LocalAccountBackstagePlanTests).Assembly,
            DeviceName: "FREEX-PC",
            UserName: userName,
            LocalOsUserName: "quinn",
            LocalOsUserDomain: localOsDomain,
            OptionsFile: @"C:\Users\quinn\AppData\Roaming\FreeX\options.json",
            CurrentWorkbookPath: currentFilePath,
            CurrentWorkbookName: workbookName,
            Workbook: workbook,
            HasSelection: hasSelection,
            FileExists: fileExists));
        var pane = FreeXBackstageAccountPanePlanner.Build(
            LocalAccountInfoPlanner.CreateBackstageAccountPaneRequest(
                info,
                currentFilePath,
                workbookName));
        return (info, pane);
    }

    private static string Resolve(
        FreeXBackstageAccountPanePlan pane,
        FreeXBackstageAccountDetailId id) =>
        pane.Details.Single(detail => detail.Id == id).Value.Resolve(UiText.Get);
}
