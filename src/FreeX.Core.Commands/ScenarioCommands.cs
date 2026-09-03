using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class SaveScenarioCommand : IWorkbookCommand
{
    private readonly WorkbookScenario _scenario;
    private readonly string? _replaceScenarioName;
    private WorkbookScenario? _previousScenario;
    private int _previousIndex = -1;
    private bool _applied;

    public string Label => "Save Scenario";

    public SaveScenarioCommand(
        string name,
        IReadOnlyList<ScenarioCellValue> changingCells,
        string? comment = null,
        bool hidden = false,
        bool locked = false,
        string? replaceScenarioName = null)
    {
        _scenario = new WorkbookScenario(
            name.Trim(),
            changingCells.ToList(),
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            hidden,
            locked);
        _replaceScenarioName = string.IsNullOrWhiteSpace(replaceScenarioName) ? null : replaceScenarioName.Trim();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_scenario.Name))
            return new CommandOutcome(false, "Scenario name cannot be blank.");
        if (_scenario.ChangingCells.Count == 0)
            return new CommandOutcome(false, "Scenario must include at least one changing cell.");

        foreach (var cell in _scenario.ChangingCells)
        {
            var sheet = ctx.Workbook.GetSheet(cell.Address.Sheet);
            if (sheet is null)
                return ScenarioCommandHelpers.ChangingCellsOutsideWorkbook();
        }

        if (ScenarioProtectionGuards.RejectIfChangingCellsProtected(ctx.Workbook, _scenario.ChangingCells) is { } protectedOutcome)
            return protectedOutcome;

        var targetNameIndex = ScenarioCommandHelpers.FindScenarioIndex(ctx.Workbook.Scenarios, _scenario.Name);
        if (_replaceScenarioName is not null &&
            targetNameIndex >= 0 &&
            !ScenarioCommandHelpers.IsScenarioNamed(ctx.Workbook.Scenarios[targetNameIndex], _replaceScenarioName))
            return new CommandOutcome(false, "Scenario name already exists.");

        var nameToReplace = _replaceScenarioName ?? _scenario.Name;
        _previousIndex = ScenarioCommandHelpers.FindScenarioIndex(ctx.Workbook.Scenarios, nameToReplace);
        if (_previousIndex >= 0)
        {
            _previousScenario = ctx.Workbook.Scenarios[_previousIndex];
            if (ScenarioProtectionGuards.RejectIfScenarioLocked(ctx.Workbook, _previousScenario) is { } lockedOutcome)
            {
                _previousScenario = null;
                _previousIndex = -1;
                return lockedOutcome;
            }

            ctx.Workbook.Scenarios[_previousIndex] = _scenario;
        }
        else
        {
            ctx.Workbook.Scenarios.Add(_scenario);
        }

        _applied = true;
        return new CommandOutcome(
            true,
            AffectedCells: ScenarioCommandHelpers.BuildAffectedCells(_scenario.ChangingCells),
            IsNoOp: NothingChanged());
    }

    /// <summary>
    /// r258: saving a scenario again under the same name with nothing changed in between writes back
    /// an equal scenario -- re-confirming the Scenario Manager's Edit dialog without touching a
    /// value. Without this the command still pushed an undo entry, and UndoRedoStack.Push clears the
    /// redo stack, destroying a real edit the user could have redone.
    ///
    /// <para>r231 declined to guard this because <c>newValue == previous</c> could not fire:
    /// WorkbookScenario carries a ChangingCells list, which record equality compares by reference,
    /// and every save builds a fresh one. <see cref="SaveTargetComparison"/> compares it by content.
    /// The decision covers the command's whole undo record -- Revert replaces
    /// <c>_previousScenario</c> at <c>_previousIndex</c>, or removes the added scenario when there
    /// was none -- and that second case is never a no-op, which is why it answers false.</para>
    /// </summary>
    private bool NothingChanged() =>
        _previousIndex >= 0
        && _previousScenario is not null
        && SaveTargetComparison.Same(_previousScenario, _scenario);

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        if (_previousIndex >= 0 && _previousScenario is not null)
        {
            ctx.Workbook.Scenarios[_previousIndex] = _previousScenario;
        }
        else
        {
            ctx.Workbook.Scenarios.RemoveAll(s => ScenarioCommandHelpers.IsScenarioNamed(s, _scenario.Name));
        }

        _applied = false;
    }
}

