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
        };

        identities.Should().Equal(
            "FreePBackstageOverlay",
            "BackstageNewBlankPresentation",
            "FreePRichTextEditorInput");
        identities.Should().OnlyHaveUniqueItems();
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
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
