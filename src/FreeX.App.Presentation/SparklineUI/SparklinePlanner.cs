using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SparklineUI;

/// <summary>
/// The outcome of validating the Insert Sparkline inputs: either both ranges parsed (carrying the
/// resolved data range + anchor cell) or which field is at fault.
/// </summary>
public enum SparklineInputValidation
{
    Valid,
    InvalidDataRange,
    InvalidLocation,
}

public enum SparklineRangeSelectionTarget
{
    DataRange,
    Location,
}

public sealed record SparklineDialogResult(string DataRangeText, string LocationText, SparklineKind Kind);

/// <summary>
/// One member of a multi-sparkline group: the data range feeding a single sparkline and the single
/// cell it is drawn into. <see cref="SparklinePlanner.ValidateInsertGroup"/> expands a multi-row/column
/// Location Range into one of these per row (or column), matching Excel's "Insert Sparklines" dialog.
/// </summary>
public readonly record struct SparklineGroupMember(GridRange DataRange, CellAddress Location);

public sealed record SparklineRangeSelectionRequest(
    SparklineRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog);

/// <summary>
/// The marker / point-emphasis flags a Sparkline edit can toggle, surfaced as a catalog so the shell
/// can render one checkbox per entry without hard-coding the list.
/// </summary>
public enum SparklinePointToggle
{
    Markers,
    HighPoint,
    LowPoint,
    FirstPoint,
    LastPoint,
    NegativePoints,
}

/// <summary>
/// Portable, framework-free decision logic behind the Sparkline insert / edit dialogs, shared by every
/// shell (the cross-platform shell today, macOS by inheritance). It owns the sparkline type catalog, the
/// data-range and location parsing rules (single-sourced with the Core <see cref="SparklineRangeLimits"/>
/// cap), the
/// marker / point flag catalog + projection, and the build of a <see cref="SparklineSettings"/> snapshot
/// the Core <see cref="ConfigureSparklineCommand"/> applies. No UI types, so the shells only wire
/// controls to it.
/// </summary>
public static class SparklinePlanner
{
    public const double InsertDialogCaptureWidth = 380;
    public const double InsertDialogCaptureHeight = 280;

    /// <summary>The sparkline kinds offered by the dialog, in display order.</summary>
    public static IReadOnlyList<SparklineKind> Kinds { get; } =
    [
        SparklineKind.Line,
        SparklineKind.Column,
        SparklineKind.WinLoss,
    ];

    /// <summary>The point-emphasis toggles offered by the edit dialog, in display order.</summary>
    public static IReadOnlyList<SparklinePointToggle> PointToggles { get; } =
    [
        SparklinePointToggle.Markers,
        SparklinePointToggle.HighPoint,
        SparklinePointToggle.LowPoint,
        SparklinePointToggle.FirstPoint,
        SparklinePointToggle.LastPoint,
        SparklinePointToggle.NegativePoints,
    ];

    /// <summary>The neutral display label for a sparkline kind (used as a localization-key suffix too).</summary>
    public static string KindKey(SparklineKind kind) =>
        kind switch
        {
            SparklineKind.Column => "Column",
            SparklineKind.WinLoss => "WinLoss",
            _ => "Line",
        };

    /// <summary>The neutral display label for a point toggle (used as a localization-key suffix too).</summary>
    public static string ToggleKey(SparklinePointToggle toggle) => toggle.ToString();

    public static SparklineDialogResult CreateDialogResult(
        string? dataRangeText,
        string? locationText,
        SparklineKind kind) =>
        new((dataRangeText ?? string.Empty).Trim(), (locationText ?? string.Empty).Trim(), kind);

    public static SparklineRangeSelectionRequest CreateRangeSelectionRequest(
        SparklineRangeSelectionTarget target,
        string? currentText) =>
        new(target, (currentText ?? string.Empty).Trim(), CollapseDialog: true);

    public static SparklineInputValidation ValidateDialogInputs(
        string? dataRangeText,
        string? locationText,
        SheetId sheetId) =>
        ValidateInsert(dataRangeText ?? string.Empty, locationText ?? string.Empty, sheetId, out _, out _);

