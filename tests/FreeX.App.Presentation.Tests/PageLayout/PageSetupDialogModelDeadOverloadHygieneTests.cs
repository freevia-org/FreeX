using System.Reflection;

using FluentAssertions;

using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R55-added-but-never-wired-sweep-4: <c>PageSetupDialogModel.TryBuildCommand(Sheet, PageSetupDialogFields)</c>
/// (the single-sheet convenience overload) had zero live callers -- both shells were migrated to
/// <see cref="PageSetupDialogModel.TryBuildCommandPlan(FreeX.Core.Model.Sheet, PageSetupDialogFields)"/>
/// to support cross-sheet Print Titles/Print Area remap, and dedicated source-hygiene tests in each
/// shell (tests/FreeX.App.Host.Tests/PageLayoutCommandSourceTests.cs,
/// tests/FreeX.App.Avalonia.Tests/AvaloniaMainWindowChromeSourceTests.cs) already pin that neither
/// shell calls it. Deleting the orphaned overload (and its now-unused
/// <c>PageSetupCommandBuildResult</c> return type) removes the maintenance trap of a public API that
/// looks callable but is never exercised in production.
/// </summary>
public sealed class PageSetupDialogModelDeadOverloadHygieneTests
{
    [Fact]
    public void PageSetupDialogModel_DoesNotDeclareTheDeadSingleSheetTryBuildCommandOverload()
    {
        var method = typeof(PageSetupDialogModel).GetMethod(
            "TryBuildCommand",
            BindingFlags.Public | BindingFlags.Static);

        method.Should().BeNull(
            "the single-sheet TryBuildCommand overload has no remaining live caller (both shells use " +
            "TryBuildCommandPlan) and should be removed rather than left as an untested, unreachable API");
    }

    [Fact]
    public void PageSetupDialogModel_DoesNotDeclareTheNowUnusedCommandBuildResultType()
    {
        var type = typeof(PageSetupDialogModel).Assembly.GetType(
            "FreeX.App.Presentation.PageLayout.PageSetupCommandBuildResult");

        type.Should().BeNull(
            "PageSetupCommandBuildResult was only ever produced by the now-deleted TryBuildCommand overload");
    }

    // Sibling no-regression: the supported entry point (TryBuildCommandPlan) that TryBuildCommand used
    // to delegate to must still build a working command from valid fields -- deleting the dead
    // convenience wrapper must not regress the underlying validation/build logic it shared.
    [Fact]
    public void PageSetupDialogModel_TryBuildCommandPlan_StillBuildsAWorkingCommandFromValidFields()
    {
        var sheet = new FreeX.Core.Model.Sheet(FreeX.Core.Model.SheetId.New(), "Sheet1");
        var fields = PageSetupDialogModel.FromSheet(sheet) with
        {
            Orientation = FreeX.Core.Model.WorksheetPageOrientation.Landscape,
        };

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeTrue(result.Error);
        result.Plan!.PageSetupCommand.Should().NotBeNull();
    }
}
