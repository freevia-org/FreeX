namespace FreeX.App.Host;

public static class WorksheetSizeInputParser
{
    public static bool TryParsePositiveSize(string input, out double size)
        => TryParseSizeInRange(input, minInclusive: double.Epsilon, maxInclusive: double.MaxValue, out size);

    public static bool TryParseSizeInRange(string input, double minInclusive, double maxInclusive, out double size)
    {
        if (double.TryParse(input.Trim(), out var parsed) &&
            double.IsFinite(parsed) &&
            parsed >= minInclusive &&
            parsed <= maxInclusive)
        {
            size = parsed;
            return true;
        }

        size = 0;
        return false;
    }
}
