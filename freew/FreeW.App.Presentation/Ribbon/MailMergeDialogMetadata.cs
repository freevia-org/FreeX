using FreeW.App.Localization;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>Renderer-neutral titles, labels, prompts, and feedback copy for FreeW mail-merge dialogs.</summary>
public static class MailMergeDialogMetadata
{
    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
    [
        "MailMerge_Dialog_Title",
        "Common_OkText",
        "Common_CancelText",
        "MailMerge_Start_Title",
        "MailMerge_SelectRecipients_Title",
        "MailMerge_RecipientCsv_Hint",
        "MailMerge_RecipientCsv_Placeholder",
        "MailMerge_InsertField_Title",
        "MailMerge_Field_Placeholder",
        "MailMerge_DocumentType_Label",
        "MailMerge_MatchFields_Title",
        "MailMerge_FilterSort_Title",
        "MailMerge_SortBy_Label",
        "MailMerge_Ascending_Label",
        "MailMerge_Descending_Label",
        "MailMerge_Filter_Instruction",
        "MailMerge_Envelopes_Title",
        "MailMerge_EnvelopeSize_Label",
        "MailMerge_Note_Label",
        "MailMerge_InvalidLabelGrid_Message",
        "MailMerge_Labels_Title",
        "MailMerge_LabelProduct_Label",
        "MailMerge_Rows_Label",
        "MailMerge_Columns_Label",
        "MailMerge_PreviewResults_Title",
        "MailMerge_Previous_Label",
        "MailMerge_Next_Label",
        "MailMerge_Done_Label",
        "MailMerge_FindRecipient_Title",
        "MailMerge_Find_Label",
        "MailMerge_Find_Placeholder",
        "MailMerge_Finish_Title",
        "MailMerge_Destination_Label",
        "MailMerge_MergeTo_Label",
        "MailMerge_Records_Label",
        "MailMerge_RecordsToMerge_Label",
        "MailMerge_Range_Label",
        "MailMerge_Validation_Label",
        "MailMerge_From_Label",
        "MailMerge_To_Label",
        "MailMerge_ReadyToFinish_Message",
        "MailMerge_CheckErrors_Title",
        "MailMerge_CheckErrors_Label",
        "MailMerge_SendEmail_Title",
        "MailMerge_ToField_Label",
        "MailMerge_Subject_Label",
        "MailMerge_Output_Label",
        "MailMerge_BodyFormat_Label",
        "MailMerge_SendRecords_Label",
        "MailMerge_ReadyEmail_Message",
        "MailMerge_IfThenElse_Title",
        "MailMerge_FieldName_Label",
        "MailMerge_Comparison_Label",
        "MailMerge_CompareTo_Label",
        "MailMerge_ThenInsert_Label",
        "MailMerge_OtherwiseInsert_Label",
        "MailMerge_BookmarkName_Label",
        "MailMerge_Data_Title",
        "MailMerge_Data_Prompt",
        "MailMerge_DataHeader_Hint",
        "MailMerge_FinishIssue_Format",
        "MailMerge_FieldsHint_Format",
    ];

