using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class SheetProtectionWorkflowTests
{
    [Fact]
    public void CreateCommand_ForUnprotectedSheet_ProtectsWithPasswordPromptResult()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var action = SheetProtectionWorkflow.CreateCommand(sheet, "secret");

        action.Title.Should().Be(UiText.Get("MainWindowMessage_ProtectSheetTitle"));
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

        action.Title.Should().Be(UiText.Get("MainWindowMessage_ProtectSheetTitle"));
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

        uiText.ButtonContent.Should().Be(UiText.Get("MainWindow_Content_ProtectSheet"));
        uiText.TooltipTitle.Should().Be(UiText.Get("MainWindow_TooltipTitle_ProtectSheet"));
        uiText.TooltipDescription.Should().Be(UiText.Get("MainWindow_TooltipDescription_SetSheetProtectionForLockedCellsWithAnOptionalPassword"));
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

    [Fact]
    public void ProtectSheetDialogPrompt_UsesPasswordAccessKey()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));

        UiText.Get("MainWindowMessage_OptionalPasswordLabel")
            .Should().Contain("_", "the password prompt should expose an access key");
        source.Should().Contain("new PasswordProtectionDialog(");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ProtectSheetTitle\"),");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_OptionalPasswordLabel\"))");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
