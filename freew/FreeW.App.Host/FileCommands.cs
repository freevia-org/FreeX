using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Shell.Wpf;
using FreeW.App.Host.Editing;
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
    private readonly Func<DocumentSaveCompatibilityPlan, bool> _confirmSaveCompatibility;
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
        _confirmSaveCompatibility = confirmSaveCompatibility ??
            (plan => SaveCompatibilityWarningDialog.Show(_window, plan));
        _promptPdfImportPath = promptPdfImportPath ?? PromptPdfImportPath;
        _workflow = new SisterWpfFileCommandWorkflow(
            "FreeW",
            () => _options.RecentFilesCap,
            onChanged,
            Save,
            loadRecentFilesStore,
            messageService);
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
        try
        {
            var result = _persistence.OpenSnapshot(snapshotPath, originalPath);
            _editor.LoadModel(result.Document);
            _workflow.MarkDirtyWithPath(
                result.TargetPath,
                () => _editor.CurrentFileName = result.TargetPath is null ? null : Path.GetFileName(result.TargetPath));
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not recover the document", ex);
            return false;
        }
    }

    public void MarkDirty()
    {
        _workflow.MarkDirty();
    }

    /// <summary>
    /// File &gt; New. Routes through the shared dirty-gate so unsaved work is not silently lost
    /// (previously FreeW dropped changes without prompting). Returns false if the user cancels.
    /// </summary>
    public bool New() =>
        _workflow.New(
            "creating a new document",
            () => _editor.LoadModel(TextDocument.CreateEmpty()),
            () => _editor.CurrentFileName = null);

    /// <summary>
    /// File &gt; Open. Dirty-gates first, then shows the open dialog and loads the chosen file.
    /// Returns false if the user cancels at either step.
    /// </summary>
    public bool Open() =>
        _workflow.Open("opening another document", () => PromptOpenPath(), OpenPath);

    public bool OpenRecentPath(string path) =>
        _workflow.Open("opening another document", () => path, OpenPath);

    public bool OpenFromFolder(string folderPath) =>
        _workflow.Open("opening another document", () => PromptOpenPath(folderPath), OpenPath);

    /// <summary>
    /// File &gt; Import PDF (text only). This is deliberately not a normal Open path: PDF extraction is lossy,
    /// read-only text import, so the result becomes an untitled dirty document that must be saved elsewhere.
    /// </summary>
    public bool ImportPdfText() =>
        _workflow.Open("importing a PDF", _promptPdfImportPath, ImportPdfTextPath);

    /// <summary>
    /// Dialog-free PDF text import for tests and host integrations. The PDF path is never associated with the
    /// document or recent-files list because the imported text must be saved to a writable document format.
    /// </summary>
    public bool ImportPdfTextPath(string path)
    {
        try
        {
            var result = _persistence.ImportPdfText(path);
            _editor.LoadModel(result.Document);

            _workflow.MarkDirtyWithPath(null, () => _editor.CurrentFileName = null);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            ShowError("Unrecognized PDF import file", ex);
            return false;
        }
        catch (Exception ex)
        {
            ShowError("Could not import PDF text", ex);
            return false;
        }
    }

    /// <summary>
    /// Loads a specific path (recent-files click / drag-drop / startup). Does NOT dirty-gate: callers
    /// that bypass the dialog already chose to replace the document. Returns true on success.
    /// </summary>
    public bool OpenPath(string path) => OpenPath(path, suppressRecentFiles: false);

    private bool OpenPath(string path, bool suppressRecentFiles)
    {
        if (!_persistence.CanOpenPath(path))
        {
            var extension = Path.GetExtension(path);
            ShowError(
                "Unrecognized file type",
                new InvalidOperationException($"FreeW has no reader for “{extension}” files."));
            return false;
        }

        try
        {
            var result = _persistence.Open(path);
            _editor.LoadModel(result.Document);

            // Word's w:updateFields requests one field refresh when the document is opened. Establish
            // the filename first so FILENAME fields have their live value, then mark the result saved
            // after the refresh so opening a document never creates a dirty edit.
            _editor.CurrentFileName = result.SavedPath is null ? null : Path.GetFileName(result.SavedPath);
            if (result.Document.UpdateFieldsOnOpen)
                _editor.UpdateFields();

            ApplyOpenMetadata(result, suppressRecentFiles);

            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not open the document", ex);
            return false;
        }
    }

    /// <summary>Recent files (most recent first) from the shared store; never throws.</summary>
    public IReadOnlyList<RecentFileEntry> RecentEntries => _workflow.RecentEntries;

    /// <summary>
    /// File &gt; Save. Resolves Save-vs-Save-As via the shared planner: writes to the existing path
    /// when there is one, otherwise falls through to Save-As. Returns true on a successful (or no-op)
    /// save, false on cancel/error.
    /// </summary>
    public bool Save() => _workflow.Save(SaveToCurrentPath, SaveAs);

    /// <summary>File &gt; Save As. Always prompts for a target. Returns true on a successful save.</summary>
    public bool SaveAs() => SaveAs(preferredExtension: null);

    public bool SaveAs(string? preferredExtension) =>
        SaveAsSuggested(suggestedFileName: null, preferredExtension);

    public bool SaveAsSuggested(string? suggestedFileName, string? preferredExtension) =>
        TryPromptSaveTarget(preferredExtension, suggestedFileName, out var target) && SaveTo(target);

    /// <summary>
    /// File &gt; Save a Copy. Writes to a chosen path WITHOUT changing the current file or dirty state,
    /// reusing the same resolver + adapter plumbing as Save-As. Returns true on a successful save.
    /// </summary>
    public bool SaveCopy()
    {
        if (!TryPromptSaveTarget(preferredExtension: null, suggestedFileName: null, out var target))
            return false;

        return SaveCopyTo(target);
    }

    internal bool SaveCopyToPath(string path, int filterIndex = 0)
    {
        if (!_persistence.TryResolveSaveTarget(path, filterIndex, out var target))
        {
            ShowError(
                "Could not save a copy",
                new InvalidOperationException($"FreeW has no writer for \u201c{Path.GetExtension(path)}\u201d files."));
            return false;
        }

        return SaveCopyTo(target);
    }

    private bool SaveCopyTo(DocumentSaveTarget target)
    {
        try
        {
            _editor.CommitToModel();
            if (!ConfirmSaveCompatibility(target))
                return false;

            _persistence.Save(_editor.Model, target);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not save a copy", ex);
            return false;
        }
    }

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
    private bool SaveToCurrentPath(string path)
    {
        return _persistence.TryResolveCurrentSaveTarget(path, out var target)
            ? SaveTo(target)
            : SaveAs();
    }

    private bool SaveTo(DocumentSaveTarget target)
    {
        try
        {
            _editor.CommitToModel();
            if (!ConfirmSaveCompatibility(target))
                return false;

            _persistence.Save(_editor.Model, target);
            SetSaved(target.Path, suppressRecentFiles: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not save the document", ex);
            return false;
        }
    }

    /// <summary>
    /// Shows the Save dialog and resolves the chosen target path + writable adapter. The adapter is derived
    /// from the CHOSEN filename's extension (not the selected filter row), so a user-typed extension wins.
    /// Returns false on cancel or when the chosen extension is not a writable format.
    /// </summary>
    private bool TryPromptSaveTarget(
        string? preferredExtension,
        string? suggestedFileName,
        out DocumentSaveTarget target)
    {
        target = null!;

        var plan = _persistence.BuildSaveDialogPlan(
            _workflow.CurrentPath,
            _workflow.CurrentFileName,
            suggestedFileName,
            preferredExtension);

        var result = WpfFileDialogService.ShowSaveDialog(_window, plan);
        if (!result.Chosen)
            return false;

        var chosenExtension = Path.GetExtension(result.FileName!);
        if (!_persistence.TryResolveSaveTarget(result.FileName!, result.FilterIndex, out target))
        {
            ShowError(
                "Cannot save",
                new InvalidOperationException($"“{chosenExtension}” is not a writable format."));
            return false;
        }

        return true;
    }

    private bool ConfirmSaveCompatibility(DocumentSaveTarget target)
    {
        var plan = _persistence.BuildSaveCompatibilityPlan(_editor.Model, target);
        return !plan.RequiresConfirmation || _confirmSaveCompatibility(plan);
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
            title: "Import PDF (text only)");
        return result.Chosen ? result.FileName : null;
    }

    private void ApplyOpenMetadata(DocumentOpenResult result, bool suppressRecentFiles)
    {
        if (result.SavedPath is null)
            _workflow.MarkSavedWithoutPath(() => _editor.CurrentFileName = null);
        else
            SetSaved(result.SavedPath, suppressRecentFiles);
    }

    private void SetSaved(string path, bool suppressRecentFiles)
    {
        _workflow.MarkSavedWithPath(path, suppressRecentFiles, () =>
        {
            // Surface the file name to the editor so FILENAME field runs resolve to it at render.
            _editor.CurrentFileName = Path.GetFileName(path);
        });
    }

    // ── Host seams (WPF) ─────────────────────────────────────────────────────
    // The planners decide; this host supplies WPF pickers, editor commit/load, and message dialogs.

    private void ShowError(string summary, Exception ex) =>
        _workflow.ShowError(summary, ex);

    private sealed class SaveCompatibilityWarningDialog : Window
    {
        private SaveCompatibilityWarningDialog(DocumentSaveCompatibilityPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);

            Title = plan.Title;
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var message = new TextBlock
            {
                Text = plan.Message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16),
            };

            var continueButton = new Button
            {
                Content = plan.ContinueButtonText,
                MinWidth = 90,
                IsDefault = true,
            };
            continueButton.Click += (_, _) => DialogResult = true;

            var cancelButton = new Button
            {
                Content = plan.CancelButtonText,
                MinWidth = 90,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true,
            };
            cancelButton.Click += (_, _) => DialogResult = false;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 0, 16, 16),
            };
            buttons.Children.Add(continueButton);
            buttons.Children.Add(cancelButton);

            Content = new StackPanel
            {
                Children =
                {
                    message,
                    new Border
                    {
                        BorderBrush = Brushes.Gainsboro,
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Child = buttons,
                    },
                },
            };
        }

        public static bool Show(Window owner, DocumentSaveCompatibilityPlan plan)
        {
            var dialog = new SaveCompatibilityWarningDialog(plan)
            {
                Owner = owner,
            };
            return dialog.ShowDialog() == true;
        }
    }
}