    /// <summary>
    /// Markers apply only to line sparklines; negative-point emphasis applies only to column / win-loss
    /// sparklines. Used to grey out toggles that do not affect the selected kind.
    /// </summary>
    public static bool IsToggleApplicable(SparklinePointToggle toggle, SparklineKind kind) =>
        toggle switch
        {
            SparklinePointToggle.Markers => kind == SparklineKind.Line,
            SparklinePointToggle.NegativePoints => kind != SparklineKind.Line,
            _ => true,
        };

    /// <summary>Reads the current state of a toggle off a settings snapshot.</summary>
    public static bool GetToggle(SparklineSettings settings, SparklinePointToggle toggle) =>
        toggle switch
        {
            SparklinePointToggle.Markers => settings.ShowMarkers,
            SparklinePointToggle.HighPoint => settings.ShowHighPoint,
            SparklinePointToggle.LowPoint => settings.ShowLowPoint,
            SparklinePointToggle.FirstPoint => settings.ShowFirstPoint,
            SparklinePointToggle.LastPoint => settings.ShowLastPoint,
            SparklinePointToggle.NegativePoints => settings.ShowNegativePoints,
            _ => false,
        };

    /// <summary>
    /// Validates the Insert Sparkline inputs, returning the resolved data range + anchor cell when both
    /// parse. The data range must be a real range within the supported cell cap; the location must be a
    /// single cell. Uses the shared cell-reference parser so absolute A1 and R1C1 inputs behave
    /// consistently across shells.
    /// </summary>
    public static SparklineInputValidation ValidateInsert(
        string dataRangeText,
        string locationText,
        SheetId sheetId,
        out GridRange dataRange,
        out CellAddress location)
    {
        dataRange = default;
        location = default;

        if (!TryParseDataRange(dataRangeText, sheetId, out dataRange))
            return SparklineInputValidation.InvalidDataRange;

        return CellReferenceInputParser.TryParseCell((locationText ?? string.Empty).Trim(), sheetId, out location)
            ? SparklineInputValidation.Valid
            : SparklineInputValidation.InvalidLocation;
    }

    /// <summary>
    /// Validates the Insert Sparkline inputs allowing a multi-cell Location Range, expanding it into one
    /// sparkline per row (or per column) of the data range -- matching Excel's "Insert Sparklines"
    /// dialog, where a data range of B2:D6 with a location range of E2:E6 creates a 5-member sparkline
    /// group, one sparkline per row. The location must have the same row count as the data range (one
    /// sparkline per row, laid out down a single column) or the same column count (one sparkline per
    /// column, laid out across a single row); a plain single-cell location still yields a single-member
    /// group anchored at that cell, matching <see cref="ValidateInsert"/>.
    /// </summary>
    public static SparklineInputValidation ValidateInsertGroup(
        string dataRangeText,
        string locationText,
        SheetId sheetId,
        out IReadOnlyList<SparklineGroupMember> members)
    {
        members = [];

        if (!TryParseDataRange(dataRangeText, sheetId, out var dataRange))
            return SparklineInputValidation.InvalidDataRange;

        var trimmedLocation = (locationText ?? string.Empty).Trim();

        // Single-cell location: one sparkline over the whole data range, anchored at that cell.
        if (CellReferenceInputParser.TryParseCell(trimmedLocation, sheetId, out var singleLocation))
        {
            members = [new SparklineGroupMember(dataRange, singleLocation)];
            return SparklineInputValidation.Valid;
        }

        if (!TryParseLocationRange(trimmedLocation, sheetId, out var locationRange))
            return SparklineInputValidation.InvalidLocation;

        var dataRows = dataRange.RowCount;
        var dataCols = dataRange.ColCount;
        var locationRows = locationRange.RowCount;
        var locationCols = locationRange.ColCount;

        List<SparklineGroupMember> group;
        if (locationCols == 1 && locationRows == dataRows && dataRows > 1)
        {
            // One sparkline per data row, stacked down a single location column.
            group = new List<SparklineGroupMember>((int)dataRows);
            for (uint i = 0; i < dataRows; i++)
            {
                var rowRange = new GridRange(
                    new CellAddress(sheetId, dataRange.Start.Row + i, dataRange.Start.Col),
                    new CellAddress(sheetId, dataRange.Start.Row + i, dataRange.End.Col));
                var location = new CellAddress(sheetId, locationRange.Start.Row + i, locationRange.Start.Col);
                group.Add(new SparklineGroupMember(rowRange, location));
            }
        }
        else if (locationRows == 1 && locationCols == dataCols && dataCols > 1)
        {
            // One sparkline per data column, spread across a single location row.
            group = new List<SparklineGroupMember>((int)dataCols);
            for (uint i = 0; i < dataCols; i++)
            {
                var colRange = new GridRange(
                    new CellAddress(sheetId, dataRange.Start.Row, dataRange.Start.Col + i),
                    new CellAddress(sheetId, dataRange.End.Row, dataRange.Start.Col + i));
                var location = new CellAddress(sheetId, locationRange.Start.Row, locationRange.Start.Col + i);
                group.Add(new SparklineGroupMember(colRange, location));
            }
        }
        else
        {
            // Location range shape doesn't correspond to the data range's rows or columns.
            return SparklineInputValidation.InvalidLocation;
        }

        members = group;
        return SparklineInputValidation.Valid;
    }

