using Free.Shared.Ribbon;

namespace FreeX.App.Services.Ribbon;

/// <summary>
/// A platform-neutral ribbon toggle command bound to a <see cref="WorkbookSession"/>. The WPF and
/// Avalonia ribbon renderers execute this <em>same</em> class through the shared command registry, so a
/// command such as "Bold" performs identical work — apply a <c>StyleDiff</c> to the session's selection —
/// on every platform. The command holds no UI types; the host supplies a session accessor and an optional
/// post-apply callback (e.g. to redraw its viewport / status bar).
/// </summary>
public sealed class WorkbookToggleFormatCommand : IRibbonStatefulCommand
{
    private readonly Func<WorkbookSession?> _session;
    private readonly Func<WorkbookSession, bool> _read;
    private readonly Func<WorkbookSession, bool, WorkbookCellEditResult> _apply;
    private readonly Action<WorkbookCellEditResult, bool>? _onApplied;

    /// <param name="session">Accessor for the live session (the host's session field may be replaced on
    /// open/new, so this is read each time rather than captured once).</param>
    /// <param name="read">Reads the command's current checked state from the session (e.g. is the
    /// selection's start cell bold).</param>
    /// <param name="apply">Applies the toggled state to the selection, returning the edit result.</param>
    /// <param name="onApplied">Optional host callback invoked after applying: <c>(result, newState)</c>.
    /// Hosts use it to refresh their viewport/status and to roll back UI on failure.</param>
    public WorkbookToggleFormatCommand(
        Func<WorkbookSession?> session,
        Func<WorkbookSession, bool> read,
        Func<WorkbookSession, bool, WorkbookCellEditResult> apply,
        Action<WorkbookCellEditResult, bool>? onApplied = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _onApplied = onApplied;
    }

    public void Execute(RibbonCommandContext context)
    {
        if (_session() is not { } session)
            return;

        var next = !_read(session);
        var result = _apply(session, next);
        _onApplied?.Invoke(result, next);
    }

    public RibbonCommandState GetState() =>
        _session() is { } session
            ? new RibbonCommandState(IsEnabled: true, IsChecked: _read(session))
            : RibbonCommandState.Default;
}

/// <summary>
/// Factory for the platform-neutral cell-formatting ribbon commands. Each command is built from a session
/// accessor (and an optional post-apply callback) and is reusable by any ribbon renderer — this is the
/// shared command logic the WPF host and the Avalonia host both bind to, so formatting behaves the same
/// across platforms.
/// </summary>
public static class WorkbookFormatRibbonCommands
{
    public static WorkbookToggleFormatCommand Bold(
        Func<WorkbookSession?> session,
        Action<WorkbookCellEditResult, bool>? onApplied = null) =>
        new(session,
            s => s.IsSelectedRangeStartBold,
            (s, on) => s.SetSelectedRangeBold(on),
            onApplied);

    public static WorkbookToggleFormatCommand Italic(
        Func<WorkbookSession?> session,
        Action<WorkbookCellEditResult, bool>? onApplied = null) =>
        new(session,
            s => s.IsSelectedRangeStartItalic,
            (s, on) => s.SetSelectedRangeItalic(on),
            onApplied);

    public static WorkbookToggleFormatCommand Underline(
        Func<WorkbookSession?> session,
        Action<WorkbookCellEditResult, bool>? onApplied = null) =>
        new(session,
            s => s.IsSelectedRangeStartUnderline,
            (s, on) => s.SetSelectedRangeUnderline(on),
            onApplied);
}
