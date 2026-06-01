using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal sealed class RejectedWorkbookCommand(string label, string errorMessage) : IWorkbookCommand
{
    public string Label { get; } = label;

    public CommandOutcome Apply(ICommandContext ctx) => new(false, errorMessage);

    public void Revert(ICommandContext ctx)
    {
    }
}
