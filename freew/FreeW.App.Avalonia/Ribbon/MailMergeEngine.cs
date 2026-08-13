using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Ribbon;

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
    private readonly FreeWRibbonHostExecutionPorts _callbacks;
    private readonly Func<string, string?>? _getText;
    private readonly MailMergeSessionWorkflow _workflow = new();

    public MailMergeEngine(
        DocumentView editor,
        FreeWRibbonHostExecutionPorts callbacks,
        Func<string, string?>? getText = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        _getText = getText;
    }

    /// <summary>The shared session (recipient data + mapping + preview state). Exposed for tests.</summary>
    public MailMergeSession Session => _workflow.Session;

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
        var fields = MailMerge.FieldNames(_editor.Document);
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
        var (data, _) = LoadRecipientsCsvCore(csv);
        return data;
    }

    public MailMergeSessionTransition LoadRecipientsCsvWithTransition(string csv) =>
        LoadRecipientsCsvCore(csv).Transition;

    private (MergeData Data, MailMergeSessionTransition Transition) LoadRecipientsCsvCore(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var data = MergeData.FromCsv(csv);
        var transition = _workflow.LoadRecipients(data);
        Realize(transition);
        return (data, transition);
    }

    /// <summary>The field names available from the loaded recipient list (empty when none loaded).</summary>
    public IReadOnlyList<string> AvailableFieldNames =>
        _workflow.AvailableFieldNames;

    public void StartMailMergeLetters() =>
        SetMergeMode(MailMergeOutputMode.Letters);

    public void StartMailMergeDirectory() =>
        SetMergeMode(MailMergeOutputMode.Directory);

    public void ClearMergeSession()
    {
        var transition = _workflow.Clear();
        Realize(transition);
        ShowInfo(transition.Message);
    }

    private void SetMergeMode(MailMergeOutputMode mode)
    {
        var transition = _workflow.SetMode(mode);
        Realize(transition);
        ShowInfo(transition.Message);
    }

    public void MatchFields()
    {
        if (!ValidateAndShow(MailMergeOperation.MatchFields))
            return;

        ApplyFieldMapping(MailMerge.AutoMatchFields(Session.Data!.Header));
        ShowInfo(UiText.Get("MailMerge_MatchedFields_Status"));
    }

    /// <summary>
    /// Apply a Match Fields result and leave an active preview in a coherent editable state. Changing the
    /// mapping invalidates the rendered record, so restore the stashed template before clearing preview mode.
    /// </summary>
    public void ApplyFieldMapping(FieldMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        Realize(_workflow.ApplyFieldMapping(mapping));
    }

    /// <summary>
    /// Ensure the Preview Results navigation dialog always opens over a rendered record, matching the WPF
    /// command's first-preview behavior. Returns false after the normal no-recipient feedback path.
    /// </summary>
    public bool EnsurePreviewingForNavigation()
    {
        return Realize(_workflow.EnsurePreviewing(_editor.Document));
    }

    public void FilterSortRecipients()
    {
        if (!ValidateAndShow(MailMergeOperation.FilterSortRecipients))
            return;

        var data = Session.Data!;
        var sortColumn = data.Header.FirstOrDefault();
        var filtered = MailMergeRecipientFilterSortPlanner.Apply(
            data,
            Enumerable.Range(0, data.Count),
            sortColumn,
            ascending: true);
        ApplyRecipientFilter(filtered);
        ShowInfo(sortColumn is null
            ? UiText.Get("MailMerge_DocumentOrder_Status")
            : UiText.Format("MailMerge_SortedBy_Status_Format", sortColumn));
    }

    public void ApplyRecipientFilter(MergeData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Realize(_workflow.ApplyRecipientFilter(data));
    }

    public MailMergeFindExecution FindRecipient(string? query)
    {
        var execution = _workflow.FindRecipient(query);
        if (execution.DocumentToLoad is { } document)
            _editor.LoadDocument(document);
        return execution;
    }

    // ── Rules ──────────────────────────────────────────────────────────────────────

    public MailMergeRuleDialogRequest CreateRuleRequest(MailMergeRuleKind kind) =>
        MailMergeRuleDialogPlanner.CreateRequest(kind, AvailableFieldNames, _getText);

    public void InsertRule(MailMergeRuleKind kind)
    {
        if (_callbacks.AskMergeRule is not { } ask)
            return;

        AuthorRuleAsync(
                kind,
                (request, _) => ValueTask.FromResult(ask(request)))
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public ValueTask<MailMergeRuleAuthoringExecution> AuthorRuleAsync(
        MailMergeRuleKind kind,
        MailMergeRuleDialogPresenter showDialog,
        CancellationToken cancellationToken = default) =>
        MailMergeRuleAuthoringWorkflow.RunAsync(
            CreateRuleRequest(kind),
            showDialog,
            (plan, _) =>
            {
                RealizeMailMergeFieldPlan(plan);
                return ValueTask.CompletedTask;
            },
            cancellationToken);

    public void InsertNextRecordField() =>
        InsertNativeSpecialField(MailMerge.NextRecordField);

    public void InsertMergeRecordNumberField() =>
        InsertNativeSpecialField(MailMerge.MergeRecordNumberField);

    public void InsertMergeSequenceNumberField() =>
        InsertNativeSpecialField(MailMerge.MergeSequenceNumberField);

    private void RealizeMailMergeFieldPlan(MailMergeFieldInsertionPlan? plan)
    {
        if (plan is null)
            return;

        _editor.InsertComplexField(plan.Field, plan.CachedLabel);
    }

    private void InsertNativeSpecialField(string fieldName)
    {
        RealizeMailMergeFieldPlan(
            MailMergeFieldAuthoringPlanner.CreateSpecialFieldPlan(fieldName));
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
    /// Insert a native Word MERGEFIELD for <paramref name="name"/> at the caret through the shared
    /// authoring plan. A blank name is ignored.
    /// </summary>
    public void InsertMergeFieldNamed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        RealizeMailMergeFieldPlan(
            MailMergeFieldAuthoringPlanner.CreateMergeFieldPlan(name));
    }

    // ── Address Block / Greeting Line ───────────────────────────────────────────────

    /// <summary>
    /// Mailings &gt; Address Block. Inserts a native ADDRESSBLOCK field at the caret (undoable),
    /// resolved per-record at preview / merge time via the session's field mapping. No-ops (with an info
    /// message) when no recipients are loaded, mirroring the WPF host.
    /// </summary>
    public void InsertAddressBlock()
    {
        if (!ValidateAndShow(MailMergeOperation.InsertAddressBlock))
            return;
        RealizeMailMergeFieldPlan(
            MailMergeFieldAuthoringPlanner.CreateAddressBlockPlan());
    }

    /// <summary>
    /// Mailings &gt; Greeting Line. Inserts a native default GREETINGLINE field at the caret (undoable),
    /// resolved per-record at preview / merge time. No-ops (with an info message) when no recipients are loaded.
    /// </summary>
    public void InsertGreetingLine()
    {
        if (!ValidateAndShow(MailMergeOperation.InsertGreetingLine))
            return;
        RealizeMailMergeFieldPlan(
            MailMergeFieldAuthoringPlanner.CreateGreetingLinePlan());
    }

    // ── Preview Results ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Mailings &gt; Preview Results. Toggles preview mode: entering preview captures the current document as
    /// the editable template and loads record 0's merged values into the editor; clicking again leaves
    /// preview and restores the template. No-ops (with an info message) when no recipients are loaded.
    /// </summary>
    public void TogglePreview()
    {
        Realize(_workflow.TogglePreview(_editor.Document));
    }

    /// <summary>
    /// Mailings &gt; Next Record. Advances the preview to the next record (clamped to the last record),
    /// auto-entering preview first if not already previewing. No-ops when no recipients are loaded.
    /// </summary>
    public void NextRecord() => NavigateRecord(MailMergePreviewNavigationAction.Next);

    /// <summary>
    /// Mailings &gt; Previous Record. Steps the preview to the previous record (clamped at record 0),
    /// auto-entering preview first if not already previewing. No-ops when no recipients are loaded.
    /// </summary>
    public void PreviousRecord() => NavigateRecord(MailMergePreviewNavigationAction.Previous);

    /// <summary>Mailings &gt; First Record. Enters preview if needed and shows the first recipient.</summary>
    public void FirstRecord() => NavigateRecord(MailMergePreviewNavigationAction.First);

    /// <summary>Mailings &gt; Last Record. Enters preview if needed and shows the last recipient.</summary>
    public void LastRecord() => NavigateRecord(MailMergePreviewNavigationAction.Last);

    private void NavigateRecord(MailMergePreviewNavigationAction action)
    {
        Realize(_workflow.NavigatePreview(_editor.Document, action));
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
        var finishPlan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(Session.Data?.Count ?? 0);
        return FinishMerge(finishPlan);
    }

    public TextDocument? FinishMerge(
        MailMergeFinishPlan finishPlan,
        MergeState? mergeState = null)
    {
        var route = RouteFinish(
            finishPlan,
            printingAvailable: false,
            emailAvailable: false);
        if (!route.Success || route.Route != MailMergeFinishRoute.NewDocument)
            return null;

        var execution = _workflow.BuildFinish(_editor.Document, finishPlan, mergeState);
        if (!execution.Success || execution.Document is null)
        {
            ShowInfo(execution.Message);
            return null;
        }

        _editor.LoadDocument(execution.Document);
        _workflow.CompleteFinish(execution);
        ShowInfo(execution.Message);
        return execution.Document;
    }

    internal TextDocument ApplyFinishedMerge(MailMergeFinishExecution result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.Document);
        _editor.LoadDocument(result.Document);
        _workflow.CompleteFinish(result);
        ShowInfo(result.Message);
        return result.Document;
    }

    /// <summary>
    /// Builds the selected merge output without replacing the visible document or changing preview/session
    /// state. Print Documents uses this path so cancelling or completing printer submission leaves the merge
    /// template open and reusable.
    /// </summary>
    public MailMergeFinishExecution? BuildFinishedMerge(
        MailMergeFinishPlan finishPlan,
        MergeState? mergeState = null,
        TextDocument? templateSnapshot = null)
    {
        var execution = _workflow.BuildFinish(
            templateSnapshot ?? _editor.Document,
            finishPlan,
            mergeState);
        if (!execution.Success || execution.Document is null)
            return null;

        return execution;
    }

    public MailMergeFinishRoutingPlan RouteFinish(
        MailMergeFinishPlan finishPlan,
        bool printingAvailable,
        bool emailAvailable)
    {
        var route = _workflow.RouteFinish(finishPlan, printingAvailable, emailAvailable);
        if (!route.Success)
            ShowInfo(route.Message);
        return route;
    }

    public IReadOnlyList<MailMergeInteractivePrompt> GetInteractiveFinishPrompts() =>
        MailMergeInteractivePromptPlanner.Plan(Session.Template ?? _editor.Document);

    /// <summary>
    /// Simulate every selected recipient against the current merge template. Complete modes load the
    /// merged document only when their Word-compatible pause policy permits it.
    /// </summary>
    public MailMergeErrorCheckResult? CheckForErrors(
        MailMergeCheckForErrorsMode mode,
        bool completeMerge = true)
    {
        var execution = CheckForErrorsPlan(mode);
        if (!execution.Success || execution.Result is null)
        {
            ShowInfo(execution.Message);
            return null;
        }

        if (completeMerge && execution.Result.ShouldCompleteMerge)
            FinishMerge();
        return execution.Result;
    }

    public MailMergeCheckExecution CheckForErrorsPlan(MailMergeCheckForErrorsMode mode) =>
        _workflow.CheckForErrors(_editor.Document, mode);

    /// <summary>
    /// Mailings &gt; Send E-mail Messages. Builds and validates the delivery plan, merges one message-body
    /// draft per valid recipient, and asks the host to open each draft in the default mail client.
    /// </summary>
    public MailMergeEmailDeliveryPlan? PlanEmailMerge(MailMergeEmailDeliveryIntent? intent = null)
    {
        var launch = _workflow.ExecuteEmailDrafts(
            _editor.Document,
            intent,
            _callbacks.OpenMailDraft);
        LastEmailPlan = launch.Execution.Plan;
        LastEmailDraftPlan = launch.Execution.DraftPlan;
        ShowInfo(launch.Message);
        return launch.Execution.Plan;
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
        ShowInfo(UiText.Get("MailMerge_EnvelopeSetupApplied_Status"));
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
        var template = Session.IsPreviewing ? Session.Template! : _editor.Document;
        var cellContents = Session.BuildLabelCellContents(template, rows * columns);
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

        ShowInfo(UiText.Format("MailMerge_LabelGridInserted_Status_Format", rows, columns));
    }

    public MailMergeValidationPlan ValidateOperation(MailMergeOperation operation) =>
        _workflow.Validate(operation);

    private bool ValidateAndShow(MailMergeOperation operation)
    {
        var validation = ValidateOperation(operation);
        if (validation.IsValid)
            return true;

        ShowInfo(validation.Message);
        return false;
    }

    private void Realize(MailMergeSessionTransition transition)
    {
        if (transition.DocumentToLoad is { } document)
            _editor.LoadDocument(document);
    }

    private bool Realize(MailMergePreviewExecution execution)
    {
        if (execution.DocumentToLoad is { } document)
            _editor.LoadDocument(document);
        if (!execution.Success)
            ShowInfo(execution.Message);
        return execution.Success;
    }

    private void ShowInfo(string message) => _callbacks.ShowMailMergeInfo?.Invoke(message);
}
