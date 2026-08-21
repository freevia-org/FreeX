using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// r154 remediation (M2): the WPF shell was fixed in round 154 (see
/// FreeP.App.Host.Tests.CommentAuthorIdentityStampingTests) to stamp the real author identity
/// (the presentation's Properties.Author, falling back to the OS account name) when a comment is
/// added or replied to, instead of the hard-coded "FreeP User" default -- but the Avalonia shell's
/// own "New Comment"/"Reply" button.Click handlers (freep/FreeP.App.Avalonia/MainWindow.cs) were
/// never updated to pass an author at all, so Avalonia still dropped it. These tests drive the
/// actual button.Click handlers built by BuildAddCommentInput/BuildReviewCommentCard -- not just
/// the AddComment/ReplyToSelectedComment wrapper methods those handlers call into -- so a
/// regression in the handler's argument wiring is caught even if the wrapper and the shared
/// planner underneath it stay correct.
/// </summary>
public sealed class CommentAuthorIdentityStampingTests : IDisposable
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.CommentAuthorIdentityStampingTests-");

    static CommentAuthorIdentityStampingTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    public void Dispose() => _temporaryDirectory.Dispose();

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    [Fact]
    public async Task ResolveCommentAuthor_PrefersPresentationPropertiesAuthor()
    {
        string? resolved = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.Presentation.Properties.Author = "  Dana Reviewer  ";

            resolved = window.ResolveCommentAuthor();
        });

        if (!ran) return;
        resolved.Should().Be("Dana Reviewer");
    }

    [Fact]
    public async Task ResolveCommentAuthor_FallsBackToOsAccountName_WhenDocumentAuthorIsBlank()
    {
        string? resolved = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.Presentation.Properties.Author = "   ";

            resolved = window.ResolveCommentAuthor();
        });

        if (!ran) return;
        resolved.Should().Be(Environment.UserName);
    }

    [Fact]
    public async Task NewCommentButtonClick_StampsRealDocumentAuthor_NotTheFreePUserDefault()
    {
        SlideComment? comment = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.Presentation.Properties.Author = "Dana Reviewer";
            window.ShowReviewCommentsPane();

            window.ClickNewCommentButtonForTests("Please check this build.").Should().BeTrue();

            comment = window.Editor.CurrentSlide!.Comments.Single();
        });

        if (!ran) return;
        comment!.Author.Should().Be("Dana Reviewer");
        comment.Initials.Should().Be("DR");
        comment.Author.Should().NotBe("FreeP User");
        comment.Initials.Should().NotBe("FU");
    }

    [Fact]
    public async Task ReplyButtonClick_StampsRealDocumentAuthor_NotTheFreePUserDefault()
    {
        SlideCommentReply? reply = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
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

            reply = window.Editor.CurrentSlide.Comments.Single().Replies.Single();
        });

        if (!ran) return;
        reply!.Author.Should().Be("Dana Reviewer");
        reply.Initials.Should().Be("DR");
        reply.Author.Should().NotBe("FreeP User");
        reply.Initials.Should().NotBe("FU");
    }

    /// <summary>
    /// r154 remediation (N2): mirrors FreeP.App.Host.Tests's
    /// MentionButtonReplyAutoApply_StampsRealDocumentAuthor_NotTheFreePUserDefault -- the mention
    /// button's ("@") own single-candidate auto-apply route was an undisclosed fourth path that
    /// still fell through to ApplyCommentMention's un-stamped ReplyToSelectedComment call on the
    /// Avalonia shell too. Drives the real mention button.Click handler, not
    /// DispatchCommentMentionPicker directly.
    /// </summary>
    [Fact]
    public async Task MentionButtonReplyAutoApply_StampsRealDocumentAuthor_NotTheFreePUserDefault()
    {
        SlideCommentReply? reply = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Nora Reviewer",
                Initials = "NR",
                Text = "Original text.",
                Idx = 1
            });
            window.Editor.Presentation.Properties.Author = "Dana Reviewer";
            window.SetSelectedReviewCommentIndexForTests(0);
            window.ShowReviewCommentsPane();

            window.ClickCommentMentionButtonForTests(
                PresentationSemanticIdentityCatalog.CommentMentionReplyTag,
                "cc @Nora").Should().BeTrue();

            reply = window.Editor.CurrentSlide.Comments.Single().Replies.Single();
        });

        if (!ran) return;
        reply!.Author.Should().Be("Dana Reviewer");
        reply.Author.Should().NotBe("FreeP User");
    }

    /// <summary>
    /// Sibling/no-regression case: this finding is scoped to AddComment and ReplyToSelectedComment
    /// (and, in the coordinator file, ResolveSelectedComment) -- EditSelectedComment's call site is
    /// deliberately left untouched because BuildEditCommentPlan already preserves the comment's
    /// existing author when none is supplied (current.Author fallback), which is correct edit
    /// semantics, not an identity bug. Mirrors the WPF sibling test of the same name.
    /// </summary>
    [Fact]
    public async Task EditComment_KeepsPreservingTheCommentsOwnAuthor_UnaffectedByThisFix()
    {
        PresentationCommentMutationPlan? edited = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Original Reviewer",
                Initials = "OR",
                Text = "Original text.",
                Idx = 1
            });
            window.Editor.Presentation.Properties.Author = "Dana Reviewer";
            window.SetSelectedReviewCommentIndexForTests(0);

            edited = window.EditSelectedComment("Updated text.");
        });

        if (!ran) return;
        edited!.Comment!.Author.Should().Be("Original Reviewer");
        edited.Comment.Text.Should().Be("Updated text.");
    }
}
