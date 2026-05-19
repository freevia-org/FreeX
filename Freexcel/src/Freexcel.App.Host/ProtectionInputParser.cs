using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ProtectionInputParser
{
    public static bool TryParseAllowEditRange(string input, SheetId sheetId, out GridRange range)
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
