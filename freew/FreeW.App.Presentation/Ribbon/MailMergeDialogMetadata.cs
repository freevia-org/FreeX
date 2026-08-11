namespace FreeW.App.Presentation.Ribbon;

/// <summary>Renderer-neutral titles, labels, prompts, and feedback copy for FreeW mail-merge dialogs.</summary>
public static class MailMergeDialogMetadata
{
    public const string MailMergeTitle = "Mail Merge";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const string StartMailMergeTitle = "Start Mail Merge";
    public const string SelectRecipientsTitle = "Select Recipients";
    public const string RecipientCsvHint =
        "Type or paste a recipient list as CSV. The first line is the column headers.";
    public const string RecipientCsvPlaceholder = "FirstName,LastName,City...";
    public const string InsertMergeFieldTitle = "Insert Merge Field";
    public const string MergeFieldPlaceholder = "...or type a field name";
    public const string DocumentTypeLabel = "Document type:";
    public const string MatchFieldsTitle = "Match Fields";
    public const string FilterSortRecipientsTitle = "Filter and Sort Recipients";
    public const string SortByLabel = "Sort by:";
    public const string AscendingLabel = "Ascending";
    public const string DescendingLabel = "Descending";
    public const string FilterInstruction = "Check recipients to include, then choose a sort order.";
    public const string EnvelopesTitle = "Envelopes";
    public const string EnvelopeSizeLabel = "Envelope size:";
    public const string NoteLabel = "Note:";
    public const string InvalidLabelGridMessage =
        "Enter valid positive integers for rows and columns.";
    public const string LabelsTitle = "Labels";
    public const string LabelProductLabel = "Label product:";
    public const string RowsLabel = "Rows:";
    public const string ColumnsLabel = "Columns:";
    public const string PreviewResultsTitle = "Preview Results";
    public const string PreviousLabel = "Previous";
    public const string NextLabel = "Next";
    public const string DoneLabel = "Done";
    public const string FindRecipientTitle = "Find Recipient";
    public const string FindLabel = "Find:";
    public const string FindPlaceholder = "Name, company, or other value";
    public const string FinishAndMergeTitle = "Finish and Merge";
    public const string DestinationLabel = "Destination:";
    public const string MergeToLabel = "Merge to";
    public const string RecordsLabel = "Records:";
    public const string RecordsToMergeLabel = "Records to merge";
    public const string RangeLabel = "Range:";
    public const string ValidationLabel = "Validation:";
    public const string FromLabel = "From:";
    public const string ToLabel = "To:";
    public const string ReadyToFinishMessage = "Ready to finish the merge.";
    public const string CheckForErrorsTitle = "Check for Errors";
    public const string CheckForErrorsLabel = "How should errors be checked?";
    public const string SendEmailTitle = "Send E-mail Messages";
    public const string ToFieldLabel = "To field:";
    public const string SubjectLabel = "Subject:";
    public const string OutputLabel = "Output:";
    public const string BodyFormatLabel = "Body format:";
    public const string SendRecordsLabel = "Send records:";
    public const string ReadyEmailMessage =
        "Ready to prepare an e-mail merge plan. No messages will be sent.";
    public const string IfThenElseTitle = "If...Then...Else";
    public const string FieldNameLabel = "Field name:";
    public const string ComparisonLabel = "Comparison:";
    public const string CompareToLabel = "Compare to:";
    public const string ThenInsertLabel = "Insert this text (true):";
    public const string OtherwiseInsertLabel = "Otherwise insert (false):";
    public const string BookmarkNameLabel = "Bookmark name:";
    public const string MergeDataTitle = "Mail Merge Data";
    public const string MergeDataPrompt = "Paste or type CSV (first line = field names):";
    public const string MergeDataHeaderHint = "Tip: the first line is the header row of field names.";

    public static string FormatFinishIssue(MailMergeFinishIssue issue) =>
        $"Finish and merge: {issue}.";

    public static string FormatFieldsHint(IEnumerable<string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var values = fields.ToArray();
        return values.Length == 0
            ? MergeDataHeaderHint
            : "Fields in this document: " + string.Join(", ", values);
    }
}
