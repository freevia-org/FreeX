using FreeW.App.Localization;
using FreeW.Core.IO.RecipientSources;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// Localized text for Word's Select Recipients surfaces: Use an Existing List (file picker and import
/// options), Type a New List (the address-list grid) and Edit Recipient List (include / sort / filter).
/// </summary>
public static class MailMergeRecipientSourceText
{
    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
    [
        "MailMerge_Source_SelectTitle",
        "MailMerge_Source_Filter_All",
        "MailMerge_Source_Filter_Excel",
        "MailMerge_Source_Filter_Text",
        "MailMerge_Source_Filter_Word",
        "MailMerge_Source_ImportTitle",
        "MailMerge_Source_FileLabelFormat",
        "MailMerge_Source_TableLabel",
        "MailMerge_Source_Table_WorksheetFormat",
        "MailMerge_Source_Table_NamedRangeFormat",
        "MailMerge_Source_Table_TableFormat",
        "MailMerge_Source_DelimiterLabel",
        "MailMerge_Source_Delimiter_Comma",
        "MailMerge_Source_Delimiter_Tab",
        "MailMerge_Source_Delimiter_Semicolon",
        "MailMerge_Source_Delimiter_Pipe",
        "MailMerge_Source_EncodingFormat",
        "MailMerge_Source_FirstRowHeaders",
        "MailMerge_Source_PreviewLabel",
        "MailMerge_Source_PreviewCountFormat",
        "MailMerge_Source_DocumentHint",
        "MailMerge_Source_Error_Unsupported",
        "MailMerge_Source_Error_Unreadable",
        "MailMerge_Source_Error_NoTable",
        "MailMerge_Source_Error_Empty",
        "MailMerge_Source_Error_TooLarge",
        "MailMerge_NewList_Title",
        "MailMerge_NewList_Hint",
        "MailMerge_NewList_NewEntry",
        "MailMerge_NewList_DeleteEntry",
        "MailMerge_NewList_AddColumn",
        "MailMerge_NewList_RemoveColumn",
        "MailMerge_NewList_RenameColumn",
        "MailMerge_NewList_ColumnNamePrompt",
        "MailMerge_NewList_ColumnNameTitle",
        "MailMerge_NewList_SaveTitle",
        "MailMerge_NewList_SaveFilter_Csv",
        "MailMerge_NewList_SaveFilter_Text",
        "MailMerge_NewList_DefaultFileName",
        "MailMerge_NewList_Error_ColumnName",
        "MailMerge_NewList_Error_DuplicateColumnFormat",
        "MailMerge_NewList_Error_LastColumn",
        "MailMerge_NewList_Error_SaveFormat",
        "MailMerge_EditList_Title",
        "MailMerge_EditList_Hint",
        "MailMerge_EditList_NoRecipients",
        "MailMerge_EditList_IncludeColumn",
        "MailMerge_EditList_SortLabel",
        "MailMerge_EditList_Sort_None",
        "MailMerge_EditList_Sort_Ascending",
        "MailMerge_EditList_Sort_Descending",
        "MailMerge_EditList_FilterLabel",
        "MailMerge_EditList_Filter_AllFields",
        "MailMerge_EditList_Filter_Equals",
        "MailMerge_EditList_Filter_NotEquals",
        "MailMerge_EditList_Filter_Contains",
        "MailMerge_EditList_Filter_NotContains",
        "MailMerge_EditList_Filter_IsBlank",
        "MailMerge_EditList_Filter_IsNotBlank",
        "MailMerge_EditList_ClearFilter",
        "MailMerge_EditList_SelectAll",
        "MailMerge_EditList_ClearAll",
        "MailMerge_EditList_SummaryFormat",
    ];

    public static string SelectTitle => Text("MailMerge_Source_SelectTitle");
    public static string FilterAll => Text("MailMerge_Source_Filter_All");
    public static string FilterExcel => Text("MailMerge_Source_Filter_Excel");
    public static string FilterText => Text("MailMerge_Source_Filter_Text");
    public static string FilterWord => Text("MailMerge_Source_Filter_Word");
    public static string ImportTitle => Text("MailMerge_Source_ImportTitle");
    public static string FileLabel(string fileName) => Format("MailMerge_Source_FileLabelFormat", fileName);
    public static string TableLabel => Text("MailMerge_Source_TableLabel");
    public static string DelimiterLabel => Text("MailMerge_Source_DelimiterLabel");
    public static string Encoding(string encodingName) => Format("MailMerge_Source_EncodingFormat", encodingName);
    public static string FirstRowHeaders => Text("MailMerge_Source_FirstRowHeaders");
    public static string PreviewLabel => Text("MailMerge_Source_PreviewLabel");
    public static string PreviewCount(int shown, int total) =>
        Format("MailMerge_Source_PreviewCountFormat", shown, total);
    public static string DocumentHint => Text("MailMerge_Source_DocumentHint");

