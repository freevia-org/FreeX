using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class WorkbookProtectionWorkflowTests
{
    [Fact]
    public void CreateCommand_ForUnprotectedWorkbook_ProtectsWithPasswordPromptResult()
    {
        var workbook = new Workbook("test");

        var action = WorkbookProtectionWorkflow.CreateCommand(workbook, "secret");

        action.Title.Should().Be("Protect Workbook");
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

        uiText.ButtonContent.Should().Be("Protect Workbook");
        uiText.TooltipTitle.Should().Be("Protect Workbook");
        uiText.TooltipDescription.Should().Contain("Prevent");
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
}