public sealed class ApplyScenarioCommand : IWorkbookCommand
{
    private readonly string _name;
    private List<(CellAddress Address, Cell? PreviousCell)>? _snapshot;
    private bool _applied;

    public string Label => "Show Scenario";

    public ApplyScenarioCommand(string name)
    {
        _name = name.Trim();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var scenario = ScenarioCommandHelpers.FindScenario(ctx.Workbook.Scenarios, _name);
        if (scenario is null)
            return ScenarioCommandHelpers.ScenarioNotFound();
        if (ScenarioProtectionGuards.RejectIfChangingCellsProtected(ctx.Workbook, scenario.ChangingCells) is { } protectedOutcome)
            return protectedOutcome;

        // r213: applying a scenario whose values every changing cell already holds writes the same
        // bytes back. The scenario list highlights the active scenario, so re-clicking Show on it is
        // an ordinary gesture -- and the undo entry it pushed cleared redo.
        // Checked in a separate pass so no cell is written before the answer is known.
        var alreadyMatches = true;
        foreach (var change in scenario.ChangingCells)
        {
            var probeSheet = ctx.Workbook.GetSheet(change.Address.Sheet);
            if (probeSheet is null)
                return ScenarioCommandHelpers.ChangingCellsOutsideWorkbook();
            if (!Equals(probeSheet.GetCell(change.Address)?.Value, change.Value))
            {
                alreadyMatches = false;
                break;
            }
        }

        if (alreadyMatches)
            return new CommandOutcome(true, IsNoOp: true);

        _snapshot = [];
        foreach (var change in scenario.ChangingCells)
        {
            var sheet = ctx.Workbook.GetSheet(change.Address.Sheet);
            if (sheet is null)
                return ScenarioCommandHelpers.ChangingCellsOutsideWorkbook();

            _snapshot.Add((change.Address, sheet.GetCell(change.Address)?.Clone()));
            sheet.SetCell(change.Address, Cell.FromValue(change.Value));
        }

        _applied = true;
        return new CommandOutcome(true, AffectedCells: ScenarioCommandHelpers.BuildAffectedCells(scenario.ChangingCells));
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _snapshot is null)
            return;

        foreach (var (address, previousCell) in _snapshot)
        {
            var sheet = ctx.Workbook.GetSheet(address.Sheet);
            if (sheet is null)
                continue;

            if (previousCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, previousCell.Clone());
        }

        _applied = false;
    }
}

public sealed class DeleteScenarioCommand : IWorkbookCommand
{
    private readonly string _name;
    private WorkbookScenario? _removedScenario;
    private int _removedIndex = -1;
    private bool _applied;

    public string Label => "Delete Scenario";

    public DeleteScenarioCommand(string name)
    {
        _name = name.Trim();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _removedIndex = ScenarioCommandHelpers.FindScenarioIndex(ctx.Workbook.Scenarios, _name);
        if (_removedIndex < 0)
            return ScenarioCommandHelpers.ScenarioNotFound();

        _removedScenario = ctx.Workbook.Scenarios[_removedIndex];
        if (ScenarioProtectionGuards.RejectIfChangingCellsProtected(ctx.Workbook, _removedScenario.ChangingCells) is { } protectedOutcome)
            return protectedOutcome;
        if (ScenarioProtectionGuards.RejectIfScenarioLocked(ctx.Workbook, _removedScenario) is { } lockedOutcome)
            return lockedOutcome;

        ctx.Workbook.Scenarios.RemoveAt(_removedIndex);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: ScenarioCommandHelpers.BuildAffectedCells(_removedScenario.ChangingCells));
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _removedScenario is null)
            return;

        var index = Math.Clamp(_removedIndex, 0, ctx.Workbook.Scenarios.Count);
        ctx.Workbook.Scenarios.Insert(index, _removedScenario);
        _applied = false;
    }
}

/// <summary>
/// Excel's Scenario Manager "Merge..." command: pulls scenarios saved on another worksheet (or
/// workbook) into this workbook's scenario set. Callers are responsible for remapping each source
/// scenario's <see cref="ScenarioCellValue.Address"/> sheet references onto this workbook's own
/// <see cref="SheetId"/>s (e.g. by matching sheet names) before constructing this command --
/// exactly like <see cref="FreeX.App.Services.ScenarioManagerPlanner.CreateMergePlan"/> already
/// validates. Rejects the merge (without adding anything) if any source scenario references a
/// cell outside this workbook or on a protected sheet without the EditScenarios permission,
/// mirroring the checks <c>CreateMergePlan</c> performs before this command ever existed.
/// </summary>
public sealed class MergeScenarioCommand : IWorkbookCommand
{
    private readonly IReadOnlyList<WorkbookScenario> _sourceScenarios;
    private int _addedCount;
    private bool _applied;

