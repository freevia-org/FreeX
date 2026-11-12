using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

public sealed record FreeWFileCommandLifecyclePorts(
    Func<string?> CurrentPath,
    Func<string?> CurrentFileName,
    Func<string, Func<Task>, Task<bool>> NewAsync,
    Func<string, Func<Task<string?>>, Func<string, Task<bool>>, Task<bool>> OpenAsync,
    Func<Func<string, Task<bool>>, Func<Task<bool>>, Task<bool>> SaveAsync);

public sealed record FreeWDocumentOpenPickerRequest(string? InitialDirectory = null);

public sealed record FreeWDocumentSavePickerRequest(
    string Title,
    string? CurrentPath,
    string? CurrentFileName,
    string? SuggestedFileName = null,
    string? PreferredExtension = null);

public sealed record FreeWDocumentSavePickerResult(string Path, int FilterIndex = 0);

public sealed record FreeWDocumentFileCommandPorts(
    Func<Task> LoadNewDocumentAsync,
    Func<FreeWDocumentOpenPickerRequest, Task<string?>> PickOpenPathAsync,
    Func<Task<string?>> PickPdfImportPathAsync,
    Func<FreeWDocumentSavePickerRequest, Task<FreeWDocumentSavePickerResult?>> PickSaveTargetAsync,
    Action<FreeWDocumentFileFeedback> PresentFeedback);

/// <summary>
/// Owns FreeW's renderer-neutral file-command sequencing. Renderers retain their native dirty gate,
/// pickers, messages, and editor projection behind the supplied ports.
/// </summary>
public sealed class FreeWDocumentFileCommandSession
{
    private readonly FreeWDocumentFileWorkflow _workflow;
    private readonly FreeWFileCommandLifecyclePorts _lifecycle;
    private readonly FreeWDocumentFileCommandPorts _ports;
    private readonly SisterAppFileTextSpec _text;

    public FreeWDocumentFileCommandSession(
        FreeWDocumentFileWorkflow workflow,
        FreeWFileCommandLifecyclePorts lifecycle,
        FreeWDocumentFileCommandPorts ports,
        SisterAppFileTextSpec text)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public Task<bool> NewAsync() =>
        _lifecycle.NewAsync(_text.NewAction, _ports.LoadNewDocumentAsync);

    public Task<bool> OpenAsync(string? initialDirectory = null) =>
        _lifecycle.OpenAsync(
            _text.OpenAction,
            () => _ports.PickOpenPathAsync(new FreeWDocumentOpenPickerRequest(initialDirectory)),
            // File > Open on a template edits the template itself, as Word does.
            path => OpenPathAsync(path, templateMode: DocumentTemplateOpenMode.EditTemplate));

    public Task<bool> OpenSelectedPathAsync(string path) =>
        _lifecycle.OpenAsync(
            _text.OpenAction,
            () => Task.FromResult<string?>(path),
            // A template only reaches the recent list by having been edited or saved as a template,
            // so reopening it from there edits it again.
            selectedPath => OpenPathAsync(selectedPath, templateMode: DocumentTemplateOpenMode.EditTemplate));

    /// <summary>
    /// File &gt; New from an in-memory (built-in) template: dirty-gates, then loads
    /// <paramref name="createDocument"/>'s result as a new untitled document with no save path.
    /// </summary>
    public Task<bool> NewFromTemplateAsync(Func<TextDocument> createDocument)
    {
        ArgumentNullException.ThrowIfNull(createDocument);
        return _lifecycle.NewAsync(
            _text.NewAction,
            () => _workflow.LoadNewDocumentAsync(createDocument()));
    }

    /// <summary>
    /// File &gt; New from a template file (a personal .dotx/.dotm): dirty-gates, then creates a new
    /// untitled document based on it. The template itself is neither targeted for save nor added to
    /// the recent list.
    /// </summary>
    public Task<bool> NewFromTemplatePathAsync(string templatePath) =>
        _lifecycle.OpenAsync(
            _text.NewAction,
            () => Task.FromResult<string?>(templatePath),
            path => OpenPathAsync(path, suppressRecentFiles: true, DocumentTemplateOpenMode.NewDocument));

    /// <summary>
    /// Loads a specific path without a dirty gate (startup argument, drag-drop, double-click). A
    /// template opens as a new untitled document unless <paramref name="templateMode"/> says to edit it.
    /// </summary>
    public async Task<bool> OpenPathAsync(
        string path,
        bool suppressRecentFiles = false,
        DocumentTemplateOpenMode templateMode = DocumentTemplateOpenMode.NewDocument)
    {
        var execution = await _workflow.OpenPathAsync(path, suppressRecentFiles, templateMode: templateMode);
        return Present(FreeWDocumentFileFeedbackPlanner.PlanOpen(execution, path));
    }

    public Task<bool> ImportPdfTextAsync() =>
        _lifecycle.OpenAsync(
            FreeWDocumentFileFeedbackPlanner.ImportPdfAction,
            _ports.PickPdfImportPathAsync,
            ImportPdfTextPathAsync);

    public async Task<bool> ImportPdfTextPathAsync(string path)
    {
        var execution = await _workflow.ImportPdfTextPathAsync(path);
        return Present(FreeWDocumentFileFeedbackPlanner.PlanImport(execution, path));
    }

    public Task<bool> SaveAsync() =>
        _lifecycle.SaveAsync(SaveToCurrentPathAsync, SaveAsAsync);

    public Task<bool> SaveAsAsync() => SaveAsAsync(null, null);

    public Task<bool> SaveAsFormatAsync(string preferredExtension) =>
        SaveAsAsync(suggestedFileName: null, preferredExtension);

    public async Task<bool> SaveAsAsync(
        string? suggestedFileName,
        string? preferredExtension)
    {
        var selection = await _ports.PickSaveTargetAsync(new FreeWDocumentSavePickerRequest(
            _text.SavePickerTitle,
            _lifecycle.CurrentPath(),
            _lifecycle.CurrentFileName(),
            suggestedFileName,
            preferredExtension));
        return selection is not null
            && await SavePathAsync(
                selection.Path,
                selection.FilterIndex,
                DocumentSaveExecutionKind.Save);
    }

    public async Task<bool> SaveCopyAsync()
    {
        var selection = await _ports.PickSaveTargetAsync(new FreeWDocumentSavePickerRequest(
            FreeWDocumentFileFeedbackPlanner.SaveCopyCommand,
            _lifecycle.CurrentPath(),
            _lifecycle.CurrentFileName()));
        return selection is not null
            && await SavePathAsync(
                selection.Path,
                selection.FilterIndex,
                DocumentSaveExecutionKind.SaveCopy);
    }

    public async Task<bool> SaveToCurrentPathAsync(string path)
    {
        var execution = await _workflow.SaveCurrentPathAsync(path);
        var feedback = FreeWDocumentFileFeedbackPlanner.PlanSave(
            execution,
            DocumentSaveExecutionKind.Save,
            path);
        return feedback.RequiresSaveAs
            ? await SaveAsAsync()
            : Present(feedback);
    }

    public async Task<bool> SavePathAsync(
        string path,
        int filterIndex = 0,
        DocumentSaveExecutionKind kind = DocumentSaveExecutionKind.Save)
    {
        var execution = await _workflow.SavePathAsync(path, filterIndex, kind);
        return Present(FreeWDocumentFileFeedbackPlanner.PlanSave(execution, kind, path));
    }

    private bool Present(FreeWDocumentFileFeedback feedback)
    {
        _ports.PresentFeedback(feedback);
        return feedback.Succeeded;
    }
}
