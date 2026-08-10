using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Ribbon;

internal sealed record MailMergeFinishBuildResult(
    TextDocument Document,
    int MergedRecordCount,
    int SkippedRecordCount);

/// <summary>
/// AV-MAIL: the Avalonia shell's mail-merge glue between the Mailings ribbon commands and the portable
/// <see cref="MailMerge"/> engine. Owns a single <see cref="MailMergeSession"/> shared by every Mailings
/// command so the loaded recipient list, field mapping and preview cursor persist across clicks.
///
/// <para>
/// Every public method is the action behind one ribbon command; they are public (not just private
/// lambdas) so tests can drive the glue directly with a mock recipient set without going through the
/// dialog callbacks. Document mutations route through the editor's undoable
/// <see cref="DocumentView.InsertText"/>; preview / finish swap the whole document via
/// <see cref="DocumentView.LoadDocument"/>.
/// </para>
///
/// <para>
/// Send E-mail Messages creates merged message-body drafts and hands them to the host mail launcher. The
/// external client owns review and sending; FreeW never sends automatically.
/// </para>
/// </summary>
internal sealed class MailMergeEngine
{
    private readonly DocumentView _editor;
    private readonly RibbonHostCallbacks _callbacks;

    public MailMergeEngine(DocumentView editor, RibbonHostCallbacks callbacks)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    /// <summary>The shared session (recipient data + mapping + preview state). Exposed for tests.</summary>
    public MailMergeSession Session { get; } = new();

    /// <summary>The most recent plan produced by Send E-mail Messages. Exposed for tests/status only.</summary>
    public MailMergeEmailDeliveryPlan? LastEmailPlan { get; private set; }

    /// <summary>The most recent default-client draft plan. Exposed for deterministic host tests.</summary>
    public MailMergeEmailClientDraftPlan? LastEmailDraftPlan { get; private set; }

    // ── Select Recipients ────────────────────────────────────────────────────────

    /// <summary>
    /// Mailings &gt; Select Recipients. Asks the host for a recipient list (CSV; first line = headers),
    /// seeding the prompt with a header line built from the merge fields already in the document, then
    /// loads the parsed <see cref="MergeData"/> into the session and auto-matches the field roles. When no
    /// recipient-CSV callback was supplied (tests / parallel waves) this is a safe no-op.
    /// </summary>
    public void SelectRecipients()
    {
        if (_callbacks.AskRecipientCsv is not { } ask)
            return;

        // Seed the dialog with a header line built from the «Field» placeholders already in the document,
        // so the user knows which columns the template expects.
        var fields = MailMerge.FieldNames(Session.Template ?? _editor.Document);
        var seed = fields.Count > 0 ? string.Join(",", fields) : string.Empty;

        var csv = ask(seed);
        if (string.IsNullOrWhiteSpace(csv))
            return; // cancelled

        LoadRecipientsCsv(csv);
    }

    /// <summary>
    /// Parse <paramref name="csv"/> into the session's recipient list and auto-match the field roles. The
    /// preview (if active) is reset so the next Preview Results starts from the new record set. Returns the
    /// loaded data so tests can assert on it. Exposed so tests / programmatic callers can load recipients
    /// without a dialog.
    /// </summary>
    public MergeData LoadRecipientsCsv(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var data = MergeData.FromCsv(csv);
        RestoreEditableTemplateIfPreviewing();
        Session.Data = data;
        Session.Mapping = MailMerge.AutoMatchFields(data.Header);
        Session.CurrentIndex = 0;
        return data;
    }

    /// <summary>The field names available from the loaded recipient list (empty when none loaded).</summary>
    public IReadOnlyList<string> AvailableFieldNames =>
        Session.Data?.Header ?? [];

    public void StartMailMergeLetters() =>
        SetMergeMode(MailMergeOutputMode.Letters, "Mail merge output set to Letters.");

    public void StartMailMergeDirectory() =>
        SetMergeMode(MailMergeOutputMode.Directory, "Mail merge output set to Directory.");

    public void ClearMergeSession()
    {
        RestoreEditableTemplateIfPreviewing();
        Session.Clear();
        ShowInfo("Mail merge reset to a normal document.");
    }

