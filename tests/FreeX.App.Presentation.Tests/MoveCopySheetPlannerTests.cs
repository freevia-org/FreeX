using FluentAssertions;

using FreeX.App.Presentation.SheetUI;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Unit tests for the portable planner backing the Move-or-Copy Sheet dialog: target-list building,
/// initial selection, plan clamping, and the move-target index resolution. No running UI.
/// </summary>
public sealed class MoveCopySheetPlannerTests
{
    private static readonly string[] Sheets = ["Jan", "Feb", "Mar"];

    [Fact]
    public void BuildTargets_EmitsOnePerSheetPlusMoveToEnd()
    {
        var targets = MoveCopySheetPlanner.BuildTargets(Sheets, "(move to end)");

        targets.Should().HaveCount(4);
        targets[0].Should().Be(new MoveCopySheetTarget("Jan", 0));
        targets[1].Should().Be(new MoveCopySheetTarget("Feb", 1));
        targets[2].Should().Be(new MoveCopySheetTarget("Mar", 2));
        targets[3].Should().Be(new MoveCopySheetTarget("(move to end)", 3));
    }

    [Fact]
    public void InitialTargetIndex_PicksSourceOwnSlot()
    {
        var targets = MoveCopySheetPlanner.BuildTargets(Sheets, "(move to end)");

        MoveCopySheetPlanner.InitialTargetIndex(targets, sourceIndex: 1).Should().Be(1);
    }

    [Fact]
    public void InitialTargetIndex_FallsBackToMoveToEndWhenSourceMissing()
    {
        var targets = MoveCopySheetPlanner.BuildTargets(Sheets, "(move to end)");

        MoveCopySheetPlanner.InitialTargetIndex(targets, sourceIndex: 99).Should().Be(3);
    }

    [Fact]
    public void InitialTargetIndex_EmptyListReturnsZero()
    {
        MoveCopySheetPlanner.InitialTargetIndex([], sourceIndex: 0).Should().Be(0);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(2, 2)]
    [InlineData(10, 3)]
    public void CreatePlan_ClampsInsertIndexIntoBounds(int requested, int expected)
    {
        var plan = MoveCopySheetPlanner.CreatePlan(requested, createCopy: true, sheetCount: 3);

        plan.InsertBeforeIndex.Should().Be(expected);
        plan.CreateCopy.Should().BeTrue();
    }

    [Theory]
    // Insert before an earlier slot: index is the destination directly.
    [InlineData(2, 0, 3, 0)]
    // Insert before a later slot: source removal shifts the landing left by one.
    [InlineData(0, 2, 3, 1)]
    // Insert at the end (index == count): lands at the last slot.
    [InlineData(0, 3, 3, 2)]
    // Insert before own slot: no movement.
    [InlineData(1, 1, 3, 1)]
    public void ResolveMoveTargetIndex_AccountsForSourceRemoval(
        int sourceIndex,
        int insertBeforeIndex,
        int sheetCount,
        int expected)
    {
        MoveCopySheetPlanner.ResolveMoveTargetIndex(sourceIndex, insertBeforeIndex, sheetCount)
            .Should().Be(expected);
    }

    [Theory]
    // Copying Jan before Mar: MoveActiveSheetTo removes the copy before inserting at the final index.
    [InlineData(0, 2, 3, 2)]
    // Copying Jan to the end of a 3-sheet workbook: the new last index is 3.
    [InlineData(0, 3, 3, 3)]
    // Copying Mar before Jan: destination is still the first slot.
    [InlineData(2, 0, 3, 0)]
    // Copying Feb before itself mirrors Excel's default copy-before-source behavior.
    [InlineData(1, 1, 3, 1)]
    public void ResolveCopyTargetIndex_AccountsForInsertedDuplicate(
        int sourceIndex,
        int insertBeforeIndex,
        int originalSheetCount,
        int expected)
    {
        MoveCopySheetPlanner.ResolveCopyTargetIndex(sourceIndex, insertBeforeIndex, originalSheetCount)
            .Should().Be(expected);
    }

    [Fact]
    public void MoveOrCopySheetDialog_DelegatesTargetAndResultPolicyToPlanner()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "MoveOrCopySheetDialog.cs"));

        source.Should().Contain("MoveCopySheetPlanner.BuildTargets(");
        source.Should().Contain("MoveCopySheetPlanner.InitialTargetIndex(");
        source.Should().Contain("MoveCopySheetPlanner.CreatePlan(");
        source.Should().Contain("public MoveCopySheetPlan Result");
        source.Should().Contain("DisplayMemberPath = nameof(MoveCopySheetTarget.DisplayName)");
        source.Should().NotContain("MoveOrCopySheetDialogResult");
        source.Should().NotContain("private static IEnumerable<MoveOrCopySheetTarget> BuildTargets");
        source.Should().NotContain("private sealed record MoveOrCopySheetTarget");
        source.Should().NotContain("Math.Clamp(insertBeforeIndex");
    }
}
