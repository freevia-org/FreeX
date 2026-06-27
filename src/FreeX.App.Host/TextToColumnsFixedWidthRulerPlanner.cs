namespace FreeX.App.Host;

internal static class TextToColumnsFixedWidthRulerPlanner
{
    public static int PositionFromRulerX(double x, double rulerWidth, int maxLength)
        => FreeX.App.Presentation.TextToColumns.TextToColumnsFixedWidthRulerPlanner.PositionFromRulerX(x, rulerWidth, maxLength);

    public static double RulerXFromPosition(int position, double rulerWidth, int maxLength)
        => FreeX.App.Presentation.TextToColumns.TextToColumnsFixedWidthRulerPlanner.RulerXFromPosition(position, rulerWidth, maxLength);

    public static int FindNearestBreakIndex(
        IReadOnlyList<int> positions,
        double x,
        double tolerance,
        double rulerWidth,
        int maxLength)
        => FreeX.App.Presentation.TextToColumns.TextToColumnsFixedWidthRulerPlanner.FindNearestBreakIndex(
            positions,
            x,
            tolerance,
            rulerWidth,
            maxLength);

    public static int MaxLength(IReadOnlyList<string> previewRows) =>
        FreeX.App.Presentation.TextToColumns.TextToColumnsFixedWidthRulerPlanner.MaxLength(previewRows);

    public static double EffectiveRulerWidth(double actualWidth) =>
        FreeX.App.Presentation.TextToColumns.TextToColumnsFixedWidthRulerPlanner.EffectiveRulerWidth(actualWidth);
}
