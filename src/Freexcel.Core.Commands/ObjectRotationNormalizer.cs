namespace Freexcel.Core.Commands;

internal static class ObjectRotationNormalizer
{
    public static double NormalizeDegrees(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
