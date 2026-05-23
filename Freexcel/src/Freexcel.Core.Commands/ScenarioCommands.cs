using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class SaveScenarioCommand : IWorkbookCommand
{
    private readonly WorkbookScenario _scenario;
    private WorkbookScenario? _previousScenario;
    private int _previousIndex = -1;
    private bool _applied;

    public string Label => "Save Scenario";

    public SaveScenarioCommand(string name, IReadOnlyList<ScenarioCellValue> changingCells, string? comment = null)
    {
        _scenario = new WorkbookScenario(name.Trim(), changingCells.ToList(), string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_scenario.Name))
            return new CommandOutcome(false, "Scenario name cannot be blank.");
        if (_scenario.ChangingCells.Count == 0)
            return new CommandOutcome(false, "Scenario must include at least one changing cell.");

        foreach (var cell in _scenario.ChangingCells)
        {
            if (ctx.Workbook.GetSheet(cell.Address.Sheet) is null)
                return new CommandOutcome(false, "Scenario changing cells must belong to this workbook.");
        }

        _previousIndex = ctx.Workbook.Scenarios.FindIndex(s =>
            string.Equals(s.Name, _scenario.Name, StringComparison.OrdinalIgnoreCase));
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

public sealed class ScenarioSummaryReportCommand : IWorkbookCommand
{
    private SheetId? _reportSheetId;

    public string Label => "Scenario Summary";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;
        if (ctx.Workbook.Scenarios.Count == 0)
            return new CommandOutcome(false, "No scenarios are saved in this workbook.");

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
}
