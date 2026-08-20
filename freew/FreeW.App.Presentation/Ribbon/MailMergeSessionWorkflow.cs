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
    MailMergeEmailClientDraftPlan? DraftPlan,
    string Message);

public enum MailMergeFinishRoute
{
    None,
    NewDocument,
    Printer,
    Email,
}

public sealed record MailMergeFinishRoutingPlan(
    bool Success,
    MailMergeFinishRoute Route,
    IReadOnlyList<int> EmailRecordIndexes,
    string Message);

public sealed record MailMergeEmailLaunchExecution(
    bool Success,
    MailMergeEmailExecution Execution,
    int LaunchedDraftCount,
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

    // Parallel to Session.Data.Rows: the 1-based row number each currently-active record held in the
    // ORIGINAL, unfiltered/unsorted recipient list as loaded by LoadRecipients. Word's MERGEREC field
    // must report this original position, not the record's position after Edit Recipient List has
    // filtered/reordered Session.Data -- see BuildFinish and RenderCurrentPreview below.
    private IReadOnlyList<int> _originalRecordNumbers = [];

    public IReadOnlyList<string> AvailableFieldNames => Session.Data?.Header ?? [];

    public MailMergeValidationPlan Validate(MailMergeOperation operation) =>
        MailMergeValidationPlanner.Validate(Session.Data, operation);

    public MailMergeFinishRoutingPlan RouteFinish(
        MailMergeFinishPlan finishPlan,
        bool printingAvailable,
        bool emailAvailable)
    {
        ArgumentNullException.ThrowIfNull(finishPlan);
        var validation = Validate(MailMergeOperation.FinishMerge);
        if (!validation.IsValid)
            return FinishRouteFailure(validation.Message);
        if (!finishPlan.Success)
        {
            return FinishRouteFailure(
                $"Finish & Merge cannot continue: {finishPlan.Issue}.");
        }

        return finishPlan.Destination switch
        {
            MailMergeFinishDestination.NewDocument => new(
                true,
                MailMergeFinishRoute.NewDocument,
                [],
                string.Empty),
            MailMergeFinishDestination.Printer when printingAvailable => new(
                true,
                MailMergeFinishRoute.Printer,
                [],
                string.Empty),
            MailMergeFinishDestination.Printer =>
                FinishRouteFailure("Printing is not available in this window."),
            MailMergeFinishDestination.Email when emailAvailable => new(
                true,
                MailMergeFinishRoute.Email,
                finishPlan.RowIndexes,
                string.Empty),
            MailMergeFinishDestination.Email =>
                FinishRouteFailure("E-mail drafts are not available in this window."),
            _ => FinishRouteFailure("Finish & Merge destination is not supported."),
        };
    }

    public MailMergeSessionTransition LoadRecipients(MergeData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var editableTemplate = Session.EndPreview();
        Session.Load(data);
        _originalRecordNumbers = Enumerable.Range(1, data.Count).ToList();
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
        _originalRecordNumbers = [];
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
        // Filter/sort rebuilds MergeData from scratch (no row identity survives), so recover each
        // surviving row's true original recipient-list number by matching its values back against the
        // still-current Session.Data/_originalRecordNumbers pair before it is overwritten below. This
        // composes correctly across repeated filter/sort passes because _originalRecordNumbers already
        // carries the lineage from any prior pass.
        _originalRecordNumbers = ResolveOriginalRecordNumbers(Session.Data, _originalRecordNumbers, data);
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
        var recordNumbers = finishPlan.RowIndexes.Select(OriginalRecordNumber).ToList();
        var merged = MailMerge.MergeAllWithRules(template, augmentedData, state, recordNumbers);
        if (state.CancelRequested)
            return FinishFailure(finishPlan, "Finish & Merge was cancelled.");

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
        => PlanEmailCore(currentDocument: null, intent);

    public MailMergeEmailExecution PlanEmail(
        TextDocument currentDocument,
        MailMergeEmailDeliveryIntent? intent = null)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);
        return PlanEmailCore(currentDocument, intent);
    }

    public MailMergeEmailLaunchExecution ExecuteEmailDrafts(
        TextDocument currentDocument,
        MailMergeEmailDeliveryIntent? intent,
        Func<string, bool>? launchDraft)
    {
        var execution = PlanEmail(currentDocument, intent);
        if (execution.DraftPlan is not { IsReady: true } drafts)
            return new(false, execution, 0, execution.Message);

        var launchedDraftCount = launchDraft is null
            ? 0
            : drafts.Drafts.Count(draft => launchDraft(draft.LaunchTarget));
        return new(
            true,
            execution,
            launchedDraftCount,
            MailMergeEmailDeliveryPlanner.FormatClientDraftStatus(
                drafts,
                launchedDraftCount));
    }

    private MailMergeEmailExecution PlanEmailCore(
        TextDocument? currentDocument,
        MailMergeEmailDeliveryIntent? intent)
    {
        var validation = Validate(MailMergeOperation.SendEmail);
        if (!validation.IsValid)
            return new(false, null, null, validation.Message);

        var data = Session.Data!;
        intent ??= MailMergeEmailDeliveryPlanner.CreateDefaultIntent(data, Session.CurrentIndex);
        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);
        if (currentDocument is null)
            return new(true, plan, null, MailMergeEmailDeliveryPlanner.FormatStatus(plan));

        var template = Session.IsPreviewing ? Session.Template! : currentDocument;
        var drafts = MailMergeEmailDeliveryPlanner.CreateClientDraftPlan(
            template,
            data,
            plan,
            row => Session.AugmentRow(row));
        var message = drafts.IsReady
            ? MailMergeEmailDeliveryPlanner.FormatStatus(plan)
            : string.Join(Environment.NewLine, drafts.Errors.Concat(drafts.Warnings));
        return new(true, plan, drafts, message);
    }

    private MailMergePreviewExecution RenderCurrentPreview()
    {
        var data = Session.Data!;
        var template = Session.Template!;
        var index = Math.Clamp(Session.CurrentIndex, 0, data.Count - 1);
        Session.CurrentIndex = index;
        // Report the record's real 1-based position for MERGEREC/MERGESEQ so a «Merge Record #»/
        // «Merge Sequence #» field previewed here matches what Finish & Merge will actually print for
        // this same record, instead of always showing record 1/sequence 0 regardless of which record
        // is being navigated to.
        var document = MailMerge.MergeRecord(
            template,
            Session.AugmentRow(data.Rows[index]),
            recordIndex: OriginalRecordNumber(index),
            sequenceNumber: index + 1);
        return new(true, document, index, true, string.Empty);
    }

    // The 1-based number OF this row IN the original, unfiltered/unsorted recipient list (MERGEREC),
    // as opposed to `index + 1`, its 1-based position within the current, possibly filtered/sorted
    // Session.Data (which is what MERGESEQ reports). Falls back to the row's own position if lineage
    // was never recorded for it (defensive only -- LoadRecipients and ApplyRecipientFilter always keep
    // _originalRecordNumbers sized to match Session.Data).
    private int OriginalRecordNumber(int index) =>
        index >= 0 && index < _originalRecordNumbers.Count
            ? _originalRecordNumbers[index]
            : index + 1;

    // Recover, for each row of the newly filtered/sorted `newData`, the original recipient-list row
    // number it corresponds to. Filter/sort planners copy each surviving row's values verbatim into a
    // freshly constructed MergeData (see MailMergeRecipientFilterSortPlanner.Apply), so a row's values
    // are unchanged even though its identity is not preserved -- match on content instead, consuming
    // each previous row at most once (in `newData` order) so duplicate-content rows still line up with
    // whichever original occurrence they were built from, since the planner's own filtering/sorting is
    // stable and preserves relative order among equal rows.
    private static IReadOnlyList<int> ResolveOriginalRecordNumbers(
        MergeData? previousData,
        IReadOnlyList<int> previousRecordNumbers,
        MergeData newData)
    {
        if (previousData is null || previousRecordNumbers.Count != previousData.Count)
            return Enumerable.Range(1, newData.Count).ToList();

        var used = new bool[previousData.Count];
        var result = new int[newData.Count];
        for (var newRow = 0; newRow < newData.Count; newRow++)
        {
            var matchIndex = -1;
            for (var oldRow = 0; oldRow < previousData.Count; oldRow++)
            {
                if (used[oldRow])
                    continue;
                if (RowsMatch(newData.Rows[newRow], previousData.Rows[oldRow], previousData.Header))
                {
                    matchIndex = oldRow;
                    break;
                }
            }

            result[newRow] = matchIndex >= 0
                ? previousRecordNumbers[matchIndex]
                : newRow + 1; // No lineage found (e.g. rows were added, not just filtered) -- best effort.
            if (matchIndex >= 0)
                used[matchIndex] = true;
        }

        return result;
    }

    private static bool RowsMatch(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right,
        IReadOnlyList<string> header)
    {
        foreach (var column in header)
        {
            var leftValue = left.TryGetValue(column, out var value) ? value : string.Empty;
            var rightValue = right.TryGetValue(column, out var otherValue) ? otherValue : string.Empty;
            if (!string.Equals(leftValue, rightValue, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private MailMergePreviewExecution PreviewFailure(string message) =>
        new(false, null, Session.CurrentIndex, Session.IsPreviewing, message);

    private static MailMergeFinishExecution FinishFailure(
        MailMergeFinishPlan plan,
        string message) =>
        new(false, plan, null, 0, 0, message);

    private static MailMergeFinishRoutingPlan FinishRouteFailure(string message) =>
        new(false, MailMergeFinishRoute.None, [], message);
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
    public static MailMergeFieldInsertionPlan CreateIfPlan(MailMergeRuleIfDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return MailMergeFieldAuthoringPlanner.CreateNativeFieldPlan(
            MergeRuleEvaluator.BuildNativeIfField(
                result.FieldName,
                result.Operator,
                result.Value,
                result.TrueText,
                result.FalseText),
            MergeRuleEvaluator.BuildIfInstruction(
                result.FieldName,
                result.Operator,
                result.Value,
                result.TrueText,
                result.FalseText));
    }

    public static MailMergeFieldInsertionPlan CreateConditionPlan(
        MailMergeRuleConditionDialogResult result,
        bool skipRecord)
    {
        ArgumentNullException.ThrowIfNull(result);
        var field = skipRecord
            ? MergeRuleEvaluator.BuildNativeSkipIfField(
                result.FieldName,
                result.Operator,
                result.Value)
            : MergeRuleEvaluator.BuildNativeNextIfField(
                result.FieldName,
                result.Operator,
                result.Value);
        var instruction = skipRecord
            ? MergeRuleEvaluator.BuildSkipRecordIfInstruction(
                result.FieldName,
                result.Operator,
                result.Value)
            : MergeRuleEvaluator.BuildNextRecordIfInstruction(
                result.FieldName,
                result.Operator,
                result.Value);
        return MailMergeFieldAuthoringPlanner.CreateNativeFieldPlan(field, instruction);
    }

    public static MailMergeFieldInsertionPlan CreateFillInPlan(string prompt) =>
        MailMergeFieldAuthoringPlanner.CreateNativeFieldPlan(
            new ComplexField(MergeRuleEvaluator.BuildNativeFillInInstruction(prompt)),
            MergeRuleEvaluator.BuildFillInInstruction(prompt));

    public static MailMergeFieldInsertionPlan? CreateAskPlan(string bookmarkName, string prompt)
    {
        if (string.IsNullOrWhiteSpace(bookmarkName))
            return null;

        var normalizedName = bookmarkName.Trim();
        return MailMergeFieldAuthoringPlanner.CreateNativeFieldPlan(
            new ComplexField(MergeRuleEvaluator.BuildNativeAskInstruction(normalizedName, prompt)),
            MergeRuleEvaluator.BuildAskInstruction(normalizedName, prompt));
    }

    public static MailMergeFieldInsertionPlan? CreateSetPlan(string bookmarkName, string value)
    {
        if (string.IsNullOrWhiteSpace(bookmarkName))
            return null;

        var normalizedName = bookmarkName.Trim();
        return MailMergeFieldAuthoringPlanner.CreateNativeFieldPlan(
            new ComplexField(MergeRuleEvaluator.BuildNativeSetInstruction(normalizedName, value)),
            MergeRuleEvaluator.BuildSetInstruction(normalizedName, value));
    }

    public static MailMergeFieldInsertionPlan? CreateRefPlan(string bookmarkName)
    {
        if (string.IsNullOrWhiteSpace(bookmarkName))
            return null;

        var normalizedName = bookmarkName.Trim();
        return MailMergeFieldAuthoringPlanner.CreateNativeFieldPlan(
            new ComplexField(MergeRuleEvaluator.BuildNativeRefInstruction(normalizedName)),
            MergeRuleEvaluator.BuildRefInstruction(normalizedName));
    }
}
