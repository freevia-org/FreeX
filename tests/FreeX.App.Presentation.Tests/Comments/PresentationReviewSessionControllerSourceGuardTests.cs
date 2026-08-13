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
        mutationSource.Should().Contain("PlanToggleNoteVisibility");
        mutationSource.Should().Contain("PlanToggleAllNotesVisibility");
    }

    [Fact]
    public void Renderers_DelegateNoteVisibilityCommandsToSharedReviewController()
    {
        var repository = RepositoryFileLocator.FindDirectory("src");
        var wpfSource = File.ReadAllText(Path.Combine(
            repository,
            "FreeX.App.Host",
            "MainWindow.ReviewCommands.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(
            repository,
            "FreeX.App.Avalonia",
            "MainWindow.Comments.cs"));

        wpfSource.Should().Contain("ReviewSessionController.ToggleNoteVisibility");
        wpfSource.Should().Contain("ReviewSessionController.ToggleAllNotesVisibility");
        wpfSource.Should().NotContain("new ShowHideCommentCommand");
        wpfSource.Should().NotContain("new ShowAllNotesCommand");
        avaloniaSource.Should().Contain("ReviewSessionController.ToggleNoteVisibility");
        avaloniaSource.Should().Contain("ReviewSessionController.ToggleAllNotesVisibility");
        avaloniaSource.Should().NotContain("new ShowHideCommentCommand");
        avaloniaSource.Should().NotContain("new ShowAllNotesCommand");
    }
}
