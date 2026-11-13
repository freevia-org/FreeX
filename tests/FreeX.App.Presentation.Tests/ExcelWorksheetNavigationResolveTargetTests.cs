using FluentAssertions;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Decision table for the single portable "key -> target" decision
/// (<see cref="ExcelWorksheetNavigationPlanner.ResolveTarget"/>) that both the WPF and the Avalonia
/// shells now call. Every row here used to be written twice (and had drifted: ready-mode Enter on
/// Avalonia ignored "After pressing Enter, move selection", and a plain arrow off a merged cell was
/// absorbed on Avalonia).
/// </summary>
public sealed class ExcelWorksheetNavigationResolveTargetTests
{
    private const ExcelWorksheetNavigationModifiers None = ExcelWorksheetNavigationModifiers.None;
    private const ExcelWorksheetNavigationModifiers Shift = ExcelWorksheetNavigationModifiers.Shift;
    private const ExcelWorksheetNavigationModifiers Ctrl = ExcelWorksheetNavigationModifiers.Control;
    private const ExcelWorksheetNavigationModifiers Alt = ExcelWorksheetNavigationModifiers.Alt;

    private static (Workbook Workbook, Sheet Sheet) NewSheet()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static CellAddress At(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static ExcelWorksheetNavigationResolution? Resolve(
        Sheet? sheet,
        SheetId sheetId,
        CellAddress current,
        ExcelWorksheetNavigationKey key,
        ExcelWorksheetNavigationModifiers modifiers = None,
        bool endMode = false,
        int pageRows = 10,
        int pageColumns = 4,
        bool moveAfterEnter = true,
        FormulaEditorEnterDirection direction = FormulaEditorEnterDirection.Down,
        ExcelWorksheetNavigationKey systemKey = ExcelWorksheetNavigationKey.None) =>
        ExcelWorksheetNavigationPlanner.ResolveTarget(
            sheet,
            sheetId,
            current,
            key,
            new ExcelWorksheetNavigationRequest(
                modifiers,
                endMode,
                pageRows,
                pageColumns,
                moveAfterEnter,
                direction,
                systemKey));

    private static CellAddress? Target(
        Sheet sheet,
        CellAddress current,
        ExcelWorksheetNavigationKey key,
        ExcelWorksheetNavigationModifiers modifiers = None,
        bool endMode = false,
        int pageRows = 10,
        int pageColumns = 4,
        bool moveAfterEnter = true,
        FormulaEditorEnterDirection direction = FormulaEditorEnterDirection.Down,
        ExcelWorksheetNavigationKey systemKey = ExcelWorksheetNavigationKey.None) =>
        Resolve(sheet, sheet.Id, current, key, modifiers, endMode, pageRows, pageColumns, moveAfterEnter, direction, systemKey)?.Target;

    // ── plain arrow steps ───────────────────────────────────────────────────

    [Theory]
    [InlineData(ExcelWorksheetNavigationKey.Up, 4u, 5u)]
    [InlineData(ExcelWorksheetNavigationKey.Down, 6u, 5u)]
    [InlineData(ExcelWorksheetNavigationKey.Left, 5u, 4u)]
    [InlineData(ExcelWorksheetNavigationKey.Right, 5u, 6u)]
    public void PlainArrow_StepsOneCell(ExcelWorksheetNavigationKey key, uint row, uint col)
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, 5, 5), key).Should().Be(At(sheet, row, col));
        Target(sheet, At(sheet, 5, 5), key, Shift).Should().Be(At(sheet, row, col));
    }

    [Fact]
    public void PlainArrow_ClampsAtSheetEdges()
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, 1, 5), ExcelWorksheetNavigationKey.Up).Should().Be(At(sheet, 1, 5));
        Target(sheet, At(sheet, 5, 1), ExcelWorksheetNavigationKey.Left).Should().Be(At(sheet, 5, 1));
        Target(sheet, At(sheet, CellAddress.MaxRow, 5), ExcelWorksheetNavigationKey.Down)
            .Should().Be(At(sheet, CellAddress.MaxRow, 5));
        Target(sheet, At(sheet, 5, CellAddress.MaxCol), ExcelWorksheetNavigationKey.Right)
            .Should().Be(At(sheet, 5, CellAddress.MaxCol));
    }

    [Fact]
    public void PlainArrow_WithNullSheet_StillSteps()
    {
        var sheetId = SheetId.New();
        var current = new CellAddress(sheetId, 5, 5);

        Resolve(null, sheetId, current, ExcelWorksheetNavigationKey.Down)!.Value.Target
            .Should().Be(new CellAddress(sheetId, 6, 5));
    }

    // ── merged cell: every plain step clears the whole merge (drift b) ──────

    [Theory]
    [InlineData(ExcelWorksheetNavigationKey.Up, 1u, 2u)]
    [InlineData(ExcelWorksheetNavigationKey.Down, 5u, 2u)]
    [InlineData(ExcelWorksheetNavigationKey.Left, 2u, 1u)]
    [InlineData(ExcelWorksheetNavigationKey.Right, 2u, 5u)]
    public void PlainArrow_FromMergedAnchor_StepsBeyondTheMerge(ExcelWorksheetNavigationKey key, uint row, uint col)
    {
        var (_, sheet) = NewSheet();
        sheet.AddMergedRegion(new GridRange(At(sheet, 2, 2), At(sheet, 4, 4)));

        Target(sheet, At(sheet, 2, 2), key).Should().Be(At(sheet, row, col));
        Target(sheet, At(sheet, 2, 2), key, Shift).Should().Be(At(sheet, row, col));
    }

    [Theory]
    [InlineData(ExcelWorksheetNavigationKey.Up, 1u, 3u)]
    [InlineData(ExcelWorksheetNavigationKey.Down, 5u, 3u)]
    [InlineData(ExcelWorksheetNavigationKey.Left, 3u, 1u)]
    [InlineData(ExcelWorksheetNavigationKey.Right, 3u, 5u)]
    public void PlainArrow_FromMergeInterior_StepsBeyondTheMerge(ExcelWorksheetNavigationKey key, uint row, uint col)
    {
        var (_, sheet) = NewSheet();
        sheet.AddMergedRegion(new GridRange(At(sheet, 2, 2), At(sheet, 4, 4)));

        Target(sheet, At(sheet, 3, 3), key).Should().Be(At(sheet, row, col));
    }

    [Fact]
    public void CtrlArrow_FromMergedCell_UsesDataBoundaryNotMergeStep()
    {
        var (_, sheet) = NewSheet();
        sheet.AddMergedRegion(new GridRange(At(sheet, 2, 2), At(sheet, 4, 4)));

        // Blank merge, no data below: the boundary is the sheet edge, not "just past the merge".
        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.Down, Ctrl)
            .Should().Be(At(sheet, CellAddress.MaxRow, 2));
    }

    // ── Ctrl+arrow / End-mode data boundaries ───────────────────────────────

    [Fact]
    public void CtrlArrow_JumpsToDataBoundaries()
    {
        var (_, sheet) = NewSheet();
        foreach (var row in new uint[] { 1, 2, 3, 8 })
            sheet.SetCell(At(sheet, row, 1), new NumberValue(row));

        Target(sheet, At(sheet, 1, 1), ExcelWorksheetNavigationKey.Down, Ctrl).Should().Be(At(sheet, 3, 1));
        Target(sheet, At(sheet, 3, 1), ExcelWorksheetNavigationKey.Down, Ctrl).Should().Be(At(sheet, 8, 1));
        Target(sheet, At(sheet, 8, 1), ExcelWorksheetNavigationKey.Down, Ctrl).Should().Be(At(sheet, CellAddress.MaxRow, 1));
        Target(sheet, At(sheet, 8, 1), ExcelWorksheetNavigationKey.Up, Ctrl).Should().Be(At(sheet, 3, 1));
        Target(sheet, At(sheet, 8, 1), ExcelWorksheetNavigationKey.Up, Ctrl | Shift).Should().Be(At(sheet, 3, 1));
    }

    [Fact]
    public void CtrlArrow_Horizontal_JumpsToDataBoundaries()
    {
        var (_, sheet) = NewSheet();
        foreach (var col in new uint[] { 1, 2, 3, 7 })
            sheet.SetCell(At(sheet, 1, col), new NumberValue(col));

        Target(sheet, At(sheet, 1, 1), ExcelWorksheetNavigationKey.Right, Ctrl).Should().Be(At(sheet, 1, 3));
        Target(sheet, At(sheet, 1, 3), ExcelWorksheetNavigationKey.Right, Ctrl).Should().Be(At(sheet, 1, 7));
        Target(sheet, At(sheet, 1, 7), ExcelWorksheetNavigationKey.Left, Ctrl).Should().Be(At(sheet, 1, 3));
        Target(sheet, At(sheet, 1, 7), ExcelWorksheetNavigationKey.Right, Ctrl).Should().Be(At(sheet, 1, CellAddress.MaxCol));
    }

    [Fact]
    public void EndMode_PlainAndShiftArrowsJumpLikeCtrlArrow_ButCtrlArrowIsIgnored()
    {
        var (_, sheet) = NewSheet();
        foreach (var row in new uint[] { 1, 2, 3 })
            sheet.SetCell(At(sheet, row, 1), new NumberValue(row));

        Target(sheet, At(sheet, 1, 1), ExcelWorksheetNavigationKey.Down, endMode: true).Should().Be(At(sheet, 3, 1));
        Target(sheet, At(sheet, 1, 1), ExcelWorksheetNavigationKey.Down, Shift, endMode: true).Should().Be(At(sheet, 3, 1));
        Target(sheet, At(sheet, 1, 1), ExcelWorksheetNavigationKey.Down, Ctrl, endMode: true).Should().BeNull();
        // Without End mode a plain Down is just one step.
        Target(sheet, At(sheet, 1, 1), ExcelWorksheetNavigationKey.Down).Should().Be(At(sheet, 2, 1));
    }

    // ── Enter (drift a) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(FormulaEditorEnterDirection.Down, 6u, 5u, 4u, 5u)]
    [InlineData(FormulaEditorEnterDirection.Up, 4u, 5u, 6u, 5u)]
    [InlineData(FormulaEditorEnterDirection.Right, 5u, 6u, 5u, 4u)]
    [InlineData(FormulaEditorEnterDirection.Left, 5u, 4u, 5u, 6u)]
    public void Enter_HonorsConfiguredDirection_AndShiftReversesIt(
        FormulaEditorEnterDirection direction,
        uint row,
        uint col,
        uint reverseRow,
        uint reverseCol)
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.Enter, direction: direction)
            .Should().Be(At(sheet, row, col));
        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.Enter, Shift, direction: direction)
            .Should().Be(At(sheet, reverseRow, reverseCol));
    }

    [Theory]
    [InlineData(FormulaEditorEnterDirection.Down)]
    [InlineData(FormulaEditorEnterDirection.Right)]
    [InlineData(FormulaEditorEnterDirection.Up)]
    [InlineData(FormulaEditorEnterDirection.Left)]
    public void Enter_WithMoveAfterEnterOff_DoesNotMove_NorDoesShiftEnter(FormulaEditorEnterDirection direction)
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.Enter, moveAfterEnter: false, direction: direction)
            .Should().Be(At(sheet, 5, 5));
        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.Enter, Shift, moveAfterEnter: false, direction: direction)
            .Should().Be(At(sheet, 5, 5));
    }

    [Fact]
    public void Enter_FromMergedCell_StepsBeyondTheMerge_InTheConfiguredDirection()
    {
        var (_, sheet) = NewSheet();
        sheet.AddMergedRegion(new GridRange(At(sheet, 2, 2), At(sheet, 4, 4)));

        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.Enter).Should().Be(At(sheet, 5, 2));
        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.Enter, Shift).Should().Be(At(sheet, 1, 2));
        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.Enter, direction: FormulaEditorEnterDirection.Right)
            .Should().Be(At(sheet, 2, 5));
    }

    [Fact]
    public void Enter_AtSheetEdge_ClampsInsteadOfWrapping()
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, CellAddress.MaxRow, 3), ExcelWorksheetNavigationKey.Enter)
            .Should().Be(At(sheet, CellAddress.MaxRow, 3));
        Target(sheet, At(sheet, 1, 3), ExcelWorksheetNavigationKey.Enter, Shift)
            .Should().Be(At(sheet, 1, 3));
    }

    // ── Tab ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Tab_MovesRight_AndShiftTabMovesLeft_IgnoringTheEnterOption()
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.Tab).Should().Be(At(sheet, 5, 6));
        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.Tab, Shift).Should().Be(At(sheet, 5, 4));
        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.Tab, moveAfterEnter: false, direction: FormulaEditorEnterDirection.Up)
            .Should().Be(At(sheet, 5, 6));
        Target(sheet, At(sheet, 5, 1), ExcelWorksheetNavigationKey.Tab, Shift).Should().Be(At(sheet, 5, 1));
    }

    [Fact]
    public void Tab_FromMergedCell_StepsBeyondTheMerge()
    {
        var (_, sheet) = NewSheet();
        sheet.AddMergedRegion(new GridRange(At(sheet, 2, 2), At(sheet, 4, 4)));

        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.Tab).Should().Be(At(sheet, 2, 5));
        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.Tab, Shift).Should().Be(At(sheet, 2, 1));
    }

    [Theory]
    [InlineData(ExcelWorksheetNavigationKey.Enter, GridSelectionCycleKey.Enter)]
    [InlineData(ExcelWorksheetNavigationKey.Tab, GridSelectionCycleKey.Tab)]
    public void EnterAndTab_ReportTheirCycleKey(ExcelWorksheetNavigationKey key, GridSelectionCycleKey expected)
    {
        var (_, sheet) = NewSheet();

        Resolve(sheet, sheet.Id, At(sheet, 5, 5), key)!.Value.CycleKey.Should().Be(expected);
        Resolve(sheet, sheet.Id, At(sheet, 5, 5), key, Shift)!.Value.CycleKey.Should().Be(expected);
        Resolve(sheet, sheet.Id, At(sheet, 5, 5), ExcelWorksheetNavigationKey.Down)!.Value.CycleKey.Should().BeNull();
    }

    // ── paging (drift c: one clamping implementation) ───────────────────────

    [Fact]
    public void PageDownAndUp_MoveByPageRows_AndClampAtSheetEdges()
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, 5, 3), ExcelWorksheetNavigationKey.PageDown).Should().Be(At(sheet, 15, 3));
        Target(sheet, At(sheet, 15, 3), ExcelWorksheetNavigationKey.PageUp).Should().Be(At(sheet, 5, 3));
        Target(sheet, At(sheet, 5, 3), ExcelWorksheetNavigationKey.PageUp).Should().Be(At(sheet, 1, 3));
        Target(sheet, At(sheet, 1, 3), ExcelWorksheetNavigationKey.PageUp).Should().Be(At(sheet, 1, 3));
        Target(sheet, At(sheet, CellAddress.MaxRow - 3, 3), ExcelWorksheetNavigationKey.PageDown)
            .Should().Be(At(sheet, CellAddress.MaxRow, 3));
        Target(sheet, At(sheet, CellAddress.MaxRow, 3), ExcelWorksheetNavigationKey.PageDown)
            .Should().Be(At(sheet, CellAddress.MaxRow, 3));
        Target(sheet, At(sheet, 5, 3), ExcelWorksheetNavigationKey.PageDown, Shift).Should().Be(At(sheet, 15, 3));
    }

    [Fact]
    public void Paging_TreatsNonPositivePageSizeAsOne()
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, 5, 3), ExcelWorksheetNavigationKey.PageDown, pageRows: 0).Should().Be(At(sheet, 6, 3));
        Target(sheet, At(sheet, 5, 3), ExcelWorksheetNavigationKey.PageUp, pageRows: -7).Should().Be(At(sheet, 4, 3));
    }

    [Fact]
    public void AltPageDownAndUp_MoveHorizontallyByPageColumns_ViaKeyOrSystemKey()
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.PageDown, Alt).Should().Be(At(sheet, 5, 9));
        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.PageUp, Alt).Should().Be(At(sheet, 5, 1));
        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.PageUp, Alt | Shift).Should().Be(At(sheet, 5, 1));
        Target(sheet, At(sheet, 5, CellAddress.MaxCol - 1), ExcelWorksheetNavigationKey.PageDown, Alt)
            .Should().Be(At(sheet, 5, CellAddress.MaxCol));
        // WPF reports Alt+PageDown as Key.System with the real key in SystemKey.
        Target(sheet, At(sheet, 5, 5), ExcelWorksheetNavigationKey.System, Alt, systemKey: ExcelWorksheetNavigationKey.PageDown)
            .Should().Be(At(sheet, 5, 9));
    }

    // ── Home / End ──────────────────────────────────────────────────────────

    [Fact]
    public void Home_GoesToColumnA_AndCtrlHomeToFirstUnfrozenCell()
    {
        var (_, sheet) = NewSheet();
        var current = At(sheet, 50, 26);

        Target(sheet, current, ExcelWorksheetNavigationKey.Home).Should().Be(At(sheet, 50, 1));
        Target(sheet, current, ExcelWorksheetNavigationKey.Home, Shift).Should().Be(At(sheet, 50, 1));
        Target(sheet, current, ExcelWorksheetNavigationKey.Home, Ctrl).Should().Be(At(sheet, 1, 1));

        sheet.FrozenRows = 2;
        sheet.FrozenCols = 3;
        Target(sheet, current, ExcelWorksheetNavigationKey.Home).Should().Be(At(sheet, 50, 1));
        Target(sheet, current, ExcelWorksheetNavigationKey.Home, Ctrl).Should().Be(At(sheet, 3, 4));
        Target(sheet, current, ExcelWorksheetNavigationKey.Home, Ctrl | Shift).Should().Be(At(sheet, 3, 4));
    }

    [Fact]
    public void EndModeThenHome_ReproducesCtrlEnd()
    {
        var (_, sheet) = NewSheet();
        sheet.SetCell(At(sheet, 7, 4), new NumberValue(1));

        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.Home, endMode: true).Should().Be(At(sheet, 7, 4));
    }

    [Fact]
    public void CtrlEnd_GoesToUsedRangeEnd_AndPlainEndIsNotANavigationKey()
    {
        var (_, sheet) = NewSheet();

        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.End, Ctrl).Should().Be(At(sheet, 1, 1));

        sheet.SetCell(At(sheet, 7, 4), new NumberValue(1));
        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.End, Ctrl).Should().Be(At(sheet, 7, 4));
        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.End, Ctrl | Shift).Should().Be(At(sheet, 7, 4));
        // Plain End only arms End mode (TryToggleEndMode); it never resolves a target.
        Target(sheet, At(sheet, 2, 2), ExcelWorksheetNavigationKey.End).Should().BeNull();
    }

    // ── keys / modifiers that are not worksheet navigation ──────────────────

    [Theory]
    [InlineData(ExcelWorksheetNavigationKey.Enter, ExcelWorksheetNavigationModifiers.Control)]
    [InlineData(ExcelWorksheetNavigationKey.Tab, ExcelWorksheetNavigationModifiers.Control)]
    [InlineData(ExcelWorksheetNavigationKey.Enter, ExcelWorksheetNavigationModifiers.Alt)]
    [InlineData(ExcelWorksheetNavigationKey.Up, ExcelWorksheetNavigationModifiers.Alt)]
    [InlineData(ExcelWorksheetNavigationKey.PageDown, ExcelWorksheetNavigationModifiers.Control)]
    [InlineData(ExcelWorksheetNavigationKey.Home, ExcelWorksheetNavigationModifiers.Alt)]
    [InlineData(ExcelWorksheetNavigationKey.Other, ExcelWorksheetNavigationModifiers.None)]
    [InlineData(ExcelWorksheetNavigationKey.None, ExcelWorksheetNavigationModifiers.None)]
    public void NonNavigationKeyCombinations_ResolveNoTarget(ExcelWorksheetNavigationKey key, ExcelWorksheetNavigationModifiers modifiers)
    {
        var (_, sheet) = NewSheet();

        Resolve(sheet, sheet.Id, At(sheet, 5, 5), key, modifiers).Should().BeNull();
    }

    // ── protected sheet: unselectable locked cells (host applies it after the cycle) ─

    [Fact]
    public void ProtectedSheet_ResolvedTargetOntoLockedCell_IsSkippedInTheDirectionOfTravel()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var unlocked = workbook.RegisterStyle(new CellStyle { Locked = false });
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);
        sheet.SetStyleOnly(1, 1, unlocked);
        sheet.SetStyleOnly(1, 3, unlocked);
        sheet.SetStyleOnly(3, 1, unlocked);

        // Right from A1 would land on locked B1; the host then skips to the next selectable cell C1.
        var right = Resolve(sheet, sheet.Id, At(sheet, 1, 1), ExcelWorksheetNavigationKey.Right)!.Value.Target;
        right.Should().Be(At(sheet, 1, 2));
        ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(workbook, sheet, right, ExcelWorksheetNavigationKey.Right, shiftHeld: false)
            .Should().Be(At(sheet, 1, 3));

        // Enter (Down) from A1 lands on locked A2 and is skipped to A3; Shift+Enter reverses direction.
        var down = Resolve(sheet, sheet.Id, At(sheet, 1, 1), ExcelWorksheetNavigationKey.Enter)!.Value.Target;
        ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(workbook, sheet, down, ExcelWorksheetNavigationKey.Enter, shiftHeld: false)
            .Should().Be(At(sheet, 3, 1));
        var up = Resolve(sheet, sheet.Id, At(sheet, 3, 1), ExcelWorksheetNavigationKey.Enter, Shift)!.Value.Target;
        ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(workbook, sheet, up, ExcelWorksheetNavigationKey.Enter, shiftHeld: true)
            .Should().Be(At(sheet, 1, 1));

        // Nothing selectable further left of A1's neighbours: Left from A1 stays at the edge, which is
        // itself selectable, so the host simply does not move.
        var left = Resolve(sheet, sheet.Id, At(sheet, 1, 1), ExcelWorksheetNavigationKey.Left)!.Value.Target;
        ExcelWorksheetNavigationPlanner.ResolveProtectedSheetTarget(workbook, sheet, left, ExcelWorksheetNavigationKey.Left, shiftHeld: false)
            .Should().Be(At(sheet, 1, 1));
    }
}

