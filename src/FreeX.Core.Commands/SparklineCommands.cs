using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class AddSparklineCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly SparklineModel _sparkline;
    private bool _added;

    public string Label => "Insert Sparkline";

    public AddSparklineCommand(
        SheetId sheetId,
        GridRange dataRange,
        CellAddress location,
        SparklineKind kind)
        : this(sheetId, dataRange, location, kind, groupId: 0)
    {
    }

    /// <summary>
    /// Creates a sparkline that is a member of a multi-sparkline group (Excel's "Insert Sparklines"
    /// dialog with a multi-row/column Location Range). Every member of the group must share the same
    /// nonzero <paramref name="groupId"/> so <c>XlsxSparklineMapper.Save</c> (which groups sparklines by
    /// <c>GroupId</c>) round-trips them as a single &lt;x14:sparklineGroup&gt; instead of one singleton
    /// group per sparkline.
    /// </summary>
    public AddSparklineCommand(
        SheetId sheetId,
        GridRange dataRange,
        CellAddress location,
        SparklineKind kind,
        int groupId)
    {
        _sheetId = sheetId;
        _sparkline = new SparklineModel
        {
            DataRange = dataRange,
            Location = location,
            Kind = kind,
            GroupId = groupId
        };
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sparkline.DataRange.Start.Sheet != _sheetId ||
            _sparkline.DataRange.End.Sheet != _sheetId ||
            _sparkline.Location.Sheet != _sheetId)
        {
            return new CommandOutcome(false, "Sparkline data range and location must be on the target sheet.");
        }
        if (!SparklineRangeLimits.IsSupportedDataRange(_sparkline.DataRange))
        {
            return new CommandOutcome(
                false,
                $"Sparkline data range must contain {SparklineRangeLimits.MaxDataCellCount:N0} cells or fewer.");
        }
        if (!Enum.IsDefined(_sparkline.Kind))
            return new CommandOutcome(false, "Sparkline type is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        sheet.Sparklines.Add(_sparkline);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [_sparkline.Location]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        ctx.GetSheet(_sheetId).Sparklines.Remove(_sparkline);
        _added = false;
    }
}

/// <summary>
/// The editable display options of a sparkline: its kind, the marker/point emphasis flags, and an
/// optional series color. Captured as a snapshot so <see cref="ConfigureSparklineCommand"/> can apply
/// a new set and restore the previous one on undo.
/// </summary>
public readonly record struct SparklineSettings(
    SparklineKind Kind,
    bool ShowMarkers,
    bool ShowHighPoint,
    bool ShowLowPoint,
    bool ShowFirstPoint,
    bool ShowLastPoint,
    bool ShowNegativePoints,
    CellColor? SeriesColor)
{
    public static SparklineSettings Capture(SparklineModel sparkline)
    {
        ArgumentNullException.ThrowIfNull(sparkline);
        return new SparklineSettings(
            sparkline.Kind,
            sparkline.ShowMarkers,
            sparkline.ShowHighPoint,
            sparkline.ShowLowPoint,
            sparkline.ShowFirstPoint,
            sparkline.ShowLastPoint,
            sparkline.ShowNegativePoints,
            sparkline.SeriesColor);
    }

    public void ApplyTo(SparklineModel sparkline)
    {
        ArgumentNullException.ThrowIfNull(sparkline);
        sparkline.Kind = Kind;
        sparkline.ShowMarkers = ShowMarkers;
        sparkline.ShowHighPoint = ShowHighPoint;
        sparkline.ShowLowPoint = ShowLowPoint;
        sparkline.ShowFirstPoint = ShowFirstPoint;
        sparkline.ShowLastPoint = ShowLastPoint;
        sparkline.ShowNegativePoints = ShowNegativePoints;
        sparkline.SeriesColor = SeriesColor;
    }
}

/// <summary>
/// Changes the type / marker / point-emphasis / color options of an existing sparkline (identified by
/// its id) in place, snapshotting the previous settings so the edit undoes cleanly. The data range and
/// location are left untouched — those are set at insert time.
/// </summary>
public sealed class ConfigureSparklineCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _sparklineId;
    private readonly SparklineSettings _settings;
    private SparklineSettings _previous;
    private bool _applied;

    public string Label => "Edit Sparkline";

    public ConfigureSparklineCommand(SheetId sheetId, Guid sparklineId, SparklineSettings settings)
    {
        _sheetId = sheetId;
        _sparklineId = sparklineId;
        _settings = settings;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_settings.Kind))
            return new CommandOutcome(false, "Sparkline type is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var sparkline = sheet.Sparklines.FirstOrDefault(s => s.Id == _sparklineId);
        if (sparkline is null)
            return new CommandOutcome(false, "The sparkline to edit was not found.");

        _previous = SparklineSettings.Capture(sparkline);
        _settings.ApplyTo(sparkline);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [sparkline.Location]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        var sparkline = ctx.GetSheet(_sheetId).Sparklines.FirstOrDefault(s => s.Id == _sparklineId);
        if (sparkline is not null)
            _previous.ApplyTo(sparkline);
        _applied = false;
    }
}

/// <summary>
/// Removes a sparkline (identified by its id) from a sheet, remembering its position so an undo
/// re-inserts it at the same index with the same model instance.
/// </summary>
public sealed class ClearSparklineCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _sparklineId;
    private SparklineModel? _removed;
    private int _removedIndex = -1;

    public string Label => "Clear Sparkline";

    public ClearSparklineCommand(SheetId sheetId, Guid sparklineId)
    {
        _sheetId = sheetId;
        _sparklineId = sparklineId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var index = -1;
        for (var i = 0; i < sheet.Sparklines.Count; i++)
        {
            if (sheet.Sparklines[i].Id == _sparklineId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return new CommandOutcome(false, "The sparkline to clear was not found.");

        _removed = sheet.Sparklines[index];
        _removedIndex = index;
        sheet.Sparklines.RemoveAt(index);
        return new CommandOutcome(true, AffectedCells: [_removed.Location]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removed is null)
            return;

        var sparklines = ctx.GetSheet(_sheetId).Sparklines;
        var index = Math.Clamp(_removedIndex, 0, sparklines.Count);
        sparklines.Insert(index, _removed);
        _removed = null;
        _removedIndex = -1;
    }
}
