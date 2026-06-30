using FluentAssertions;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class ProtectionWorkflowPlannerTests
{
    [Fact]
    public void CreateSheetChromePlan_ExposesProtectAndUnprotectResourceKeys()
    {
        ProtectionWorkflowPlanner.CreateSheetChromePlan(isProtected: false)
            .Should()
            .Be(new ProtectionChromePlan(
                "MainWindow_Content_ProtectSheet",
                "MainWindow_TooltipTitle_ProtectSheet",
                "MainWindow_TooltipDescription_SetSheetProtectionForLockedCellsWithAnOptionalPassword"));

        ProtectionWorkflowPlanner.CreateSheetChromePlan(isProtected: true)
            .Should()
            .Be(new ProtectionChromePlan(
                "Protection_UnprotectSheetButton",
                "Protection_UnprotectSheetTitle",
                "Protection_UnprotectSheetDescription"));
    }

    [Fact]
    public void CreateSheetCommandPlan_ForUnprotectedSheet_CarriesProtectIntentAndMessageKeys()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var plan = ProtectionWorkflowPlanner.CreateSheetCommandPlan(
            sheetId,
            isProtected: false,
            password: "secret",
            [
                SheetProtectionPermission.Sort,
                SheetProtectionPermission.SelectUnlockedCells,
                SheetProtectionPermission.Sort
            ]);

        plan.CommandIntent.Should().Be(ProtectionCommandIntent.ProtectSheet);
        plan.SheetId.Should().Be(sheetId);
        plan.Password.Should().Be("secret");
        plan.Permissions.Should().Equal(
            SheetProtectionPermission.SelectUnlockedCells,
            SheetProtectionPermission.Sort);
        plan.TitleResourceKey.Should().Be("MainWindowMessage_ProtectSheetTitle");
        plan.SuccessMessageResourceKey.Should().Be("Protection_SheetProtectedMessage");
    }

    [Fact]
    public void CreateSheetCommandPlan_ForProtectedSheet_CarriesUnprotectIntentAndNoPermissions()
    {
        var plan = ProtectionWorkflowPlanner.CreateSheetCommandPlan(
            new SheetId(Guid.NewGuid()),
            isProtected: true,
            password: "secret",
            [SheetProtectionPermission.Sort]);

        plan.CommandIntent.Should().Be(ProtectionCommandIntent.UnprotectSheet);
        plan.Password.Should().Be("secret");
        plan.Permissions.Should().BeEmpty();
        plan.TitleResourceKey.Should().Be("Protection_UnprotectSheetTitle");
        plan.SuccessMessageResourceKey.Should().Be("Protection_SheetUnprotectedMessage");
    }

    [Fact]
    public void CreateWorkbookCommandPlan_ExposesProtectAndUnprotectIntents()
    {
        var protect = ProtectionWorkflowPlanner.CreateWorkbookCommandPlan(
            isStructureProtected: false,
            password: "secret");

        protect.CommandIntent.Should().Be(ProtectionCommandIntent.ProtectWorkbook);
        protect.TitleResourceKey.Should().Be("MainWindowMessage_ProtectWorkbookTitle");
        protect.SuccessMessageResourceKey.Should().Be("Protection_WorkbookProtectedMessage");

        var unprotect = ProtectionWorkflowPlanner.CreateWorkbookCommandPlan(
            isStructureProtected: true,
            password: "secret");

        unprotect.CommandIntent.Should().Be(ProtectionCommandIntent.UnprotectWorkbook);
        unprotect.TitleResourceKey.Should().Be("Protection_UnprotectWorkbookTitle");
        unprotect.SuccessMessageResourceKey.Should().Be("Protection_WorkbookUnprotectedMessage");
    }

    [Fact]
    public void HostProtectionWorkflows_DoNotOwnResourceKeySelection()
    {
        var hostRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");
        var sheetWorkflow = File.ReadAllText(Path.Combine(hostRoot, "SheetProtectionWorkflow.cs"));
        var workbookWorkflow = File.ReadAllText(Path.Combine(hostRoot, "WorkbookProtectionWorkflow.cs"));

        sheetWorkflow.Should().Contain("ProtectionWorkflowPlanner.CreateSheetChromePlan(sheet.IsProtected)");
        sheetWorkflow.Should().Contain("ProtectionWorkflowPlanner.CreateSheetCommandPlan(");
        sheetWorkflow.Should().NotContain("Protection_SheetProtectedMessage");
        sheetWorkflow.Should().NotContain("MainWindowMessage_ProtectSheetTitle");
        sheetWorkflow.Should().NotContain("if (sheet.IsProtected)");

        workbookWorkflow.Should().Contain("ProtectionWorkflowPlanner.CreateWorkbookChromePlan(workbook.IsStructureProtected)");
        workbookWorkflow.Should().Contain("ProtectionWorkflowPlanner.CreateWorkbookCommandPlan(");
        workbookWorkflow.Should().NotContain("Protection_WorkbookProtectedMessage");
        workbookWorkflow.Should().NotContain("MainWindowMessage_ProtectWorkbookTitle");
        workbookWorkflow.Should().NotContain("if (workbook.IsStructureProtected)");
    }
}
