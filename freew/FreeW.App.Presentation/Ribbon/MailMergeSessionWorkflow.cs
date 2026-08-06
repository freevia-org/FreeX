using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public enum MailMergeOperation
{
    MatchFields,
    FilterSortRecipients,
    InsertAddressBlock,
    InsertGreetingLine,
    PreviewRecord,
    StepRecords,
    FindRecipient,
    CheckForErrors,
    FinishMerge,
    SendEmail,
}

public readonly record struct MailMergeValidationPlan(bool IsValid, string Message);

public readonly record struct MailMergeSessionTransition(
    TextDocument? DocumentToLoad,
    string Message);

public sealed record MailMergePreviewExecution(
    bool Success,
    TextDocument? DocumentToLoad,
    int CurrentIndex,
    bool IsPreviewing,
    string Message);

public sealed record MailMergeFindExecution(
    bool Success,
    MailMergeFindRecipientResult? Result,
    TextDocument? DocumentToLoad,
    string Message);

public sealed record MailMergeCheckExecution(
    bool Success,
    MailMergeErrorCheckResult? Result,
    IReadOnlyList<string> Messages,
    TextDocument? ReportDocument,
    string Message);

public sealed record MailMergeFinishExecution(
    bool Success,
    MailMergeFinishPlan Plan,
    TextDocument? Document,
    int MergedRecordCount,
    int SkippedRecordCount,
    string Message);

public sealed record MailMergeEmailExecution(
    bool Success,
    MailMergeEmailDeliveryPlan? Plan,
    string Message);

/// <summary>
/// Coordinates renderer-neutral mail-merge state, document production, validation, and feedback.
/// Native hosts commit the current editor model before calling and realize returned documents,
/// dialogs, focus, printing, and status presentation.
/// </summary>
public sealed class MailMergeSessionWorkflow
{
    public MailMergeSessionWorkflow(MailMergeSession? session = null)
    {
        Session = session ?? new MailMergeSession();
    }

    public MailMergeSession Session { get; }

    public IReadOnlyList<string> AvailableFieldNames => Session.Data?.Header ?? [];

    public MailMergeValidationPlan Validate(MailMergeOperation operation) =>
        MailMergeValidationPlanner.Validate(Session.Data, operation);

    public MailMergeSessionTransition LoadRecipients(MergeData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var editableTemplate = Session.EndPreview();
        Session.Load(data);
        return new(
            editableTemplate,
            $"Loaded {data.Count} record(s) with {data.Header.Count} field(s).");
    }

    public MailMergeSessionTransition SetMode(MailMergeOutputMode mode)
    {
        var editableTemplate = Session.EndPreview();
        Session.SetMode(mode);
        var label = mode == MailMergeOutputMode.Directory ? "Directory" : "Letters";
        return new(editableTemplate, $"Mail merge output set to {label}.");
    }

    public MailMergeSessionTransition Clear()
    {
        var editableTemplate = Session.EndPreview();
        Session.Clear();
        return new(editableTemplate, "Mail merge reset to a normal document.");
    }

    public MailMergeSessionTransition ApplyFieldMapping(FieldMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        Session.Mapping = mapping;
        return new(Session.EndPreview(), "Matched recipient fields.");
    }

    public MailMergeSessionTransition ApplyRecipientFilter(MergeData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var editableTemplate = Session.EndPreview();
        Session.Data = data;
        Session.CurrentIndex = 0;
        return new(editableTemplate, $"Recipient list now contains {data.Count} record(s).");
    }

