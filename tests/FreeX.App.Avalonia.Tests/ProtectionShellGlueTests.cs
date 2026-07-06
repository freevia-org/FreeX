using FluentAssertions;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the non-UI glue backing the Avalonia Protect Sheet and Protect Workbook dialogs: projecting
/// the current Core protection state into the portable dialog models, mapping validated dialog options onto the
/// Core protect/unprotect commands, and the password confirm-match validation that gates the protect action.
/// The commands are run against a workbook through a minimal command context to assert their effect
/// (permissions / password / structure). No running UI is required.
/// </summary>
public sealed class ProtectionShellGlueTests
{
    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static CommandOutcome Run(Workbook workbook, IWorkbookCommand command) =>
        command.Apply(new ProtectionTestCommandContext(workbook));

    // ── Project sheet/workbook state ──────────────────────────────────────────

    [Fact]
    public void ProjectSheet_ForUnprotectedSheet_ReturnsDefaultsAndNotProtected()
    {
        var (_, sheet) = CreateWorkbook();

        var state = ProtectionShellGlue.ProjectSheet(sheet);

        state.IsProtected.Should().BeFalse();
        state.HasPassword.Should().BeFalse();
        state.Options.EnabledPermissions.Should().Equal(SheetProtectionOptions.DefaultEnabledPermissions);
    }

    [Fact]
    public void ProjectSheet_ForProtectedSheet_ReflectsStoredPermissionsAndPassword()
    {
        var (workbook, sheet) = CreateWorkbook();
        Run(workbook, new ProtectSheetCommand(
            sheet.Id,
            "secret",
            [SheetProtectionPermission.FormatCells, SheetProtectionPermission.Sort]));

        var state = ProtectionShellGlue.ProjectSheet(sheet);

        state.IsProtected.Should().BeTrue();
        state.HasPassword.Should().BeTrue();
        state.Options.EnabledPermissions.Should().Equal(
            SheetProtectionPermission.FormatCells,
            SheetProtectionPermission.Sort);
    }

    [Fact]
    public void ProjectWorkbook_ReflectsStructureProtectionState()
    {
        var (workbook, _) = CreateWorkbook();

        ProtectionShellGlue.ProjectWorkbook(workbook).IsStructureProtected.Should().BeFalse();

        Run(workbook, new ProtectWorkbookCommand("pw"));

        var state = ProtectionShellGlue.ProjectWorkbook(workbook);
        state.IsStructureProtected.Should().BeTrue();
        state.HasPassword.Should().BeTrue();
    }

    // ── Build protect-sheet command from options ──────────────────────────────

    [Fact]
    public void BuildProtectSheetCommand_AppliesPermissionsAndPassword()
    {
        var (workbook, sheet) = CreateWorkbook();
        var options = ProtectSheetOptions.FromCorePermissions(
            [SheetProtectionPermission.SelectLockedCells, SheetProtectionPermission.FormatCells],
            password: "hunter2",
            passwordConfirmation: "hunter2");

        var command = ProtectionShellGlue.BuildProtectSheetCommand(sheet.Id, options);
        var outcome = Run(workbook, command);

        outcome.Success.Should().BeTrue();
        sheet.IsProtected.Should().BeTrue();
        // N57: the command hashes the typed password at Apply-time rather than storing it raw, so
        // the stored value must be verified via the helper, not compared to the plaintext directly.
        sheet.ProtectionPassword.Should().NotBe("hunter2");
        ProtectionPasswordHelper.VerifyStoredPassword(sheet.ProtectionPassword, "hunter2").Should().BeTrue();
        sheet.ProtectionPermissions.Should().Equal(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.FormatCells);
    }

    [Fact]
    public void BuildProtectSheetCommand_WithoutPassword_LeavesStoredPasswordNull()
    {
        var (workbook, sheet) = CreateWorkbook();
        var options = ProtectSheetOptions.Default;

        Run(workbook, ProtectionShellGlue.BuildProtectSheetCommand(sheet.Id, options));

        sheet.IsProtected.Should().BeTrue();
        sheet.ProtectionPassword.Should().BeNull();
        sheet.ProtectionPermissions.Should().Equal(SheetProtectionOptions.DefaultEnabledPermissions);
    }

    // ── Build unprotect-sheet command ─────────────────────────────────────────

