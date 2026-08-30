using System.Collections.ObjectModel;
using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.SlicerTimeline;

/// <summary>
/// Reuses the native slicer/timeline projection and its source-item hydration while only the
/// viewport origin changes. Workbook mutations advance the host navigation revision and rebuild
/// the projection; culture is part of the key because source captions are culture-formatted.
/// </summary>
public sealed class ViewportNativeVisualFilterCache
{
    private static readonly ReadOnlyCollection<string> EmptyItems =
        Array.AsReadOnly(Array.Empty<string>());

    private Workbook? _workbook;
    private Sheet? _sheet;
    private ulong _revision;
    private string? _cultureName;
    private NativeVisualFilters? _filters;

    public NativeVisualFilters GetOrCreate(Workbook workbook, Sheet sheet, ulong revision)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        var cultureName = CultureInfo.CurrentCulture.Name;
        if (_filters is not null &&
            ReferenceEquals(_workbook, workbook) &&
            ReferenceEquals(_sheet, sheet) &&
            _revision == revision &&
            string.Equals(_cultureName, cultureName, StringComparison.Ordinal))
        {
            return _filters;
        }

        var planned = SlicerTimelinePanePlanner.GetNativeVisualFilters(workbook, sheet);
        var slicers = FreezeModels(planned.Slicers);
        var timelines = FreezeModels(planned.Timelines);
        if (slicers.Count > 0)
        {
            var sourceSession = new SlicerTimelineSourceSession(workbook);
            foreach (var slicer in slicers)
                slicer.AvailableItems = FreezeItems(sourceSession.ReadSlicerSourceItems(slicer));
        }

        var filters = new NativeVisualFilters(slicers, timelines);
        _workbook = workbook;
        _sheet = sheet;
        _revision = revision;
        _cultureName = cultureName;
        _filters = filters;
        return filters;
    }

    public void Clear()
    {
        _workbook = null;
        _sheet = null;
        _revision = 0;
        _cultureName = null;
        _filters = null;
    }

    private static ReadOnlyCollection<string> FreezeItems(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return EmptyItems;

        var snapshot = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
            snapshot[index] = values[index];

        return Array.AsReadOnly(snapshot);
    }

    private static ReadOnlyCollection<T> FreezeModels<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
            return Array.AsReadOnly(Array.Empty<T>());

        var snapshot = new T[values.Count];
        for (var index = 0; index < values.Count; index++)
            snapshot[index] = values[index];

        return Array.AsReadOnly(snapshot);
    }
}
