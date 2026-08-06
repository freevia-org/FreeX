using FluentAssertions;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class ProtectionShellGlueTests
{
    [Fact]
    public void SharedSessionProjectsAndProtectsSheetWithCanonicalPermissions()
    {
        var (workbook, sheet) = CreateWorkbook();
        var session = CreateSession(workbook);
        var options = ProtectSheetOptions.FromCorePermissions(
            [SheetProtectionPermission.Sort, SheetProtectionPermission.SelectLockedCells],
            password: "hunter2",
            passwordConfirmation: "hunter2");

        session.ProjectSheet(sheet).IsProtected.Should().BeFalse();
        var outcome = session.ExecuteSheet(sheet, options);

        outcome.Success.Should().BeTrue();
        outcome.SuccessStatusResourceKey.Should().Be("ShellLoc_ProtectedSheet");
        sheet.IsProtected.Should().BeTrue();
        sheet.ProtectionPermissions.Should().Equal(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.Sort);
        ProtectionPasswordHelper.VerifyStoredPassword(sheet.ProtectionPassword, "hunter2").Should().BeTrue();
    }

    [Fact]
    public void SharedSessionReturnsCommandErrorAndKeepsDialogOpenStateForWrongSheetPassword()
    {
        var (workbook, sheet) = CreateWorkbook();
        var session = CreateSession(workbook);
        session.ExecuteSheet(sheet, "secret").Success.Should().BeTrue();

        var outcome = session.ExecuteSheet(
            sheet,
            ProtectSheetOptions.Default with { Password = "wrong" });

        outcome.Success.Should().BeFalse();
        outcome.Executed.Should().BeTrue();
        outcome.StateChanged.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("password");
        outcome.ErrorResourceKey.Should().Be("ShellLoc_CouldNotUnprotectSheet");
        sheet.IsProtected.Should().BeTrue();
    }

    [Fact]
    public void SharedSessionValidatesConfirmationBeforeCommandExecution()
    {
        var (workbook, sheet) = CreateWorkbook();
        var executions = 0;
        var session = new ProtectionWorkflowSession(
            workbook,
            (_, _) =>
            {
                executions++;
                return new ProtectionCommandExecutionResult(true);
            });

        var outcome = session.ExecuteSheet(
            sheet,
            ProtectSheetOptions.Default with
            {
                Password = "abc",
                PasswordConfirmation = "different",
            });

        outcome.Success.Should().BeFalse();
        outcome.Executed.Should().BeFalse();
        outcome.ErrorResourceKey.Should().Be("ShellLoc_PasswordsDoNotMatch");
        executions.Should().Be(0);
    }

    [Fact]
    public void SharedSessionProtectsWorkbookAndAppliesWindowsSelection()
    {
        var (workbook, _) = CreateWorkbook();
        var session = CreateSession(workbook);
        var options = ProtectWorkbookOptions.Default with
        {
            ProtectWindows = true,
            Password = "pw",
            PasswordConfirmation = "pw",
        };

        var outcome = session.ExecuteWorkbook(options);

        outcome.Success.Should().BeTrue();
        workbook.IsStructureProtected.Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(workbook.StructureProtectionPassword, "pw").Should().BeTrue();
        workbook.ProtectionMetadata!.Get("workbookProtection").Should().Contain("lockWindows=\"1\"");
    }

    [Fact]
    public void SharedSessionPreservesWindowsOnlyPasswordDroppingSemantics()
    {
        var (workbook, _) = CreateWorkbook();
        var executions = 0;
        var session = new ProtectionWorkflowSession(
            workbook,
            (_, _) =>
            {
                executions++;
                return new ProtectionCommandExecutionResult(true);
            });
        var options = ProtectWorkbookOptions.Default with
        {
            ProtectStructure = false,
            ProtectWindows = true,
            Password = "pw",
            PasswordConfirmation = "pw",
        };

        var outcome = session.ExecuteWorkbook(options);
        var plan = ProtectionWorkflowSession.CreateWorkbookCommandPlan(workbook, options);

        outcome.Success.Should().BeFalse();
        outcome.Executed.Should().BeFalse();
        outcome.ErrorResourceKey.Should().Be("ShellLoc_SelectStructureOrWindows");
        plan.NormalizedPassword.Should().BeNull();
        executions.Should().Be(0);
        workbook.IsStructureProtected.Should().BeFalse();
        workbook.StructureProtectionPassword.Should().BeNull();
        workbook.ProtectionMetadata.Should().BeNull();
    }

    [Fact]
    public void AvaloniaProtectionRendererHasNoLocalBehaviorOwnerOrEnglishPermissionSwitch()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "MainWindow.Protection.cs"));
        var gluePath = Path.Combine(root, "src", "FreeX.App.Avalonia", "Dialogs", "ProtectionShellGlue.cs");

        source.Should().Contain("ProtectionSession.ExecuteSheet(");
        source.Should().Contain("ProtectionSession.ExecuteWorkbook(");
        source.Should().Contain("Content = UiText.Get(option.LabelKey)");
        source.Should().NotContain("new ProtectSheetCommand");
        source.Should().NotContain("new UnprotectSheetCommand");
        source.Should().NotContain("new ProtectWorkbookCommand");
        source.Should().NotContain("new UnprotectWorkbookCommand");
        source.Should().NotContain("Select locked cells");
        source.Should().NotContain("Use PivotTable and PivotChart reports");
        source.Should().NotContain("ApplyWorkbookLockWindows");
        source.Should().NotContain("ProtectText(");
        File.Exists(gluePath).Should().BeFalse();
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        return (workbook, workbook.AddSheet("Sheet1"));
    }

    private static ProtectionWorkflowSession CreateSession(Workbook workbook) =>
        new(
            workbook,
            (command, _) =>
            {
                var result = command.Apply(new ProtectionTestCommandContext(workbook));
                return new ProtectionCommandExecutionResult(
                    result.Success,
                    result.ErrorMessage,
                    result.IsNoOp);
            });

    private sealed class ProtectionTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException();
    }
}
