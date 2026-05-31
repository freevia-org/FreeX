using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Applies a style diff to the same row/column range across multiple grouped sheets.
/// </summary>
public sealed class GroupedApplyStyleCommand : IWorkbookCommand, IEstimatesMemory
{
    private readonly IReadOnlyList<SheetId> _sheetIds;
    private readonly GridRange _sourceRange;
    private readonly StyleDiff _diff;
    private List<(SheetId SheetId, CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    private const int BytesPerCell = 200;

    public string Label => "Apply Style to Grouped Sheets";

    /// <inheritdoc/>
    public int EstimatedBytes => (int)Math.Min(_sourceRange.CellCount * _sheetIds.Count * BytesPerCell, int.MaxValue);

    public GroupedApplyStyleCommand(
        IReadOnlyCollection<SheetId> sheetIds,
        GridRange sourceRange,
        StyleDiff diff)
    {
        _sheetIds = sheetIds.Distinct().ToList();
        _sourceRange = sourceRange;
        _diff = diff;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        foreach (var sheetId in _sheetIds)
        {
            var sheet = ctx.GetSheet(sheetId);
            if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
                return protectedOutcome;
        }
        if (StyleDiffValidator.Validate(_diff) is { } validationOutcome)
            return validationOutcome;

        _snapshot = [];
        var styleCache = new Dictionary<StyleId, StyleId>();

        foreach (var sheetId in _sheetIds)
        {
            var sheet = ctx.GetSheet(sheetId);
            foreach (var sourceAddress in _sourceRange.AllCells())
            {
                var address = new CellAddress(sheetId, sourceAddress.Row, sourceAddress.Col);
                var cell = sheet.GetCell(address);

                if (cell is null)
                {
                    _snapshot.Add((sheetId, address, null, sheet.GetStyleOnly(address.Row, address.Col)));

                    var newStyleId = StyleDiffStyleCache.GetOrRegister(
                        ctx.Workbook,
                        _diff,
                        StyleId.Default,
                        styleCache);
                    sheet.SetStyleOnly(address.Row, address.Col, newStyleId);
                }
                else
                {
                    _snapshot.Add((sheetId, address, cell.Clone(), null));

                    cell.StyleId = StyleDiffStyleCache.GetOrRegister(
                        ctx.Workbook,
                        _diff,
                        cell.StyleId,
                        styleCache);
                }
            }
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        foreach (var (sheetId, address, oldCell, oldStyleOnly) in _snapshot)
        {
            var sheet = ctx.GetSheet(sheetId);
            if (oldCell is null)
            {
                if (oldStyleOnly.HasValue)
                    sheet.SetStyleOnly(address.Row, address.Col, oldStyleOnly.Value);
                else
                    sheet.ClearStyleOnly(address.Row, address.Col);
            }
            else
            {
                sheet.SetCell(address, oldCell.Clone());
            }
        }
    }
}
