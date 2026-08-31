using System;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Shell.Wpf;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's File lifecycle: New / Open / Save / Save As / Close over the docx reader+writer.
///
/// <para>
/// The file-lifecycle <em>ceremony</em> — the dirty-gate before destructive actions, the
/// Save-vs-Save-As resolution, and recent-files registration — is decided by the shared, neutral
/// <see cref="FileLifecyclePlanner"/> (P2). FreeW supplies only the thin host side: the native
/// <see cref="OpenFileDialog"/>/<see cref="SaveFileDialog"/> for its catalog formats
/// and the message prompts. The document-format read/write decisions live in the neutral
/// <see cref="DocumentPersistenceWorkflow"/>; dirty/path state and lifecycle ceremony live in the
/// shared <see cref="FileCommandWorkflow"/>.
/// </para>
///
/// <para>
/// Recent files are tracked through the shared <see cref="RecentFilesStore"/> (which persists under
/// FreeW's own data folder because Program.Main set AppProduct = "FreeW").
/// </para>
/// </summary>
internal sealed class FileCommands
{
    private readonly Window _window;
    private readonly DocumentView _editor;
    private readonly SisterWpfFileCommandWorkflow _workflow;

    // FreeW's persisted settings (shared JsonSettingsStore under %APPDATA%\FreeW). The recent-files cap
    // is read from here when registering a saved/opened file — a real read site that proves the options
    // mechanism end-to-end. Defaults are used when no store is supplied (e.g. tests).
    private readonly FreeWOptions _options;

    private readonly DocumentPersistenceWorkflow _persistence;
    private readonly FreeWDocumentFileWorkflow _documentWorkflow;
    private readonly FreeWDocumentFileCommandSession _fileCommands;
    private readonly Func<string?> _promptPdfImportPath;

    public FileCommands(
        Window window,
        DocumentView editor,
        Action onChanged,
        FreeWOptions? options = null,
        IReadOnlyList<IDocumentFileAdapter>? adapters = null,
        Func<RecentFilesStore>? loadRecentFilesStore = null,
        IUserMessageService? messageService = null,
        Func<DocumentSaveCompatibilityPlan, bool>? confirmSaveCompatibility = null,
        Func<string?>? promptPdfImportPath = null)
    {
        _window = window;
        _editor = editor;
        _options = options ?? new FreeWOptions();
        _persistence = new DocumentPersistenceWorkflow(adapters);
        _promptPdfImportPath = promptPdfImportPath ?? PromptPdfImportPath;
        _workflow = new SisterWpfFileCommandWorkflow(
            "FreeW",
            () => _options.RecentFilesCap,
            onChanged,
            Save,
            loadRecentFilesStore,
            messageService);
        var confirmCompatibility = confirmSaveCompatibility ??
            (plan => SaveCompatibilityWarningDialog.Show(_window, plan));
        _documentWorkflow = new FreeWDocumentFileWorkflow(
            _workflow.Workflow,
            _persistence,
            new FreeWDocumentFilePorts(
                // While Mailings > Preview Results is active, _editor.Model holds the merged, single-record
                // document that the preview loaded for on-screen display (MailMergeSessionWorkflow.EnsurePreviewing/
                // NavigatePreview/MovePreviewTo -> Realize -> editor.LoadModel), not the mail-merge template.
                // Saving that straight to disk would permanently replace every MERGEFIELD/ADDRESSBLOCK/
                // GREETINGLINE/IF/SKIPIF/NEXTIF in the user's template with the one previewed recipient's
                // literal values -- across Save, Save As, and Save a Copy, which all funnel through this same
                // GetDocument port (FreeWDocumentFileWorkflow.SaveTargetAsync). Fall back to the session's
                // still-live Template whenever a preview is active, exactly as FreeWRibbonCommands'
                // CurrentMailMergeDocument already does for every other mail-merge operation.
                GetDocument: () => _editor.MailMergeSession?.Template ?? _editor.Model,
                LoadDocumentAsync: (document, _) =>
                {
                    AbandonStaleMailMergePreview();
                    _editor.LoadModel(document);
                    return ValueTask.CompletedTask;
                },
                PrepareDocumentAsync: _ =>
                {
                    _editor.CommitToModel();
                    return ValueTask.CompletedTask;
                },
                ConfirmSaveCompatibilityAsync: (plan, _) =>
                    ValueTask.FromResult(confirmCompatibility(plan)),
                ConfirmExternallyModifiedOverwriteAsync: (path, cancellationToken) =>
                    _workflow.ConfirmExternallyModifiedOverwriteAsync(path, cancellationToken),
                UpdateFieldsAsync: _ =>
                {
                    _editor.UpdateFields();
                    return ValueTask.CompletedTask;
                },
                SetCurrentFileName: fileName => _editor.CurrentFileName = fileName));
        _fileCommands = new FreeWDocumentFileCommandSession(
            _documentWorkflow,
            new FreeWFileCommandLifecyclePorts(
                CurrentPath: () => _workflow.CurrentPath,
                CurrentFileName: () => _workflow.CurrentFileName,
                NewAsync: (action, loadAsync) => Task.FromResult(_workflow.New(
                    action,
                    () => loadAsync().GetAwaiter().GetResult())),
                OpenAsync: (action, pickAsync, openAsync) => Task.FromResult(_workflow.Open(
                    action,
                    () => pickAsync().GetAwaiter().GetResult(),
                    path => openAsync(path).GetAwaiter().GetResult())),
                SaveAsync: (saveCurrentAsync, saveAsAsync) => Task.FromResult(_workflow.Save(
                    path => saveCurrentAsync(path).GetAwaiter().GetResult(),
                    () => saveAsAsync().GetAwaiter().GetResult()))),
            new FreeWDocumentFileCommandPorts(
                LoadNewDocumentAsync: () =>
                {
                    AbandonStaleMailMergePreview();
                    _editor.LoadModel(TextDocument.CreateEmpty());
                    _editor.CurrentFileName = null;
                    return Task.CompletedTask;
                },
                PickOpenPathAsync: request => Task.FromResult(PromptOpenPath(request.InitialDirectory)),
                PickPdfImportPathAsync: () => Task.FromResult(_promptPdfImportPath()),
                PickSaveTargetAsync: request => Task.FromResult(
                    TryPromptSavePath(
                        request.PreferredExtension,
                        request.SuggestedFileName,
                        out var path,
                        out var filterIndex)
                        ? new FreeWDocumentSavePickerResult(path, filterIndex)
                        : null),
                PresentFeedback: feedback => ApplyFeedback(feedback)),
            FreeWFileTextResources.Document);
    }