    public MailMergePreviewExecution TogglePreview(TextDocument currentDocument)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);

        if (Session.IsPreviewing)
        {
            var editableTemplate = Session.EndPreview();
            return new(true, editableTemplate, 0, false, string.Empty);
        }

        return EnsurePreviewing(currentDocument);
    }

    public MailMergePreviewExecution EnsurePreviewing(TextDocument currentDocument)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);

        var validation = Validate(MailMergeOperation.PreviewRecord);
        if (!validation.IsValid)
            return PreviewFailure(validation.Message);

        if (!Session.IsPreviewing)
        {
            Session.Template = currentDocument;
            Session.CurrentIndex = 0;
        }

        return RenderCurrentPreview();
    }

    public MailMergePreviewExecution NavigatePreview(
        TextDocument currentDocument,
        MailMergePreviewNavigationAction action)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);

        var validation = Validate(MailMergeOperation.StepRecords);
        if (!validation.IsValid)
            return PreviewFailure(validation.Message);

        if (!Session.IsPreviewing)
        {
            Session.Template = currentDocument;
            Session.CurrentIndex = 0;
        }

        Session.CurrentIndex = MailMergePreviewNavigationPlanner.TargetIndex(
            action,
            Session.CurrentIndex,
            Session.Data!.Count);
        return RenderCurrentPreview();
    }

    public MailMergePreviewExecution MovePreviewTo(
        TextDocument currentDocument,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);

        var validation = Validate(MailMergeOperation.StepRecords);
        if (!validation.IsValid)
            return PreviewFailure(validation.Message);

        if (!Session.IsPreviewing)
            Session.Template = currentDocument;

        Session.CurrentIndex = Math.Clamp(targetIndex, 0, Session.Data!.Count - 1);
        return RenderCurrentPreview();
    }

    public MailMergeFindExecution FindRecipient(string? query)
    {
        var validation = Validate(MailMergeOperation.FindRecipient);
        if (!validation.IsValid)
            return new(false, null, null, validation.Message);

        var result = MailMergeFindRecipientPlanner.Find(Session.Data!, query, Session.CurrentIndex);
        Session.CurrentIndex = result.Index;
        var preview = result.Found && Session.IsPreviewing
            ? RenderCurrentPreview().DocumentToLoad
            : null;
        return new(result.Found, result, preview, result.Message);
    }

    public MailMergeCheckExecution CheckForErrors(
        TextDocument currentDocument,
        MailMergeCheckForErrorsMode mode)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);

        var validation = Validate(MailMergeOperation.CheckForErrors);
        if (!validation.IsValid)
            return new(false, null, [validation.Message], null, validation.Message);

        var template = Session.Template ?? currentDocument;
        var rows = Session.Data!.Rows.Select(row => Session.AugmentRow(row)).ToList();
        var result = MailMergeCheckForErrorsPlanner.Check(template, rows, mode);
        var messages = result.ShouldPauseForErrors
            ? result.Issues.Select(issue => issue.Message).ToList()
            : result.ShouldOpenReportDocument
                ? []
                : [result.Message];
        var report = result.ShouldOpenReportDocument
            ? MailMergeCheckForErrorsPlanner.BuildReportDocument(result)
            : null;
        return new(true, result, messages, report, result.Message);
    }

    public MailMergeFinishExecution ExecuteFinish(
        TextDocument currentDocument,
        MailMergeFinishPlan finishPlan,
        MergeState? mergeState = null)
    {
        var execution = BuildFinish(currentDocument, finishPlan, mergeState);
        CompleteFinish(execution);
        return execution;
    }

    public MailMergeFinishExecution BuildFinish(
        TextDocument currentDocument,
        MailMergeFinishPlan finishPlan,
        MergeState? mergeState = null)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);
        ArgumentNullException.ThrowIfNull(finishPlan);

        var validation = Validate(MailMergeOperation.FinishMerge);
        if (!validation.IsValid)
            return FinishFailure(finishPlan, validation.Message);
        if (!finishPlan.Success)
            return FinishFailure(
                finishPlan,
                $"Finish & Merge cannot continue: {finishPlan.Issue}.");

        var data = Session.Data!;
        if (finishPlan.RowIndexes.Any(index => index < 0 || index >= data.Count))
            return FinishFailure(finishPlan, "Finish & Merge cannot continue: InvalidRange.");

        var template = Session.Template ?? currentDocument;
        var augmentedData = Session.BuildAugmentedData(finishPlan.RowIndexes);
        var state = mergeState ?? new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, augmentedData, state);
        var combined = MailMerge.CombineMergedRecords(merged, Session.Mode);
        var skipped = state.SkippedIndices.Count;
        var message = skipped > 0
            ? $"Merged {merged.Count} record(s) into a single document ({skipped} skipped)."
            : $"Merged {merged.Count} record(s) into a single document.";

        return new(true, finishPlan, combined, merged.Count, skipped, message);
    }

    public void CompleteFinish(MailMergeFinishExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        if (execution.Success &&
            execution.Plan.Destination == MailMergeFinishDestination.NewDocument)
        {
            Session.EndPreview();
        }
    }

    public MailMergeEmailExecution PlanEmail(MailMergeEmailDeliveryIntent? intent = null)
    {
        var validation = Validate(MailMergeOperation.SendEmail);
        if (!validation.IsValid)
            return new(false, null, validation.Message);

        var data = Session.Data!;
        intent ??= MailMergeEmailDeliveryPlanner.CreateDefaultIntent(data, Session.CurrentIndex);
        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);
        return new(true, plan, MailMergeEmailDeliveryPlanner.FormatStatus(plan));
    }

    private MailMergePreviewExecution RenderCurrentPreview()
    {
        var data = Session.Data!;
        var template = Session.Template!;
        var index = Math.Clamp(Session.CurrentIndex, 0, data.Count - 1);
        Session.CurrentIndex = index;
        var document = MailMerge.MergeRecord(template, Session.AugmentRow(data.Rows[index]));
        return new(true, document, index, true, string.Empty);
    }

    private MailMergePreviewExecution PreviewFailure(string message) =>
        new(false, null, Session.CurrentIndex, Session.IsPreviewing, message);

    private static MailMergeFinishExecution FinishFailure(
        MailMergeFinishPlan plan,
        string message) =>
        new(false, plan, null, 0, 0, message);
}