    public string Label => "Merge Scenarios";

    public MergeScenarioCommand(IReadOnlyList<WorkbookScenario> sourceScenarios)
    {
        _sourceScenarios = sourceScenarios ?? [];
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceScenarios.Count == 0)
            return new CommandOutcome(false, "There are no scenarios to merge.");

        foreach (var scenario in _sourceScenarios)
        {
            foreach (var cell in scenario.ChangingCells)
            {
                if (ctx.Workbook.GetSheet(cell.Address.Sheet) is null)
                    return ScenarioCommandHelpers.ChangingCellsOutsideWorkbook();
            }

            if (ScenarioProtectionGuards.RejectIfChangingCellsProtected(ctx.Workbook, scenario.ChangingCells) is { } protectedOutcome)
                return protectedOutcome;
        }

        // Merged scenarios must not silently collide with (and shadow) an existing scenario name --
        // Excel's own Merge dialog keeps every merged scenario distinct, so a name that already
        // exists in the target (or among scenarios merged earlier in this same call) is uniquified.
        var existingNames = new HashSet<string>(
            ctx.Workbook.Scenarios.Select(static scenario => scenario.Name),
            StringComparer.OrdinalIgnoreCase);
        var affectedCells = new List<CellAddress>();
        foreach (var scenario in _sourceScenarios)
        {
            var uniqueName = MakeUniqueScenarioName(scenario.Name, existingNames);
            existingNames.Add(uniqueName);

            var mergedScenario = string.Equals(uniqueName, scenario.Name, StringComparison.Ordinal)
                ? scenario
                : scenario with { Name = uniqueName };
            ctx.Workbook.Scenarios.Add(mergedScenario);
            affectedCells.AddRange(ScenarioCommandHelpers.BuildAffectedCells(mergedScenario.ChangingCells));
        }

        _addedCount = _sourceScenarios.Count;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        // Merged scenarios are always appended at the tail (never inserted/reordered), so undo can
        // simply drop the last _addedCount entries -- the same tail-append/tail-trim pairing this
        // command's own Apply relies on, without needing per-scenario identity tracking.
        var start = ctx.Workbook.Scenarios.Count - _addedCount;
        if (start >= 0 && _addedCount > 0)
            ctx.Workbook.Scenarios.RemoveRange(start, _addedCount);

        _addedCount = 0;
        _applied = false;
    }

    private static string MakeUniqueScenarioName(string name, IReadOnlySet<string> existingNames)
    {
        var candidate = name;
        var suffix = 2;
        while (existingNames.Contains(candidate))
        {
            candidate = $"{name} ({suffix})";
            suffix++;
        }

        return candidate;
    }
}

internal static class ScenarioProtectionGuards
{
    private const string ScenarioLockedMessage = "This scenario is locked and cannot be changed while the sheet is protected.";

    public static CommandOutcome? RejectIfChangingCellsProtected(
        Workbook workbook,
        IEnumerable<ScenarioCellValue> changingCells)
    {
        var checkedSheets = new HashSet<SheetId>();
        foreach (var cell in changingCells)
        {
            var sheetId = cell.Address.Sheet;
            if (!checkedSheets.Add(sheetId))
                continue;

            var sheet = workbook.GetSheet(sheetId);
            if (sheet is null)
                return ScenarioCommandHelpers.ChangingCellsOutsideWorkbook();
            if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditScenarios) is { } protectedOutcome)
                return protectedOutcome;
        }

        return null;
    }

    /// <summary>
    /// Enforces the scenario's own "Prevent changes" (Locked) flag, which -- like a cell's
    /// Locked style -- only takes effect once its sheet is protected, independent of whether
    /// the sheet-level EditScenarios permission is granted.
    /// </summary>
    public static CommandOutcome? RejectIfScenarioLocked(Workbook workbook, WorkbookScenario scenario)
    {
        if (!scenario.Locked)
            return null;

        var checkedSheets = new HashSet<SheetId>();
        foreach (var cell in scenario.ChangingCells)
        {
            var sheetId = cell.Address.Sheet;
            if (!checkedSheets.Add(sheetId))
                continue;

            var sheet = workbook.GetSheet(sheetId);
            if (sheet is { IsProtected: true })
                return new CommandOutcome(false, ScenarioLockedMessage);
        }

        return null;
    }
}

