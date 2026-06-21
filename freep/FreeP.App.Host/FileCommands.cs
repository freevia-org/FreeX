using System.IO;
using System.Windows;
using Microsoft.Win32;
using Free.Shared.AppServices;
using Free.Shared.Shell;
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
/// state and lifecycle ceremony live in the shared <see cref="FileCommandWorkflow"/>; recent files in the
/// shared <see cref="RecentFilesStore"/>. Mirrors FreeW.FileCommands exactly (FreeW already adopted these seams).
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
    private readonly FileCommandWorkflow _workflow;
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
        FreePOptions? options = null,
        Func<RecentFilesStore>? loadRecentFilesStore = null)
    {
        _window = window;
        _getModel = getModel;
        _loadModel = loadModel;
        _onChanged = onChanged;
        _options = options ?? new FreePOptions();
        _workflow = new FileCommandWorkflow(
            () => _options.RecentFilesCap,
            _onChanged,
            PromptSaveChanges,
            Save,
            loadRecentFilesStore: loadRecentFilesStore);
    }

    public bool IsDirty => _workflow.IsDirty;

    public string? CurrentPath => _workflow.CurrentPath;

    public string DisplayName => _workflow.DisplayName;

    public void MarkDirty()
    {
        _workflow.MarkDirty();
    }

    /// <summary>File &gt; New. Dirty-gates so unsaved work is not silently lost. Returns false on cancel.</summary>
    public bool New() =>
        _workflow.New("creating a new presentation", () => _loadModel(Presentation.CreateEmpty()));

    /// <summary>File &gt; Open. Dirty-gates, then shows the open dialog and loads the chosen file.</summary>
    public bool Open() =>
        _workflow.Open("opening another presentation", PromptOpenPath, OpenPath);

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
    public IReadOnlyList<RecentFileEntry> RecentEntries => _workflow.RecentEntries;

    /// <summary>File &gt; Save. Resolves Save-vs-Save-As via the shared planner.</summary>
    public bool Save() => _workflow.Save(SaveTo, SaveAs);

    /// <summary>File &gt; Save As. Always prompts for a target.</summary>
    public bool SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = Filter,
            DefaultExt = DefaultExtension,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = _workflow.CurrentPath is null
                ? "Presentation" + DefaultExtension
                : Path.GetFileName(_workflow.CurrentPath)
        };
        return dialog.ShowDialog(_window) == true && SaveTo(dialog.FileName);
    }

    /// <summary>Save-before-close gate, called from the window's Closing handler.</summary>
    public bool ConfirmCloseAllowed() => _workflow.ConfirmCloseAllowed();

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
        _workflow.MarkSavedWithPath(path, suppressRecentFiles);
    }

    private string? PromptOpenPath()
    {
        var dialog = new OpenFileDialog { Filter = Filter, DefaultExt = DefaultExtension };
        return dialog.ShowDialog(_window) == true ? dialog.FileName : null;
    }

    // ── Host seams (WPF) ─────────────────────────────────────────────────────
    private SaveChangesPrompt PromptSaveChanges(string action)
        => FileCommandMessageBox.PromptSaveChanges(_window, DisplayName, action, "FreeP");

    private void ShowError(string summary, Exception ex) =>
        FileCommandMessageBox.ShowError(_window, summary, ex, "FreeP");
}