    /// <summary>
    /// r163 remediation. MailMergeSession is built once per WINDOW, but its Template is set per
    /// DOCUMENT by the preview workflow, and nothing cleared it when a different document was loaded.
    /// With the save port above preferring Template whenever one exists, a preview left active on
    /// document A would then be written over document B the moment the user opened B and pressed
    /// Ctrl+S -- destroying a file that has nothing to do with the mail merge. That is a wider blast
    /// radius than the bug the save port was added to fix, so every path that swaps the document out
    /// from under the window ends the preview first.
    ///
    /// EndPreview only drops Template; the loaded recipient data and mapping survive, so re-running a
    /// merge in this window after opening another document does not have to start from scratch.
    /// </summary>
    private void AbandonStaleMailMergePreview() => _editor.MailMergeSession?.EndPreview();

    public bool IsDirty => _workflow.IsDirty;

    /// <summary>Monotonic dirty-edit counter, used by autosave to suppress redundant snapshots.</summary>
    public int DirtyGeneration => _workflow.DirtyGeneration;

    public string? CurrentPath => _workflow.CurrentPath;

    public string DisplayName => _workflow.DisplayName;

    /// <summary>
    /// r174-freew-persistence-readonly-open: whether the open document's source file cannot be
    /// written back to (OS read-only attribute, read-only share/volume, denied ACL). Surfaced in
    /// the window title exactly like FreeX's read-only session marker; Save is routed to Save-As
    /// inside FreeWDocumentFileWorkflow, so nothing else in this host has to branch on it.
    /// </summary>
    public bool IsFileSystemReadOnly => _documentWorkflow.IsCurrentFileReadOnly;

    public IReadOnlyList<FileFormatDescriptor> SaveFormats => _persistence.SaveFormats;

    /// <summary>
    /// Load a recovered autosave snapshot, targeting the original path and marking dirty.
    /// Returns true when the snapshot was loaded successfully, false when the load failed (e.g.
    /// corrupt or locked file). The caller must NOT delete the snapshot on false so the user's
    /// only copy of the unsaved document is preserved.
    /// </summary>
    public bool OpenSnapshot(string snapshotPath, string? originalPath) =>
        OpenSnapshotCore(snapshotPath, originalPath);

