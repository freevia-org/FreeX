using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Owns the model-aware policy for carrying source defined names into a rewritten workbook.
/// Package traversal, container placement, collision ordering, and XML writeback remain caller-owned.
/// </summary>
internal sealed class XlsxDefinedNamePreservationPolicy
{
    private readonly HashSet<string> _liveModelDefinedNameKeys;
    private readonly Workbook _workbook;
    private readonly IReadOnlyList<string> _sourceSheetNamesByLocalId;
    private readonly IReadOnlyList<SheetId> _sourceSheetIdsByLocalId;
    private readonly IReadOnlyList<string> _targetSheetNames;

    public XlsxDefinedNamePreservationPolicy(
        Workbook workbook,
        IReadOnlyList<string> sourceSheetNamesByLocalId,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId,
        IReadOnlyList<string> targetSheetNames,
        HashSet<string>? liveModelDefinedNameKeys = null)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        _sourceSheetNamesByLocalId = sourceSheetNamesByLocalId ??
            throw new ArgumentNullException(nameof(sourceSheetNamesByLocalId));
        _sourceSheetIdsByLocalId = sourceSheetIdsByLocalId ??
            throw new ArgumentNullException(nameof(sourceSheetIdsByLocalId));
        _targetSheetNames = targetSheetNames ?? throw new ArgumentNullException(nameof(targetSheetNames));
        _liveModelDefinedNameKeys = liveModelDefinedNameKeys ?? XlsxNamedRangeMapper.GetLiveDefinedNameKeys(workbook);
    }

    public bool TryPrepareCandidate(XElement sourceName, out XElement candidate)
    {
        ArgumentNullException.ThrowIfNull(sourceName);

        candidate = new XElement(sourceName);
        var localSheetIdAttribute = candidate.Attribute("localSheetId");
        if (localSheetIdAttribute is null)
            return true;

        if (!int.TryParse(localSheetIdAttribute.Value, out var oldLocalSheetId) ||
            oldLocalSheetId < 0 ||
            oldLocalSheetId >= _sourceSheetNamesByLocalId.Count)
        {
            return false;
        }

        var sourceSheetName = _sourceSheetNamesByLocalId[oldLocalSheetId];
        var newLocalSheetId = FindSheetByName(sourceSheetName);
        if (newLocalSheetId < 0)
            newLocalSheetId = FindRenamedSheetByStableId(oldLocalSheetId);
        if (newLocalSheetId < 0)
            return false;

        localSheetIdAttribute.Value = newLocalSheetId.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public bool ShouldPreserveMissingCandidate(XElement candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return ShouldPreserveModelCandidate(candidate) && ShouldPreservePrintSetting(candidate);
    }

    public bool ShouldPreserveModelCandidate(XElement candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var name = candidate.Attribute("name")?.Value;
        var isModelRepresentable = !string.IsNullOrWhiteSpace(name) &&
            _workbook.ValidateNamedRangeName(name) is null &&
            !XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo(candidate.Value);
        return !isModelRepresentable ||
            _liveModelDefinedNameKeys.Contains(GetKey(candidate)) ||
            XlsxNamedRangeMapper.IsExcelReservedDefinedName(name);
    }

    public bool ShouldPreservePrintSetting(XElement candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var name = candidate.Attribute("name")?.Value;
        var localSheetIdAttribute = candidate.Attribute("localSheetId");
        if (XlsxPrintSettingNameClassifier.TryClassify(name, out var printSettingKind) &&
            localSheetIdAttribute is not null &&
            int.TryParse(localSheetIdAttribute.Value, out var scopeSheetIndex) &&
            scopeSheetIndex >= 0 &&
            scopeSheetIndex < _workbook.Sheets.Count)
        {
            var scopeSheet = _workbook.Sheets[scopeSheetIndex];
            return printSettingKind == XlsxPrintSettingKind.PrintArea
                ? scopeSheet.PrintAreas.Count > 0
                : scopeSheet.PrintTitleRows is not null || scopeSheet.PrintTitleColumns is not null;
        }

        return true;
    }

    public static string GetKey(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var name = element.Attribute("name")?.Value ?? string.Empty;
        var localSheetId = element.Attribute("localSheetId")?.Value ?? string.Empty;
        return $"{name}\u001f{localSheetId}";
    }

    public static bool BackfillMissingAttributes(XElement source, XElement target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var changed = false;
        foreach (var attribute in source.Attributes())
        {
            if (target.Attribute(attribute.Name) is not null)
                continue;

            target.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private int FindSheetByName(string sourceSheetName)
    {
        for (var sheetIndex = 0; sheetIndex < _targetSheetNames.Count; sheetIndex++)
        {
            if (string.Equals(
                    _targetSheetNames[sheetIndex],
                    sourceSheetName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return sheetIndex;
            }
        }

        return -1;
    }

    private int FindRenamedSheetByStableId(int oldLocalSheetId)
    {
        if (oldLocalSheetId >= _sourceSheetIdsByLocalId.Count)
            return -1;

        var sourceSheetId = _sourceSheetIdsByLocalId[oldLocalSheetId];
        for (var sheetIndex = 0; sheetIndex < _workbook.Sheets.Count; sheetIndex++)
        {
            if (_workbook.Sheets[sheetIndex].Id == sourceSheetId)
                return sheetIndex;
        }

        return -1;
    }
}