file static class ScenarioCommandHelpers
{
    private const string ScenarioNotFoundMessage = "Scenario was not found.";
    private const string ChangingCellsOutsideWorkbookMessage = "Scenario changing cells must belong to this workbook.";

    public static CommandOutcome ScenarioNotFound() =>
        new(false, ScenarioNotFoundMessage);

    public static CommandOutcome ChangingCellsOutsideWorkbook() =>
        new(false, ChangingCellsOutsideWorkbookMessage);

    public static bool IsScenarioNamed(WorkbookScenario scenario, string name) =>
        string.Equals(scenario.Name, name, StringComparison.OrdinalIgnoreCase);

    public static int FindScenarioIndex(IReadOnlyList<WorkbookScenario> scenarios, string name)
    {
        for (var index = 0; index < scenarios.Count; index++)
        {
            if (IsScenarioNamed(scenarios[index], name))
                return index;
        }

        return -1;
    }

    public static WorkbookScenario? FindScenario(IReadOnlyList<WorkbookScenario> scenarios, string name)
    {
        var index = FindScenarioIndex(scenarios, name);
        return index >= 0 ? scenarios[index] : null;
    }

    public static List<CellAddress> BuildAffectedCells(IReadOnlyList<ScenarioCellValue> changingCells)
    {
        var affectedCells = new List<CellAddress>(changingCells.Count);
        for (var index = 0; index < changingCells.Count; index++)
            affectedCells.Add(changingCells[index].Address);

        return affectedCells;
    }

    public static Dictionary<SheetId, int> BuildSheetOrder(Workbook workbook)
    {
        var sheetOrder = new Dictionary<SheetId, int>(workbook.Sheets.Count);
        for (var index = 0; index < workbook.Sheets.Count; index++)
            sheetOrder[workbook.Sheets[index].Id] = index;

        return sheetOrder;
    }

    public static List<CellAddress> CollectOrderedChangingCells(
        IReadOnlyList<WorkbookScenario> scenarios,
        IReadOnlyDictionary<SheetId, int> sheetOrder)
    {
        var changingCells = new List<CellAddress>();
        var seen = new HashSet<CellAddress>();
        for (var scenarioIndex = 0; scenarioIndex < scenarios.Count; scenarioIndex++)
        {
            var scenarioCells = scenarios[scenarioIndex].ChangingCells;
            for (var cellIndex = 0; cellIndex < scenarioCells.Count; cellIndex++)
            {
                var address = scenarioCells[cellIndex].Address;
                if (seen.Add(address))
                    changingCells.Add(address);
            }
        }

        changingCells.Sort(new CellAddressWorkbookOrderComparer(sheetOrder));
        return changingCells;
    }

    private sealed class CellAddressWorkbookOrderComparer(
        IReadOnlyDictionary<SheetId, int> sheetOrder) : IComparer<CellAddress>
    {
        public int Compare(CellAddress left, CellAddress right)
        {
            var leftOrder = sheetOrder.TryGetValue(left.Sheet, out var leftIndex)
                ? leftIndex
                : int.MaxValue;
            var rightOrder = sheetOrder.TryGetValue(right.Sheet, out var rightIndex)
                ? rightIndex
                : int.MaxValue;
            var orderComparison = leftOrder.CompareTo(rightOrder);
            if (orderComparison != 0)
                return orderComparison;

            var rowComparison = left.Row.CompareTo(right.Row);
            return rowComparison != 0
                ? rowComparison
                : left.Col.CompareTo(right.Col);
        }
    }
}

public sealed class ScenarioSummaryReportCommand : IWorkbookCommand
{
    private readonly IReadOnlyList<CellAddress> _resultCells;
    private readonly Action<Workbook, IReadOnlyList<CellAddress>>? _recalculate;
    private SheetId? _reportSheetId;

    public string Label => "Scenario Summary";

