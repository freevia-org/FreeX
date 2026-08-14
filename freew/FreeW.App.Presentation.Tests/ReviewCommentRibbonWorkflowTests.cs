using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewCommentRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowOwnsEveryCommentRouteInCanonicalOrder()
    {
        var events = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        ReviewCommentRibbonWorkflow.Register(
            bindings,
            new ReviewCommentRibbonCommands(
                Command("new"),
                Command("delete"),
                Command("previous"),
                Command("next"),
                Command("reply"),
                Command("resolve"),
                Command("show")));

        foreach (var action in new[]
                 {
                     FreeWRibbonCommandAction.NewComment,
                     FreeWRibbonCommandAction.DeleteComment,
                     FreeWRibbonCommandAction.PreviousComment,
                     FreeWRibbonCommandAction.NextComment,
                     FreeWRibbonCommandAction.ReplyComment,
                     FreeWRibbonCommandAction.ResolveComment,
                     FreeWRibbonCommandAction.ShowComments,
                 })
        {
            var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
            bindings.TryGet(route.CommandId, out var command).Should().BeTrue(action.ToString());
            command!.Execute(RibbonCommandContext.Empty);
        }

        events.Should().Equal("new", "delete", "previous", "next", "reply", "resolve", "show");

        IRibbonCommand Command(string name) => new RecordingCommand(() => events.Add(name));
    }

    [Fact]
    public void BothRenderersDelegateCommentRoutingAndAvaloniaPromptsForNewComment()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = Read(root, "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var window = Read(root, "FreeW.App.Avalonia", "MainWindow.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ReviewCommentRibbonWorkflow.Register(");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.NewComment,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.ReplyComment,");
            source.Should().NotContain(".Bind(FreeWRibbonCommandAction.ShowComments,");
        }

        avalonia.Should().Contain("OptionalHostCommand(callbacks.NewComment)");
        avalonia.Should().NotContain("editor.NewComment()");
        window.Should().Contain("NewComment: () => _ = NewCommentAsync()");
        window.Should().Contain("CommentTextEntryKind.NewComment");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, "freew", .. parts]));

    private sealed class RecordingCommand(Action execute) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => execute();
    }
}
