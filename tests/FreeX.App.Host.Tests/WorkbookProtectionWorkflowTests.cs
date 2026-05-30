using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookProtectionWorkflowTests
{
    [Fact]
    public void CreateCommand_ForUnprotectedWorkbook_ProtectsWithPasswordPromptResult()
    {
        var workbook = new Workbook("test");

        var action = WorkbookProtectionWorkflow.CreateCommand(workbook, "secret");

        action.Title.Should().Be(UiText.Get("MainWindowMessage_ProtectWorkbookTitle"));
        action.SuccessMessage.Should().Contain("protected");
        action.Command.Should().BeOfType<ProtectWorkbookCommand>();
    }

    [Fact]
    public void CreateCommand_ForProtectedWorkbook_UnprotectsWithoutNewPassword()
    {
        var workbook = new Workbook("test")
        {
            IsStructureProtected = true,
            StructureProtectionPassword = "secret"
        };

        var action = WorkbookProtectionWorkflow.CreateCommand(workbook, "new-password-should-be-ignored");

        action.Title.Should().Be("Unprotect Workbook");
        action.SuccessMessage.Should().Contain("unprotected");
        action.Command.Should().BeOfType<UnprotectWorkbookCommand>();
    }

    [Fact]
    public void GetUiText_ForUnprotectedWorkbook_ShowsProtectWorkbook()
    {
        var workbook = new Workbook("test");

        var uiText = WorkbookProtectionWorkflow.GetUiText(workbook);

        uiText.ButtonContent.Should().Be(UiText.Get("MainWindow_Content_ProtectWorkbook"));
        uiText.TooltipTitle.Should().Be(UiText.Get("MainWindow_TooltipTitle_ProtectWorkbook"));
        uiText.TooltipDescription.Should().Be(UiText.Get("MainWindow_TooltipDescription_PreventStructuralChangesToTheWorkbookSuchAsAddingDeletingOrRenamingSheet_47267D4F"));
    }

    [Fact]
    public void GetUiText_ForProtectedWorkbook_ShowsUnprotectWorkbook()
    {
        var workbook = new Workbook("test")
        {
            IsStructureProtected = true
        };

        var uiText = WorkbookProtectionWorkflow.GetUiText(workbook);

        uiText.ButtonContent.Should().Be("Unprotect Workbook");
        uiText.TooltipTitle.Should().Be("Unprotect Workbook");
        uiText.TooltipDescription.Should().Contain("Allow");
    }

    [Fact]
    public void ProtectWorkbookDialogPrompt_UsesPasswordAccessKey()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        UiText.Get("MainWindowMessage_OptionalPasswordLabel")
            .Should().Contain("_", "the password prompt should expose an access key");
        source.Should().Contain("new PasswordProtectionDialog(");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ProtectWorkbookTitle\"),");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_OptionalPasswordLabel\"))");
    }
}
