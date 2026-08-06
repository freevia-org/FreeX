using System.IO;
using FluentAssertions;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class SheetProtectionWorkflowTests
{
    [Fact]
    public void SharedSessionProtectsWithLocalizedOutcomeAndSelectedPermissions()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var session = CreateSession(workbook);
        var options = ProtectSheetOptions.FromCorePermissions(
            [SheetProtectionPermission.SelectUnlockedCells, SheetProtectionPermission.Sort],
            password: "secret",
            passwordConfirmation: "secret");

        var outcome = session.ExecuteSheet(sheet, options);

        outcome.Success.Should().BeTrue();
        UiText.Get(outcome.TitleResourceKey).Should().Be(UiText.Get("MainWindowMessage_ProtectSheetTitle"));
        UiText.Get(outcome.SuccessMessageResourceKey).Should().Contain("protected");
        sheet.IsProtected.Should().BeTrue();
        sheet.ProtectionPermissions.Should().Equal(
            SheetProtectionPermission.SelectUnlockedCells,
            SheetProtectionPermission.Sort);
    }

    [Fact]
    public void SharedSessionUnprotectsProtectedSheetWithoutReusingPermissionInput()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var session = CreateSession(workbook);
        session.ExecuteSheet(sheet, "secret").Success.Should().BeTrue();

        var outcome = session.ExecuteSheet(
            sheet,
            ProtectSheetOptions.FromCorePermissions(
                [SheetProtectionPermission.Sort],
                password: "secret"));

        outcome.Success.Should().BeTrue();
        outcome.CommandIntent.Should().Be(ProtectionCommandIntent.UnprotectSheet);
        UiText.Get(outcome.TitleResourceKey).Should().Be(UiText.Get("Protection_UnprotectSheetTitle"));
        sheet.IsProtected.Should().BeFalse();
        sheet.ProtectionPermissions.Should().BeEmpty();
    }

    [Fact]
    public void SharedChromePlanKeepsWpfLocalizedRendering()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var protect = ProtectionWorkflowSession.CreateSheetChromePlan(sheet);
        UiText.Get(protect.ButtonContentResourceKey).Should().Be(UiText.Get("MainWindow_Content_ProtectSheet"));

        sheet.IsProtected = true;
        var unprotect = ProtectionWorkflowSession.CreateSheetChromePlan(sheet);
        UiText.Get(unprotect.ButtonContentResourceKey).Should().Be(UiText.Get("Protection_UnprotectSheetButton"));
    }

    [Fact]
    public void WpfSheetProtectionDelegatesBehaviorToSharedSession()
    {
        var reviewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var dialogSource = DialogSourceTestSupport.ReadHostSources("ProtectionDialogs.cs");

        reviewSource.Should().Contain("ProtectionSession.ProjectSheet(sheet)");
        reviewSource.Should().Contain("ProtectionSession.ExecuteSheet(sheet, options)");
        reviewSource.Should().NotContain("new ProtectSheetCommand");
        reviewSource.Should().NotContain("new UnprotectSheetCommand");
        dialogSource.Should().Contain("foreach (var option in SheetProtectionOptions.All)");
        dialogSource.Should().Contain("Content = UiText.Get(option.LabelKey)");
        var root = WorkspaceFileLocator.FindWorkspaceRoot();
        File.Exists(Path.Combine(root, "src", "FreeX.App.Host", "SheetProtectionWorkflow.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(root, "src", "FreeX.App.Host", "SheetProtectionPermissionLabels.cs"))
            .Should().BeFalse();
    }

    [Fact]
    public void ProtectSheetDialogPromptUsesPasswordAccessKey()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        UiText.Get("MainWindowMessage_OptionalPasswordLabel").Should().Contain("_");
        source.Should().Contain("new PasswordProtectionDialog(");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ProtectSheetTitle\"),");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_OptionalPasswordLabel\"))");
    }

    private static ProtectionWorkflowSession CreateSession(Workbook workbook) =>
        new(
            workbook,
            (command, _) =>
            {
                var result = command.Apply(new TestCommandContext(workbook));
                return new ProtectionCommandExecutionResult(
                    result.Success,
                    result.ErrorMessage,
                    result.IsNoOp);
            });
}
