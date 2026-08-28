using FreeX.App.Presentation.DefinedNames;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// shared-undo-boundaries F2: the Avalonia "Create Names from Selection" dialog
/// (<c>MainWindow.DefinedNames.cs</c>'s <c>ShowCreateNamesFromSelectionDialogAsync</c>) planned N
/// defined names from the selection via <see cref="DefinedNamesSession.PlanCreateNamesFromSelection"/>
/// and <see cref="DefinedNamesSession.BuildCreateCommands"/>, then executed each of the N resulting
/// <see cref="DefineNamedRangeCommand"/>s as its own, separate call to
/// <c>WorkbookSession.ExecuteReviewCommand</c> inside a foreach loop. Each call pushes its own
/// undo entry, so a single "Create Names from Selection" dialog action left N undo entries on the
/// stack instead of one -- pressing Ctrl+Z once only removed the last-created name, leaving the
/// rest behind. The WPF host's identical feature (<c>MainWindow.FormulaCommands.cs</c>'s
/// <c>CreateNamesFromSelectionBtn_Click</c>) instead builds a single
/// <see cref="CreateNamedRangesFromSelectionCommand"/> and executes it once, so undo removes every
/// name the dialog created in one step, matching Excel.
///
/// The fix wraps the same N <see cref="DefineNamedRangeCommand"/>s <see cref="DefinedNamesSession"/>
/// already builds into one <see cref="CompositeWorkbookCommand"/> (the same helper the WPF host and
/// several other Avalonia dialogs already use to group multi-command edits into a single undo step)
/// and executes that once. These tests exercise the exact mechanism the dialog handler now uses --
/// <see cref="DefinedNamesSession.PlanCreateNamesFromSelection"/>,
/// <see cref="DefinedNamesSession.BuildCreateCommands"/>, <see cref="CompositeWorkbookCommand"/>, and
/// <see cref="WorkbookCellEditService"/>'s undo stack -- directly (rather than driving the live modal
/// <c>Window</c>, which several sibling Avalonia dialog tests in this project document as unsafe to
/// drive headless), so they prove the undo-boundary behavior itself, not just a source string.
/// </summary>
public sealed class SharedUndoBoundariesF2_CreateNamesFromSelectionUndoTests
{
    // ── (1) Source contract: the dialog handler must build one CompositeWorkbookCommand (or a
    // single command when there is exactly one planned name) and execute it once, not loop
    // ExecuteReviewCommand over BuildCreateCommands' results. ──────────────────────────────────

    [Fact]
    public void CreateNamesFromSelectionSource_ExecutesOneCompositeCommand_NotOnePerPlannedName()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromBaseDirectory(
            "src", "FreeX.App.Avalonia", "MainWindow.DefinedNames.cs");

