using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class LocalAccountPlannerTests
{
    [Fact]
    public void Create_BuildsLocalIdentityStorageAndSharingStatus()
    {
        var workbook = new Workbook("Budget.xlsx");
        workbook.AddSheet("Sheet1");

        var plan = LocalAccountPlanner.Create(
            new AppOptions { UserName = "Analyst" },
            @"C:\Work\Budget.xlsx",
            "Budget.xlsx",
            userNameProvider: () => "quinn",
            userDomainProvider: () => "DESKTOP",
            machineNameProvider: () => "FREEX-PC",
            optionsPathProvider: () => @"C:\Users\quinn\AppData\Roaming\FreeX\options.json",
            fileExists: path => path == @"C:\Work\Budget.xlsx",
            workbook: workbook,
            hasSelection: true);

        plan.Title.Should().Be("Account");
        plan.WorkbookStatus.Should().Be(@"Budget.xlsx (C:\Work\Budget.xlsx)");
        plan.SharingStatus.Should().Be(@"Ready for Windows Share from C:\Work\Budget.xlsx.");
        plan.ExportStatus.Should().Contain("selected range");
        plan.Details.Should().ContainEquivalentOf(new LocalAccountDetail("FreeX user name", "Analyst"));
        plan.Details.Should().ContainEquivalentOf(new LocalAccountDetail("Local OS account", @"DESKTOP\quinn"));
        plan.Details.Should().ContainEquivalentOf(new LocalAccountDetail("Device", "FREEX-PC"));
        plan.Details.Should().Contain(detail =>
            detail.Label == "App version" &&
            detail.Value.StartsWith("Version ", StringComparison.Ordinal));
        plan.Details.Should().Contain(detail =>
            detail.Label == "Options file" &&
            detail.Value == @"C:\Users\quinn\AppData\Roaming\FreeX\options.json");
        plan.Details.Should().Contain(detail =>
            detail.Label == "Export" &&
            detail.Value.Contains("Ready for local PDF/XPS export"));
        plan.Details.Should().NotContain(detail => detail.Label == "Microsoft 365 services");
    }

    [Fact]
    public void Create_ProjectsDisplayedRowsThroughBackstageAccountCatalog()
    {
        var plan = LocalAccountPlanner.Create(
            new AppOptions { UserName = "Analyst" },
            @"C:\Work\Budget.xlsx",
            "Budget.xlsx",
            userNameProvider: () => "quinn",
            userDomainProvider: () => "DESKTOP",
            machineNameProvider: () => "FREEX-PC",
            optionsPathProvider: () => "options.json",
            fileExists: _ => true);

        plan.Details.Select(detail => detail.Label).Should().Equal(
            FreeXBackstagePaneCatalog.BuildAccountDetails()
                .Select(detail => UiText.Get(detail.LabelKey)));
        Detail(plan, FreeXBackstageAccountDetailId.FreeXUserName).Value.Should().Be("Analyst");
        Detail(plan, FreeXBackstageAccountDetailId.LocalOsAccount).Value.Should().Be(@"DESKTOP\quinn");
        Detail(plan, FreeXBackstageAccountDetailId.CurrentWorkbook).Value.Should().Be(@"Budget.xlsx (C:\Work\Budget.xlsx)");
        Detail(plan, FreeXBackstageAccountDetailId.Sharing).Value.Should().Be(@"Ready for Windows Share from C:\Work\Budget.xlsx.");
        Detail(plan, FreeXBackstageAccountDetailId.Export).Value.Should().Contain("Ready for local PDF/XPS export");
    }

    [Fact]
    public void Create_ReportsSaveAsRequiredForUnsavedOrMissingWorkbookPaths()
    {
        var unsaved = LocalAccountPlanner.Create(
            new AppOptions { UserName = "" },
            null,
            "Book1",
            userNameProvider: () => "quinn",
            userDomainProvider: () => "",
            machineNameProvider: () => "FREEX-PC",
            optionsPathProvider: () => "options.json",
            fileExists: _ => false);

        unsaved.WorkbookStatus.Should().Be("Book1 (not saved yet)");
        unsaved.SharingStatus.Should().Be("Save As is required before Windows Share can send the workbook because it has not been saved yet.");
        unsaved.ExportStatus.Should().Contain("Ready for local PDF/XPS export");
        unsaved.Details.Should().ContainEquivalentOf(new LocalAccountDetail("FreeX user name", "quinn"));

        var missing = LocalAccountPlanner.Create(
            new AppOptions { UserName = "Analyst" },
            @"C:\Missing\Book1.xlsx",
            "Book1",
            userNameProvider: () => "quinn",
            userDomainProvider: () => "",
            machineNameProvider: () => "FREEX-PC",
            optionsPathProvider: () => "options.json",
            fileExists: _ => false);

        missing.WorkbookStatus.Should().Be(@"Book1 (saved path missing: C:\Missing\Book1.xlsx)");
        missing.SharingStatus.Should().Be(@"Save As is required before Windows Share can send the workbook because the saved path is missing: C:\Missing\Book1.xlsx.");
        missing.ExportStatus.Should().Contain("Ready for local PDF/XPS export");
    }

    [Fact]
    public void Create_ReportsInvalidWorkbookPathsWithoutProbingTheFileSystem()
    {
        var plan = LocalAccountPlanner.Create(
            new AppOptions { UserName = "Analyst" },
            "bad\0path.xlsx",
            "Book1",
            userNameProvider: () => "quinn",
            userDomainProvider: () => "",
            machineNameProvider: () => "FREEX-PC",
            optionsPathProvider: () => "options.json",
            fileExists: _ => throw new InvalidOperationException("invalid paths must not be probed"));

        plan.WorkbookStatus.Should().Be("Book1 (saved path is not a valid local file path: bad\0path.xlsx)");
        plan.SharingStatus.Should().Be("Save As is required before Windows Share can send the workbook because the saved path is not a valid local file path.");
        plan.ExportStatus.Should().Contain("Ready for local PDF/XPS export");
    }

    [Fact]
    public void FormatMessageBody_IncludesTheNoMicrosoftAccountBoundaryAndLocalDetails()
    {
        var plan = LocalAccountPlanner.Create(
            new AppOptions { UserName = "Analyst" },
            @"C:\Work\Budget.xlsx",
            "Budget.xlsx",
            userNameProvider: () => "quinn",
            userDomainProvider: () => "DESKTOP",
            machineNameProvider: () => "FREEX-PC",
            optionsPathProvider: () => "options.json",
            fileExists: _ => true);

        var textResolver = new Free.Shared.Localization.ResourceKeyTextResolver(UiText.Get, UiText.Format);
        var message = FreeX.App.Presentation.Shell.DeferredCommandMessageResolver.Resolve(
            FreeX.App.Presentation.Shell.DeferredCommandMessagePlanner.LocalAccountInfo(),
            textResolver,
            body => LocalAccountWorkflowPlanner.FormatMessageBody(plan, body));

        message.Body.Should().Contain("Microsoft account integration is not implemented");
        message.Body.Should().Contain("FreeX user name: Analyst");
        message.Body.Should().Contain(@"Local OS account: DESKTOP\quinn");
        message.Body.Should().Contain("Sharing: Ready for Windows Share");
        message.Body.Should().Contain("Export: Ready for local PDF/XPS export");
        message.Body.Should().NotContain("Microsoft 365 services");
    }

    private static LocalAccountDetail Detail(
        LocalAccountPlan plan,
        FreeXBackstageAccountDetailId id)
    {
        var labelKey = FreeXBackstagePaneCatalog.BuildAccountDetails()
            .Single(detail => detail.Id == id)
            .LabelKey;
        var label = UiText.Get(labelKey);

        return plan.Details.Single(detail => detail.Label == label);
    }
}
