using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Microsoft.Win32;

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
    private readonly SisterWpfFileCommandWorkflow _workflow;
    private readonly FreePOptions _options;
    private readonly Func<PresentationSlideRangeRequest?> _getImageExportRange;

    private static readonly FileOpenDialogPlan OpenDialogPlan =
        PresentationFileDialogPlanner.BuildOpenDialogPlan();

    public FileCommands(
        Window window,
        Func<Presentation> getModel,
        Action<Presentation> loadModel,
        Action onChanged,
        FreePOptions? options = null,
        Func<RecentFilesStore>? loadRecentFilesStore = null,
        IUserMessageService? messageService = null,
        Func<PresentationSlideRangeRequest?>? getImageExportRange = null)
    {
        _window = window;
        _getModel = getModel;
        _loadModel = loadModel;
        _options = options ?? new FreePOptions();
        _getImageExportRange = getImageExportRange ?? (() => null);
        _workflow = new SisterWpfFileCommandWorkflow(
            "FreeP",
            () => _options.RecentFilesCap,
            onChanged,
            Save,
            loadRecentFilesStore,
            messageService);
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
            var result = PresentationFilePersistenceWorkflow.Open(path);
            _loadModel(result.Presentation);
            SetSaved(result.SavedPath, suppressRecentFiles || result.SuppressRecentFiles);
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
        var plan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan(_workflow.CurrentFileName);
        var result = WpfFileDialogService.ShowSaveDialog(_window, plan);
        return result.Chosen && SaveTo(result.FileName!);
    }

    /// <summary>
    /// File &gt; Export to PDF. Prompts for a target and writes a fixed-layout PDF (one page per slide) via the
    /// shared portable PDF tier. Does not change the dirty/saved state (the presentation document is the source of record).
    /// </summary>
    public bool ExportPdf()
    {
        var plan = PresentationExportPlanner.BuildPdfExportDialogPlan(_workflow.CurrentFileName);
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

    /// <summary>
    /// File &gt; Export &gt; Notes Page PDF. Prompts for a target and writes one speaker-notes page per selected slide.
    /// </summary>
    public bool ExportNotesPagePdf(PresentationSlideRangeRequest? range = null)
    {
        var presentation = _getModel();
        var exportPlan = PresentationExportPlanner.BuildNotesPagePdfExportPlan(range, presentation.Slides.Count);
        if (!exportPlan.CanExecute)
            return false;

        var savePlan = PresentationExportPlanner.BuildNotesPagePdfExportDialogPlan(_workflow.CurrentFileName);
        var result = WpfFileDialogService.ShowSaveDialog(_window, savePlan);
        if (!result.Chosen)
            return false;

        try
        {
            var request = new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                range));
            var bytes = PresentationNotesPagePdfExporter.ExportToBytes(presentation, request);
            ExportAtomicWriter.WriteAllBytes(result.FileName!, bytes);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not export the presentation notes pages to PDF", ex);
            return false;
        }
    }

    /// <summary>
    /// File &gt; Export &gt; Images. Prompts for a folder, then exports the host-selected slide range.
    /// </summary>
    public bool ExportImages()
    {
        var outputDirectory = PromptImageExportFolder();
        return outputDirectory is not null && ExportImagesToFolder(outputDirectory, _getImageExportRange());
    }

    /// <summary>
    /// Exports one PNG per requested slide to an already chosen folder. The host owns folder picking;
    /// shared code owns PowerPoint-style range policy, naming, and atomic writes.
    /// </summary>
    public bool ExportImagesToFolder(string outputDirectory, PresentationSlideRangeRequest? range = null)
    {
        try
        {
            PresentationImageExportExecutor.Export(
                _getModel(),
                new PresentationImageExportRequest(
                    outputDirectory,
                    BaseFileName: Path.GetFileNameWithoutExtension(_workflow.CurrentFileName),
                    SlideRange: range),
                WpfPresentationSlideImageRenderer.RenderSlideToPng);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not export the presentation slides to images", ex);
            return false;
        }
    }

    /// <summary>
    /// Builds the shared PowerPoint-style handout page plan. WPF owns native print/preview UI later;
    /// page slots and range policy stay in the shared presentation planner.
    /// </summary>
    public PresentationHandoutLayoutPlan BuildHandoutLayoutPlan(
        int? slidesPerPage = null,
        PresentationSlideRangeRequest? range = null)
    {
        var presentation = _getModel();
        return PresentationExportPlanner.BuildHandoutLayoutPlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                range,
                HandoutSlidesPerPage: slidesPerPage),
            presentation.Slides.Count,
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu);
    }

    /// <summary>
    /// Builds the shared notes-page PDF render plan. WPF owns native print/export UI later;
    /// notes-page range, geometry, slide thumbnail placement, and speaker-note drawing stay shared.
    /// </summary>
    public PresentationNotesPagePdfRenderPlan BuildNotesPagePdfRenderPlan(
        PresentationSlideRangeRequest? range = null)
    {
        var presentation = _getModel();
        return PresentationNotesPagePdfExporter.BuildRenderPlan(
            presentation,
            new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                range)));
    }

    /// <summary>
    /// Builds the shared PowerPoint-style video export workflow plan. WPF owns native encoder/media integration
    /// later; slide-range, quality, timing, narration, and deferred execution state stay shared.
    /// </summary>
    public PresentationVideoExportPlan BuildVideoExportPlan(PresentationVideoExportRequest? request = null)
    {
        var presentation = _getModel();
        return PresentationExportPlanner.BuildVideoExportPlan(request, presentation.Slides.Count);
    }

    /// <summary>Save-before-close gate, called from the window's Closing handler.</summary>
    public bool ConfirmCloseAllowed() => _workflow.ConfirmCloseAllowed();

    private bool SaveTo(string path)
    {
        try
        {
            var result = PresentationFilePersistenceWorkflow.Save(path, _getModel());
            SetSaved(result.SavedPath, result.SuppressRecentFiles);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not save the presentation", ex);
            return false;
        }
    }

    private void SetSaved(string? path, bool suppressRecentFiles)
    {
        if (path is null)
            _workflow.MarkSavedWithoutPath();
        else
            _workflow.MarkSavedWithPath(path, suppressRecentFiles);
    }

    private string? PromptOpenPath()
    {
        var result = WpfFileDialogService.ShowOpenDialog(_window, OpenDialogPlan);
        return result.Chosen ? result.FileName : null;
    }

    private string? PromptImageExportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = PresentationExportPlanner.ImageExportPickerTitle,
            Multiselect = false,
        };

        var currentDirectory = _workflow.CurrentPath is null
            ? null
            : Path.GetDirectoryName(_workflow.CurrentPath);
        if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
            dialog.InitialDirectory = currentDirectory;

        return dialog.ShowDialog(_window) == true && !string.IsNullOrWhiteSpace(dialog.FolderName)
            ? dialog.FolderName
            : null;
    }

    // ── Host seams (WPF) ─────────────────────────────────────────────────────
    private void ShowError(string summary, Exception ex) =>
        _workflow.ShowError(summary, ex);
}