    /// <summary>
    /// Builds the undoable insert command for a validated single sparkline or sparkline group. A group
    /// receives one shared, nonzero id so it survives XLSX round-trips as one x14:sparklineGroup.
    /// </summary>
    public static IWorkbookCommand BuildInsertCommand(
        SheetId sheetId,
        IReadOnlyList<SparklineGroupMember> members,
        SparklineKind kind,
        IEnumerable<SparklineModel> existingSparklines,
        CellAddress? singleLocationOverride = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(existingSparklines);
        if (members.Count == 0)
            throw new ArgumentException("At least one sparkline group member is required.", nameof(members));

        if (members.Count == 1)
        {
            var member = members[0];
            return new AddSparklineCommand(
                sheetId,
                member.DataRange,
                singleLocationOverride ?? member.Location,
                kind);
        }

        var groupId = SparklineGroupIdAllocator.NextGroupId(existingSparklines);
        var commands = members
            .Select(member => (IWorkbookCommand)new AddSparklineCommand(
                sheetId,
                member.DataRange,
                member.Location,
                kind,
                groupId))
            .ToList();
        return new CompositeWorkbookCommand("Insert Sparkline", commands);
    }

    /// <summary>Parses a sparkline location, accepting a multi-cell range (for a sparkline group).</summary>
    private static bool TryParseLocationRange(string? input, SheetId sheetId, out GridRange range)
    {
        range = default;
        try
        {
            range = GridRange.Parse((input ?? string.Empty).Trim(), sheetId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Maps toolbar / command identifiers to the core sparkline kind.</summary>
    public static SparklineKind ParseKind(string? type) =>
        (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "column" => SparklineKind.Column,
            "winloss" => SparklineKind.WinLoss,
            _ => SparklineKind.Line,
        };

    /// <summary>Parses a sparkline data range, rejecting ranges over the supported cell cap.</summary>
    public static bool TryParseDataRange(string? input, SheetId sheetId, out GridRange range)
    {
        range = default;
        try
        {
            var parsed = GridRange.Parse((input ?? string.Empty).Trim(), sheetId);
            if (!SparklineRangeLimits.IsSupportedDataRange(parsed))
                return false;

            range = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the settings snapshot the edit command applies, clearing flags that do not apply to the
    /// chosen kind so an edit never leaves a stale flag (e.g. markers on a column sparkline).
    /// </summary>
    public static SparklineSettings BuildSettings(
        SparklineKind kind,
        bool showMarkers,
        bool showHighPoint,
        bool showLowPoint,
        bool showFirstPoint,
        bool showLastPoint,
        bool showNegativePoints,
        CellColor? seriesColor)
    {
        var isLine = kind == SparklineKind.Line;
        return new SparklineSettings(
            kind,
            ShowMarkers: showMarkers && isLine,
            showHighPoint,
            showLowPoint,
            showFirstPoint,
            showLastPoint,
            ShowNegativePoints: showNegativePoints && !isLine,
            seriesColor);
    }
}
