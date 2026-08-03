using FreeX.App.Presentation.TableUI;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record CreateTableDialogPlan(GridRange Range, bool FirstRowHasHeaders, string TableStyleName);

public static class CreateTableDialogPlanner
{
    public const string TitleKey = "CreateTable_Title";
    public const string RangeLabelKey = "CreateTable_RangeLabel";
    public const string RangeAutomationNameKey = "CreateTable_RangeAutomationName";
    public const string RangeAutomationHelpTextKey = "CreateTable_RangeAutomationHelpText";
    public const string RangePickerAutomationNameKey = "CreateTable_RangePickerAutomationName";
    public const string HeadersCheckBoxKey = "CreateTable_HeadersCheckBox";
    public const string HeadersAutomationNameKey = "CreateTable_HeadersAutomationName";
    public const string HeadersAutomationHelpTextKey = "CreateTable_HeadersAutomationHelpText";
    public const string MissingRangeMessageKey = "CreateTable_MissingRangeMessage";
    public const string MinimumRowsMessageKey = "CreateTable_MinimumRowsMessage";
    public const string InvalidRangeMessageKey = "CreateTable_InvalidRangeMessage";

    public const string DialogAutomationId = "CreateTableDialog";
    public const string RangeBoxAutomationId = "CreateTableRangeBox";
    public const string HeadersBoxAutomationId = "CreateTableHeadersBox";

    public const double Width = 360;
    public const double Height = 190;
    public const double ButtonWidth = 76;
    public const bool DefaultFirstRowHasHeaders = true;
    public const double ContentMargin = 16;
    public const double RangeLabelBottomMargin = 4;
    public const double RangeEditorBottomMargin = 12;
    public const double HeadersBottomMargin = 16;
    public const double RangeBoxMinimumWidth = 248;
    public const double RangePickerWidth = 28;
    public const double RangePickerGap = 6;
    public const double ActionRowTopMargin = 12;

    public static bool TryParse(
        SheetId sheetId,
        string rangeText,
        bool firstRowHasHeaders,
        string tableStyleName,
        out CreateTableDialogPlan plan,
        out string? errorKey)
    {
        plan = default!;
        if (!CreateTableInputParser.TryParse(
                sheetId,
                rangeText,
                firstRowHasHeaders,
                tableStyleName,
                out var parsed,
                out var issue))
        {
            errorKey = issue switch
            {
                CreateTableInputParseIssue.MissingRange => MissingRangeMessageKey,
                CreateTableInputParseIssue.MinimumRows => MinimumRowsMessageKey,
                CreateTableInputParseIssue.InvalidRange => InvalidRangeMessageKey,
                _ => InvalidRangeMessageKey,
            };
            return false;
        }

        plan = new CreateTableDialogPlan(parsed.Range, parsed.FirstRowHasHeaders, parsed.TableStyleName);
        errorKey = null;
        return true;
    }
}
