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
                return new CommandOutcome(false, "Scenario changing cells must belong to this workbook.");
        }

        if (ScenarioProtectionGuards.RejectIfChangingCellsProtected(ctx.Workbook, _scenario.ChangingCells) is { } protectedOutcome)
            return protectedOutcome;

        var targetNameIndex = ctx.Workbook.Scenarios.FindIndex(s =>
            string.Equals(s.Name, _scenario.Name, StringComparison.OrdinalIgnoreCase));
        if (_replaceScenarioName is not null &&
            targetNameIndex >= 0 &&
            !string.Equals(ctx.Workbook.Scenarios[targetNameIndex].Name, _replaceScenarioName, StringComparison.OrdinalIgnoreCase))
            return new CommandOutcome(false, "Scenario name already exists.");

        var nameToReplace = _replaceScenarioName ?? _scenario.Name;
        _previousIndex = ctx.Workbook.Scenarios.FindIndex(s =>
            string.Equals(s.Name, nameToReplace, StringComparison.OrdinalIgnoreCase));
        if (_previousIndex >= 0)
        {
            _previousScenario = ctx.Workbook.Scenarios[_previousIndex];
            ctx.Workbook.Scenarios[_previousIndex] = _scenario;
        }
        else
        {
            ctx.Workbook.Scenarios.Add(_scenario);
        }

        _applied = true;
        return new CommandOutcome(true, AffectedCells: _scenario.ChangingCells.Select(c => c.Address).ToList());
    }

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
            ctx.Workbook.Scenarios.RemoveAll(s =>
                string.Equals(s.Name, _scenario.Name, StringComparison.OrdinalIgnoreCase));
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
        var scenario = ctx.Workbook.Scenarios.FirstOrDefault(s =>
            string.Equals(s.Name, _name, StringComparison.OrdinalIgnoreCase));
        if (scenario is null)
            return new CommandOutcome(false, "Scenario was not found.");
        if (ScenarioProtectionGuards.RejectIfChangingCellsProtected(ctx.Workbook, scenario.ChangingCells) is { } protectedOutcome)
            return protectedOutcome;

        _snapshot = [];
        foreach (var change in scenario.ChangingCells)
        {
            var sheet = ctx.Workbook.GetSheet(change.Address.Sheet);
            if (sheet is null)
                return new CommandOutcome(false, "Scenario changing cells must belong to this workbook.");

            _snapshot.Add((change.Address, sheet.GetCell(change.Address)?.Clone()));
            sheet.SetCell(change.Address, Cell.FromValue(change.Value));
        }

        _applied = true;
        return new CommandOutcome(true, AffectedCells: scenario.ChangingCells.Select(c => c.Address).ToList());
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
        _removedIndex = ctx.Workbook.Scenarios.FindIndex(s =>
            string.Equals(s.Name, _name, StringComparison.OrdinalIgnoreCase));
        if (_removedIndex < 0)
            return new CommandOutcome(false, "Scenario was not found.");

        _removedScenario = ctx.Workbook.Scenarios[_removedIndex];
        if (ScenarioProtectionGuards.RejectIfChangingCellsProtected(ctx.Workbook, _removedScenario.ChangingCells) is { } protectedOutcome)
            return protectedOutcome;

        ctx.Workbook.Scenarios.RemoveAt(_removedIndex);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: _removedScenario.ChangingCells.Select(c => c.Address).ToList());
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

internal static class ScenarioProtectionGuards
{
    public static CommandOutcome? RejectIfChangingCellsProtected(
        Workbook workbook,
        IEnumerable<ScenarioCellValue> changingCells)
    {
        foreach (var sheetId in changingCells.Select(cell => cell.Address.Sheet).Distinct())
        {
            var sheet = workbook.GetSheet(sheetId);
            if (sheet is null)
                return new CommandOutcome(false, "Scenario changing cells must belong to this workbook.");
            if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditScenarios) is { } protectedOutcome)
                return protectedOutcome;
        }

        return null;
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

        var report = ctx.Workbook.AddSheet(GetUniqueReportSheetName(ctx.Workbook));
        _reportSheetId = report.Id;

        report.SetCell(new CellAddress(report.Id, 1, 1), new TextValue("Scenario Summary"));
        report.SetCell(new CellAddress(report.Id, 3, 1), new TextValue("Changing Cells"));
        for (var index = 0; index < ctx.Workbook.Scenarios.Count; index++)
        {
            report.SetCell(
                new CellAddress(report.Id, 3, (uint)index + 2),
                new TextValue(ctx.Workbook.Scenarios[index].Name));
        }

        var sheetOrder = ctx.Workbook.Sheets
            .Select((sheet, index) => (sheet.Id, index))
            .ToDictionary(item => item.Id, item => item.index);
        var changingCells = ctx.Workbook.Scenarios
            .SelectMany(s => s.ChangingCells.Select(c => c.Address))
            .Distinct()
            .OrderBy(a => sheetOrder.TryGetValue(a.Sheet, out var index) ? index : int.MaxValue)
            .ThenBy(a => a.Row)
            .ThenBy(a => a.Col)
            .ToList();

        for (var rowIndex = 0; rowIndex < changingCells.Count; rowIndex++)
        {
            var address = changingCells[rowIndex];
            var reportRow = (uint)rowIndex + 4;
            report.SetCell(new CellAddress(report.Id, reportRow, 1), new TextValue(FormatAddress(ctx.Workbook, address)));

            for (var scenarioIndex = 0; scenarioIndex < ctx.Workbook.Scenarios.Count; scenarioIndex++)
            {
                var scenario = ctx.Workbook.Scenarios[scenarioIndex];
                var change = scenario.ChangingCells.FirstOrDefault(c => c.Address == address);
                if (change is null)
                    continue;

                report.SetCell(
                    new CellAddress(report.Id, reportRow, (uint)scenarioIndex + 2),
                    Cell.FromValue(change.Value));
            }
        }

        if (_resultCells.Count > 0)
            AddResultCellsSection(ctx.Workbook, report, (uint)changingCells.Count + 6);

        return new CommandOutcome(true);
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
