using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ChartInputParser
{
    public static bool TryParseDataRange(string input, SheetId sheetId, out GridRange range)
    {
        try
        {
            range = GridRange.Parse(input.Trim(), sheetId);
            return true;
        }
        catch
        {
            range = default;
            return false;
        }
    }
}
