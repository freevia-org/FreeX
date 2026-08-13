using FluentAssertions;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class ProtectionDialogPlannerTests
{
    [Fact]
    public void ProtectSheetSize_MatchesSharedWpfLogicalEvidenceTarget()
    {
        ProtectionDialogPlanner.ProtectSheetWidth.Should().Be(430);
        ProtectionDialogPlanner.ProtectSheetHeight.Should().Be(540);
    }

    [Fact]
    public void ProtectWorkbookCaptureSize_MatchesSharedVisualEvidenceContract()
    {
        ProtectionDialogPlanner.ProtectWorkbookCaptureWidth.Should().Be(380);
        ProtectionDialogPlanner.ProtectWorkbookCaptureHeight.Should().Be(250);
    }

    [Fact]
    public void CreateSheetResult_ForProtectedSheetRequestsUnprotect()
    {
        var result = ProtectionDialogPlanner.CreateSheetResult(
            isProtected: true,
            password: "ignored",
            selectedSheetPermissions: [SheetProtectionPermission.SelectLockedCells]);

        result.Mode.Should().Be(ProtectionDialogMode.Unprotect);
        result.Password.Should().Be("ignored");
        result.SelectedSheetPermissions.Should().BeEmpty();
    }

    [Fact]
    public void CreateSheetResult_ForUnprotectedSheetKeepsSelectedPermissions()
    {
        var result = ProtectionDialogPlanner.CreateSheetResult(
            isProtected: false,
            password: "secret",
            selectedSheetPermissions:
            [
                SheetProtectionPermission.SelectUnlockedCells,
                SheetProtectionPermission.Sort,
            ]);

        result.Mode.Should().Be(ProtectionDialogMode.Protect);
        result.Password.Should().Be("secret");
        result.SelectedSheetPermissions.Should().Equal(
            SheetProtectionPermission.SelectUnlockedCells,
            SheetProtectionPermission.Sort);
    }

    [Fact]
    public void CreateSheetResult_RejectsMismatchedConfirmation()
    {
        var result = ProtectionDialogPlanner.CreateSheetResult(
            isProtected: false,
            password: "secret",
            confirmation: "Secret",
            defaultSelectedSheetPermissions:
            [
                SheetProtectionPermission.SelectLockedCells,
                SheetProtectionPermission.SelectUnlockedCells,
            ]);

        result.Mode.Should().Be(ProtectionDialogMode.Protect);
        result.Password.Should().BeNull();
        result.SelectedSheetPermissions.Should().Equal(
            SheetProtectionPermission.SelectLockedCells,
            SheetProtectionPermission.SelectUnlockedCells);
    }

    [Fact]
    public void CreateWorkbookResult_ForProtectedWorkbookRequestsUnprotect()
    {
        var result = ProtectionDialogPlanner.CreateWorkbookResult(isStructureProtected: true, password: "ignored");

        result.Mode.Should().Be(ProtectionDialogMode.Unprotect);
        result.Password.Should().Be("ignored");
        result.SelectedSheetPermissions.Should().BeEmpty();
    }
}