    private void SetMergeMode(MailMergeOutputMode mode, string message)
    {
        RestoreEditableTemplateIfPreviewing();
        Session.Mode = mode;
        Session.CurrentIndex = 0;
        ShowInfo(message);
    }

    public void MatchFields()
    {
        if (!RequireRecipients("Select recipients first (Mailings > Select Recipients), then match fields."))
            return;

        ApplyFieldMapping(MailMerge.AutoMatchFields(Session.Data!.Header));
        ShowInfo("Matched recipient fields automatically.");
    }

    /// <summary>
    /// Apply a Match Fields result and leave an active preview in a coherent editable state. Changing the
    /// mapping invalidates the rendered record, so restore the stashed template before clearing preview mode.
    /// </summary>
    public void ApplyFieldMapping(FieldMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        Session.Mapping = mapping;

        if (!Session.IsPreviewing)
            return;

        var template = Session.Template!;
        _editor.LoadDocument(template);
        Session.Template = null;
        Session.CurrentIndex = 0;
    }

    /// <summary>
    /// Ensure the Preview Results navigation dialog always opens over a rendered record, matching the WPF
    /// command's first-preview behavior. Returns false after the normal no-recipient feedback path.
    /// </summary>
    public bool EnsurePreviewingForNavigation()
    {
        if (!Session.IsPreviewing)
            TogglePreview();

        return Session.IsPreviewing;
    }

    public void FilterSortRecipients()
    {
        if (!RequireRecipients("Select recipients first (Mailings > Select Recipients), then filter and sort."))
            return;

        var data = Session.Data!;
        RestoreEditableTemplateIfPreviewing();
        var sortColumn = data.Header.FirstOrDefault();
        Session.Data = MailMergeRecipientFilterSortPlanner.Apply(
            data,
            Enumerable.Range(0, data.Count),
            sortColumn,
            ascending: true);
        Session.Template = null;
        Session.CurrentIndex = 0;
        ShowInfo(sortColumn is null
            ? "Recipient list kept in document order."
            : $"Recipient list sorted by {sortColumn}.");
    }

    // ── Rules ──────────────────────────────────────────────────────────────────────

    private void RestoreEditableTemplateIfPreviewing()
    {
        if (Session.Template is not { } template)
            return;

        _editor.LoadDocument(template);
        Session.Template = null;
    }

    // Rules commands are inserted through the same shared field-instruction builders as WPF.
    public void InsertIfRule()
    {
        if (_callbacks.AskMergeRuleIf is not { } ask)
            return;
        var result = ask(AvailableFieldNames);
        if (result is null)
            return;
        InsertIfRule(result);
    }