    public static string MailMergeTitle => Text("MailMerge_Dialog_Title");
    public static string OkLabel => Text("Common_OkText");
    public static string CancelLabel => Text("Common_CancelText");
    public static string StartMailMergeTitle => Text("MailMerge_Start_Title");
    public static string SelectRecipientsTitle => Text("MailMerge_SelectRecipients_Title");
    public static string RecipientCsvHint => Text("MailMerge_RecipientCsv_Hint");
    public static string RecipientCsvPlaceholder => Text("MailMerge_RecipientCsv_Placeholder");
    public static string InsertMergeFieldTitle => Text("MailMerge_InsertField_Title");
    public static string MergeFieldPlaceholder => Text("MailMerge_Field_Placeholder");
    public static string DocumentTypeLabel => Text("MailMerge_DocumentType_Label");
    public static string MatchFieldsTitle => Text("MailMerge_MatchFields_Title");
    public static string FilterSortRecipientsTitle => Text("MailMerge_FilterSort_Title");
    public static string SortByLabel => Text("MailMerge_SortBy_Label");
    public static string AscendingLabel => Text("MailMerge_Ascending_Label");
    public static string DescendingLabel => Text("MailMerge_Descending_Label");
    public static string FilterInstruction => Text("MailMerge_Filter_Instruction");
    public static string EnvelopesTitle => Text("MailMerge_Envelopes_Title");
    public static string EnvelopeSizeLabel => Text("MailMerge_EnvelopeSize_Label");
    public static string NoteLabel => Text("MailMerge_Note_Label");
    public static string InvalidLabelGridMessage => Text("MailMerge_InvalidLabelGrid_Message");
    public static string LabelsTitle => Text("MailMerge_Labels_Title");
    public static string LabelProductLabel => Text("MailMerge_LabelProduct_Label");
    public static string RowsLabel => Text("MailMerge_Rows_Label");
    public static string ColumnsLabel => Text("MailMerge_Columns_Label");
    public static string PreviewResultsTitle => Text("MailMerge_PreviewResults_Title");
    public static string PreviousLabel => Text("MailMerge_Previous_Label");
    public static string NextLabel => Text("MailMerge_Next_Label");
    public static string DoneLabel => Text("MailMerge_Done_Label");
    public static string FindRecipientTitle => Text("MailMerge_FindRecipient_Title");
    public static string FindLabel => Text("MailMerge_Find_Label");
    public static string FindPlaceholder => Text("MailMerge_Find_Placeholder");
    public static string FinishAndMergeTitle => Text("MailMerge_Finish_Title");
    public static string DestinationLabel => Text("MailMerge_Destination_Label");
    public static string MergeToLabel => Text("MailMerge_MergeTo_Label");
    public static string RecordsLabel => Text("MailMerge_Records_Label");
    public static string RecordsToMergeLabel => Text("MailMerge_RecordsToMerge_Label");
    public static string RangeLabel => Text("MailMerge_Range_Label");
    public static string ValidationLabel => Text("MailMerge_Validation_Label");
    public static string FromLabel => Text("MailMerge_From_Label");
    public static string ToLabel => Text("MailMerge_To_Label");
    public static string ReadyToFinishMessage => Text("MailMerge_ReadyToFinish_Message");
    public static string CheckForErrorsTitle => Text("MailMerge_CheckErrors_Title");
    public static string CheckForErrorsLabel => Text("MailMerge_CheckErrors_Label");
    public static string SendEmailTitle => Text("MailMerge_SendEmail_Title");
    public static string ToFieldLabel => Text("MailMerge_ToField_Label");
    public static string SubjectLabel => Text("MailMerge_Subject_Label");
    public static string OutputLabel => Text("MailMerge_Output_Label");
    public static string BodyFormatLabel => Text("MailMerge_BodyFormat_Label");
    public static string SendRecordsLabel => Text("MailMerge_SendRecords_Label");
    public static string ReadyEmailMessage => Text("MailMerge_ReadyEmail_Message");
    public static string IfThenElseTitle => Text("MailMerge_IfThenElse_Title");
    public static string FieldNameLabel => Text("MailMerge_FieldName_Label");
    public static string ComparisonLabel => Text("MailMerge_Comparison_Label");
    public static string CompareToLabel => Text("MailMerge_CompareTo_Label");
    public static string ThenInsertLabel => Text("MailMerge_ThenInsert_Label");
    public static string OtherwiseInsertLabel => Text("MailMerge_OtherwiseInsert_Label");
    public static string BookmarkNameLabel => Text("MailMerge_BookmarkName_Label");
    public static string MergeDataTitle => Text("MailMerge_Data_Title");
    public static string MergeDataPrompt => Text("MailMerge_Data_Prompt");
    public static string MergeDataHeaderHint => Text("MailMerge_DataHeader_Hint");

    public static string FormatFinishIssue(MailMergeFinishIssue issue) =>
        Loc.Format("MailMerge_FinishIssue_Format", issue);

    public static string FormatFieldsHint(IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var values = fields.ToArray();
        return values.Length == 0
            ? MergeDataHeaderHint
            : Loc.Format("MailMerge_FieldsHint_Format", string.Join(", ", values));
    }

    private static string Text(string resourceKey) => Loc.Get(resourceKey);
}
