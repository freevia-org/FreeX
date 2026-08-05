namespace FreeW.App.Presentation.Dialogs;

public interface IFindReplaceDialogCommandHost
{
    bool FindNext(FindReplaceSearchRequest request);

    bool ReplaceNext(FindReplaceReplaceRequest request);

    FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request);
}

public readonly record struct FindReplaceAllExecutionResult(
    int ReplacementCount,
    bool InSelection = false);

public sealed record FindReplaceDialogState(
    FindReplaceDialogOpenMode OpenMode,
    string Query,
    string Replacement,
    FindReplaceSearchOptions Options,
    bool WholeWordEnabled,
    string StatusText);

/// <summary>
/// Owns renderer-neutral state and command sequencing for the modeless find/replace dialog.
/// Hosts retain native controls, focus, selection, and document traversal behind the command adapter.
/// </summary>
public sealed class FindReplaceDialogSession
{
    private readonly IFindReplaceDialogCommandHost _commandHost;
    private FindReplaceDialogOpenMode _openMode;
    private string _query = string.Empty;
    private string _replacement = string.Empty;
    private FindReplaceSearchOptions _options;
    private string _statusText = string.Empty;

    public FindReplaceDialogSession(
        IFindReplaceDialogCommandHost commandHost,
        FindReplaceDialogOpenMode openMode = FindReplaceDialogOpenMode.Find)
    {
        _commandHost = commandHost ?? throw new ArgumentNullException(nameof(commandHost));
        _openMode = openMode;
    }

    public FindReplaceDialogState State => BuildState();

    public FindReplaceDialogState ActivateFor(FindReplaceDialogOpenMode openMode)
    {
        _openMode = openMode;
        return BuildState();
    }

    public FindReplaceDialogState SetInput(
        string? query,
        string? replacement,
        bool matchCase,
        bool wholeWord,
        bool useWildcards)
    {
        _query = query ?? string.Empty;
        _replacement = replacement ?? string.Empty;
        _options = FindReplaceDialogPlanner.NormalizeOptions(new FindReplaceSearchOptions(
            matchCase,
            wholeWord,
            useWildcards));
        return BuildState();
    }

    public FindReplaceDialogState FindNext()
    {
        if (!FindReplaceDialogPlanner.TryCreateSearchRequest(
                _query,
                _options,
                out var request,
                out var error))
        {
            return SetStatus(FindReplaceDialogPlanner.ValidationMessageFor(error));
        }

        var found = _commandHost.FindNext(request!);
        return SetStatus(FindReplaceDialogPlanner.BuildFindStatus(request!, found));
    }

    public FindReplaceDialogState ReplaceNext()
    {
        if (!TryCreateReplaceRequest(out var request, out var error))
            return SetStatus(FindReplaceDialogPlanner.ValidationMessageFor(error));

        var found = _commandHost.ReplaceNext(request!);
        return SetStatus(FindReplaceDialogPlanner.BuildReplaceStatus(request!, found));
    }

    public FindReplaceDialogState ReplaceAll()
    {
        if (!TryCreateReplaceRequest(out var request, out var error))
            return SetStatus(FindReplaceDialogPlanner.ValidationMessageFor(error));

        var result = _commandHost.ReplaceAll(request!);
        return SetStatus(FindReplaceDialogPlanner.BuildReplaceAllStatus(
            request!,
            result.ReplacementCount,
            result.InSelection));
    }

    public FindReplaceDialogState SetStatus(string? statusText)
    {
        _statusText = statusText ?? string.Empty;
        return BuildState();
    }

    private bool TryCreateReplaceRequest(
        out FindReplaceReplaceRequest? request,
        out FindReplaceValidationError? error) =>
        FindReplaceDialogPlanner.TryCreateReplaceRequest(
            _query,
            _replacement,
            _options,
            out request,
            out error);

    private FindReplaceDialogState BuildState() => new(
        _openMode,
        _query,
        _replacement,
        _options,
        FindReplaceDialogPlanner.IsOptionEnabled(FindReplaceOptionKind.WholeWord, _options),
        _statusText);
}
