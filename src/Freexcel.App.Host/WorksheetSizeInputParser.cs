namespace Freexcel.App.Host;

public static class WorksheetSizeInputParser
{
    public static bool TryParsePositiveSize(string input, out double size)
    {
        if (double.TryParse(input.Trim(), out var parsed) &&
            double.IsFinite(parsed) &&
            parsed > 0)
        {
            size = parsed;
            return true;
        }

        size = 0;
        return false;
    }
}
