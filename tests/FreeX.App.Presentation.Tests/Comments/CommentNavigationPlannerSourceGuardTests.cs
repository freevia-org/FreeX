using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Comments;

public sealed class CommentNavigationPlannerSourceGuardTests
{
    [Fact]
    public void CommentNavigationPlanner_IsPortableAndRendererReady()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "Comments");
        var source = File.ReadAllText(Path.Combine(directory, "CommentNavigationPlanner.cs"));

        source.Should().Contain("public sealed record CommentListRowPlan");
        source.Should().Contain("public static IReadOnlyList<CommentListRowPlan> CreateThreadedCommentRows");
        source.Should().Contain("public static IReadOnlyList<CommentListRowPlan> CreateNoteRows");
        source.Should().Contain("public static List<CellAddress> OrderedThreadedCommentAddresses");
        source.Should().Contain("public static string FormatThreadedComment");
        source.Should().Contain("public static string? FormatCellCommentPreview");
        source.Should().Contain("public static string GetDefaultCommentText");
        source.Should().Contain("FindFirstAfter");
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("FreeX.App.Host");
    }
}
