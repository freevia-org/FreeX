using FluentAssertions;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class ProtectionWorkflowSessionTests
{
    [Fact]
    public void ChromePlansExposeProtectAndUnprotectResourceKeys()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");

        ProtectionWorkflowSession.CreateSheetChromePlan(sheet)
            .ButtonContentResourceKey.Should().Be("MainWindow_Content_ProtectSheet");
        ProtectionWorkflowSession.CreateWorkbookChromePlan(workbook)
            .ButtonContentResourceKey.Should().Be("MainWindow_Content_ProtectWorkbook");

        sheet.IsProtected = true;
        workbook.IsStructureProtected = true;

        ProtectionWorkflowSession.CreateSheetChromePlan(sheet)
            .ButtonContentResourceKey.Should().Be("Protection_UnprotectSheetButton");
        ProtectionWorkflowSession.CreateWorkbookChromePlan(workbook)
            .ButtonContentResourceKey.Should().Be("Protection_UnprotectWorkbookButton");
    }

    [Fact]
    public void SheetPlanNormalizesPasswordAndPermissionsAndComposesProtectCommand()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var options = ProtectSheetOptions.FromCorePermissions(
            [
                SheetProtectionPermission.Sort,
                SheetProtectionPermission.SelectUnlockedCells,
                SheetProtectionPermission.Sort,
                (SheetProtectionPermission)999,
            ],
            password: "secret",
            passwordConfirmation: "secret");

        var plan = ProtectionWorkflowSession.CreateSheetCommandPlan(sheet, options);

        plan.CanExecute.Should().BeTrue();
        plan.CommandIntent.Should().Be(ProtectionCommandIntent.ProtectSheet);
        plan.Command.Should().BeOfType<ProtectSheetCommand>();
        plan.NormalizedPassword.Should().Be("secret");
        plan.Permissions.Should().Equal(
            SheetProtectionPermission.SelectUnlockedCells,
            SheetProtectionPermission.Sort);
        plan.SuccessStatusResourceKey.Should().Be("ShellLoc_ProtectedSheet");

        plan.Command!.Apply(new TestContext(workbook)).Success.Should().BeTrue();
        sheet.ProtectionPermissions.Should().Equal(plan.Permissions);
        ProtectionPasswordHelper.VerifyStoredPassword(sheet.ProtectionPassword, "secret").Should().BeTrue();
    }

    [Fact]
    public void SheetPlanDropsEmptyPasswordAndRejectsMismatchedConfirmationWithoutExecuting()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var executions = 0;
        var session = new ProtectionWorkflowSession(
            workbook,
            (_, _) =>
            {
                executions++;
                return new ProtectionCommandExecutionResult(true);
            });

        var emptyPlan = ProtectionWorkflowSession.CreateSheetCommandPlan(sheet, "");
        var mismatch = session.ExecuteSheet(
            sheet,
            ProtectSheetOptions.Default with
            {
                Password = "one",
                PasswordConfirmation = "two",
            });

        emptyPlan.NormalizedPassword.Should().BeNull();
        mismatch.Success.Should().BeFalse();
        mismatch.Executed.Should().BeFalse();
        mismatch.Issue.Should().Be(ProtectionWorkflowIssue.PasswordConfirmationMismatch);
        mismatch.ErrorResourceKey.Should().Be("ShellLoc_PasswordsDoNotMatch");
        executions.Should().Be(0);
    }

    [Fact]
    public void ProtectedSheetPlanComposesUnprotectAndIgnoresPermissionProjection()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        new ProtectSheetCommand(sheet.Id, "secret").Apply(new TestContext(workbook));

        var plan = ProtectionWorkflowSession.CreateSheetCommandPlan(
            sheet,
            ProtectSheetOptions.FromCorePermissions(
                [SheetProtectionPermission.Sort],
                password: "secret"));

        plan.CommandIntent.Should().Be(ProtectionCommandIntent.UnprotectSheet);
        plan.Command.Should().BeOfType<UnprotectSheetCommand>();
        plan.Permissions.Should().BeEmpty();
        plan.Command!.Apply(new TestContext(workbook)).Success.Should().BeTrue();
        sheet.IsProtected.Should().BeFalse();
    }

    [Fact]
    public void WorkbookPlanRejectsWindowsOnlyAndDropsItsPassword()
    {
        var workbook = new Workbook("Book");
        var options = ProtectWorkbookOptions.Default with
        {
            ProtectStructure = false,
            ProtectWindows = true,
            Password = "secret",
            PasswordConfirmation = "secret",
        };

        var plan = ProtectionWorkflowSession.CreateWorkbookCommandPlan(workbook, options);

        plan.CanExecute.Should().BeFalse();
        plan.Command.Should().BeNull();
        plan.NormalizedPassword.Should().BeNull();
        plan.Issue.Should().Be(ProtectionWorkflowIssue.WorkbookStructureRequired);
    }

    [Fact]
    public void WorkbookDialogPlanAppliesWindowsMetadataAndReplaysItAcrossUndoRedo()
    {
        var workbook = new Workbook("Book");
        var context = new TestContext(workbook);
        var originalBag = new NativeXmlPreserveBag();
        originalBag.Set("workbookProtection", "<e lockRevision=\"1\"/>");
        workbook.ProtectionMetadata = originalBag;
        var options = ProtectWorkbookOptions.Default with
        {
            ProtectWindows = true,
            Password = "secret",
            PasswordConfirmation = "secret",
        };
        var command = ProtectionWorkflowSession.CreateWorkbookCommandPlan(workbook, options).Command!;

        command.Apply(context).Success.Should().BeTrue();
        workbook.ProtectionMetadata.Should().NotBeSameAs(originalBag);
        workbook.ProtectionMetadata!.Get("workbookProtection").Should().Contain("lockRevision=\"1\"");
        workbook.ProtectionMetadata.Get("workbookProtection").Should().Contain("lockWindows=\"1\"");
        originalBag.Get("workbookProtection").Should().NotContain("lockWindows");

        command.Revert(context);
        workbook.IsStructureProtected.Should().BeFalse();
        workbook.ProtectionMetadata.Should().BeSameAs(originalBag);

        command.Apply(context).Success.Should().BeTrue();
        workbook.ProtectionMetadata!.Get("workbookProtection").Should().Contain("lockWindows=\"1\"");
    }

    [Fact]
    public void WpfStyleWorkbookPlanPreservesExistingWindowsMetadata()
    {
        var workbook = new Workbook("Book");
        var bag = new NativeXmlPreserveBag();
        bag.Set("workbookProtection", "<e lockWindows=\"1\"/>");
        workbook.ProtectionMetadata = bag;

        var command = ProtectionWorkflowSession.CreateWorkbookCommandPlan(workbook, "secret").Command!;
        command.Apply(new TestContext(workbook)).Success.Should().BeTrue();

        workbook.ProtectionMetadata!.Get("workbookProtection").Should().Contain("lockWindows=\"1\"");
    }

    [Fact]
    public void WindowsOnlyWorkbookWithStoredPasswordProjectsAndPlansUnprotect()
    {
        var workbook = new Workbook("Book")
        {
            IsStructureProtected = false,
            StructureProtectionPassword = ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash("secret"),
        };

        var chrome = ProtectionWorkflowSession.CreateWorkbookChromePlan(workbook);
        var plan = ProtectionWorkflowSession.CreateWorkbookCommandPlan(workbook, "secret");

        chrome.ButtonContentResourceKey.Should().Be("Protection_UnprotectWorkbookButton");
        plan.CommandIntent.Should().Be(ProtectionCommandIntent.UnprotectWorkbook);
        plan.Command!.Apply(new TestContext(workbook)).Success.Should().BeTrue();
        workbook.StructureProtectionPassword.Should().BeNull();
    }

    [Fact]
    public void SessionReturnsSharedSuccessAndCommandFailureOutcomes()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var successSession = new ProtectionWorkflowSession(
            workbook,
            (command, _) =>
            {
                var result = command.Apply(new TestContext(workbook));
                return new ProtectionCommandExecutionResult(result.Success, result.ErrorMessage, result.IsNoOp);
            });

        var success = successSession.ExecuteSheet(sheet, password: null);

        success.Success.Should().BeTrue();
        success.Executed.Should().BeTrue();
        success.StateChanged.Should().BeTrue();
        success.SuccessMessageResourceKey.Should().Be("Protection_SheetProtectedMessage");

        var failureSession = new ProtectionWorkflowSession(
            workbook,
            (_, _) => new ProtectionCommandExecutionResult(false, "command failed"));
        var failure = failureSession.ExecuteSheet(sheet, password: "wrong");

        failure.Success.Should().BeFalse();
        failure.Executed.Should().BeTrue();
        failure.ErrorMessage.Should().Be("command failed");
        failure.ErrorResourceKey.Should().Be("ShellLoc_CouldNotUnprotectSheet");
    }

    private sealed class TestContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException();
    }
}
