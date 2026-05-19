using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class SparklineInputParser
{
    public static bool TryParseDataRange(string input, SheetId sheetId, out GridRange range)
    {
        try
        {
            range = GridRange.Parse(input, sheetId);
            return true;
        }
        catch
        {
            range = default;
            return false;
        }
    }

    public static bool TryParseLocation(string input, SheetId sheetId, out CellAddress location) =>
        CellAddress.TryParse(input, sheetId, out location);

    public static SparklineKindChoice ParseDialogKindChoice(string type) =>
        type switch
        {
            "column" => SparklineKindChoice.Column,
            "winloss" => SparklineKindChoice.WinLoss,
            _ => SparklineKindChoice.Line
        };

    public static SparklineKind ToModelKind(SparklineKindChoice choice) =>
        choice switch
        {
            SparklineKindChoice.Column => SparklineKind.Column,
            SparklineKindChoice.WinLoss => SparklineKind.WinLoss,
            _ => SparklineKind.Line
        };

    public static SparklineKind ParseKind(string type) =>
        type switch
        {
            "column" => SparklineKind.Column,
            "winloss" => SparklineKind.WinLoss,
            _ => SparklineKind.Line
        };
}
