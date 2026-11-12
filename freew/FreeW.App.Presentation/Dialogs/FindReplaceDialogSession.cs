using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public interface IFindReplaceDialogCommandHost
{
    bool FindNext(FindReplaceSearchRequest request);

    bool ReplaceNext(FindReplaceReplaceRequest request);

    FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request);

    /// <summary>
    /// A status line for the match the last <see cref="FindNext"/> / <see cref="ReplaceNext"/> landed
    /// on, when the host could not show it as a selection -- a match in a header, a note, a comment or
    /// a text box. Null (the default) when the match is selected on the editing surface.
    /// </summary>
    string? LastMatchStatus => null;
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
    string StatusText)
{
    /// <summary>Formatting criteria for Find what, or null when none is set.</summary>
    public FindFormat? FindFormat { get; init; }

    /// <summary>Formatting applied by Replace with, or null when none is set.</summary>
    public FindFormat? ReplaceFormat { get; init; }

    /// <summary>The "Format: ..." line under Find what; empty when no formatting is set.</summary>
    public string FindFormatSummary { get; init; } = string.Empty;

    /// <summary>The "Format: ..." line under Replace with; empty when no formatting is set.</summary>
    public string ReplaceFormatSummary { get; init; } = string.Empty;

    public bool IsOptionEnabled(FindReplaceOptionKind kind) =>
        FindReplaceDialogPlanner.IsOptionEnabled(kind, Options);

    public bool IsOptionChecked(FindReplaceOptionKind kind) => kind switch
    {
        FindReplaceOptionKind.MatchCase => Options.MatchCase,
        FindReplaceOptionKind.WholeWord => Options.WholeWord,
        FindReplaceOptionKind.UseWildcards => Options.UseWildcards,
        FindReplaceOptionKind.MatchPrefix => Options.MatchPrefix,
        FindReplaceOptionKind.MatchSuffix => Options.MatchSuffix,
        FindReplaceOptionKind.IgnorePunctuation => Options.IgnorePunctuation,
        FindReplaceOptionKind.IgnoreWhiteSpace => Options.IgnoreWhiteSpace,
        _ => false,
    };
}

