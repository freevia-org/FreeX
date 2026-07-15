using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Comments;

public sealed class PresentationReviewSessionControllerSourceGuardTests
{
    [Fact]
    public void SharedReviewController_UsesMutationServiceAndHasNoRendererDependency()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "Comments");
        var source = File.ReadAllText(Path.Combine(directory, "PresentationReviewSessionController.cs"));
        var mutationSource = File.ReadAllText(Path.Combine(directory, "PresentationCommentMutationService.cs"));

        source.Should().Contain("PresentationCommentMutationService");
        source.Should().Contain("CreateRefreshPlan");
        source.Should().Contain("PresentationReviewRefreshPlan");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("System.Windows");
        mutationSource.Should().Contain("PlanThreadedComment");
        mutationSource.Should().Contain("ApplyThreadedCommentChangesCommand");
    }
}
