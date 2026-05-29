namespace FreeX.App.Host;

internal static class TextToColumnsFixedWidthRulerPlanner
{
    public static int PositionFromRulerX(double x, double rulerWidth, int maxLength)
    {
        var width = EffectiveRulerWidth(rulerWidth);
        var length = EffectiveMaxLength(maxLength);
        return (int)Math.Round(Math.Clamp(x, 0, width) / width * length);
    }

    public static double RulerXFromPosition(int position, double rulerWidth, int maxLength)
    {
        var length = EffectiveMaxLength(maxLength);
        return Math.Clamp(position, 0, length) / (double)length * EffectiveRulerWidth(rulerWidth);
    }

    public static int FindNearestBreakIndex(
        IReadOnlyList<int> positions,
        double x,
        double tolerance,
        double rulerWidth,
        int maxLength)
    {
        var nearestIndex = -1;
        var nearestDistance = double.MaxValue;
        for (var index = 0; index < positions.Count; index++)
        {
            var distance = Math.Abs(RulerXFromPosition(positions[index], rulerWidth, maxLength) - x);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        return nearestDistance <= tolerance ? nearestIndex : -1;
    }

    public static int MaxLength(IReadOnlyList<string> previewRows) =>
        Math.Max(2, previewRows.Count == 0 ? 2 : previewRows.Max(row => row.Length));

    public static double EffectiveRulerWidth(double actualWidth) =>
        actualWidth > 1 ? actualWidth : 440;

    private static int EffectiveMaxLength(int maxLength) =>
        Math.Max(2, maxLength);
}
