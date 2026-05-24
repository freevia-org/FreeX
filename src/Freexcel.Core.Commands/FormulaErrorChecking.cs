using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed record FormulaErrorInfo(
    SheetId SheetId,
    string SheetName,
    CellAddress Address,
    ErrorValue Error,
    string? FormulaText);

public sealed record FormulaErrorIssue(
    SheetId SheetId,
    string SheetName,
    CellAddress Address,
    string Cell,
    string ErrorCode,
    string? FormulaText,
    string Description);

public sealed record FormulaErrorCheckingRule(string ErrorCode, string Label, string Description);

public static class FormulaErrorCheckingRuleCatalog
{
    public static IReadOnlyList<FormulaErrorCheckingRule> SupportedRules { get; } =
    [
        new(ErrorValue.DivByZero.Code, "Formulas that divide by zero", "Flag formulas that result in #DIV/0!."),
        new(ErrorValue.Value.Code, "Formulas with incompatible values", "Flag formulas that result in #VALUE!."),
        new(ErrorValue.Ref.Code, "Formulas with invalid cell references", "Flag formulas that result in #REF!."),
        new(ErrorValue.Name.Code, "Formulas with unrecognized names", "Flag formulas that result in #NAME?."),
        new(ErrorValue.NA.Code, "Formulas returning #N/A", "Flag formulas that result in #N/A."),
        new(ErrorValue.Num.Code, "Formulas with invalid numbers", "Flag formulas that result in #NUM!."),
        new(ErrorValue.Null.Code, "Formulas with invalid intersections", "Flag formulas that result in #NULL!."),
        new(ErrorValue.Spill.Code, "Formulas with blocked spill ranges", "Flag formulas that result in #SPILL!."),
        new(ErrorValue.Circular.Code, "Formulas with circular references", "Flag formulas that result in #CIRCULAR!."),
        new(FormulaAuditingService.InconsistentFormulaErrorCode, "Formulas inconsistent with nearby formulas", "Flag formulas whose relative reference pattern differs from adjacent formulas."),
        new(FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode, "Formulas which omit cells in a region", "Flag SUM formulas that omit adjacent cells in the region."),
        new(FormulaAuditingService.UnlockedFormulaCellsErrorCode, "Unlocked cells containing formulas", "Flag formula cells that are not locked for worksheet protection."),
        new(FormulaAuditingService.FormulaRefersToBlankCellsErrorCode, "Formulas referring to blank cells", "Flag formulas that refer to blank cells."),
        new(FormulaAuditingService.TwoDigitYearTextDateErrorCode, "Cells containing years represented as 2 digits", "Flag text dates whose year is entered with only two digits."),
        new(FormulaAuditingService.NumberStoredAsTextErrorCode, "Numbers formatted as text or preceded by an apostrophe", "Flag numbers stored as text.")
    ];

    public static bool IsSupported(string errorCode) =>
        SupportedRules.Any(rule => string.Equals(rule.ErrorCode, errorCode, StringComparison.OrdinalIgnoreCase));
}

public sealed class SetFormulaErrorIgnoredCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellAddress _address;
    private readonly bool _ignored;
    private bool _previousIgnored;

    public SetFormulaErrorIgnoredCommand(SheetId sheetId, CellAddress address, bool ignored)
    {
        _sheetId = sheetId;
        _address = address;
        _ignored = ignored;
    }

    public string Label => _ignored ? "Ignore Error" : "Reset Ignored Error";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var cell = ctx.GetSheet(_sheetId).GetCell(_address);
        if (cell is null)
            return new CommandOutcome(false, "No cell exists at the selected issue.");

        if (_ignored && !FormulaAuditingService.HasIgnorableFormulaIssue(ctx.Workbook, _sheetId, _address, cell))
            return new CommandOutcome(false, "The selected cell does not currently contain an issue.");

        _previousIgnored = cell.IgnoreFormulaError;
        cell.IgnoreFormulaError = _ignored;
        return new CommandOutcome(true, AffectedCells: [_address]);
    }

    public void Revert(ICommandContext ctx)
    {
        var cell = ctx.GetSheet(_sheetId).GetCell(_address);
        if (cell is not null)
            cell.IgnoreFormulaError = _previousIgnored;
    }
}

public sealed class SetFormulaErrorCheckingRuleCommand : IWorkbookCommand
{
    private readonly string _errorCode;
    private readonly bool _enabled;
    private bool _wasDisabled;

    public SetFormulaErrorCheckingRuleCommand(string errorCode, bool enabled)
    {
        _errorCode = errorCode;
        _enabled = enabled;
    }

    public string Label => "Error Checking Options";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!FormulaErrorCheckingRuleCatalog.IsSupported(_errorCode))
            return new CommandOutcome(false, "Formula error checking rule is not supported.");

        _wasDisabled = ctx.Workbook.DisabledFormulaErrorCodes.Contains(_errorCode);
        if (_enabled)
            ctx.Workbook.DisabledFormulaErrorCodes.Remove(_errorCode);
        else
            ctx.Workbook.DisabledFormulaErrorCodes.Add(_errorCode);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_wasDisabled)
            ctx.Workbook.DisabledFormulaErrorCodes.Add(_errorCode);
        else
            ctx.Workbook.DisabledFormulaErrorCodes.Remove(_errorCode);
    }
}
