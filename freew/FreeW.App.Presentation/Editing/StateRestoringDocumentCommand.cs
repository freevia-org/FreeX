using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

/// <summary>
/// Couples a reversible document edit to caller-owned state. State is restored before the command
/// bus publishes the completed history transition; UI observer notifications belong outside the command.
/// </summary>
public sealed class StateRestoringDocumentCommand<TState> : IDocumentCommand
{
    private readonly IDocumentCommand _command;
    private readonly TState _before;
    private readonly Func<TState> _captureAfter;
    private readonly Action<TState> _restore;
    private readonly Action _placeInitialState;
    private readonly int _estimatedStateBytes;
    private TState _after = default!;
    private bool _hasAfter;

    public StateRestoringDocumentCommand(
        IDocumentCommand command,
        TState before,
        Func<TState> captureAfter,
        Action<TState> restore,
        Action placeInitialState,
        int estimatedStateBytes)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(captureAfter);
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(placeInitialState);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedStateBytes);
        _command = command;
        _before = before;
        _captureAfter = captureAfter;
        _restore = restore;
        _placeInitialState = placeInitialState;
        _estimatedStateBytes = estimatedStateBytes;
    }

    public string Label => _command.Label;
    public int EstimatedBytes => (int)Math.Min(int.MaxValue, (long)_command.EstimatedBytes + _estimatedStateBytes);
    public DocumentCommandMutationKind MutationKind => _command.MutationKind;
    public bool HasEffect(IDocumentCommandContext context) => _command.HasEffect(context);

    /// <summary>Attaches state to commands already executed in a quiet gesture scope.</summary>
    public static StateRestoringDocumentCommand<TState> FromApplied(
        IDocumentCommand command, TState before, TState after, Action<TState> restore, int estimatedStateBytes)
    {
        var wrapper = new StateRestoringDocumentCommand<TState>(command, before, () => after,
            restore, () => { }, estimatedStateBytes);
        wrapper._after = after;
        wrapper._hasAfter = true;
        return wrapper;
    }

    public void Apply(IDocumentCommandContext context)
    {
        _command.Apply(context);
        if (_hasAfter)
        {
            _restore(_after);
            return;
        }

        _placeInitialState();
        _after = _captureAfter();
        _hasAfter = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        _command.Revert(context);
        _restore(_before);
    }
}
