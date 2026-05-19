namespace Freexcel.App.Host;

public static class ForecastSheetInputParser
{
    public static bool TryParsePeriods(string input, out uint periods)
    {
        if (uint.TryParse(input.Trim(), out var parsed) && parsed > 0)
        {
            periods = parsed;
            return true;
        }

        periods = 0;
        return false;
    }
}
