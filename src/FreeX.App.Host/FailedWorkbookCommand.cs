using FreeX.Core.Commands;

namespace FreeX.App.Host;

public sealed class FailedWorkbookCommand(string message) : IWorkbookCommand
{
    public string Label => "Unavailable";

    public CommandOutcome Apply(ICommandContext ctx) => new(false, message);

    public void Revert(ICommandContext ctx)
    {
    }
}
