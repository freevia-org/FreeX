namespace FreeX.Core.Commands;

internal static class ObjectRotationNormalizer
{
    private const double FullCircleDegrees = 360;

    public static double NormalizeDegrees(double value)
    {
        var normalized = value % FullCircleDegrees;
        return normalized < 0 ? normalized + FullCircleDegrees : normalized;
    }
}