    public bool RecoverSnapshot(string snapshotPath, string? originalPath) =>
        _workflow.Open(
            "recovering an unsaved document",
            () => snapshotPath,
            path => OpenSnapshotCore(path, originalPath));

    private bool OpenSnapshotCore(string snapshotPath, string? originalPath)
    {
        var result = _documentWorkflow.OpenSnapshotAsync(snapshotPath, originalPath).GetAwaiter().GetResult();
        return ApplyFeedback(FreeWDocumentFileFeedbackPlanner.PlanSnapshot(result));
    }

    public void MarkDirty()
    {
        _workflow.MarkDirty();
    }

    /// <summary>See <see cref="Free.Shared.AppServices.FileCommandWorkflow.MarkSavedAtUndoDepth"/>.</summary>
    public void MarkSavedAtUndoDepth(int undoDepthAtSave, long undoStackVersionAtSave) =>
        _workflow.MarkSavedAtUndoDepth(undoDepthAtSave, undoStackVersionAtSave);

    /// <summary>See <see cref="Free.Shared.AppServices.FileCommandWorkflow.TryMarkCleanIfAtSavePoint"/>.</summary>
    public bool TryMarkCleanIfAtSavePoint(int currentUndoDepth, long currentUndoStackVersion) =>
        _workflow.TryMarkCleanIfAtSavePoint(currentUndoDepth, currentUndoStackVersion);

    /// <summary>Loads the shared New Window snapshot and restores its file identity.</summary>
    public void LoadDocumentWindow(FreeWDocumentWindowPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        AbandonStaleMailMergePreview();
        _editor.LoadModel(plan.Document);
        _workflow.ApplyDocumentState(
            plan.CurrentPath,
            plan.IsDirty,
            () => _editor.CurrentFileName = plan.CurrentPath is null ? null : Path.GetFileName(plan.CurrentPath));
        // Without this, this window's own external-modification guard baseline stays null forever
        // (New Window never goes through Open/Save), so its first save would skip the conflict
        // check even if the source window saved to the same path in between. See
        // FreeWDocumentFileWorkflow.ApplyWindowState for the full rationale.
        _documentWorkflow.ApplyWindowState(plan.CurrentPath);
    }

    /// <summary>
    /// File &gt; New. Routes through the shared dirty-gate so unsaved work is not silently lost
    /// (previously FreeW dropped changes without prompting). Returns false if the user cancels.
    /// </summary>
    public bool New() => _fileCommands.NewAsync().GetAwaiter().GetResult();

    /// <summary>
    /// File &gt; Open. Dirty-gates first, then shows the open dialog and loads the chosen file.
    /// Returns false if the user cancels at either step.
    /// </summary>
    public bool Open() => _fileCommands.OpenAsync().GetAwaiter().GetResult();

    public bool OpenRecentPath(string path) =>
        _fileCommands.OpenSelectedPathAsync(path).GetAwaiter().GetResult();

    public bool OpenFromFolder(string folderPath) =>
        _fileCommands.OpenAsync(folderPath).GetAwaiter().GetResult();

    /// <summary>
    /// File &gt; Import PDF (text only). This is deliberately not a normal Open path: PDF extraction is lossy,
    /// read-only text import, so the result becomes an untitled dirty document that must be saved elsewhere.
    /// </summary>
    public bool ImportPdfText() =>
        _fileCommands.ImportPdfTextAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Dialog-free PDF text import for tests and host integrations. The PDF path is never associated with the
    /// document or recent-files list because the imported text must be saved to a writable document format.
    /// </summary>
    public bool ImportPdfTextPath(string path) =>
        _fileCommands.ImportPdfTextPathAsync(path).GetAwaiter().GetResult();

    /// <summary>
    /// Loads a specific path (recent-files click / drag-drop / startup). Does NOT dirty-gate: callers
    /// that bypass the dialog already chose to replace the document. Returns true on success.
    /// </summary>
    public bool OpenPath(string path) =>
        _fileCommands.OpenPathAsync(path).GetAwaiter().GetResult();

    /// <summary>Recent files (most recent first) from the shared store; never throws.</summary>
    public IReadOnlyList<RecentFileEntry> RecentEntries => _workflow.RecentEntries;

