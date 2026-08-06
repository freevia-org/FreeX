using System.IO;
using FluentAssertions;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookProtectionWorkflowTests
{
    [Fact]
    public void SharedSessionProtectsWorkbookWithLocalizedOutcome()
    {
        var workbook = new Workbook("test");
        var session = CreateSession(workbook);

        var outcome = session.ExecuteWorkbook("secret");

        outcome.Success.Should().BeTrue();
        UiText.Get(outcome.TitleResourceKey).Should().Be(UiText.Get("MainWindowMessage_ProtectWorkbookTitle"));
        UiText.Get(outcome.SuccessMessageResourceKey).Should().Contain("protected");
        workbook.IsStructureProtected.Should().BeTrue();
        ProtectionPasswordHelper.VerifyStoredPassword(
            workbook.StructureProtectionPassword,
            "secret").Should().BeTrue();
    }

    [Fact]
    public void SharedSessionTreatsStoredWindowsOnlyPasswordAsUnprotectMode()
    {
        var workbook = new Workbook("test")
        {
            IsStructureProtected = false,
            StructureProtectionPassword = ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash("secret"),
        };
        var session = CreateSession(workbook);

        session.ProjectWorkbook().IsStructureProtected.Should().BeTrue();
        var outcome = session.ExecuteWorkbook("secret");

        outcome.Success.Should().BeTrue();
        outcome.CommandIntent.Should().Be(ProtectionCommandIntent.UnprotectWorkbook);
        workbook.StructureProtectionPassword.Should().BeNull();
    }

    [Fact]
    public void SharedChromePlanKeepsWpfLocalizedRenderingAndWindowsOnlyMode()
    {
        var workbook = new Workbook("test");
        var protect = ProtectionWorkflowSession.CreateWorkbookChromePlan(workbook);
        UiText.Get(protect.ButtonContentResourceKey)
            .Should().Be(UiText.Get("MainWindow_Content_ProtectWorkbook"));

        workbook.StructureProtectionPassword = "stored";
        var unprotect = ProtectionWorkflowSession.CreateWorkbookChromePlan(workbook);
        UiText.Get(unprotect.ButtonContentResourceKey)
            .Should().Be(UiText.Get("Protection_UnprotectWorkbookButton"));
    }

    [Fact]
    public void WpfWorkbookProtectionDelegatesBehaviorToSharedSession()
    {
        var reviewSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");
        var sessionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ProtectionWorkflowSession.cs");

        reviewSource.Should().Contain("ProtectionSession.ProjectWorkbook()");
        reviewSource.Should().Contain("ProtectionSession.ExecuteWorkbook(pwd)");
        reviewSource.Should().NotContain("new ProtectWorkbookCommand");
        reviewSource.Should().NotContain("new UnprotectWorkbookCommand");
        sessionSource.Should().Contain("ProtectionWorkflowSession");
        var root = WorkspaceFileLocator.FindWorkspaceRoot();
        File.Exists(Path.Combine(root, "src", "FreeX.App.Host", "WorkbookProtectionWorkflow.cs"))
            .Should().BeFalse();
    }

    [Fact]
    public void ProtectWorkbookDialogPromptUsesPasswordAccessKey()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ReviewCommands.cs");

        UiText.Get("MainWindowMessage_OptionalPasswordLabel").Should().Contain("_");
        source.Should().Contain("new PasswordProtectionDialog(");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ProtectWorkbookTitle\"),");
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