    [Fact]
    public void BuildUnprotectSheetCommand_WithCorrectPassword_RemovesProtection()
    {
        var (workbook, sheet) = CreateWorkbook();
        Run(workbook, new ProtectSheetCommand(sheet.Id, "secret"));

        var outcome = Run(workbook, ProtectionShellGlue.BuildUnprotectSheetCommand(sheet.Id, "secret"));

        outcome.Success.Should().BeTrue();
        sheet.IsProtected.Should().BeFalse();
        sheet.ProtectionPassword.Should().BeNull();
    }

    [Fact]
    public void BuildUnprotectSheetCommand_WithWrongPassword_Fails()
    {
        var (workbook, sheet) = CreateWorkbook();
        Run(workbook, new ProtectSheetCommand(sheet.Id, "secret"));

        var outcome = Run(workbook, ProtectionShellGlue.BuildUnprotectSheetCommand(sheet.Id, "nope"));

        outcome.Success.Should().BeFalse();
        sheet.IsProtected.Should().BeTrue();
    }

    // ── Build protect/unprotect-workbook command ──────────────────────────────

    [Fact]
    public void BuildProtectWorkbookCommand_ProtectsStructureWithPassword()
    {
        var (workbook, _) = CreateWorkbook();
        var options = new ProtectWorkbookOptions
        {
            ProtectStructure = true,
            ProtectWindows = true,
            Password = "pw",
            PasswordConfirmation = "pw",
        };

        var outcome = Run(workbook, ProtectionShellGlue.BuildProtectWorkbookCommand(options));

        outcome.Success.Should().BeTrue();
        workbook.IsStructureProtected.Should().BeTrue();
        // N57: the command hashes the typed password at Apply-time rather than storing it raw, so
        // the stored value must be verified via the helper, not compared to the plaintext directly.
        workbook.StructureProtectionPassword.Should().NotBe("pw");
        ProtectionPasswordHelper.VerifyStoredPassword(workbook.StructureProtectionPassword, "pw").Should().BeTrue();
    }

    [Fact]
    public void BuildUnprotectWorkbookCommand_WithCorrectPassword_RemovesProtection()
    {
        var (workbook, _) = CreateWorkbook();
        Run(workbook, new ProtectWorkbookCommand("pw"));

        var outcome = Run(workbook, ProtectionShellGlue.BuildUnprotectWorkbookCommand("pw"));

        outcome.Success.Should().BeTrue();
        workbook.IsStructureProtected.Should().BeFalse();
        workbook.StructureProtectionPassword.Should().BeNull();
    }

    [Fact]
    public void BuildUnprotectWorkbookCommand_WithWrongPassword_Fails()
    {
        var (workbook, _) = CreateWorkbook();
        Run(workbook, new ProtectWorkbookCommand("pw"));

        var outcome = Run(workbook, ProtectionShellGlue.BuildUnprotectWorkbookCommand("wrong"));

        outcome.Success.Should().BeFalse();
        workbook.IsStructureProtected.Should().BeTrue();
    }

    // ── Password confirm-match validation ─────────────────────────────────────

    [Fact]
    public void ValidatePassword_OnSheetOptions_RejectsMismatchedConfirmation()
    {
        var options = ProtectSheetOptions.FromCorePermissions(
            SheetProtectionOptions.DefaultEnabledPermissions,
            password: "abc",
            passwordConfirmation: "xyz");

        var validation = options.ValidatePassword();

        validation.IsValid.Should().BeFalse();
        validation.ConfirmationMismatch.Should().BeTrue();
    }

    [Fact]
    public void ValidatePassword_OnSheetOptions_AcceptsMatchingConfirmation()
    {
        var options = ProtectSheetOptions.FromCorePermissions(
            SheetProtectionOptions.DefaultEnabledPermissions,
            password: "abc",
            passwordConfirmation: "abc");

        options.ValidatePassword().IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidatePassword_OnWorkbookOptions_RejectsMismatchedConfirmation()
    {
        var options = new ProtectWorkbookOptions
        {
            Password = "abc",
            PasswordConfirmation = "different",
        };

        options.ValidatePassword().ConfirmationMismatch.Should().BeTrue();
    }

    [Fact]
    public void DescribePermission_ReturnsHumanReadableLabels()
    {
        ProtectionShellGlue.DescribePermission(SheetProtectionPermission.SelectLockedCells)
            .Should().Be("Select locked cells");
        ProtectionShellGlue.DescribePermission(SheetProtectionPermission.UsePivotTableReports)
            .Should().Be("Use PivotTable and PivotChart reports");
    }

    /// <summary>A minimal <see cref="ICommandContext"/> for running protection commands against a workbook.</summary>
    private sealed class ProtectionTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
