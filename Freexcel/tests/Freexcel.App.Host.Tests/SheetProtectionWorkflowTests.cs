using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SheetProtectionWorkflowTests
{
    [Fact]
    public void CreateCommand_ForUnprotectedSheet_ProtectsWithPasswordPromptResult()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var action = SheetProtectionWorkflow.CreateCommand(sheet, "secret");

        action.Title.Should().Be("Protect Sheet");
        action.SuccessMessage.Should().Contain("protected");
        action.Command.Should().BeOfType<ProtectSheetCommand>();
    }

    [Fact]
    public void CreateCommand_ForUnprotectedSheet_CarriesSelectedDialogPermissions()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var result = ProtectionDialogPlanner.CreateSheetResult(
            sheet,
            password: "secret",
            selectedSheetPermissions: ["Select unlocked cells", "Sort"]);

        var action = SheetProtectionWorkflow.CreateCommand(sheet, result);

        action.Title.Should().Be("Protect Sheet");
        action.SelectedSheetPermissions.Should().Equal(["Select unlocked cells", "Sort"]);
        action.Command.Apply(new SimpleCtx(workbook)).Success.Should().BeTrue();
        sheet.ProtectionPermissions.Should().Equal(
            SheetProtectionPermission.SelectUnlockedCells,
            SheetProtectionPermission.Sort);
    }

    [Fact]
    public void CreateCommand_ForProtectedSheet_UnprotectsWithoutNewPassword()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "secret";

        var action = SheetProtectionWorkflow.CreateCommand(sheet, "new-password-should-be-ignored");

        action.Title.Should().Be("Unprotect Sheet");
        action.SuccessMessage.Should().Contain("unprotected");
        action.Command.Should().BeOfType<UnprotectSheetCommand>();
    }

    [Fact]
    public void GetUiText_ForUnprotectedSheet_ShowsProtectSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var uiText = SheetProtectionWorkflow.GetUiText(sheet);

        uiText.ButtonContent.Should().Be("Protect Sheet");
        uiText.TooltipTitle.Should().Be("Protect Sheet");
        uiText.TooltipDescription.Should().Contain("Set");
    }

    [Fact]
    public void GetUiText_ForProtectedSheet_ShowsUnprotectSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsProtected = true;

        var uiText = SheetProtectionWorkflow.GetUiText(sheet);

        uiText.ButtonContent.Should().Be("Unprotect Sheet");
        uiText.TooltipTitle.Should().Be("Unprotect Sheet");
        uiText.TooltipDescription.Should().Contain("Remove");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
