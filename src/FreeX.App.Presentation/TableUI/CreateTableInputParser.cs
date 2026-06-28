using FreeX.Core.Model;

namespace FreeX.App.Presentation.TableUI;

public enum CreateTableInputParseIssue
{
    None,
    MissingRange,
    MinimumRows,
    InvalidRange
}

public sealed record CreateTableInputParseResult(
    GridRange Range,
    bool FirstRowHasHeaders,
    string TableStyleName);

public static class CreateTableInputParser
{
    public static bool TryParse(
        SheetId sheetId,
        string? rangeText,
        bool firstRowHasHeaders,
        string? tableStyleName,
        out CreateTableInputParseResult result,
        out CreateTableInputParseIssue issue)
    {
        result = default!;
        issue = CreateTableInputParseIssue.None;
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            issue = CreateTableInputParseIssue.MissingRange;
            return false;
        }

        try
        {
            var trimmedRangeText = rangeText.Trim();
            var range = trimmedRangeText.Contains(':', StringComparison.Ordinal)
                ? GridRange.Parse(trimmedRangeText, sheetId)
                : new GridRange(
                    CellAddress.Parse(trimmedRangeText, sheetId),
                    CellAddress.Parse(trimmedRangeText, sheetId));

            if (range.End.Row <= range.Start.Row)
            {
                issue = CreateTableInputParseIssue.MinimumRows;
                return false;
            }

            result = new CreateTableInputParseResult(
                range,
                firstRowHasHeaders,
                tableStyleName?.Trim() ?? "");
            return true;
        }
        catch (FormatException)
        {
            issue = CreateTableInputParseIssue.InvalidRange;
            return false;
        }
    }
}
