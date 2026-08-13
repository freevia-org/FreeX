namespace FreeX.App.Presentation.Dialogs;

public static class DialogRangeSelectionGeometryPlanner
{
    public static double ResolveDimension(
        double actual,
        double configured,
        double fallback)
    {
        if (!double.IsNaN(actual) && actual > 0)
            return actual;
        if (!double.IsNaN(configured) && configured > 0)
            return configured;
        return fallback;
    }
}