    /// <summary>
    /// File &gt; Save. Resolves Save-vs-Save-As via the shared planner: writes to the existing path
    /// when there is one, otherwise falls through to Save-As. Returns true on a successful (or no-op)
    /// save, false on cancel/error.
    /// </summary>
    public bool Save() => _fileCommands.SaveAsync().GetAwaiter().GetResult();

    /// <summary>File &gt; Save As. Always prompts for a target. Returns true on a successful save.</summary>
    public bool SaveAs() => SaveAs(preferredExtension: null);

    public bool SaveAs(string? preferredExtension) =>
        string.IsNullOrWhiteSpace(preferredExtension)
            ? SaveAsSuggested(suggestedFileName: null, preferredExtension: null)
            : _fileCommands.SaveAsFormatAsync(preferredExtension).GetAwaiter().GetResult();

    public bool SaveAsSuggested(string? suggestedFileName, string? preferredExtension) =>
        _fileCommands
            .SaveAsAsync(suggestedFileName, preferredExtension)
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// File &gt; Save a Copy. Writes to a chosen path WITHOUT changing the current file or dirty state,
    /// reusing the same resolver + adapter plumbing as Save-As. Returns true on a successful save.
    /// </summary>
    public bool SaveCopy() => _fileCommands.SaveCopyAsync().GetAwaiter().GetResult();

    internal bool SaveCopyToPath(string path, int filterIndex = 0) =>
        _fileCommands
            .SavePathAsync(path, filterIndex, DocumentSaveExecutionKind.SaveCopy)
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// Save-before-close gate, called from the window's Closing handler. Returns true if the window
    /// may close (clean, saved, or the user chose Don't&#160;Save) and false to cancel the close.
    /// This is a behaviour <em>addition</em>: FreeW previously closed without prompting on unsaved work.
    /// </summary>
    public bool ConfirmCloseAllowed() => _workflow.ConfirmCloseAllowed();

    /// <summary>
    /// Same dirty-gate as <see cref="ConfirmCloseAllowed()"/>, but for a caller that is about to
    /// replace the current window's document for a reason other than closing (e.g. recovering a
    /// different unsaved document into it) and wants the save-changes prompt worded for that action.
    /// </summary>
    public bool ConfirmCloseAllowed(string action) => _workflow.ConfirmCloseAllowed(action);

    /// <summary>
    /// Save to the current path, resolving its format adapter. Falls back to Save-As when the current file is
    /// a read-only format (e.g. a legacy format opened for viewing), so the user is steered to a writable one.
    /// </summary>
    private bool ApplyFeedback(FreeWDocumentFileFeedback feedback)
    {
        if (feedback.ShouldShowError)
            ShowError(feedback.ErrorSummary!, feedback.Exception!);
        return feedback.Succeeded;
    }

    /// <summary>
    /// Shows the native Save dialog and returns its path/filter selection. Adapter and target resolution
    /// remain in <see cref="FreeWDocumentFileWorkflow"/> so a user-typed extension wins consistently.
    /// </summary>
    private bool TryPromptSavePath(
        string? preferredExtension,
        string? suggestedFileName,
        out string path,
        out int filterIndex)
    {
        path = string.Empty;
        filterIndex = 0;

        var plan = _persistence.BuildSaveDialogPlan(
            _workflow.CurrentPath,
            _workflow.CurrentFileName,
            suggestedFileName,
            preferredExtension);

        var result = WpfFileDialogService.ShowSaveDialog(_window, plan);
        if (!result.Chosen)
            return false;

        path = result.FileName!;
        filterIndex = result.FilterIndex;
        return true;
    }

    private string? PromptOpenPath(string? initialDirectory = null)
    {
        var plan = _persistence.BuildOpenDialogPlan();
        var result = WpfFileDialogService.ShowOpenDialog(_window, plan, initialDirectory: initialDirectory);
        return result.Chosen ? result.FileName : null;
    }

    private string? PromptPdfImportPath()
    {
        var plan = _persistence.BuildPdfImportDialogPlan();
        var result = WpfFileDialogService.ShowOpenDialog(
            _window,
            plan,
            title: FreeWDocumentFileFeedbackPlanner.ImportPdfPickerTitle);
        return result.Chosen ? result.FileName : null;
    }

    // ── Host seams (WPF) ─────────────────────────────────────────────────────
    // The planners decide; this host supplies WPF pickers, editor commit/load, and message dialogs.

    private void ShowError(string summary, Exception ex) =>
        _workflow.ShowError(summary, ex);

}
