using FreeX.Core.Model;

namespace FreeX.Core.Calc;

/// <summary>
/// Evaluates conditional formatting for a single sheet scan using one immutable rule and aggregate
/// snapshot. Unlike <see cref="ViewportService"/>, the session is not retained across render passes.
/// </summary>
public sealed class ConditionalFormatEvaluationSession
{
    private readonly Sheet _sheet;
    private readonly Workbook _workbook;
    private readonly CfEvaluationContext _context;

    public ConditionalFormatEvaluationSession(
        Sheet sheet,
        Workbook workbook,
        IReadOnlyDictionary<(uint Row, uint Col), Cell> occupiedCells)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(occupiedCells);

        _sheet = sheet;
        _workbook = workbook;
        _context = ViewportConditionalFormatEvaluator.BuildContext(sheet, workbook, occupiedCells);
    }

    /// <summary>Returns the base style with every matching conditional differential style applied.</summary>
    public CellStyle EvaluateEffectiveStyle(
        CellAddress address,
        ScalarValue value,
        CellStyle? baseStyle = null)
    {
        if (address.Sheet != _sheet.Id)
            throw new ArgumentException("The cell address must belong to the session sheet.", nameof(address));

        var result = ViewportConditionalFormatEvaluator.Evaluate(
            _sheet,
            address,
            value,
            _workbook,
            _context,
            ViewportService.MatchesFormula);

        return result is null
            ? baseStyle ?? CellStyle.Default
            : ViewportConditionalFormatEvaluator.MergeStyles(baseStyle, result.Value.Style);
    }
}