public readonly record struct FindReplaceDialogInput(
    string? Query,
    string? Replacement,
    bool MatchCase,
    bool WholeWord,
    bool UseWildcards,
    bool MatchPrefix = false,
    bool MatchSuffix = false,
    bool IgnorePunctuation = false,
    bool IgnoreWhiteSpace = false);

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
    private readonly FindReplacePolicyTextSpec _policyText;
    private readonly FindReplaceDialogText _dialogText;
    private readonly Func<TextDocument?>? _document;
    private FindReplaceOpenMode _openMode;
    private string _query = string.Empty;
    private string _replacement = string.Empty;
    private FindReplaceSearchOptions _options;
    private FindFormat? _findFormat;
    private FindFormat? _replaceFormat;
    private string _statusText = string.Empty;

    public FindReplaceDialogSession(
        IFindReplaceDialogCommandHost commandHost,
        FindReplaceOpenMode openMode = FindReplaceOpenMode.Find,
        FindReplacePolicyTextSpec? policyText = null,
        FindReplaceDialogText? dialogText = null,
        Func<TextDocument?>? document = null)
    {
        _commandHost = commandHost ?? throw new ArgumentNullException(nameof(commandHost));
        _openMode = openMode;
        _policyText = policyText ?? FindReplacePolicyTextSpec.NeutralEnglish;
        _dialogText = dialogText ?? FindReplaceDialogText.NeutralEnglish;
        _document = document;
    }

    public FindReplaceDialogState State => BuildState();

    public FindReplaceDialogText Text => _dialogText;

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
        bool useWildcards) =>
        SetInput(new FindReplaceDialogInput(query, replacement, matchCase, wholeWord, useWildcards));

    public FindReplaceDialogState SetInput(FindReplaceDialogInput input)
    {
        _query = input.Query ?? string.Empty;
        _replacement = input.Replacement ?? string.Empty;
        _options = FindReplaceDialogPlanner.NormalizeOptions(new FindReplaceSearchOptions(
            input.MatchCase,
            input.WholeWord,
            input.UseWildcards,
            input.MatchPrefix,
            input.MatchSuffix,
            input.IgnorePunctuation,
            input.IgnoreWhiteSpace));
        return BuildState();
    }

    /// <summary>The formatting currently set for a box, for seeding the Format dialog.</summary>
    public FindFormat? FormatFor(FindReplaceDialogFieldKind field) =>
        field == FindReplaceDialogFieldKind.Find ? _findFormat : _replaceFormat;

    /// <summary>Sets (or, with null / empty formatting, clears) the formatting for one box.</summary>
    public FindReplaceDialogState SetFormat(FindReplaceDialogFieldKind field, FindFormat? format)
    {
        var normalized = format is { IsEmpty: false } ? format : null;
        if (field == FindReplaceDialogFieldKind.Find)
            _findFormat = normalized;
        else
            _replaceFormat = normalized;
        return BuildState();
    }

    /// <summary>Word's No Formatting button: clears the formatting of the given box.</summary>
    public FindReplaceDialogState ClearFormat(FindReplaceDialogFieldKind field) => SetFormat(field, null);

    /// <summary>The Special drop-down for a box under the current options.</summary>
    public IReadOnlyList<FindReplaceSpecialItem> BuildSpecialItems(FindReplaceDialogFieldKind field) =>
        FindReplaceDialogPlanner.BuildSpecialItems(field, _options.UseWildcards, _dialogText);

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
                _findFormat,
                out var request,
                out var error))
        {
            return SetStatus(FindReplaceDialogPlanner.ValidationMessageFor(error, _policyText, _dialogText));
        }

        var found = _commandHost.FindNext(request!);
        return SetStatus(found && _commandHost.LastMatchStatus is { Length: > 0 } hostStatus
            ? hostStatus
            : FindReplaceDialogPlanner.BuildFindStatus(request!, found, _policyText, _dialogText));
    }

    public FindReplaceDialogState ReplaceNext()
    {
        if (!TryCreateReplaceRequest(out var request, out var error))
            return SetStatus(FindReplaceDialogPlanner.ValidationMessageFor(error, _policyText, _dialogText));

        var found = _commandHost.ReplaceNext(request!);
        return SetStatus(found && _commandHost.LastMatchStatus is { Length: > 0 } hostStatus
            ? hostStatus
            : FindReplaceDialogPlanner.BuildReplaceStatus(request!, found, _policyText, _dialogText));
    }

    public FindReplaceDialogState ReplaceAll()
    {
        if (!TryCreateReplaceRequest(out var request, out var error))
            return SetStatus(FindReplaceDialogPlanner.ValidationMessageFor(error, _policyText, _dialogText));

        var result = _commandHost.ReplaceAll(request!);
        return SetStatus(FindReplaceDialogPlanner.BuildReplaceAllStatus(
            request!,
            result.ReplacementCount,
            result.InSelection,
            _policyText,
            _dialogText));
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
            _findFormat,
            _replaceFormat,
            out request,
            out error);

    private FindReplaceDialogState BuildState()
    {
        var document = _document?.Invoke();
        return new FindReplaceDialogState(
            _openMode,
            _query,
            _replacement,
            _options,
            FindReplaceDialogPlanner.IsOptionEnabled(FindReplaceOptionKind.WholeWord, _options),
            _statusText)
        {
            FindFormat = _findFormat,
            ReplaceFormat = _replaceFormat,
            FindFormatSummary = FindReplaceDialogPlanner.DescribeFormat(_findFormat, _dialogText, document),
            ReplaceFormatSummary = FindReplaceDialogPlanner.DescribeFormat(_replaceFormat, _dialogText, document),
        };
    }
}
