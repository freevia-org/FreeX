using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record ReviewCommentRibbonCommands(
    IRibbonCommand NewComment,
    IRibbonCommand DeleteComment,
    IRibbonCommand PreviousComment,
    IRibbonCommand NextComment,
    IRibbonCommand ReplyComment,
    IRibbonCommand ResolveComment,
    IRibbonCommand ShowComments);

/// <summary>
/// Owns the canonical Review &gt; Comments command routing for both renderers. Native prompt,
/// focus, pane, and feedback behavior remains behind the supplied renderer commands.
/// </summary>
public static class ReviewCommentRibbonWorkflow
{
    public static void Register(
        IRibbonCommandRegistry registry,
        ReviewCommentRibbonCommands commands)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(commands);

        registry.Bind(FreeWRibbonCommandAction.NewComment, commands.NewComment);
        registry.Bind(FreeWRibbonCommandAction.DeleteComment, commands.DeleteComment);
        registry.Bind(FreeWRibbonCommandAction.PreviousComment, commands.PreviousComment);
        registry.Bind(FreeWRibbonCommandAction.NextComment, commands.NextComment);
        registry.Bind(FreeWRibbonCommandAction.ReplyComment, commands.ReplyComment);
        registry.Bind(FreeWRibbonCommandAction.ResolveComment, commands.ResolveComment);
        registry.Bind(FreeWRibbonCommandAction.ShowComments, commands.ShowComments);
    }
}
