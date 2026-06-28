using FreeX.Core.Model;
using SharedPivotCreatePlanner = FreeX.App.Presentation.PivotUI.PivotCreatePlanner;
using SharedPivotCreateSourceRangeError = FreeX.App.Presentation.PivotUI.PivotCreateSourceRangeError;

namespace FreeX.App.Host;

internal enum PivotTableSourceRangeError
{
    None,
    MissingSource,
    MinimumShape,
    MissingHeaders
}

internal sealed record PivotTableSourceRangePlan(GridRange? SourceRange, PivotTableSourceRangeError Error)
{
    public bool IsValid => Error == PivotTableSourceRangeError.None && SourceRange is not null;
}

internal static class PivotTableSourceRangePlanner
{
    public static GridRange Create(Sheet sheet, GridRange selectedRange) =>
        SharedPivotCreatePlanner.CreateSourceRange(sheet, selectedRange);

    public static PivotTableSourceRangePlan CreatePlan(Sheet? sheet, GridRange? selectedRange)
    {
        var plan = SharedPivotCreatePlanner.CreateSourceRangePlan(sheet, selectedRange);
        return new PivotTableSourceRangePlan(plan.SourceRange, Project(plan.Error));
    }

    public static IReadOnlyList<RecommendedPivotTableLayout> CreateRecommendedLayouts(Sheet sheet, GridRange sourceRange) =>
        SharedPivotCreatePlanner.CreateRecommendedLayouts(sheet, sourceRange)
            .Select(layout => new RecommendedPivotTableLayout(
                layout.Title,
                layout.RowFieldIndexes,
                layout.DataFieldIndexes))
            .ToList();

    private static PivotTableSourceRangeError Project(SharedPivotCreateSourceRangeError error) =>
        error switch
        {
            SharedPivotCreateSourceRangeError.MissingSource => PivotTableSourceRangeError.MissingSource,
            SharedPivotCreateSourceRangeError.MinimumShape => PivotTableSourceRangeError.MinimumShape,
            SharedPivotCreateSourceRangeError.MissingHeaders => PivotTableSourceRangeError.MissingHeaders,
            _ => PivotTableSourceRangeError.None
        };
}

internal sealed record RecommendedPivotTableLayout(
    string Title,
    IReadOnlyList<int> RowFieldIndexes,
    IReadOnlyList<int> DataFieldIndexes);
