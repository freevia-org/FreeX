using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Shell;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's File lifecycle: New / Open / Save / Save As / Close over native <c>.pptx</c> packages.
///
/// <para>
/// The file-lifecycle <em>ceremony</em> — the dirty-gate before destructive actions, the Save-vs-Save-As
/// resolution, and recent-files registration — is decided by the shared, neutral
/// <see cref="FileLifecyclePlanner"/>. FreeP supplies only the thin host side: the native
/// <see cref="OpenFileDialog"/>/<see cref="SaveFileDialog"/> for <c>.pptx</c> plus legacy <c>.fxp</c> compatibility
/// (via the shared <see cref="FileDialogRequestPlanner"/>), the actual read/write, and the message prompts. Dirty/path
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

    private const string PptxExtension = ".pptx";

    // FreeP's native document format is .pptx. .fxp remains open/save compatible for legacy tests and files.
    private static readonly IReadOnlyList<FileDialogFormatDescriptor> Formats =
    [
        new FileDialogFormatDescriptor(PptxExtension, "PowerPoint presentations"),
        new FileDialogFormatDescriptor(FxpFormat.Extension, "FreeP legacy presentations"),
    ];

    // Export-only target: PDF is a fixed-layout publish format, not a FreeP document format.
    private static readonly IReadOnlyList<FileDialogFormatDescriptor> PdfFormats =
        [new FileDialogFormatDescriptor(".pdf", "PDF documents")];

    private static readonly FileOpenDialogPlan OpenDialogPlan =
        FileDialogRequestPlanner.BuildPerFormatOpenDialogPlan(Formats);

    private static string DefaultExtension => OpenDialogPlan.DefaultExtensionWithDot;

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
            _loadModel(ReadPresentation(path));
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
        var plan = BuildSaveAsDialogPlan(_workflow.CurrentPath);
        var result = WpfFileDialogService.ShowSaveDialog(_window, plan);
        return result.Chosen && SaveTo(result.FileName!);
    }

    /// <summary>
    /// File &gt; Export to PDF. Prompts for a target and writes a fixed-layout PDF (one page per slide) via the
    /// shared portable PDF tier. Does not change the dirty/saved state (the presentation document is the source of record).
    /// </summary>
    public bool ExportPdf()
    {
        var sourceName = _workflow.CurrentPath is { } current ? Path.GetFileName(current) : null;
        var plan = FileDialogRequestPlanner.BuildPerFormatSaveDialogPlanFromSourceName(
            PdfFormats, sourceName, "Presentation", ".pdf");
        var result = WpfFileDialogService.ShowSaveDialog(_window, plan);
        if (!result.Chosen)
            return false;

        try
        {
            var bytes = PresentationPdfExporter.ExportToBytes(_getModel());
            ExportAtomicWriter.WriteAllBytes(result.FileName!, bytes);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not export the presentation to PDF", ex);
            return false;
        }
    }

    /// <summary>Save-before-close gate, called from the window's Closing handler.</summary>
    public bool ConfirmCloseAllowed() => _workflow.ConfirmCloseAllowed();

    private bool SaveTo(string path)
    {
        try
        {
            // Write through a sibling temp file and atomically replace the target, mirroring the
            // ExportPdf path — so a mid-write failure (disk full, serialization error, AV lock)
            // never truncates the previously-saved presentation.
            ExportAtomicWriter.WriteAllBytes(path, SerializePresentation(path, _getModel()));
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

    private static Presentation ReadPresentation(string path) =>
        IsLegacyFxpPath(path)
            ? FxpFormat.Read(path)
            : PptxPackageReader.Read(path);

    private static byte[] SerializePresentation(string path, Presentation presentation)
    {
        if (IsLegacyFxpPath(path))
        {
            var json = FxpFormat.Serialize(presentation);
            return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        }

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static bool IsLegacyFxpPath(string path) =>
        string.Equals(Path.GetExtension(path), FxpFormat.Extension, StringComparison.OrdinalIgnoreCase);

    private string? PromptOpenPath()
    {
        var result = WpfFileDialogService.ShowOpenDialog(_window, OpenDialogPlan);
        return result.Chosen ? result.FileName : null;
    }

    private static FileSaveDialogPlan BuildSaveAsDialogPlan(string? currentPath) =>
        FileDialogRequestPlanner.BuildPerFormatSaveDialogPlanFromSourceName(
            Formats,
            currentPath is null ? null : Path.GetFileName(currentPath),
            "Presentation",
            DefaultExtension);

    // ── Host seams (WPF) ─────────────────────────────────────────────────────
    private SaveChangesPrompt PromptSaveChanges(string action)
        => FileCommandMessageBox.PromptSaveChanges(_window, DisplayName, action, "FreeP");

    private void ShowError(string summary, Exception ex) =>
        FileCommandMessageBox.ShowError(_window, summary, ex, "FreeP");
}