    public void InsertIfRule(MailMergeRuleIfDialogResult result)
    {
        InsertRuleField(
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

    public void InsertSkipRecordIfRule()
    {
        if (_callbacks.AskMergeRuleCondition is not { } ask)
            return;
        var result = ask(AvailableFieldNames, "Skip Record If");
        if (result is null)
            return;
        InsertSkipRecordIfRule(result);
    }

    public void InsertSkipRecordIfRule(MailMergeRuleConditionDialogResult result)
    {
        InsertRuleField(
            MergeRuleEvaluator.BuildNativeSkipIfField(result.FieldName, result.Operator, result.Value),
            MergeRuleEvaluator.BuildSkipRecordIfInstruction(
                result.FieldName,
                result.Operator,
                result.Value));
    }

    public void InsertNextRecordIfRule()
    {
        if (_callbacks.AskMergeRuleCondition is not { } ask)
            return;
        var result = ask(AvailableFieldNames, "Next Record If");
        if (result is null)
            return;
        InsertNextRecordIfRule(result);
    }

    public void InsertNextRecordIfRule(MailMergeRuleConditionDialogResult result)
    {
        InsertRuleField(
            MergeRuleEvaluator.BuildNativeNextIfField(result.FieldName, result.Operator, result.Value),
            MergeRuleEvaluator.BuildNextRecordIfInstruction(
                result.FieldName,
                result.Operator,
                result.Value));
    }

    public void InsertNextRecordField() =>
        InsertNativeSpecialField(MailMerge.NextRecordField);

    public void InsertMergeRecordNumberField() =>
        InsertNativeSpecialField(MailMerge.MergeRecordNumberField);

    public void InsertMergeSequenceNumberField() =>
        InsertNativeSpecialField(MailMerge.MergeSequenceNumberField);

    public void InsertFillInRule()
    {
        if (_callbacks.AskMergeRulePrompt is not { } ask)
            return;
        var prompt = ask("Fill-in", "Enter the prompt text for this Fill-in field:");
        if (prompt is null)
            return;
        InsertFillInRule(prompt);
    }

    public void InsertFillInRule(string prompt)
    {
        InsertRuleField(
            new ComplexField(MergeRuleEvaluator.BuildNativeFillInInstruction(prompt)),
            MergeRuleEvaluator.BuildFillInInstruction(prompt));
    }

    public void InsertAskRule()
    {
        if (_callbacks.AskMergeRuleNameValue is not { } ask)
            return;
        var result = ask("Ask", "Prompt text:");
        if (result is null)
            return;
        InsertAskRule(result.Value.Name, result.Value.Value);
    }

    public void InsertAskRule(string bookmarkName, string prompt)
    {
        if (string.IsNullOrWhiteSpace(bookmarkName))
            return;
        InsertRuleField(
            new ComplexField(MergeRuleEvaluator.BuildNativeAskInstruction(bookmarkName.Trim(), prompt)),
            MergeRuleEvaluator.BuildAskInstruction(bookmarkName.Trim(), prompt));
    }

    public void InsertSetRule()
    {
        if (_callbacks.AskMergeRuleNameValue is not { } ask)
            return;
        var result = ask("Set Bookmark", "Value:");
        if (result is null)
            return;
        InsertSetRule(result.Value.Name, result.Value.Value);
    }

    public void InsertSetRule(string bookmarkName, string value)
    {
        if (string.IsNullOrWhiteSpace(bookmarkName))
            return;
        InsertRuleField(
            new ComplexField(MergeRuleEvaluator.BuildNativeSetInstruction(bookmarkName.Trim(), value)),
            MergeRuleEvaluator.BuildSetInstruction(bookmarkName.Trim(), value));
    }

    public void InsertRefRule()
    {
        if (_callbacks.AskMergeRulePrompt is not { } ask)
            return;
        var name = ask("Ref Bookmark", "Enter the bookmark name to reference:");
        if (name is null)
            return;
        InsertRefRule(name);
    }

    public void InsertRefRule(string bookmarkName)
    {
        if (string.IsNullOrWhiteSpace(bookmarkName))
            return;
        InsertRuleField(
            new ComplexField(MergeRuleEvaluator.BuildNativeRefInstruction(bookmarkName.Trim())),
            MergeRuleEvaluator.BuildRefInstruction(bookmarkName.Trim()));
    }

    private void InsertRuleField(ComplexField field, string displayInstruction)
    {
        _editor.InsertComplexField(
            field,
            $"{MailMerge.FieldOpen}{displayInstruction}{MailMerge.FieldClose}");
    }

    private void InsertNativeSpecialField(string fieldName)
    {
        if (!MailMerge.TryGetNativeSpecialFieldInstruction(fieldName, out var instruction))
            return;

        _editor.InsertComplexField(
            instruction,
            $"{MailMerge.FieldOpen}{fieldName}{MailMerge.FieldClose}");
    }

    // ── Insert Merge Field ─────────────────────────────────────────────────────────

    /// <summary>
    /// Mailings &gt; Insert Merge Field. Asks the host to pick / type a field name from the loaded
    /// recipient list and inserts the merge-field placeholder at the caret (undoable). When no field-name
    /// callback was supplied this is a safe no-op; <see cref="InsertMergeFieldNamed"/> performs the actual
    /// insertion and is also usable directly (tests / programmatic callers).
    /// </summary>
    public void InsertMergeField()
    {
        if (_callbacks.AskMergeFieldName is not { } ask)
            return;
        var name = ask(AvailableFieldNames);
        if (string.IsNullOrWhiteSpace(name))
            return;
        InsertMergeFieldNamed(name);
    }

    /// <summary>
    /// Insert a native Word MERGEFIELD for <paramref name="name"/> at the caret (undoable), retaining the
    /// familiar «Field» cached label. Any existing guillemets are stripped. A blank name is ignored.
    /// </summary>
    public void InsertMergeFieldNamed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = MailMerge.NormalizeMergeFieldName(name);
        if (trimmed.Length == 0)
            return;

        _editor.InsertComplexField(
            MailMerge.BuildMergeFieldInstruction(trimmed),
            $"{MailMerge.FieldOpen}{trimmed}{MailMerge.FieldClose}");
    }

    // ── Address Block / Greeting Line ───────────────────────────────────────────────

    /// <summary>
    /// Mailings &gt; Address Block. Inserts a native ADDRESSBLOCK field at the caret (undoable),
    /// resolved per-record at preview / merge time via the session's field mapping. No-ops (with an info
    /// message) when no recipients are loaded, mirroring the WPF host.
    /// </summary>
    public void InsertAddressBlock()
    {
        if (!RequireRecipients("Select recipients first (Mailings > Select Recipients), then insert an Address Block."))
            return;
        _editor.InsertComplexField(
            MailMerge.AddressBlockInstruction,
            $"{MailMerge.FieldOpen}AddressBlock{MailMerge.FieldClose}");
    }

    /// <summary>
    /// Mailings &gt; Greeting Line. Inserts a native default GREETINGLINE field at the caret (undoable),
    /// resolved per-record at preview / merge time. No-ops (with an info message) when no recipients are loaded.
    /// </summary>
    public void InsertGreetingLine()
    {
        if (!RequireRecipients("Select recipients first (Mailings > Select Recipients), then insert a Greeting Line."))
            return;
        _editor.InsertComplexField(
            MailMerge.GreetingLineInstruction,
            $"{MailMerge.FieldOpen}GreetingLine{MailMerge.FieldClose}");
    }

    // ── Preview Results ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Mailings &gt; Preview Results. Toggles preview mode: entering preview captures the current document as
    /// the editable template and loads record 0's merged values into the editor; clicking again leaves
    /// preview and restores the template. No-ops (with an info message) when no recipients are loaded.
    /// </summary>
    public void TogglePreview()
    {
        if (Session.IsPreviewing)
        {
            // Leave preview — restore the editable template.
            var template = Session.Template!;
            Session.Template = null;
            _editor.LoadDocument(template);
            return;
        }

        if (!RequirePreviewableData("Select recipients first (Mailings > Select Recipients), then preview a record."))
            return;

        // Enter preview: stash the current document as the template and show record 0.
        Session.Template = _editor.Document;
        Session.CurrentIndex = 0;
        RenderPreviewRecord();
    }

    /// <summary>
    /// Mailings &gt; Next Record. Advances the preview to the next record (clamped to the last record),
    /// auto-entering preview first if not already previewing. No-ops when no recipients are loaded.
    /// </summary>
    public void NextRecord() => StepRecord(+1);

    /// <summary>
    /// Mailings &gt; Previous Record. Steps the preview to the previous record (clamped at record 0),
    /// auto-entering preview first if not already previewing. No-ops when no recipients are loaded.
    /// </summary>
    public void PreviousRecord() => StepRecord(-1);

    /// <summary>Mailings &gt; First Record. Enters preview if needed and shows the first recipient.</summary>
    public void FirstRecord() => NavigateRecord(MailMergePreviewNavigationAction.First);

    /// <summary>Mailings &gt; Last Record. Enters preview if needed and shows the last recipient.</summary>
    public void LastRecord() => NavigateRecord(MailMergePreviewNavigationAction.Last);

    private void StepRecord(int delta)
    {
        if (!RequirePreviewableData("Select recipients first (Mailings > Select Recipients), then step records."))
            return;

        // Auto-enter preview so the Next/Previous buttons work without first clicking Preview Results.
        if (!Session.IsPreviewing)
        {
            Session.Template = _editor.Document;
            Session.CurrentIndex = 0;
            RenderPreviewRecord();
            if (delta == 0)
                return;
        }

        var count = Session.Data!.Count;
        Session.CurrentIndex = Math.Clamp(Session.CurrentIndex + delta, 0, count - 1);
        RenderPreviewRecord();
    }

    private void NavigateRecord(MailMergePreviewNavigationAction action)
    {
        if (!RequirePreviewableData("Select recipients first (Mailings > Select Recipients), then step records."))
            return;

        if (!Session.IsPreviewing)
            Session.Template = _editor.Document;

        var count = Session.Data!.Count;
        Session.CurrentIndex = MailMergePreviewNavigationPlanner.TargetIndex(action, Session.CurrentIndex, count);
        RenderPreviewRecord();
    }

    private void RenderPreviewRecord()
    {
        var data = Session.Data!;
        var template = Session.Template!;
        var index = Math.Clamp(Session.CurrentIndex, 0, data.Count - 1);
        Session.CurrentIndex = index;
        _editor.LoadDocument(MailMerge.MergeRecord(template, Session.AugmentRow(data.Rows[index])));
    }

    // ── Finish & Merge ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Mailings &gt; Finish &amp; Merge. Merges every (non-skipped) record against the template into a single
    /// new in-memory document (records separated per the output mode) and loads it into the editor. Uses
    /// the stashed preview template when previewing, otherwise the current document. Composite
    /// «AddressBlock» / «GreetingLine» placeholders and conditional rules are resolved per record.
    /// No-ops (with an info message) when no recipients are loaded.
    ///
    /// <para>This command still merges to a document only; Send E-mail Messages is handled by PlanEmailMerge.</para>
    /// </summary>
    public TextDocument? FinishMerge()
    {
        if (Session.Data is not { Count: > 0 } data)
        {
            ShowInfo("Select recipients first (Mailings > Select Recipients), then Finish & Merge.");
            return null;
        }

        var finishPlan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(data.Count);
        return FinishMerge(finishPlan);
    }

    public TextDocument? FinishMerge(MailMergeFinishPlan finishPlan)
    {
        return FinishMerge(finishPlan, new MergeState());
    }

    public TextDocument? FinishMerge(MailMergeFinishPlan finishPlan, MergeState mergeState)
    {
        ArgumentNullException.ThrowIfNull(mergeState);
        if (Session.Data is not { Count: > 0 })
        {
            ShowInfo("Select recipients first (Mailings > Select Recipients), then Finish & Merge.");
            return null;
        }

        if (!finishPlan.Success)
        {
            ShowInfo($"Finish & Merge cannot continue: {finishPlan.Issue}.");
            return null;
        }

        if (finishPlan.Destination != MailMergeFinishDestination.NewDocument)
            return null;

        var result = BuildFinishedMerge(finishPlan, mergeState);
        if (result is null)
            return null;

        return ApplyFinishedMerge(result);
    }

    internal TextDocument ApplyFinishedMerge(MailMergeFinishBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _editor.LoadDocument(result.Document);
        Session.Template = null;
        Session.CurrentIndex = 0;

        ShowInfo(result.SkippedRecordCount > 0
            ? $"Merged {result.MergedRecordCount} record(s) into a single document ({result.SkippedRecordCount} skipped)."
            : $"Merged {result.MergedRecordCount} record(s) into a single document.");
        return result.Document;
    }

    /// <summary>
    /// Builds the selected merge output without replacing the visible document or changing preview/session
    /// state. Print Documents uses this path so cancelling or completing printer submission leaves the merge
    /// template open and reusable.
    /// </summary>
    public MailMergeFinishBuildResult? BuildFinishedMerge(MailMergeFinishPlan finishPlan)
    {
        return BuildFinishedMerge(finishPlan, new MergeState());
    }

    /// <param name="templateSnapshot">
    /// Document to merge from, captured by the caller. Finish &amp; Merge runs on a background thread
    /// (its per-record prompts marshal back to the UI thread and wait, so it cannot run on the UI
    /// thread without deadlocking). Outside preview the template would otherwise be the live,
    /// still-editable document, and the merge iterates its Blocks and Styles once per record — a
    /// keystroke that splits a paragraph mid-merge would throw "collection was modified" on the
    /// background thread. Callers that background this work must pass a snapshot; preview mode
    /// already holds one, since entering preview swaps the editor onto a rendered record.
    /// </param>
    public MailMergeFinishBuildResult? BuildFinishedMerge(
        MailMergeFinishPlan finishPlan,
        MergeState mergeState,
        TextDocument? templateSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(mergeState);
        if (!finishPlan.Success ||
            Session.Data is not { Count: > 0 } data ||
            finishPlan.RowIndexes.Any(index => index < 0 || index >= data.Count))
            return null;

        var template = templateSnapshot
            ?? (Session.IsPreviewing ? Session.Template! : _editor.Document);

        // Augment every row with the composed «AddressBlock» / «GreetingLine» values so those composite
        // placeholders resolve across every record, then run the rules-aware merge (records flagged by a
        // «Skip Record If» rule are excluded).
        var augmentedData = BuildAugmentedData(data, finishPlan.RowIndexes);
        var merged = MailMerge.MergeAllWithRules(template, augmentedData, mergeState);
        if (mergeState.CancelRequested)
            return null;

        var combined = MailMerge.CombineMergedRecords(merged, Session.Mode);

        return new MailMergeFinishBuildResult(combined, merged.Count, mergeState.SkippedIndices.Count);
    }

    public IReadOnlyList<MailMergeInteractivePrompt> GetInteractiveFinishPrompts()
    {
        var template = Session.IsPreviewing ? Session.Template! : _editor.Document;
        return MailMergeInteractivePromptPlanner.Plan(template);
    }

    /// <summary>
    /// Simulate every selected recipient against the current merge template. Complete modes load the
    /// merged document only when their Word-compatible pause policy permits it.
    /// </summary>
    public MailMergeErrorCheckResult? CheckForErrors(
        MailMergeCheckForErrorsMode mode,
        bool completeMerge = true)
    {
        if (Session.Data is not { Count: > 0 } data)
        {
            ShowInfo("Select recipients first (Mailings > Select Recipients), then check for errors.");
            return null;
        }

        var template = Session.IsPreviewing ? Session.Template! : _editor.Document;
        var rows = data.Rows.Select(row => Session.AugmentRow(row)).ToList();
        var result = MailMergeCheckForErrorsPlanner.Check(template, rows, mode);
        if (completeMerge && result.ShouldCompleteMerge)
            FinishMerge();
        return result;
    }

    /// <summary>
    /// Mailings &gt; Send E-mail Messages. Builds and validates the delivery plan, merges one message-body
    /// draft per valid recipient, and asks the host to open each draft in the default mail client.
    /// </summary>
    public MailMergeEmailDeliveryPlan? PlanEmailMerge(MailMergeEmailDeliveryIntent? intent = null)
    {
        if (Session.Data is not { Count: > 0 } data)
        {
            ShowInfo("Select recipients first (Mailings > Select Recipients), then Send E-mail Messages.");
            return null;
        }

        intent ??= MailMergeEmailDeliveryPlanner.CreateDefaultIntent(data, Session.CurrentIndex);
        var plan = MailMerge.CreateEmailDeliveryPlan(data, intent);
        LastEmailPlan = plan;
        var template = Session.IsPreviewing ? Session.Template! : _editor.Document;
        var drafts = MailMergeEmailDeliveryPlanner.CreateClientDraftPlan(
            template,
            data,
            plan,
            row => Session.AugmentRow(row));
        LastEmailDraftPlan = drafts;
        if (!drafts.IsReady)
        {
            ShowInfo(string.Join(Environment.NewLine, drafts.Errors.Concat(drafts.Warnings)));
            return plan;
        }

        var launched = _callbacks.OpenMailDraft is { } open
            ? drafts.Drafts.Count(draft => open(draft.LaunchTarget))
            : 0;
        ShowInfo(MailMergeEmailDeliveryPlanner.FormatClientDraftStatus(drafts, launched));
        return plan;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>Mailings &gt; Envelopes. Apply the default envelope geometry through the undoable page setup path.</summary>
    public void ApplyDefaultEnvelope()
    {
        var plan = MailingsEnvelopeLabelPlanner.PlanEnvelope(MailingsEnvelopeLabelPlanner.DefaultEnvelopeIndex);
        _editor.ApplyPageSettings(page =>
        {
            page.WidthPt = plan.WidthPt;
            page.HeightPt = plan.HeightPt;
            page.MarginLeftPt = plan.MarginPt;
            page.MarginRightPt = plan.MarginPt;
            page.MarginTopPt = plan.MarginPt;
            page.MarginBottomPt = plan.MarginPt;
            page.Landscape = plan.Landscape;
        });
        ShowInfo("Applied default envelope page setup.");
    }

    /// <summary>Mailings &gt; Labels. Apply the default label sheet and insert its label grid.</summary>
    public void ApplyDefaultLabels()
    {
        var plan = MailingsEnvelopeLabelPlanner.PlanLabel(
            MailingsEnvelopeLabelPlanner.DefaultLabelIndex,
            customRowsText: null,
            customColumnsText: null);
        if (!plan.Success || plan.Result is not { } result)
            return;

        ApplyLabels(result);
    }

    /// <summary>
    /// Apply a label-sheet setup, insert its grid, and populate cells from the active recipient list.
    /// Records flow left-to-right and top-to-bottom; skipped records do not consume a label cell.
    /// </summary>
    public void ApplyLabels(LabelSetupResult result)
    {
        var rows = Math.Max(1, result.Rows);
        var columns = Math.Max(1, result.Columns);
        var cellContents = BuildLabelCellContents(rows * columns);
        var existingTables = _editor.Document.Blocks.OfType<Table>().ToList();

        _editor.ApplyPageSettings(page =>
        {
            page.WidthPt = result.PageWidthPt;
            page.HeightPt = result.PageHeightPt;
            page.MarginLeftPt = result.MarginPt;
            page.MarginRightPt = result.MarginPt;
            page.MarginTopPt = result.MarginPt;
            page.MarginBottomPt = result.MarginPt;
            page.Landscape = result.Landscape;
        });
        _editor.InsertTable(rows, columns);

        var tableBlockIndex = -1;
        for (var i = 0; i < _editor.Document.Blocks.Count; i++)
        {
            if (_editor.Document.Blocks[i] is Table table && !existingTables.Contains(table))
            {
                tableBlockIndex = i;
                break;
            }
        }

        if (tableBlockIndex >= 0)
        {
            for (var index = 0; index < cellContents.Count; index++)
            {
                _editor.SetTableCellContent(
                    tableBlockIndex,
                    index / columns,
                    index % columns,
                    cellContents[index]);
            }
        }

        ShowInfo($"Inserted a {rows} x {columns} label grid.");
    }

    private IReadOnlyList<IReadOnlyList<Paragraph>> BuildLabelCellContents(int capacity)
    {
        if (Session.Data is not { Count: > 0 } data)
            return [];

        var template = Session.IsPreviewing ? Session.Template! : _editor.Document;
        var state = new MergeState();
        var contents = new List<IReadOnlyList<Paragraph>>(Math.Min(capacity, data.Count));
        var recordIndex = 0;

        while (contents.Count < capacity && recordIndex < data.Count)
        {
            state.SequenceNumber++;
            var row = Session.AugmentRow(data.Rows[recordIndex]);
            var merged = MailMerge.MergeRecordWithRules(template, row, state, recordIndex + 1);
            if (state.SkipRecordRequested)
            {
                state.SequenceNumber--;
                recordIndex++;
                continue;
            }

            contents.Add(merged.Blocks.OfType<Paragraph>().ToList());
            recordIndex += state.AdvanceRecordRequested ? 2 : 1;
        }

        return contents;
    }

    /// <summary>
    /// Build a <see cref="MergeData"/> whose every row carries the composed «AddressBlock» and
    /// «GreetingLine» columns (in addition to the original columns), so the substitution path resolves the
    /// composite placeholders per record.
    /// </summary>
    private MergeData BuildAugmentedData(MergeData data, IReadOnlyList<int> rowIndexes)
    {
        var header = data.Header.ToList();
        if (!header.Contains("AddressBlock", StringComparer.OrdinalIgnoreCase)) header.Add("AddressBlock");
        if (!header.Contains("GreetingLine", StringComparer.OrdinalIgnoreCase)) header.Add("GreetingLine");

        var rows = new List<IReadOnlyList<string>>(rowIndexes.Count);
        foreach (var rowIndex in rowIndexes)
        {
            var row = data.Rows[rowIndex];
            var augmented = Session.AugmentRow(row);
            rows.Add(header.Select(h => augmented.TryGetValue(h, out var v) ? v : string.Empty).ToList());
        }
        return new MergeData(header, rows);
    }

    private bool RequireRecipients(string message)
    {
        if (Session.Data is not null)
            return true;
        ShowInfo(message);
        return false;
    }

    private bool RequirePreviewableData(string message)
    {
        if (Session.Data is { Count: > 0 })
            return true;
        ShowInfo(message);
        return false;
    }

    private void ShowInfo(string message) => _callbacks.ShowMailMergeInfo?.Invoke(message);
}