    public ScenarioSummaryReportCommand(
        IReadOnlyList<CellAddress>? resultCells = null,
        Action<Workbook, IReadOnlyList<CellAddress>>? recalculate = null)
    {
        _resultCells = resultCells?.Distinct().ToList() ?? [];
        _recalculate = recalculate;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;
        if (ctx.Workbook.Scenarios.Count == 0)
            return new CommandOutcome(false, "No scenarios are saved in this workbook.");
        if (_resultCells.Count > 0)
        {
            foreach (var scenario in ctx.Workbook.Scenarios)
            {
                if (ScenarioProtectionGuards.RejectIfChangingCellsProtected(ctx.Workbook, scenario.ChangingCells) is { } scenarioProtectedOutcome)
                    return scenarioProtectedOutcome;
            }
        }

        foreach (var address in _resultCells)
        {
            if (ctx.Workbook.GetSheet(address.Sheet) is null)
                return new CommandOutcome(false, "Scenario result cells must belong to this workbook.");
        }

        var sheetOrder = ScenarioCommandHelpers.BuildSheetOrder(ctx.Workbook);
        var changingCells = ScenarioCommandHelpers.CollectOrderedChangingCells(ctx.Workbook.Scenarios, sheetOrder);

        var report = ctx.Workbook.AddSheet(GetUniqueReportSheetName(ctx.Workbook));
        report.ResetViewStateToA1();
        _reportSheetId = report.Id;
        report.EnsureCellCapacity(EstimateReportCellCount(ctx.Workbook.Scenarios.Count, changingCells.Count, _resultCells.Count));
        report.SetCell(new CellAddress(report.Id, 1, 1), new TextValue("Scenario Summary"));
        report.SetCell(new CellAddress(report.Id, 3, 1), new TextValue("Changing Cells"));
        for (var index = 0; index < ctx.Workbook.Scenarios.Count; index++)
        {
            report.SetCell(
                new CellAddress(report.Id, 3, (uint)index + 2),
                new TextValue(ctx.Workbook.Scenarios[index].Name));
        }

        for (var rowIndex = 0; rowIndex < changingCells.Count; rowIndex++)
        {
            var address = changingCells[rowIndex];
            var reportRow = (uint)rowIndex + 4;
            report.SetCell(new CellAddress(report.Id, reportRow, 1), new TextValue(FormatAddress(ctx.Workbook, address)));
        }

        if (!TryAddSharedChangingCellValues(ctx.Workbook.Scenarios, changingCells, report))
        {
            var changingCellRows = new Dictionary<CellAddress, int>(changingCells.Count);
            for (var rowIndex = 0; rowIndex < changingCells.Count; rowIndex++)
                changingCellRows[changingCells[rowIndex]] = rowIndex;

            for (var scenarioIndex = 0; scenarioIndex < ctx.Workbook.Scenarios.Count; scenarioIndex++)
            {
                var scenario = ctx.Workbook.Scenarios[scenarioIndex];
                for (var changeIndex = scenario.ChangingCells.Count - 1; changeIndex >= 0; changeIndex--)
                {
                    var change = scenario.ChangingCells[changeIndex];
                    if (!changingCellRows.TryGetValue(change.Address, out var rowIndex))
                        continue;

                    report.SetCell(
                        new CellAddress(report.Id, (uint)rowIndex + 4, (uint)scenarioIndex + 2),
                        Cell.FromValue(change.Value));
                }
            }
        }

        if (_resultCells.Count > 0)
            AddResultCellsSection(ctx.Workbook, report, (uint)changingCells.Count + 6);

        return new CommandOutcome(true);
    }

    private static int EstimateReportCellCount(int scenarioCount, int changingCellCount, int resultCellCount)
    {
        long count =
            2L +
            scenarioCount +
            changingCellCount +
            (long)changingCellCount * scenarioCount;

        if (resultCellCount > 0)
        {
            count +=
                1L +
                scenarioCount +
                resultCellCount +
                (long)resultCellCount * scenarioCount;
        }

        return count > int.MaxValue ? int.MaxValue : (int)count;
    }

    private static bool TryAddSharedChangingCellValues(
        IReadOnlyList<WorkbookScenario> scenarios,
        IReadOnlyList<CellAddress> changingCells,
        Sheet report)
    {
        for (var scenarioIndex = 0; scenarioIndex < scenarios.Count; scenarioIndex++)
        {
            var scenarioCells = scenarios[scenarioIndex].ChangingCells;
            if (scenarioCells.Count != changingCells.Count)
                return false;

            for (var rowIndex = 0; rowIndex < changingCells.Count; rowIndex++)
            {
                if (scenarioCells[rowIndex].Address != changingCells[rowIndex])
                    return false;
            }
        }

        for (var scenarioIndex = 0; scenarioIndex < scenarios.Count; scenarioIndex++)
        {
            var scenarioCells = scenarios[scenarioIndex].ChangingCells;
            for (var rowIndex = 0; rowIndex < scenarioCells.Count; rowIndex++)
            {
                report.SetCell(
                    new CellAddress(report.Id, (uint)rowIndex + 4, (uint)scenarioIndex + 2),
                    Cell.FromValue(scenarioCells[rowIndex].Value));
            }
        }

        return true;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_reportSheetId is null)
            return;

