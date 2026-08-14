using Free.Shared.AppServices;
using FreeW.Core.Model;

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
    FindReplaceOpenMode OpenMode,
    string Query,
    string Replacement,
    FindReplaceSearchOptions Options,
    bool WholeWordEnabled,
    string StatusText);

public readonly record struct FindReplaceDialogInput(
    string? Query,
    string? Replacement,
    bool MatchCase,
    bool WholeWord,
    bool UseWildcards);

public sealed record FindReplaceTextInsertionPlan(
    string Text,
    int CaretIndex);

public sealed record FindReplaceGoToTargetsPlan(
    IReadOnlyList<FindReplaceGoToTarget> Targets,
    int SelectedIndex);

/// <summary>
/// Owns renderer-neutral state and command sequencing for the modeless find/replace dialog.
/// Hosts retain native controls, focus, selection, and document traversal behind the command adapter.
/// </summary>
public sealed class FindReplaceDialogSession
{
    private readonly IFindReplaceDialogCommandHost _commandHost;
    private FindReplaceOpenMode _openMode;
    private string _query = string.Empty;
    private string _replacement = string.Empty;
    private FindReplaceSearchOptions _options;
    private string _statusText = string.Empty;

    public FindReplaceDialogSession(
        IFindReplaceDialogCommandHost commandHost,
        FindReplaceOpenMode openMode = FindReplaceOpenMode.Find)
    {
        _commandHost = commandHost ?? throw new ArgumentNullException(nameof(commandHost));
        _openMode = openMode;
    }

    public FindReplaceDialogState State => BuildState();

    public FindReplaceDialogState ActivateFor(FindReplaceOpenMode openMode)
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

    public FindReplaceDialogState SetInput(FindReplaceDialogInput input) =>
        SetInput(
            input.Query,
            input.Replacement,
            input.MatchCase,
            input.WholeWord,
            input.UseWildcards);

    public FindReplaceDialogState Execute(
        FindReplaceDialogActionKind action,
        FindReplaceDialogInput input)
    {
        SetInput(input);
        return action switch
        {
            FindReplaceDialogActionKind.FindNext => FindNext(),
            FindReplaceDialogActionKind.Replace => ReplaceNext(),
            FindReplaceDialogActionKind.ReplaceAll => ReplaceAll(),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
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

    public FindReplaceTextInsertionPlan PlanSpecialInsertion(
        string? currentText,
        int caretIndex,
        string? insertion)
    {
        var text = currentText ?? string.Empty;
        var insert = insertion ?? string.Empty;
        var normalizedCaret = Math.Clamp(caretIndex, 0, text.Length);
        return new FindReplaceTextInsertionPlan(
            text.Insert(normalizedCaret, insert),
            normalizedCaret + insert.Length);
    }

    public FindReplaceGoToTargetsPlan BuildGoToTargets(
        TextDocument document,
        int previousSelectedIndex)
    {
        var targets = FindReplaceDialogPlanner.BuildGoToTargets(document);
        var selectedIndex = previousSelectedIndex >= 0 && previousSelectedIndex < targets.Count
            ? previousSelectedIndex
            : 0;
        return new FindReplaceGoToTargetsPlan(targets, selectedIndex);
    }

    public FindReplaceGoToExecutionPlan? PlanGoTo(
        FindReplaceGoToTarget? target,
        int blockCount)
    {
        var plan = FindReplaceDialogPlanner.PlanGoTo(target, blockCount);
        if (plan is not null)
            SetStatus(plan.StatusText);
        return plan;
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
