using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Pivot;

internal static class PivotValueFilterOwnership
{
    // WPF treats an unbound value filter as applying to the field currently being edited.
    internal static bool BelongsToSourceField(PivotValueFilterModel filter, int sourceFieldIndex) =>
        filter.SourceFieldIndex is null || filter.SourceFieldIndex == sourceFieldIndex;
}
