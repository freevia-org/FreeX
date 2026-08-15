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

public sealed record MailMergeEmailDeliveryDialogState(
    MailMergeEmailDeliveryIntent Intent,
    string ValidationText,
    bool CanSubmit);

public sealed class MailMergeEmailDeliveryDialogSession
{
    private readonly MergeData _data;
    private readonly int _currentRecordIndex;
    private readonly IReadOnlyList<int> _selectedRecordIndexes;

    public MailMergeEmailDeliveryDialogSession(
        MergeData data,
        int currentRecordIndex,
        IReadOnlyList<int>? selectedRecordIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        _data = data;
        _currentRecordIndex = currentRecordIndex;
        _selectedRecordIndexes = selectedRecordIndexes?.ToArray() ?? [];
        InitialPlan = MailMergeEmailDeliveryPlanner.CreateDialogPlan(
            data,
            currentRecordIndex,
            _selectedRecordIndexes);
    }

    public MailMergeEmailDeliveryDialogPlan InitialPlan { get; }

    public MailMergeEmailDeliveryDialogState Evaluate(
        string? recipientAddressField,
        string? subject,
        int outputFormatIndex,
        int bodyFormatIndex,
        int recordScopeIndex)
    {
        var intent = MailMergeEmailDeliveryPlanner.CreateIntent(
            recipientAddressField,
            subject,
            outputFormatIndex,
            bodyFormatIndex,
            recordScopeIndex,
            _currentRecordIndex,
            _selectedRecordIndexes);
        var deliveryPlan = MailMerge.CreateEmailDeliveryPlan(_data, intent);
        var messages = MailMergeEmailDeliveryPlanner.GetValidationMessages(deliveryPlan);
        return new MailMergeEmailDeliveryDialogState(
            intent,
            messages.Count == 0
                ? MailMergeDialogMetadata.ReadyEmailMessage
                : string.Join(Environment.NewLine, messages),
            deliveryPlan.Errors.Count == 0);
    }
}

public sealed record MailMergeEmailClientDraft(
    int RecordIndex,
    string RecipientAddress,
    string Subject,
    string Body,
    string LaunchTarget);

public sealed record MailMergeEmailClientDraftPlan(
    IReadOnlyList<MailMergeEmailClientDraft> Drafts,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsReady => Errors.Count == 0 && Drafts.Count > 0;
}

public static class MailMergeEmailDeliveryPlanner
{
    private const int MaximumMailtoUriLength = 2000;

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
            intent.RecordScope == MailMergeEmailRecordScope.SelectedRecords ? 2 : 0,
            GetValidationMessages(plan));
    }

    public static MailMergeEmailDeliveryIntent CreateDefaultIntent(
        MergeData data,
        int currentRecordIndex,
        IReadOnlyList<int>? selectedRecordIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var selected = selectedRecordIndexes?.ToArray() ?? [];
        return new MailMergeEmailDeliveryIntent(
            MailMerge.SuggestEmailAddressField(data.Header) ?? data.Header.FirstOrDefault() ?? string.Empty,
            string.Empty,
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            selected.Length > 0
                ? MailMergeEmailRecordScope.SelectedRecords
                : MailMergeEmailRecordScope.AllRecords,
            currentRecordIndex,
            selected);
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

    public static MailMergeEmailClientDraftPlan CreateClientDraftPlan(
        TextDocument template,
        MergeData data,
        MailMergeEmailDeliveryPlan deliveryPlan,
        Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? projectRow = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(deliveryPlan);

        var errors = deliveryPlan.Errors.ToList();
        var warnings = deliveryPlan.Warnings.ToList();
        var drafts = new List<MailMergeEmailClientDraft>();
        if (errors.Count > 0)
            return new MailMergeEmailClientDraftPlan(drafts, errors, warnings);

        if (deliveryPlan.Intent.OutputFormat == MailMergeEmailOutputFormat.Attachment)
        {
            errors.Add("Attachment e-mail merge requires a mail provider with attachment support.");
            return new MailMergeEmailClientDraftPlan(drafts, errors, warnings);
        }

        if (deliveryPlan.Intent.BodyFormat == MailMergeEmailBodyFormat.Html)
            warnings.Add("The default mail client receives merged plain text; HTML formatting is not available through mailto drafts.");

        foreach (var recordIndex in deliveryPlan.DeliverableRecordIndexes)
        {
            var row = data.Rows[recordIndex];
            var address = row.First(pair => pair.Key.Equals(
                    deliveryPlan.Intent.RecipientAddressField,
                    StringComparison.OrdinalIgnoreCase))
                .Value.Trim();
            if (!System.Net.Mail.MailAddress.TryCreate(address, out var parsedAddress))
            {
                warnings.Add($"Record {recordIndex + 1} has an invalid e-mail address in '{deliveryPlan.Intent.RecipientAddressField}'.");
                continue;
            }

            var merged = MailMerge.MergeRecord(template, projectRow?.Invoke(row) ?? row);
            var body = merged.PlainText;
            var target = BuildMailtoTarget(parsedAddress.Address, deliveryPlan.Intent.Subject, body);
            if (target.Length > MaximumMailtoUriLength)
            {
                warnings.Add($"Record {recordIndex + 1} is too large for a default mail-client draft.");
                continue;
            }

            drafts.Add(new MailMergeEmailClientDraft(
                recordIndex,
                parsedAddress.Address,
                deliveryPlan.Intent.Subject,
                body,
                target));
        }

        if (drafts.Count == 0)
            errors.Add("No selected records can be opened as default mail-client drafts.");

        return new MailMergeEmailClientDraftPlan(drafts, errors, warnings);
    }

    public static string FormatClientDraftStatus(
        MailMergeEmailClientDraftPlan plan,
        int launchedDraftCount)
    {
        ArgumentNullException.ThrowIfNull(plan);
        launchedDraftCount = Math.Clamp(launchedDraftCount, 0, plan.Drafts.Count);
        var failed = plan.Drafts.Count - launchedDraftCount;
        var warningSuffix = plan.Warnings.Count == 0
            ? string.Empty
            : $" {plan.Warnings.Count} warning(s).";
        var failureSuffix = failed == 0
            ? string.Empty
            : $" {failed} draft(s) could not be opened.";
        return $"Opened {launchedDraftCount} of {plan.Drafts.Count} merged e-mail draft(s) in the default mail client; no messages were sent.{failureSuffix}{warningSuffix}";
    }

    private static string BuildMailtoTarget(string recipientAddress, string subject, string body)
    {
        var recipient = Uri.EscapeDataString(recipientAddress).Replace("%40", "@", StringComparison.OrdinalIgnoreCase);
        return $"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
    }

    public static MailMergeEmailOutputFormatChoice GetOutputFormat(int index) =>
        OutputFormats[Math.Clamp(index, 0, OutputFormats.Length - 1)];

    public static MailMergeEmailBodyFormatChoice GetBodyFormat(int index) =>
        BodyFormats[Math.Clamp(index, 0, BodyFormats.Length - 1)];

    public static MailMergeEmailRecordScopeChoice GetRecordScope(int index) =>
        RecordScopes[Math.Clamp(index, 0, RecordScopes.Length - 1)];
}
