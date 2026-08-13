namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Action<PivotRuntimeObservation>? _pivotRuntimeObserver;

    internal PivotRuntimeObservationAccessAdapter CreatePivotRuntimeObservationAccessAdapter() => new(this);

    internal sealed class PivotRuntimeObservationAccessAdapter
    {
        private readonly MainWindow _owner;

        internal PivotRuntimeObservationAccessAdapter(MainWindow owner) => _owner = owner;

        internal void SetObserver(Action<PivotRuntimeObservation> observer) =>
            _owner._pivotRuntimeObserver = observer ?? throw new ArgumentNullException(nameof(observer));
    }
}

internal sealed record PivotRuntimeObservation(
    string Stage,
    string ActiveSheet,
    string ActiveSheetId,
    string ActiveCellSheetId,
    uint ActiveCellRow,
    uint ActiveCellColumn,
    string? CurrentFilePath,
    string WorkbookName,
    IReadOnlyList<PivotRuntimeSheetObservation> WorkbookSheets,
    int SheetPivotCount,
    IReadOnlyList<PivotRuntimeTableObservation> Pivots,
    string? ResolvedPivot,
    bool PaneVisible,
    double PaneWidth,
    bool UserHidden);

internal sealed record PivotRuntimeSheetObservation(string Name, int PivotCount);

internal sealed record PivotRuntimeTableObservation(
    string Name,
    string TargetStart,
    string TargetEnd,
    string? RenderedStart,
    string? RenderedEnd);