    public static string TableName(RecipientSourceTable table) => table.Kind switch
    {
        RecipientSourceTableKind.NamedRange => Format("MailMerge_Source_Table_NamedRangeFormat", table.DisplayName),
        RecipientSourceTableKind.Table => Format("MailMerge_Source_Table_TableFormat", table.DisplayName),
        _ => Format("MailMerge_Source_Table_WorksheetFormat", table.DisplayName),
    };

    public static string DelimiterName(char delimiter) => delimiter switch
    {
        '\t' => Text("MailMerge_Source_Delimiter_Tab"),
        ';' => Text("MailMerge_Source_Delimiter_Semicolon"),
        '|' => Text("MailMerge_Source_Delimiter_Pipe"),
        _ => Text("MailMerge_Source_Delimiter_Comma"),
    };

    public static string ErrorMessage(RecipientSourceError error) => error switch
    {
        RecipientSourceError.UnsupportedFormat => Text("MailMerge_Source_Error_Unsupported"),
        RecipientSourceError.NoTable => Text("MailMerge_Source_Error_NoTable"),
        RecipientSourceError.Empty => Text("MailMerge_Source_Error_Empty"),
        RecipientSourceError.TooLarge => Text("MailMerge_Source_Error_TooLarge"),
        _ => Text("MailMerge_Source_Error_Unreadable"),
    };

    public static string NewListTitle => Text("MailMerge_NewList_Title");
    public static string NewListHint => Text("MailMerge_NewList_Hint");
    public static string NewEntry => Text("MailMerge_NewList_NewEntry");
    public static string DeleteEntry => Text("MailMerge_NewList_DeleteEntry");
    public static string AddColumn => Text("MailMerge_NewList_AddColumn");
    public static string RemoveColumn => Text("MailMerge_NewList_RemoveColumn");
    public static string RenameColumn => Text("MailMerge_NewList_RenameColumn");
    public static string ColumnNamePrompt => Text("MailMerge_NewList_ColumnNamePrompt");
    public static string ColumnNameTitle => Text("MailMerge_NewList_ColumnNameTitle");
    public static string SaveTitle => Text("MailMerge_NewList_SaveTitle");
    public static string SaveFilterCsv => Text("MailMerge_NewList_SaveFilter_Csv");
    public static string SaveFilterText => Text("MailMerge_NewList_SaveFilter_Text");
    public static string DefaultFileName => Text("MailMerge_NewList_DefaultFileName");
    public static string ColumnNameError => Text("MailMerge_NewList_Error_ColumnName");
    public static string DuplicateColumnError(string name) =>
        Format("MailMerge_NewList_Error_DuplicateColumnFormat", name);
    public static string LastColumnError => Text("MailMerge_NewList_Error_LastColumn");
    public static string SaveError(string reason) => Format("MailMerge_NewList_Error_SaveFormat", reason);

    public static string EditListTitle => Text("MailMerge_EditList_Title");
    public static string EditListHint => Text("MailMerge_EditList_Hint");
    public static string EditListNoRecipients => Text("MailMerge_EditList_NoRecipients");
    public static string IncludeColumn => Text("MailMerge_EditList_IncludeColumn");
    public static string SortLabel => Text("MailMerge_EditList_SortLabel");
    public static string SortNone => Text("MailMerge_EditList_Sort_None");
    public static string SortAscending => Text("MailMerge_EditList_Sort_Ascending");
    public static string SortDescending => Text("MailMerge_EditList_Sort_Descending");
    public static string FilterLabel => Text("MailMerge_EditList_FilterLabel");
    public static string FilterAllFields => Text("MailMerge_EditList_Filter_AllFields");
    public static string ClearFilter => Text("MailMerge_EditList_ClearFilter");
    public static string SelectAll => Text("MailMerge_EditList_SelectAll");
    public static string ClearAll => Text("MailMerge_EditList_ClearAll");
    public static string Summary(int included, int total) =>
        Format("MailMerge_EditList_SummaryFormat", included, total);

    public static string ComparisonName(RecipientFilterComparison comparison) => comparison switch
    {
        RecipientFilterComparison.NotEquals => Text("MailMerge_EditList_Filter_NotEquals"),
        RecipientFilterComparison.Contains => Text("MailMerge_EditList_Filter_Contains"),
        RecipientFilterComparison.NotContains => Text("MailMerge_EditList_Filter_NotContains"),
        RecipientFilterComparison.IsBlank => Text("MailMerge_EditList_Filter_IsBlank"),
        RecipientFilterComparison.IsNotBlank => Text("MailMerge_EditList_Filter_IsNotBlank"),
        _ => Text("MailMerge_EditList_Filter_Equals"),
    };

    private static string Text(string resourceKey) => Loc.Get(resourceKey);

    private static string Format(string resourceKey, params object?[] arguments) =>
        Loc.Format(resourceKey, arguments);
}