/// <summary>
/// Edit-commit "cycle within selection, else fall back" decision shared by both hosts.
/// </summary>
public sealed class GridSelectionEditCommitMoveTests
{
    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    private static CellAddress At(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static Sheet NewSheet() => new Workbook("Book").AddSheet("Sheet1");

    [Fact]
    public void EnterAndTab_InsideMultiCellSelection_CycleWithinIt()
    {
        var sheet = NewSheet();
        var selection = Range(sheet, 1, 1, 3, 2);

        var enter = GridSelectionNavigationPlanner.PlanEditCommitMove(
            sheet, selection, [selection], At(sheet, 1, 1), GridSelectionCycleKey.Enter, forward: true, At(sheet, 2, 1));
        enter.Should().Be(new GridSelectionEditCommitMove(At(sheet, 2, 1), CycledWithinSelection: true));

        // The cycle ignores the fallback entirely: Tab goes right inside the selection, not to the fallback.
        var tab = GridSelectionNavigationPlanner.PlanEditCommitMove(
            sheet, selection, [selection], At(sheet, 1, 1), GridSelectionCycleKey.Tab, forward: true, At(sheet, 99, 99));
        tab.Should().Be(new GridSelectionEditCommitMove(At(sheet, 1, 2), CycledWithinSelection: true));

        var shiftTabWrap = GridSelectionNavigationPlanner.PlanEditCommitMove(
            sheet, selection, [selection], At(sheet, 1, 1), GridSelectionCycleKey.Tab, forward: false, At(sheet, 99, 99));
        shiftTabWrap.Should().Be(new GridSelectionEditCommitMove(At(sheet, 3, 2), CycledWithinSelection: true));
    }

    [Fact]
    public void SingleCellOrSingleMergeSelection_FallsBackToTheSuppliedTarget()
    {
        var sheet = NewSheet();
        var single = Range(sheet, 2, 2, 2, 2);

        GridSelectionNavigationPlanner.PlanEditCommitMove(
                sheet, single, [single], At(sheet, 2, 2), GridSelectionCycleKey.Enter, forward: true, At(sheet, 3, 2))
            .Should().Be(new GridSelectionEditCommitMove(At(sheet, 3, 2), CycledWithinSelection: false));

        var merge = Range(sheet, 2, 2, 3, 3);
        sheet.AddMergedRegion(merge);
        GridSelectionNavigationPlanner.PlanEditCommitMove(
                sheet, merge, [merge], At(sheet, 2, 2), GridSelectionCycleKey.Tab, forward: true, At(sheet, 2, 4))
            .Should().Be(new GridSelectionEditCommitMove(At(sheet, 2, 4), CycledWithinSelection: false));
    }

    [Fact]
    public void NonEnterTabKeys_NeverCycle_EvenInAMultiCellSelection()
    {
        var sheet = NewSheet();
        var selection = Range(sheet, 1, 1, 3, 2);

        GridSelectionNavigationPlanner.PlanEditCommitMove(
                sheet, selection, [selection], At(sheet, 1, 1), cycleKey: null, forward: true, At(sheet, 2, 1))
            .Should().Be(new GridSelectionEditCommitMove(At(sheet, 2, 1), CycledWithinSelection: false));
    }
}