public static class MailMergeValidationPlanner
{
    public static MailMergeValidationPlan Validate(
        MergeData? data,
        MailMergeOperation operation)
    {
        var requiresRecords = operation is
            MailMergeOperation.PreviewRecord or
            MailMergeOperation.StepRecords or
            MailMergeOperation.FindRecipient or
            MailMergeOperation.CheckForErrors or
            MailMergeOperation.FinishMerge or
            MailMergeOperation.SendEmail or
            MailMergeOperation.FilterSortRecipients;
        var isValid = requiresRecords ? data is { Count: > 0 } : data is not null;
        return isValid
            ? new(true, string.Empty)
            : new(false, RequiredRecipientMessage(operation));
    }

    private static string RequiredRecipientMessage(MailMergeOperation operation) => operation switch
    {
        MailMergeOperation.MatchFields =>
            "Select recipients first (Mailings > Select Recipients), then match fields.",
        MailMergeOperation.FilterSortRecipients =>
            "Select recipients first (Mailings > Select Recipients), then filter and sort.",
        MailMergeOperation.InsertAddressBlock =>
            "Select recipients first (Mailings > Select Recipients), then insert an Address Block.",
        MailMergeOperation.InsertGreetingLine =>
            "Select recipients first (Mailings > Select Recipients), then insert a Greeting Line.",
        MailMergeOperation.PreviewRecord =>
            "Select recipients first (Mailings > Select Recipients), then preview a record.",
        MailMergeOperation.StepRecords =>
            "Select recipients first (Mailings > Select Recipients), then step records.",
        MailMergeOperation.FindRecipient =>
            "Select recipients first (Mailings > Select Recipients), then find a recipient.",
        MailMergeOperation.CheckForErrors =>
            "Select recipients first (Mailings > Select Recipients), then check for errors.",
        MailMergeOperation.FinishMerge =>
            "Select recipients first (Mailings > Select Recipients), then Finish & Merge.",
        MailMergeOperation.SendEmail =>
            "Select recipients first (Mailings > Select Recipients), then Send E-mail Messages.",
        _ => "Select recipients first (Mailings > Select Recipients).",
    };
}

