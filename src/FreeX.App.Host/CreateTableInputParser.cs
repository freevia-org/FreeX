using FreeX.App.Services;
using FreeX.Core.Model;
using SharedCreateTableInputParseIssue = FreeX.App.Presentation.TableUI.CreateTableInputParseIssue;
using SharedCreateTableInputParser = FreeX.App.Presentation.TableUI.CreateTableInputParser;

namespace FreeX.App.Host;

public static class CreateTableInputParser
{
    public static bool TryParse(
        SheetId sheetId,
        string rangeText,
        bool firstRowHasHeaders,
        string tableStyleName,
        out CreateTableDialogResult result,
        out string? error)
    {
        if (SharedCreateTableInputParser.TryParse(
                sheetId,
                rangeText,
                firstRowHasHeaders,
                tableStyleName,
                out var parsed,
                out var issue))
        {
            result = new CreateTableDialogResult(parsed.Range, parsed.FirstRowHasHeaders, parsed.TableStyleName);
            error = null;
            return true;
        }

        result = default!;
        error = DescribeIssue(issue);
        return false;
    }

    private static string? DescribeIssue(SharedCreateTableInputParseIssue issue) =>
        issue switch
        {
            SharedCreateTableInputParseIssue.MissingRange => UiText.Get(CreateTableDialogPlanner.MissingRangeMessageKey),
            SharedCreateTableInputParseIssue.MinimumRows => UiText.Get(CreateTableDialogPlanner.MinimumRowsMessageKey),
            SharedCreateTableInputParseIssue.InvalidRange => UiText.Get(CreateTableDialogPlanner.InvalidRangeMessageKey),
            _ => null
        };
}
