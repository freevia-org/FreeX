using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// freep-comments F1: every comment/reply added through FreeP's own UI must be stamped with the
/// real author (the presentation's Properties.Author, falling back to the OS account name) instead
/// of the hard-coded "FreeP User" default. These tests drive the actual "New Comment"/"Reply"
/// button.Click handlers built by AddCommentInput/AddReplyInput -- not just the AddComment/
/// ReplyToSelectedComment wrapper methods those handlers call into -- so a regression in the
/// handler's argument wiring (the finding's exact defect) is caught even if the wrapper and the
/// planner underneath it stay correct.
/// </summary>
public sealed class CommentAuthorIdentityStampingTests
{
    [StaFact]
    public void ResolveCommentAuthor_PrefersPresentationPropertiesAuthor()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.Presentation.Properties.Author = "  Dana Reviewer  ";

            window.ResolveCommentAuthor().Should().Be("Dana Reviewer");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void ResolveCommentAuthor_FallsBackToOsAccountName_WhenDocumentAuthorIsBlank()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.Presentation.Properties.Author = "   ";

            window.ResolveCommentAuthor().Should().Be(Environment.UserName);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void NewCommentButtonClick_StampsRealDocumentAuthor_NotTheFreePUserDefault()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.Presentation.Properties.Author = "Dana Reviewer";
            window.ShowReviewCommentsPane();

            window.ClickNewCommentButtonForTests("Please check this build.").Should().BeTrue();

            window.Editor.CurrentSlide!.Comments.Should().ContainSingle();
            var comment = window.Editor.CurrentSlide.Comments.Single();
            comment.Author.Should().Be("Dana Reviewer");
            comment.Initials.Should().Be("DR");
            comment.Author.Should().NotBe("FreeP User");
            comment.Initials.Should().NotBe("FU");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void ReplyButtonClick_StampsRealDocumentAuthor_NotTheFreePUserDefault()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Original Reviewer",
                Initials = "OR",
                Text = "Needs a reply.",
                Idx = 1
            });
            window.Editor.Presentation.Properties.Author = "Dana Reviewer";
            window.SetSelectedReviewCommentIndexForTests(0);
            window.ShowReviewCommentsPane();

            window.ClickReplyButtonForTests("On it.").Should().BeTrue();

            var comment = window.Editor.CurrentSlide.Comments.Single();
            comment.Replies.Should().ContainSingle();
            var reply = comment.Replies.Single();
            reply.Author.Should().Be("Dana Reviewer");
            reply.Initials.Should().Be("DR");
            reply.Author.Should().NotBe("FreeP User");
            reply.Initials.Should().NotBe("FU");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Sibling/no-regression case: this finding is scoped to AddComment and ReplyToSelectedComment
    /// (and, in the coordinator file, ResolveSelectedComment) -- EditSelectedComment's call site
    /// (AddEditCommentInput's `EditSelectedComment(input.Text)`) is deliberately left untouched
    /// because BuildEditCommentPlan already preserves the comment's existing author when none is
    /// supplied (`current.Author` fallback), which is correct edit semantics, not an identity bug.
    /// This proves that pre-existing, correct behaviour still holds after the fix.
    /// </summary>
    [StaFact]
    public void EditComment_KeepsPreservingTheCommentsOwnAuthor_UnaffectedByThisFix()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Original Reviewer",
                Initials = "OR",
                Text = "Original text.",
                Idx = 1
            });
            window.Editor.Presentation.Properties.Author = "Dana Reviewer";
            window.SetSelectedReviewCommentIndexForTests(0);

            var edited = window.EditSelectedComment("Updated text.");

            edited.Comment!.Author.Should().Be("Original Reviewer");
            edited.Comment.Text.Should().Be("Updated text.");
        }
        finally
        {
            window.Close();
        }
    }
}
