using Free.Shared.Commands;

namespace FreeP.Core.Model;

/// <summary>A reversible edit to a <see cref="Presentation"/>. Mirrors FreeW's IDocumentCommand shape.</summary>
public interface IPresentationCommand
{
    string Label { get; }
    int EstimatedBytes => 256;
    void Apply(Presentation presentation);
    void Revert(Presentation presentation);
}

/// <summary>
/// FreeP's undo/redo command bus. As in FreeW, the mechanics — paired stacks, depth/byte budget, redo
/// invalidation — are the shared <see cref="UndoRedoStack{TCommand,TPayload}"/>; this bus only adds the
/// presentation-command apply/revert and a change notification. Deliberately small: it exists to prove the
/// shared command tier is consumed, and to give the next session a place to hang real slide edits.
/// </summary>
public sealed class PresentationCommandBus
{
    private const int MaxDepth = 200;
    private const int MaxBytes = 50 * 1024 * 1024;

    private readonly UndoRedoStack<IPresentationCommand, object?> _stack = new(MaxDepth, MaxBytes);
    private readonly Presentation _presentation;

    public PresentationCommandBus(Presentation presentation) => _presentation = presentation;

    /// <summary>Raised after any execute/undo/redo so a view can refresh.</summary>
    public event Action? Changed;

    public bool CanUndo => _stack.CanUndo;
    public bool CanRedo => _stack.CanRedo;

    /// <summary>Applies a command and records it for undo (invalidating the redo history).</summary>
    public void Execute(IPresentationCommand command)
    {
        command.Apply(_presentation);
        _stack.Push(command, command.EstimatedBytes, payload: null, command.Label);
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!_stack.CanUndo)
            return;
        var entry = _stack.PopUndo();
        entry.Command.Revert(_presentation);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!_stack.CanRedo)
            return;
        var entry = _stack.PopRedo();
        entry.Command.Apply(_presentation);
        _stack.PushWithoutClearingRedo(entry);
        Changed?.Invoke();
    }
}

/// <summary>A trivial command: appends a new blank slide. Demonstrates the bus end-to-end (New Slide).</summary>
public sealed class AddSlideCommand : IPresentationCommand
{
    private readonly Slide _slide;

    public AddSlideCommand(Slide slide) => _slide = slide;

    public string Label => "Add Slide";

    public void Apply(Presentation presentation) => presentation.Slides.Add(_slide);

    public void Revert(Presentation presentation) => presentation.Slides.Remove(_slide);
}
