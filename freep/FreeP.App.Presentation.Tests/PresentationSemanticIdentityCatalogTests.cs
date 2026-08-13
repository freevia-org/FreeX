namespace FreeP.App.Compositor.Tests;

public sealed class PresentationSemanticIdentityCatalogTests
{
    [Fact]
    public void Stable_semantic_identities_are_unique_and_preserve_existing_values()
    {
        var identities = new[]
        {
            PresentationSemanticIdentityCatalog.BackstageOverlayAutomationId,
            PresentationSemanticIdentityCatalog.BackstageNewBlankPresentationAutomationId,
            PresentationSemanticIdentityCatalog.RichTextEditorInputAutomationId,
            PresentationSemanticIdentityCatalog.CommentsPaneItemAutomationIdPrefix,
            PresentationSemanticIdentityCatalog.CommentsPaneCloseTag,
            PresentationSemanticIdentityCatalog.CommentMentionSummaryPrefix,
            PresentationSemanticIdentityCatalog.CommentMentionTagPrefix,
            PresentationSemanticIdentityCatalog.CommentMentionEditTag,
            PresentationSemanticIdentityCatalog.CommentMentionReplyTag,
        };

        identities.Should().Equal(
            "FreePBackstageOverlay",
            "BackstageNewBlankPresentation",
            "FreePRichTextEditorInput",
            "FreePCommentsPaneItem",
            "comments-pane-close",
            "Mentions:",
            "comment-mention:",
            "comment-mention:edit",
            "comment-mention:reply");
        identities.Should().OnlyHaveUniqueItems();

        PresentationSemanticIdentityCatalog.IsCommentMentionSummary("Mentions: Alice").Should().BeTrue();
        PresentationSemanticIdentityCatalog.IsCommentMentionTag("comment-mention:reply").Should().BeTrue();
        PresentationSemanticIdentityCatalog.BuildCommentMentionCandidateTag(
                PresentationSemanticIdentityCatalog.CommentMentionEditTag,
                "Nora.Reviewer")
            .Should().Be("comment-mention:edit:Nora.Reviewer");
    }

    [Fact]
    public void Renderers_only_attach_portable_semantic_identities()
    {
        var backstageSources = new[]
        {
            Read("freep", "FreeP.App.Host", "Backstage", "BackstageView.cs"),
            Read("freep", "FreeP.App.Avalonia", "Backstage", "BackstageView.cs"),
        };
        var editorSources = new[]
        {
            Read("freep", "FreeP.App.Rendering.Wpf", "InCanvasTextEditor.cs"),
            Read("freep", "FreeP.App.Rendering.Wpf", "InCanvasTableCellEditor.cs"),
            Read("freep", "FreeP.App.Rendering.Avalonia", "AvaloniaRichTextEditor.cs"),
        };

        foreach (var source in backstageSources)
        {
            source.Should()
                .Contain("PresentationSemanticIdentityCatalog.BackstageOverlayAutomationId")
                .And.Contain("PresentationSemanticIdentityCatalog.BackstageNewBlankPresentationAutomationId")
                .And.NotContain("\"FreePBackstageOverlay\"")
                .And.NotContain("\"BackstageNewBlankPresentation\"");
        }

        foreach (var source in editorSources)
        {
            source.Should()
                .Contain("PresentationSemanticIdentityCatalog.RichTextEditorInputAutomationId")
                .And.NotContain("\"FreePRichTextEditorInput\"");
        }

        var commentSources = new[]
        {
            Read("freep", "FreeP.App.Host", "MainWindow.cs"),
            Read("freep", "FreeP.App.Avalonia", "MainWindow.cs"),
        };
        foreach (var source in commentSources)
        {
            source.Should()
                .Contain("PresentationSemanticIdentityCatalog.CommentMentionEditTag")
                .And.Contain("PresentationSemanticIdentityCatalog.CommentMentionReplyTag")
                .And.Contain("PresentationSemanticIdentityCatalog.CommentsPaneCloseTag")
                .And.NotContain("\"comment-mention:edit\"")
                .And.NotContain("\"comment-mention:reply\"")
                .And.NotContain("\"comments-pane-close\"");
        }
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
