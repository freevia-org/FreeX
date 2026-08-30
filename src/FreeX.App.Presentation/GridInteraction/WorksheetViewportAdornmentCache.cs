using System.Collections.Frozen;
using System.Collections.ObjectModel;
using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

public sealed record WorksheetViewportAdornments(
    IReadOnlySet<(uint Row, uint Col)>? PinnedNoteAddresses,
    GridRange? AutoFilterRange,
    IReadOnlySet<uint>? ActiveAutoFilterColumns,
    IReadOnlyList<PivotHeaderDropdownTarget> PivotHeaderDropdowns,
    IReadOnlyDictionary<(uint Row, uint Col), PivotHeaderDropdownTarget> PivotHeaderDropdownTargets,
    IReadOnlyList<PivotRowLabelAdornment> PivotRowLabelAdornments,
    IReadOnlySet<CellAddress>? HyperlinkCells,
    IReadOnlyDictionary<CellAddress, string>? HyperlinkTooltips);

/// <summary>
/// Caches sheet-stable viewport adornments by workbook/sheet identity and navigation revision.
/// Scrolling can then reuse immutable collection references instead of rebuilding projections and
/// retriggering dependency-property callbacks on every viewport origin change.
/// </summary>
public sealed class WorksheetViewportAdornmentCache
{
    private static readonly ReadOnlyCollection<PivotHeaderDropdownTarget> EmptyPivotHeaders =
        Array.AsReadOnly(Array.Empty<PivotHeaderDropdownTarget>());
    private static readonly ReadOnlyCollection<PivotRowLabelAdornment> EmptyPivotRows =
        Array.AsReadOnly(Array.Empty<PivotRowLabelAdornment>());
    private static readonly WorksheetViewportAdornments Empty = new(
        PinnedNoteAddresses: null,
        AutoFilterRange: null,
        ActiveAutoFilterColumns: null,
        PivotHeaderDropdowns: EmptyPivotHeaders,
        PivotHeaderDropdownTargets: FrozenDictionary<(uint Row, uint Col), PivotHeaderDropdownTarget>.Empty,
        PivotRowLabelAdornments: EmptyPivotRows,
        HyperlinkCells: null,
        HyperlinkTooltips: null);

    private Workbook? _workbook;
    private Sheet? _sheet;
    private ulong _revision;
    private WorksheetViewportAdornments? _adornments;

    public WorksheetViewportAdornments GetOrCreate(
        Workbook workbook,
        Sheet? sheet,
        ulong revision)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (_adornments is not null &&
            ReferenceEquals(_workbook, workbook) &&
            ReferenceEquals(_sheet, sheet) &&
            _revision == revision)
        {
            return _adornments;
        }

        var adornments = sheet is null ? Empty : Build(workbook, sheet);
        _workbook = workbook;
        _sheet = sheet;
        _revision = revision;
        _adornments = adornments;
        return adornments;
    }

    public void Clear()
    {
        _workbook = null;
        _sheet = null;
        _revision = 0;
        _adornments = null;
    }

    private static WorksheetViewportAdornments Build(Workbook workbook, Sheet sheet)
    {
        IReadOnlySet<(uint Row, uint Col)>? pinnedNoteAddresses = null;
        if (sheet.ShownComments.Count > 0)
        {
            var addresses = new HashSet<(uint Row, uint Col)>(sheet.ShownComments.Count);
            foreach (var address in sheet.ShownComments)
                addresses.Add((address.Row, address.Col));

            pinnedNoteAddresses = addresses.ToFrozenSet();
        }

        GridRange? autoFilterRange =
            AutoFilterDropdownMenuPlanner.TryGetAutoFilterRange(sheet, out var resolvedAutoFilterRange)
                ? resolvedAutoFilterRange
                : null;
        var activeColumnOffsets = autoFilterRange is { } activeFilterRange
            ? AutoFilterHeaderButtonPlanner.GetActiveColumnOffsets(sheet, activeFilterRange)
            : null;
        IReadOnlySet<uint>? activeAutoFilterColumns = activeColumnOffsets?.ToFrozenSet();

        var pivotHeaders = Freeze(PivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet));
        var pivotHeaderLookup = pivotHeaders.ToFrozenDictionary(
            static target => (target.HeaderCell.Row, target.HeaderCell.Col));
        var pivotRows = Freeze(PivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet));

        var hyperlinkCells = new HashSet<CellAddress>(sheet.Hyperlinks.Count);
        var hyperlinkTooltips = new Dictionary<CellAddress, string>(sheet.Hyperlinks.Count);
        foreach (var (sourceAddress, target) in sheet.Hyperlinks)
        {
            var address = new CellAddress(default, sourceAddress.Row, sourceAddress.Col);
            hyperlinkCells.Add(address);
            if (string.IsNullOrWhiteSpace(target))
                continue;

            var tooltip = sheet.HyperlinkMetadata.TryGetValue(sourceAddress, out var metadata) &&
                          !string.IsNullOrWhiteSpace(metadata.ScreenTip)
                ? metadata.ScreenTip
                : target;
            hyperlinkTooltips[address] = tooltip.Trim();
        }

        return new WorksheetViewportAdornments(
            pinnedNoteAddresses,
            autoFilterRange,
            activeAutoFilterColumns,
            pivotHeaders,
            pivotHeaderLookup,
            pivotRows,
            hyperlinkCells.ToFrozenSet(),
            hyperlinkTooltips.ToFrozenDictionary());
    }

    private static ReadOnlyCollection<T> Freeze<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
            return Array.AsReadOnly(Array.Empty<T>());

        var snapshot = new T[values.Count];
        for (var index = 0; index < values.Count; index++)
            snapshot[index] = values[index];

        return Array.AsReadOnly(snapshot);
    }
}
