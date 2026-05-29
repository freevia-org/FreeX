using FreeX.Core.Model;

namespace FreeX.App.Host;

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
