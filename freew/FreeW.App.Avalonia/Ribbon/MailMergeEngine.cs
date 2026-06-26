using FreeW.App.Avalonia.Editing;
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
/// <b>Mail-SEND (e-mail merge) is OUT OF SCOPE</b> — this glue only merges to a new in-memory document
/// (Finish &amp; Merge); nothing here sends mail.
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
        ArgumentNullException.ThrowIfNull(csv);
        var data = MergeData.FromCsv(csv);
        Session.Data = data;
        Session.Mapping = MailMerge.AutoMatchFields(data.Header);
        Session.Template = null;   // leaving any active preview
        Session.CurrentIndex = 0;
        return data;
    }

    /// <summary>The field names available from the loaded recipient list (empty when none loaded).</summary>
    public IReadOnlyList<string> AvailableFieldNames =>
        Session.Data?.Header ?? [];

    // ── Insert Merge Field ─────────────────────────────────────────────────────────

    /// <summary>
    /// Mailings &gt; Insert Merge Field. Asks the host to pick / type a field name from the loaded
    /// recipient list and inserts the «Field» placeholder at the caret (undoable). When no field-name
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
    /// Insert a «Field» merge-field placeholder for <paramref name="name"/> at the caret (undoable). Any
    /// guillemets the caller already wrapped around the name are stripped so the placeholder is well-formed.
    /// A blank name is ignored.
    /// </summary>
    public void InsertMergeFieldNamed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim().Trim(MailMerge.FieldOpen, MailMerge.FieldClose).Trim();
        if (trimmed.Length == 0)
            return;
        _editor.InsertText($"{MailMerge.FieldOpen}{trimmed}{MailMerge.FieldClose}");
    }

    // ── Address Block / Greeting Line ───────────────────────────────────────────────

    /// <summary>
    /// Mailings &gt; Address Block. Inserts the composite «AddressBlock» placeholder at the caret (undoable),
    /// resolved per-record at preview / merge time via the session's field mapping. No-ops (with an info
    /// message) when no recipients are loaded, mirroring the WPF host.
    /// </summary>
    public void InsertAddressBlock()
    {
        if (!RequireRecipients("Select recipients first (Mailings > Select Recipients), then insert an Address Block."))
            return;
        _editor.InsertText($"{MailMerge.FieldOpen}AddressBlock{MailMerge.FieldClose}");
    }

    /// <summary>
    /// Mailings &gt; Greeting Line. Inserts the composite «GreetingLine» placeholder at the caret (undoable),
    /// resolved per-record at preview / merge time. No-ops (with an info message) when no recipients are loaded.
    /// </summary>
    public void InsertGreetingLine()
    {
        if (!RequireRecipients("Select recipients first (Mailings > Select Recipients), then insert a Greeting Line."))
            return;
        _editor.InsertText($"{MailMerge.FieldOpen}GreetingLine{MailMerge.FieldClose}");
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
    /// <para>This merges to a document only — it does NOT send e-mail (mail-send is out of scope).</para>
    /// </summary>
    public TextDocument? FinishMerge()
    {
        if (Session.Data is not { Count: > 0 } data)
        {
            ShowInfo("Select recipients first (Mailings > Select Recipients), then Finish & Merge.");
            return null;
        }

        var template = Session.IsPreviewing ? Session.Template! : _editor.Document;

        // Augment every row with the composed «AddressBlock» / «GreetingLine» values so those composite
        // placeholders resolve across every record, then run the rules-aware merge (records flagged by a
        // «Skip Record If» rule are excluded).
        var augmentedData = BuildAugmentedData(data);
        var state = new MergeState();
        var merged = MailMerge.MergeAllWithRules(template, augmentedData, state);
        var combined = MailMerge.CombineMergedRecords(merged, Session.Mode);

        _editor.LoadDocument(combined);
        Session.Template = null;
        Session.CurrentIndex = 0;

        var skipped = state.SkippedIndices.Count;
        ShowInfo(skipped > 0
            ? $"Merged {merged.Count} record(s) into a single document ({skipped} skipped)."
            : $"Merged {merged.Count} record(s) into a single document.");
        return combined;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a <see cref="MergeData"/> whose every row carries the composed «AddressBlock» and
    /// «GreetingLine» columns (in addition to the original columns), so the substitution path resolves the
    /// composite placeholders per record.
    /// </summary>
    private MergeData BuildAugmentedData(MergeData data)
    {
        var header = data.Header.ToList();
        if (!header.Contains("AddressBlock", StringComparer.OrdinalIgnoreCase)) header.Add("AddressBlock");
        if (!header.Contains("GreetingLine", StringComparer.OrdinalIgnoreCase)) header.Add("GreetingLine");

        var rows = new List<IReadOnlyList<string>>(data.Count);
        foreach (var row in data.Rows)
        {
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
