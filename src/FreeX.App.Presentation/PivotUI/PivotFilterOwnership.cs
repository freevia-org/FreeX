using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

public static class PivotFilterOwnership
{
    // WPF treats an unbound value filter as applying to the field currently being edited.
    public static bool BelongsToSourceField(PivotValueFilterModel filter, int sourceFieldIndex) =>
        filter.SourceFieldIndex is null || filter.SourceFieldIndex == sourceFieldIndex;
}
