using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookCellEditProtectionGuardOwnershipTests
{
    [Fact]
    public void GoalSeekPrevalidation_UsesTheAuthoritativeCommandGuard()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "WorkbookCellEditService.cs");

        source.Should().Contain(
            "CommandGuards.CanEditCell(workbook, changingSheet, request.ChangingCell)");
        source.Should().NotContain("private static bool CanEditCell(");
        source.Should().NotContain("sheet.AllowEditRanges");
    }
}
