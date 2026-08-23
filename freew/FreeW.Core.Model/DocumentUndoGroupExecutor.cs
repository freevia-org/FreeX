namespace FreeW.Core.Model;

/// <summary>Executes portable document commands as one undoable change.</summary>
public static class DocumentUndoGroupExecutor
{
    public const string RollbackFailuresDataKey =
        "FreeW.DocumentUndoGroupExecutor.RollbackFailures";

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
        catch (Exception applyException)
        {
            if (ownsUndoGroup && commandBus.IsUndoGroupOpen)
            {
                var rollbackFailures = commandBus.RollbackUndoGroup();
                if (rollbackFailures.Count > 0)
                {
                    applyException.Data[RollbackFailuresDataKey] = new AggregateException(
                        "One or more failures occurred while rolling back the document command group.",
                        rollbackFailures);
                }
            }

            throw;
        }
    }
}
