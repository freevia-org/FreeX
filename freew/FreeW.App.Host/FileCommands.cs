using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Free.Shared.AppServices;
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
/// prompts. The dirty/path state lives in the shared <see cref="WorkbookDocumentState"/>, replacing
/// the hand-rolled <c>IsDirty</c> bool and <c>_currentPath</c> field this class used to carry.
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
    private readonly WorkbookDocumentState _state = new();

    // FreeW's persisted settings (shared JsonSettingsStore under %APPDATA%\FreeW). The recent-files cap
    // is read from here when registering a saved/opened file — a real read site that proves the options
    // mechanism end-to-end. Defaults are used when no store is supplied (e.g. tests).
    private readonly FreeWOptions _options;

    // FreeW ships a single .docx format; the filter/default-extension are composed by the shared
    // FileDialogFilter so any future format additions stay a data change, not a string edit.
    private static readonly IReadOnlyList<FileFormatChoice> Formats =
        [new FileFormatChoice("Word documents", ".docx")];

    private static readonly string Filter = FileDialogFilter.Build(Formats);
    private static readonly string DefaultExtension = FileDialogFilter.DefaultExtension(Formats);

    public FileCommands(Window window, DocumentView editor, Action onChanged, FreeWOptions? options = null)
    {
        _window = window;
        _editor = editor;
        _onChanged = onChanged;
        _options = options ?? new FreeWOptions();
    }

    public bool IsDirty => _state.IsDirty;

    public string? CurrentPath => _state.CurrentFilePath;

    public string DisplayName =>
        _state.CurrentFilePath is null ? "Untitled" : Path.GetFileNameWithoutExtension(_state.CurrentFilePath);

    /// <summary>Load a recovered autosave snapshot, targeting the original path and marking dirty.</summary>
    public void OpenSnapshot(string snapshotPath, string? originalPath)
    {
        try
        {
            _editor.LoadModel(DocxReader.Read(snapshotPath));
            _state.SetCurrentFilePath(originalPath);
            _state.MarkDirty();
            _editor.CurrentFileName = originalPath is null ? null : Path.GetFileName(originalPath);
            _onChanged();
        }
        catch (Exception ex)
        {
            ShowError("Could not recover the document", ex);
        }
    }

    public void MarkDirty()
    {
        if (_state.IsDirty)
            return;
        _state.MarkDirty();
        _onChanged();
    }

    /// <summary>
    /// File &gt; New. Routes through the shared dirty-gate so unsaved work is not silently lost
    /// (previously FreeW dropped changes without prompting). Returns false if the user cancels.
    /// </summary>
    public bool New()
    {
        if (!ConfirmDiscardOrSave("creating a new document"))
            return false;

        _editor.LoadModel(TextDocument.CreateEmpty());
        _state.ClearCurrentFilePath();
        _state.MarkSaved();
        _editor.CurrentFileName = null;
        _onChanged();
        return true;
    }

    /// <summary>
    /// File &gt; Open. Dirty-gates first, then shows the open dialog and loads the chosen file.
    /// Returns false if the user cancels at either step.
    /// </summary>
    public bool Open()
    {
        if (!ConfirmDiscardOrSave("opening another document"))
            return false;

        var dialog = new OpenFileDialog { Filter = Filter, DefaultExt = DefaultExtension };
        if (dialog.ShowDialog(_window) != true)
            return false;

        return OpenPath(dialog.FileName);
    }

    /// <summary>
    /// Loads a specific path (recent-files click / drag-drop / startup). Does NOT dirty-gate: callers
    /// that bypass the dialog already chose to replace the document. Returns true on success.
    /// </summary>
    public bool OpenPath(string path) => OpenPath(path, suppressRecentFiles: false);

    private bool OpenPath(string path, bool suppressRecentFiles)
    {
        try
        {
            _editor.LoadModel(DocxReader.Read(path));
            SetSaved(path, suppressRecentFiles);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not open the document", ex);
            return false;
        }
    }

    /// <summary>Recent files (most recent first) from the shared store; never throws.</summary>
    public IReadOnlyList<RecentFileEntry> RecentEntries
    {
        get
        {
            try
            {
                return RecentFilesStore.Load().Entries;
            }
            catch
            {
                return Array.Empty<RecentFileEntry>();
            }
        }
    }

    /// <summary>
    /// File &gt; Save. Resolves Save-vs-Save-As via the shared planner: writes to the existing path
    /// when there is one, otherwise falls through to Save-As. Returns true on a successful (or no-op)
    /// save, false on cancel/error.
    /// </summary>
    public bool Save() => FileLifecyclePlanner.PlanSave(_state.IsDirty, _state.CurrentFilePath) switch
    {
        FileSaveIntent.UseExistingPath => SaveTo(_state.CurrentFilePath!),
        FileSaveIntent.NothingToDo => SaveTo(_state.CurrentFilePath!),
        _ => SaveAs(),
    };

    /// <summary>File &gt; Save As. Always prompts for a target. Returns true on a successful save.</summary>
    public bool SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = Filter,
            DefaultExt = DefaultExtension,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = _state.CurrentFilePath is null ? "Document" + DefaultExtension : Path.GetFileName(_state.CurrentFilePath)
        };
        return dialog.ShowDialog(_window) == true && SaveTo(dialog.FileName);
    }

    /// <summary>
    /// Save-before-close gate, called from the window's Closing handler. Returns true if the window
    /// may close (clean, saved, or the user chose Don't&#160;Save) and false to cancel the close.
    /// This is a behaviour <em>addition</em>: FreeW previously closed without prompting on unsaved work.
    /// </summary>
    public bool ConfirmCloseAllowed() => ConfirmDiscardOrSave("closing");

    /// <summary>
    /// Shared dirty-gate. Returns true when the destructive action may proceed (clean, or the user
    /// saved / chose to discard), false when the user cancels or a required save fails.
    /// </summary>
    private bool ConfirmDiscardOrSave(string action)
    {
        if (FileLifecyclePlanner.PlanDirtyGate(_state.IsDirty) == DirtyGateIntent.ProceedWithoutPrompt)
            return true;

        var answer = PromptSaveChanges(action);
        return FileLifecyclePlanner.ResolveDirtyGate(answer) switch
        {
            DirtyGateAction.Cancel => false,
            DirtyGateAction.ProceedDiscardingChanges => true,
            DirtyGateAction.SaveThenProceed => Save(),
            _ => false,
        };
    }

    private bool SaveTo(string path)
    {
        try
        {
            _editor.CommitToModel();
            DocxWriter.Write(_editor.Model, path);
            SetSaved(path, suppressRecentFiles: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not save the document", ex);
            return false;
        }
    }

    private void SetSaved(string path, bool suppressRecentFiles)
    {
        _state.MarkSavedWithPath(path);
        // Surface the file name to the editor so FILENAME field runs resolve to it at render.
        _editor.CurrentFileName = Path.GetFileName(path);

        if (FileLifecyclePlanner.PlanRecentRegistration(path, suppressRecentFiles) == RecentFileRegistration.Register)
        {
            try
            {
                // Honour the user-configured recent-files cap (FreeWOptions.RecentFilesCap) — a real read
                // site for the shared options mechanism. The shared store still always retains pinned items.
                RecentFilesStore.Load().AddOrUpdate(path, _options.RecentFilesCap);
            }
            catch
            {
                // Recent-files tracking is best-effort; never block a save/open on it.
            }
        }

        _onChanged();
    }

    // ── Host seams (WPF) ─────────────────────────────────────────────────────
    // The planner decides; these execute the I/O effects on the WPF host. Any other platform would
    // supply its own implementations (e.g. Avalonia pickers / message dialogs).

    private SaveChangesPrompt PromptSaveChanges(string action)
    {
        var result = MessageBox.Show(
            _window,
            $"Do you want to save changes to {DisplayName} before {action}?",
            "FreeW",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => SaveChangesPrompt.Save,
            MessageBoxResult.No => SaveChangesPrompt.DontSave,
            _ => SaveChangesPrompt.Cancel,
        };
    }

    private void ShowError(string summary, Exception ex) =>
        MessageBox.Show(_window, $"{summary}:\n{ex.Message}", "FreeW", MessageBoxButton.OK, MessageBoxImage.Error);
}
