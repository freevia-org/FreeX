using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// The green error indicators of the sheet on screen, recomputed only when that sheet's content
/// or the workbook's error-checking rules change. Both grids query it per visible cell while
/// drawing, so a lookup must stay cheap: the whole-sheet scan runs once per change.
/// </summary>
public sealed class BackgroundErrorIndicatorCache
{
    private static readonly IReadOnlyDictionary<CellAddress, string> Empty = new Dictionary<CellAddress, string>();

    private Workbook? _workbook;
    private Sheet? _sheet;
    private int _contentVersion;
    private int _rulesSignature;
    private IReadOnlyDictionary<CellAddress, string> _indicators = Empty;

    /// <summary>The error-checking rule flagging <paramref name="address"/>, or null when it shows no indicator.</summary>
    public string? GetErrorCode(Workbook workbook, CellAddress address) =>
        GetIndicators(workbook, address.Sheet).TryGetValue(address, out var code) ? code : null;

    public IReadOnlyDictionary<CellAddress, string> GetIndicators(Workbook workbook, SheetId sheetId)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var sheet = workbook.GetSheet(sheetId);
        if (sheet is null || sheet.IsChartsheet)
            return Empty;

        var rulesSignature = ComputeRulesSignature(workbook);
        if (ReferenceEquals(_workbook, workbook) &&
            ReferenceEquals(_sheet, sheet) &&
            _contentVersion == sheet.ContentVersion &&
            _rulesSignature == rulesSignature)
        {
            return _indicators;
        }

        _indicators = FormulaAuditingService.FindBackgroundErrorIndicators(workbook, sheetId);
        _workbook = workbook;
        _sheet = sheet;
        _contentVersion = sheet.ContentVersion;
        _rulesSignature = rulesSignature;
        return _indicators;
    }

    private static int ComputeRulesSignature(Workbook workbook)
    {
        var signature = workbook.DisabledFormulaErrorCodes.Count;
        foreach (var code in workbook.DisabledFormulaErrorCodes)
            signature ^= StringComparer.OrdinalIgnoreCase.GetHashCode(code) * 31;

        return signature;
    }
}