        var start = source.IndexOf(
            "private async Task ShowCreateNamesFromSelectionDialogAsync()",
            StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the dialog method must still exist");
        var end = source.IndexOf(
            "// ── Helpers",
            start,
            StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var dialogMethod = source[start..end];

        dialogMethod.Should().Contain(
            "new CompositeWorkbookCommand(",
            "the handler must group the planned DefineNamedRangeCommands into a single composite " +
            "so the whole dialog action is one undo entry, matching the WPF host's " +
            "CreateNamedRangesFromSelectionCommand");
        dialogMethod.Should().NotContain(
            "foreach (var command in definedNames.BuildCreateCommands(planned))",
            "the buggy per-command loop (one ExecuteReviewCommand call per planned name, one undo " +
            "entry per name) must be gone");

        // Exactly one ExecuteReviewCommand call should remain in this method: the single composite
        // (or lone) command, not one per planned name.
        var callCount = 0;
        var searchFrom = 0;
        while (true)
        {
            var index = dialogMethod.IndexOf("_session.ExecuteReviewCommand(", searchFrom, StringComparison.Ordinal);
            if (index < 0)
                break;
            callCount++;
            searchFrom = index + 1;
        }

        callCount.Should().Be(1, "the dialog must push exactly one undo entry for the whole action");
    }

    // ── (2) Behavioral: wrapping DefinedNamesSession.BuildCreateCommands' output in a
    // CompositeWorkbookCommand (the fixed mechanism) collapses N name creations into one undo
    // step; the old per-command-loop mechanism (reproduced here as the "before" case) leaves N
    // separate undo entries behind, matching the finding's reported break. ─────────────────────

    [Fact]
    public void FixedMechanism_CompositeCommand_UndoesAllCreatedNamesInOneStep()
    {
        var (workbook, sheet, service, planned) = BuildThreeLabelFixture();
        var definedNames = new DefinedNamesSession(workbook, sheet.Id);
        var commands = definedNames.BuildCreateCommands(planned);
        commands.Should().HaveCount(3, "the fixture selects three distinct row labels");

        // Exactly what the fixed MainWindow.DefinedNames.cs handler now does.
        IWorkbookCommand command = commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand("Create Names from Selection", commands);

        var result = service.ExecuteEditCommand(workbook, command);
        result.Success.Should().BeTrue();

        workbook.NamedRanges.Should().ContainKeys("Jan", "Feb", "Mar");
        service.GetUndoStackDepth(workbook.Id).Should().Be(
            1, "the whole dialog action must push exactly one undo entry, matching the WPF host");

        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();

        // A single Ctrl+Z must remove every name the dialog just created, not just the last one.
        workbook.NamedRanges.Should().NotContainKeys("Jan", "Feb", "Mar");
        service.GetUndoStackDepth(workbook.Id).Should().Be(0);
    }

    [Fact]
    public void BuggyMechanism_PerCommandLoop_LeavesPartialNamesAfterOneUndo()
    {
        // No-regression / bug-reproduction sibling: this reconstructs the ORIGINAL per-command
        // loop the finding describes (one ExecuteReviewCommand per BuildCreateCommands entry) to
        // confirm it really does leave N separate undo entries -- i.e. that the fixed test above
        // is actually distinguishing the fix from the bug, not passing either way.
        var (workbook, sheet, service, planned) = BuildThreeLabelFixture();
        var definedNames = new DefinedNamesSession(workbook, sheet.Id);
        var commands = definedNames.BuildCreateCommands(planned);
        commands.Should().HaveCount(3);

        foreach (var command in commands)
        {
            var result = service.ExecuteEditCommand(workbook, command);
            result.Success.Should().BeTrue();
        }

        workbook.NamedRanges.Should().ContainKeys("Jan", "Feb", "Mar");
        service.GetUndoStackDepth(workbook.Id).Should().Be(
            3, "the pre-fix loop pushes one undo entry per created name");

        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();

        // Only the last-created name comes back off; the other two are left behind -- the exact
        // user-visible break the finding reports (Ctrl+Z once does not remove every name).
        workbook.NamedRanges.Keys.Should().HaveCount(2);
        service.GetUndoStackDepth(workbook.Id).Should().Be(2);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the finding's user gesture: a selection whose left column holds three distinct row
    /// labels (Jan/Feb/Mar) over numeric data, with "Left column" the only detected edge -- the
    /// same shape <see cref="CreateNamesFromSelectionPlanner.DetectOptions"/> would pre-check and
    /// <see cref="DefinedNamesSession.PlanCreateNamesFromSelection"/> would plan from real cell
    /// values in the live dialog.
    /// </summary>
    private static (Workbook Workbook, Sheet Sheet, WorkbookCellEditService Service, IReadOnlyList<PlannedDefinedName> Planned)
        BuildThreeLabelFixture()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");

        var labels = new[] { "Jan", "Feb", "Mar" };
        for (var i = 0; i < labels.Length; i++)
        {
            var row = (uint)(i + 1);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(labels[i]));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(10 + i));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(20 + i));
        }

        var selection = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        var definedNames = new DefinedNamesSession(workbook, sheet.Id);
        var planned = definedNames.PlanCreateNamesFromSelection(
            selection,
            new CreateNamesFromSelectionOptions(UseTopRow: false, UseLeftColumn: true, UseBottomRow: false, UseRightColumn: false),
            address => (sheet.GetValue(address) as TextValue)?.Value);

        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);

        return (workbook, sheet, service, planned);
    }
}