        ctx.Workbook.RemoveSheet(_reportSheetId.Value);
        _reportSheetId = null;
    }

    private static string GetUniqueReportSheetName(Workbook workbook)
    {
        const string baseName = "Scenario Summary";
        if (workbook.GetSheet(baseName) is null)
            return baseName;

        for (var index = 1; ; index++)
        {
            var candidate = $"{baseName} {index}";
            if (workbook.GetSheet(candidate) is null)
                return candidate;
        }
    }

    private static string FormatAddress(Workbook workbook, CellAddress address)
    {
        var sheet = workbook.GetSheet(address.Sheet);
        var sheetName = sheet?.Name ?? "Sheet";
        return $"{sheetName}!{address.ToA1()}";
    }

    private void AddResultCellsSection(Workbook workbook, Sheet report, uint headerRow)
    {
        report.SetCell(new CellAddress(report.Id, headerRow, 1), new TextValue("Result Cells"));
        for (var index = 0; index < workbook.Scenarios.Count; index++)
        {
            report.SetCell(
                new CellAddress(report.Id, headerRow, (uint)index + 2),
                new TextValue(workbook.Scenarios[index].Name));
        }

        for (var rowIndex = 0; rowIndex < _resultCells.Count; rowIndex++)
        {
            var address = _resultCells[rowIndex];
            var reportRow = headerRow + (uint)rowIndex + 1;
            report.SetCell(new CellAddress(report.Id, reportRow, 1), new TextValue(FormatAddress(workbook, address)));
        }

        for (var scenarioIndex = 0; scenarioIndex < workbook.Scenarios.Count; scenarioIndex++)
        {
            var scenario = workbook.Scenarios[scenarioIndex];
            var snapshot = CaptureScenarioCellSnapshot(workbook, scenario);
            var changedCells = scenario.ChangingCells.Select(cell => cell.Address).Distinct().ToList();
            try
            {
                ApplyScenarioValues(workbook, scenario);
                _recalculate?.Invoke(workbook, changedCells);
                for (var rowIndex = 0; rowIndex < _resultCells.Count; rowIndex++)
                {
                    var address = _resultCells[rowIndex];
                    var sheet = workbook.GetSheet(address.Sheet);
                    if (sheet is null)
                        continue;

                    report.SetCell(
                        new CellAddress(report.Id, headerRow + (uint)rowIndex + 1, (uint)scenarioIndex + 2),
                        Cell.FromValue(sheet.GetValue(address)));
                }
            }
            finally
            {
                RestoreScenarioCellSnapshot(workbook, snapshot);
                _recalculate?.Invoke(workbook, changedCells);
            }
        }
    }

    private static List<(CellAddress Address, Cell? PreviousCell)> CaptureScenarioCellSnapshot(
        Workbook workbook,
        WorkbookScenario scenario)
    {
        var snapshot = new List<(CellAddress Address, Cell? PreviousCell)>();
        foreach (var address in scenario.ChangingCells.Select(cell => cell.Address).Distinct())
        {
            var sheet = workbook.GetSheet(address.Sheet);
            if (sheet is null)
                continue;

            snapshot.Add((address, sheet.GetCell(address)?.Clone()));
        }

        return snapshot;
    }

    private static void ApplyScenarioValues(Workbook workbook, WorkbookScenario scenario)
    {
        foreach (var change in scenario.ChangingCells)
        {
            var sheet = workbook.GetSheet(change.Address.Sheet);
            sheet?.SetCell(change.Address, Cell.FromValue(change.Value));
        }
    }

    private static void RestoreScenarioCellSnapshot(
        Workbook workbook,
        IReadOnlyList<(CellAddress Address, Cell? PreviousCell)> snapshot)
    {
        foreach (var (address, previousCell) in snapshot)
        {
            var sheet = workbook.GetSheet(address.Sheet);
            if (sheet is null)
                continue;

            if (previousCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, previousCell.Clone());
        }
    }
}
