using System.IO;
using System.Windows;
using Microsoft.Win32;
using Free.Shared.AppServices;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's File lifecycle: New / Open / Save / Save As / Close over the stub <c>.fxp</c> reader+writer.
///
/// <para>
/// The file-lifecycle <em>ceremony</em> — the dirty-gate before destructive actions, the Save-vs-Save-As
/// resolution, and recent-files registration — is decided by the shared, neutral
/// <see cref="FileLifecyclePlanner"/>. FreeP supplies only the thin host side: the native
/// <see cref="OpenFileDialog"/>/<see cref="SaveFileDialog"/> for its single <c>.fxp</c> format (via the shared
/// <see cref="FileDialogFilter"/>), the actual <c>.fxp</c> read/write, and the message prompts. Dirty/path
/// state lives in the shared <see cref="WorkbookDocumentState"/>; recent files in the shared
/// <see cref="RecentFilesStore"/>. Mirrors FreeW.FileCommands exactly (FreeW already adopted these seams).
/// </para>
///
/// <para>
/// FreeP has no live editor surface yet, so the host exposes the current model through a getter and accepts a
/// freshly loaded model through a loader callback (the placeholder canvas re-renders on change). The next
/// session swaps these seams for a real slide editor.
/// </para>
/// </summary>
internal sealed class FileCommands
{
    private readonly Window _window;
    private readonly Func<Presentation> _getModel;
    private readonly Action<Presentation> _loadModel;
    private readonly Action _onChanged;
    private readonly WorkbookDocumentState _state = new();
    private readonly FreePOptions _options;

    // FreeP ships a single .fxp format; the filter/default-extension are composed by the shared
    // FileDialogFilter so any future format additions stay a data change, not a string edit.
    private static readonly IReadOnlyList<FileFormatChoice> Formats =
        [new FileFormatChoice("FreeP presentations", FxpFormat.Extension)];

    private static readonly string Filter = FileDialogFilter.Build(Formats);
    private static readonly string DefaultExtension = FileDialogFilter.DefaultExtension(Formats);

    public FileCommands(
        Window window,
        Func<Presentation> getModel,
        Action<Presentation> loadModel,
        Action onChanged,
        FreePOptions? options = null)
    {
        _window = window;
        _getModel = getModel;
        _loadModel = loadModel;
        _onChanged = onChanged;
        _options = options ?? new FreePOptions();
    }

    public bool IsDirty => _state.IsDirty;

    public string? CurrentPath => _state.CurrentFilePath;

    public string DisplayName =>
        _state.CurrentFilePath is null ? "Untitled" : Path.GetFileNameWithoutExtension(_state.CurrentFilePath);

    public void MarkDirty()
    {
        if (_state.IsDirty)
            return;
        _state.MarkDirty();
        _onChanged();
    }

    /// <summary>File &gt; New. Dirty-gates so unsaved work is not silently lost. Returns false on cancel.</summary>
    public bool New()
    {
        if (!ConfirmDiscardOrSave("creating a new presentation"))
            return false;

        _loadModel(Presentation.CreateEmpty());
        _state.ClearCurrentFilePath();
        _state.MarkSaved();
        _onChanged();
        return true;
    }

    /// <summary>File &gt; Open. Dirty-gates, then shows the open dialog and loads the chosen file.</summary>
    public bool Open()
    {
        if (!ConfirmDiscardOrSave("opening another presentation"))
            return false;

        var dialog = new OpenFileDialog { Filter = Filter, DefaultExt = DefaultExtension };
        if (dialog.ShowDialog(_window) != true)
            return false;

        return OpenPath(dialog.FileName);
    }

    /// <summary>Loads a specific path (recent-files click / startup). Does NOT dirty-gate.</summary>
    public bool OpenPath(string path) => OpenPath(path, suppressRecentFiles: false);

    private bool OpenPath(string path, bool suppressRecentFiles)
    {
        try
        {
            _loadModel(FxpFormat.Read(path));
            SetSaved(path, suppressRecentFiles);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not open the presentation", ex);
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

    /// <summary>File &gt; Save. Resolves Save-vs-Save-As via the shared planner.</summary>
    public bool Save() => FileLifecyclePlanner.PlanSave(_state.IsDirty, _state.CurrentFilePath) switch
    {
        FileSaveIntent.UseExistingPath => SaveTo(_state.CurrentFilePath!),
        FileSaveIntent.NothingToDo => SaveTo(_state.CurrentFilePath!),
        _ => SaveAs(),
    };

    /// <summary>File &gt; Save As. Always prompts for a target.</summary>
    public bool SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = Filter,
            DefaultExt = DefaultExtension,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = _state.CurrentFilePath is null
                ? "Presentation" + DefaultExtension
                : Path.GetFileName(_state.CurrentFilePath)
        };
        return dialog.ShowDialog(_window) == true && SaveTo(dialog.FileName);
    }

    /// <summary>Save-before-close gate, called from the window's Closing handler.</summary>
    public bool ConfirmCloseAllowed() => ConfirmDiscardOrSave("closing");

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
            FxpFormat.Write(_getModel(), path);
            SetSaved(path, suppressRecentFiles: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not save the presentation", ex);
            return false;
        }
    }

    private void SetSaved(string path, bool suppressRecentFiles)
    {
        _state.MarkSavedWithPath(path);

        if (FileLifecyclePlanner.PlanRecentRegistration(path, suppressRecentFiles) == RecentFileRegistration.Register)
        {
            try
            {
                // Honour the user-configured recent-files cap (FreePOptions.RecentFilesCap) — a real read site
                // for the shared options mechanism. The shared store still always retains pinned items.
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
    private SaveChangesPrompt PromptSaveChanges(string action)
    {
        var result = MessageBox.Show(
            _window,
            $"Do you want to save changes to {DisplayName} before {action}?",
            "FreeP",
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
        MessageBox.Show(_window, $"{summary}:\n{ex.Message}", "FreeP", MessageBoxButton.OK, MessageBoxImage.Error);
}
