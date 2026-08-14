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
                GetDocument: () => _editor.Model,
                LoadDocumentAsync: (document, _) =>
                {
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

    public bool IsDirty => _workflow.IsDirty;

    /// <summary>Monotonic dirty-edit counter, used by autosave to suppress redundant snapshots.</summary>
    public int DirtyGeneration => _workflow.DirtyGeneration;

    public string? CurrentPath => _workflow.CurrentPath;

    public string DisplayName => _workflow.DisplayName;

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

    /// <summary>Loads the shared New Window snapshot and restores its file identity.</summary>
    public void LoadDocumentWindow(FreeWDocumentWindowPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _editor.LoadModel(plan.Document);
        _workflow.ApplyDocumentState(
            plan.CurrentPath,
            plan.IsDirty,
            () => _editor.CurrentFileName = plan.CurrentPath is null ? null : Path.GetFileName(plan.CurrentPath));
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
        SaveAsSuggested(suggestedFileName: null, preferredExtension);

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
