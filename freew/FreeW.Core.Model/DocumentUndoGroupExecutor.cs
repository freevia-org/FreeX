namespace FreeW.Core.Model;

/// <summary>Executes portable document commands as one undoable change.</summary>
public static class DocumentUndoGroupExecutor
{
    public static void Execute(
        DocumentCommandBus commandBus,
        IReadOnlyList<IDocumentCommand> commands,
        string description)
    {
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (commands.Count == 1)
        {
            commandBus.Execute(commands[0]);
            return;
        }

        var ownsUndoGroup = !commandBus.IsUndoGroupOpen;
        if (ownsUndoGroup)
            commandBus.BeginUndoGroup();

        try
        {
            foreach (var command in commands)
                commandBus.Execute(command);

            if (ownsUndoGroup)
                commandBus.CommitUndoGroup(description);
        }
        catch
        {
            if (ownsUndoGroup && commandBus.IsUndoGroupOpen)
                commandBus.RollbackUndoGroup();
            throw;
        }
    }
}
