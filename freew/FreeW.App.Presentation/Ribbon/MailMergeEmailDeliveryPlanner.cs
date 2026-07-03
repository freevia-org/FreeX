using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public readonly record struct MailMergeEmailOutputFormatChoice(MailMergeEmailOutputFormat Format, string Label);

public readonly record struct MailMergeEmailBodyFormatChoice(MailMergeEmailBodyFormat Format, string Label);

public readonly record struct MailMergeEmailRecordScopeChoice(MailMergeEmailRecordScope Scope, string Label);

public sealed record MailMergeEmailDeliveryDialogPlan(
    IReadOnlyList<string> RecipientAddressFields,
    string RecipientAddressField,
    string Subject,
    IReadOnlyList<MailMergeEmailOutputFormatChoice> OutputFormats,
    int OutputFormatIndex,
    IReadOnlyList<MailMergeEmailBodyFormatChoice> BodyFormats,
    int BodyFormatIndex,
    IReadOnlyList<MailMergeEmailRecordScopeChoice> RecordScopes,
    int RecordScopeIndex,
    IReadOnlyList<string> ValidationMessages);

public static class MailMergeEmailDeliveryPlanner
{
    private static readonly MailMergeEmailOutputFormatChoice[] OutputFormats =
    [
        new(MailMergeEmailOutputFormat.MessageBody, "Message body"),
        new(MailMergeEmailOutputFormat.Attachment, "Attachment")
    ];

    private static readonly MailMergeEmailBodyFormatChoice[] BodyFormats =
    [
        new(MailMergeEmailBodyFormat.Html, "HTML"),
        new(MailMergeEmailBodyFormat.PlainText, "Plain text")
    ];

    private static readonly MailMergeEmailRecordScopeChoice[] RecordScopes =
    [
        new(MailMergeEmailRecordScope.AllRecords, "All records"),
        new(MailMergeEmailRecordScope.CurrentRecord, "Current record"),
        new(MailMergeEmailRecordScope.SelectedRecords, "Selected records")
    ];

    public static IReadOnlyList<MailMergeEmailOutputFormatChoice> GetOutputFormats() => OutputFormats;

    public static IReadOnlyList<MailMergeEmailBodyFormatChoice> GetBodyFormats() => BodyFormats;

    public static IReadOnlyList<MailMergeEmailRecordScopeChoice> GetRecordScopes() => RecordScopes;

    public static MailMergeEmailDeliveryDialogPlan CreateDialogPlan(
        MergeData data,
        int currentRecordIndex,
        IReadOnlyList<int>? selectedRecordIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var intent = CreateDefaultIntent(data, currentRecordIndex, selectedRecordIndexes);
        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);
        return new MailMergeEmailDeliveryDialogPlan(
            data.Header,
            intent.RecipientAddressField,
            intent.Subject,
            OutputFormats,
            0,
            BodyFormats,
            0,
            RecordScopes,
            0,
            GetValidationMessages(plan));
    }

    public static MailMergeEmailDeliveryIntent CreateDefaultIntent(
        MergeData data,
        int currentRecordIndex,
        IReadOnlyList<int>? selectedRecordIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new MailMergeEmailDeliveryIntent(
            MailMerge.SuggestEmailAddressField(data.Header) ?? data.Header.FirstOrDefault() ?? string.Empty,
            string.Empty,
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            MailMergeEmailRecordScope.AllRecords,
            currentRecordIndex,
            selectedRecordIndexes?.ToArray() ?? []);
    }

    public static MailMergeEmailDeliveryIntent CreateIntent(
        string? recipientAddressField,
        string? subject,
        int outputFormatIndex,
        int bodyFormatIndex,
        int recordScopeIndex,
        int currentRecordIndex,
        IReadOnlyList<int>? selectedRecordIndexes = null)
    {
        return new MailMergeEmailDeliveryIntent(
            recipientAddressField?.Trim() ?? string.Empty,
            subject?.Trim() ?? string.Empty,
            GetOutputFormat(outputFormatIndex).Format,
            GetBodyFormat(bodyFormatIndex).Format,
            GetRecordScope(recordScopeIndex).Scope,
            currentRecordIndex,
            selectedRecordIndexes?.ToArray() ?? []);
    }

    public static IReadOnlyList<string> GetValidationMessages(MailMergeEmailDeliveryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Errors.Concat(plan.Warnings).ToArray();
    }

    public static string FormatStatus(MailMergeEmailDeliveryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsReady)
            return "E-mail merge plan needs attention: " + string.Join(" ", plan.Errors);

        var output = plan.Intent.OutputFormat == MailMergeEmailOutputFormat.Attachment
            ? "attachment"
            : "message body";
        var body = plan.Intent.BodyFormat == MailMergeEmailBodyFormat.Html ? "HTML" : "plain text";
        var warningSuffix = plan.Warnings.Count == 0
            ? string.Empty
            : $" ({plan.Warnings.Count} warning(s))";

        return $"Prepared e-mail merge plan for {plan.DeliverableRecordIndexes.Count} recipient(s) as {output} / {body}; no messages were sent{warningSuffix}.";
    }

    public static MailMergeEmailOutputFormatChoice GetOutputFormat(int index) =>
        OutputFormats[Math.Clamp(index, 0, OutputFormats.Length - 1)];

    public static MailMergeEmailBodyFormatChoice GetBodyFormat(int index) =>
        BodyFormats[Math.Clamp(index, 0, BodyFormats.Length - 1)];

    public static MailMergeEmailRecordScopeChoice GetRecordScope(int index) =>
        RecordScopes[Math.Clamp(index, 0, RecordScopes.Length - 1)];
}
