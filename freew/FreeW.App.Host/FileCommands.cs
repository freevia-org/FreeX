using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeW.App.Host.Editing;
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
/// <see cref="OpenFileDialog"/>/<see cref="SaveFileDialog"/> for its single <c>.docx</c> format
/// (via the shared <see cref="FileDialogFilter"/>), the actual docx read/write, and the message
/// prompts. The dirty/path state and lifecycle ceremony live in the shared
/// <see cref="FileCommandWorkflow"/>.
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
    private readonly Action _onChanged;
    private readonly FileCommandWorkflow _workflow;

    // FreeW's persisted settings (shared JsonSettingsStore under %APPDATA%\FreeW). The recent-files cap
    // is read from here when registering a saved/opened file — a real read site that proves the options
    // mechanism end-to-end. Defaults are used when no store is supplied (e.g. tests).
    private readonly FreeWOptions _options;

    // FreeW's supported formats are data: a catalog of IDocumentFileAdapter drives the open/save dialogs and
    // the open/save dispatch, so adding a format is a catalog edit, not a string edit here.
    private readonly IReadOnlyList<IDocumentFileAdapter> _adapters;

    // Default extension used when there is no current file (new-document Save-As) and for AddExtension.
    private const string DefaultSaveExtension = ".docx";

    public FileCommands(
        Window window,
        DocumentView editor,
        Action onChanged,
        FreeWOptions? options = null,
        IReadOnlyList<IDocumentFileAdapter>? adapters = null,
        Func<RecentFilesStore>? loadRecentFilesStore = null)
    {
        _window = window;
        _editor = editor;
        _onChanged = onChanged;
        _options = options ?? new FreeWOptions();
        _adapters = adapters ?? DocumentFileAdapterCatalog.CreateDefaultAdapters();
        _workflow = new FileCommandWorkflow(
            () => _options.RecentFilesCap,
            _onChanged,
            PromptSaveChanges,
            Save,
            loadRecentFilesStore: loadRecentFilesStore);
    }

    public bool IsDirty => _workflow.IsDirty;

    /// <summary>Monotonic dirty-edit counter, used by autosave to suppress redundant snapshots.</summary>
    public int DirtyGeneration => _workflow.DirtyGeneration;

    public string? CurrentPath => _workflow.CurrentPath;

    public string DisplayName => _workflow.DisplayName;

    public IReadOnlyList<FileFormatDescriptor> SaveFormats =>
        _adapters.SelectMany(adapter => adapter.Formats).Where(format => format.CanSave).ToArray();

    /// <summary>Load a recovered autosave snapshot, targeting the original path and marking dirty.</summary>
    public void OpenSnapshot(string snapshotPath, string? originalPath)
    {
        OpenSnapshotCore(snapshotPath, originalPath);
    }

    public bool RecoverSnapshot(string snapshotPath, string? originalPath) =>
        _workflow.Open(
            "recovering an unsaved document",
            () => snapshotPath,
            path => OpenSnapshotCore(path, originalPath));

    private bool OpenSnapshotCore(string snapshotPath, string? originalPath)
    {
        try
        {
            _editor.LoadModel(DocxReader.Read(snapshotPath));
            _workflow.MarkDirtyWithPath(
                originalPath,
                () => _editor.CurrentFileName = originalPath is null ? null : Path.GetFileName(originalPath));
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
        _workflow.Open("opening another document", PromptOpenPath, OpenPath);

    /// <summary>
    /// Loads a specific path (recent-files click / drag-drop / startup). Does NOT dirty-gate: callers
    /// that bypass the dialog already chose to replace the document. Returns true on success.
    /// </summary>
    public bool OpenPath(string path) => OpenPath(path, suppressRecentFiles: false);

    private bool OpenPath(string path, bool suppressRecentFiles)
    {
        var extension = Path.GetExtension(path);
        var adapter = DocumentFileFormatResolver.FindOpenAdapter(_adapters, extension, out var format);
        if (adapter is null)
        {
            ShowError(
                "Unrecognized file type",
                new InvalidOperationException($"FreeW has no reader for “{extension}” files."));
            return false;
        }

        try
        {
            using (var fs = File.OpenRead(path))
                _editor.LoadModel(adapter.Load(fs));

            if (format!.OpensAsTemplate)
            {
                // A template seeds a new untitled document: clear the path so the next Save becomes Save-As.
                _workflow.MarkSavedWithoutPath(() => _editor.CurrentFileName = null);
            }
            else
            {
                SetSaved(path, suppressRecentFiles);
            }

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
        TryPromptSaveTarget(preferredExtension, out var path, out var adapter) && SaveTo(path, adapter);

    /// <summary>
    /// File &gt; Save a Copy. Writes to a chosen path WITHOUT changing the current file or dirty state,
    /// reusing the same resolver + adapter plumbing as Save-As. Returns true on a successful save.
    /// </summary>
    public bool SaveCopy()
    {
        if (!TryPromptSaveTarget(preferredExtension: null, out var path, out var adapter))
            return false;
        try
        {
            _editor.CommitToModel();
            using var fs = File.Create(path);
            adapter.Save(_editor.Model, fs);
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
        var adapter = DocumentFileFormatResolver.FindSaveAdapter(_adapters, Path.GetExtension(path), out _);
        return adapter is null ? SaveAs() : SaveTo(path, adapter);
    }

    private bool SaveTo(string path, IDocumentFileAdapter adapter)
    {
        try
        {
            _editor.CommitToModel();
            using (var fs = File.Create(path))
                adapter.Save(_editor.Model, fs);
            SetSaved(path, suppressRecentFiles: false);
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
    private bool TryPromptSaveTarget(string? preferredExtension, out string path, out IDocumentFileAdapter adapter)
    {
        path = "";
        adapter = null!;

        var normalizedPreferred = DocumentFileFormatResolver.NormalizeExtension(preferredExtension ?? string.Empty);
        var currentExtension = normalizedPreferred.Length > 0
            ? normalizedPreferred
            : _workflow.CurrentPath is { } existing
            ? Path.GetExtension(existing)
            : DefaultSaveExtension;
        var fileName = _workflow.CurrentPath is null
            ? "Document" + currentExtension
            : Path.GetFileName(_workflow.CurrentPath);
        if (normalizedPreferred.Length > 0)
            fileName = Path.ChangeExtension(fileName, normalizedPreferred);

        var dialog = new SaveFileDialog
        {
            Filter = DocumentFileDialogFilterBuilder.BuildSaveFilter(_adapters),
            FilterIndex = DocumentFileDialogFilterBuilder.FindSaveFilterIndex(_adapters, currentExtension),
            DefaultExt = currentExtension,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = fileName,
        };
        if (dialog.ShowDialog(_window) != true)
            return false;

        var chosenExtension = Path.GetExtension(dialog.FileName);
        var resolved = DocumentFileFormatResolver.FindSaveAdapter(_adapters, chosenExtension, out _);
        if (resolved is null)
        {
            ShowError(
                "Cannot save",
                new InvalidOperationException($"“{chosenExtension}” is not a writable format."));
            return false;
        }

        path = dialog.FileName;
        adapter = resolved;
        return true;
    }

    private string? PromptOpenPath()
    {
        var dialog = new OpenFileDialog { Filter = DocumentFileDialogFilterBuilder.BuildOpenFilter(_adapters) };
        return dialog.ShowDialog(_window) == true ? dialog.FileName : null;
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
    // The planner decides; these execute the I/O effects on the WPF host. Any other platform would
    // supply its own implementations (e.g. Avalonia pickers / message dialogs).

    private SaveChangesPrompt PromptSaveChanges(string action)
        => FileCommandMessageBox.PromptSaveChanges(_window, DisplayName, action, "FreeW");

    private void ShowError(string summary, Exception ex) =>
        FileCommandMessageBox.ShowError(_window, summary, ex, "FreeW");
}
