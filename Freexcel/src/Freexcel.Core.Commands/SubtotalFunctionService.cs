namespace Freexcel.Core.Commands;

public static class SubtotalFunctionService
{
    private static readonly Dictionary<string, int> FunctionNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["average"] = 1,
        ["count"] = 2,
        ["counta"] = 3,
        ["max"] = 4,
        ["min"] = 5,
        ["product"] = 6,
        ["stdev"] = 7,
        ["stdev.s"] = 7,
        ["stdevp"] = 8,
        ["stdev.p"] = 8,
        ["sum"] = 9,
        ["var"] = 10,
        ["var.s"] = 10,
        ["varp"] = 11,
        ["var.p"] = 11
    };

    public static bool TryParse(string text, out int functionNumber)
    {
        var normalized = text.Trim();
        if (FunctionNumbers.TryGetValue(normalized, out functionNumber))
            return true;

        if (int.TryParse(normalized, out functionNumber) && functionNumber is >= 1 and <= 11)
            return true;

        functionNumber = 0;
        return false;
    }
}