public static class MailMergeRuleAuthoringPlanner
{
    public static string CreateIf(MailMergeRuleIfDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Wrap(MergeRuleEvaluator.BuildIfInstruction(
            result.FieldName,
            result.Operator,
            result.Value,
            result.TrueText,
            result.FalseText));
    }

    public static string CreateCondition(
        MailMergeRuleConditionDialogResult result,
        bool skipRecord)
    {
        ArgumentNullException.ThrowIfNull(result);
        var instruction = skipRecord
            ? MergeRuleEvaluator.BuildSkipRecordIfInstruction(
                result.FieldName,
                result.Operator,
                result.Value)
            : MergeRuleEvaluator.BuildNextRecordIfInstruction(
                result.FieldName,
                result.Operator,
                result.Value);
        return Wrap(instruction);
    }

    public static string CreateFillIn(string prompt) =>
        Wrap(MergeRuleEvaluator.BuildFillInInstruction(prompt));

    public static string CreateAsk(string bookmarkName, string prompt) =>
        string.IsNullOrWhiteSpace(bookmarkName)
            ? string.Empty
            : Wrap(MergeRuleEvaluator.BuildAskInstruction(bookmarkName.Trim(), prompt));

    public static string CreateSet(string bookmarkName, string value) =>
        string.IsNullOrWhiteSpace(bookmarkName)
            ? string.Empty
            : Wrap(MergeRuleEvaluator.BuildSetInstruction(bookmarkName.Trim(), value));

    public static string CreateRef(string bookmarkName) =>
        string.IsNullOrWhiteSpace(bookmarkName)
            ? string.Empty
            : Wrap(MergeRuleEvaluator.BuildRefInstruction(bookmarkName.Trim()));

    private static string Wrap(string instruction) =>
        $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}";
}

public enum MailMergePromptKind
{
    FillIn,
    Ask,
}

public sealed record MailMergePromptRequest(
    MailMergePromptKind Kind,
    string Key,
    string Prompt);

public static class MailMergePromptPlanner
{
    public static IReadOnlyList<MailMergePromptRequest> GetRequests(TextDocument template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var requests = new List<MailMergePromptRequest>();
        foreach (var instruction in MailMerge.FieldNames(template))
        {
            if (TryParse(instruction, out var request) &&
                !requests.Any(existing =>
                    existing.Kind == request.Kind &&
                    existing.Key.Equals(request.Key, StringComparison.OrdinalIgnoreCase)))
            {
                requests.Add(request);
            }
        }

        return requests;
    }

    public static void ApplyResponse(
        MergeState state,
        MailMergePromptRequest request,
        string? response)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Kind == MailMergePromptKind.FillIn)
            state.FillInAnswers[request.Key] = response ?? string.Empty;
        else
            state.AskAnswers[request.Key] = response ?? string.Empty;
    }

    private static bool TryParse(
        string instruction,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MailMergePromptRequest? request)
    {
        const string fillInPrefix = "Fill-in ";
        const string askPrefix = "Ask ";

        if (instruction.StartsWith(fillInPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var prompt = Unquote(instruction[fillInPrefix.Length..]);
            request = new(MailMergePromptKind.FillIn, prompt, prompt);
            return true;
        }

        if (instruction.StartsWith(askPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = instruction[askPrefix.Length..].TrimStart();
            var separator = remainder.IndexOf(' ');
            if (separator > 0)
            {
                var bookmarkName = remainder[..separator];
                request = new(
                    MailMergePromptKind.Ask,
                    bookmarkName,
                    Unquote(remainder[(separator + 1)..]));
                return true;
            }
        }

        request = null;
        return false;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : trimmed;
    }
}
