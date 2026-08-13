namespace FreeP.App.Compositor.Tests;

public sealed class FreePWorkareaSemanticOwnershipSourceTests
{
    [Fact]
    public void Renderer_hosts_consume_final_workarea_and_picker_semantics()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sources = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "MainWindow.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"),
        };

        foreach (var source in sources)
        {
            source.Should().Contain("plan.ShouldShowEmptyState")
                .And.Contain("row.DisplayTitle")
                .And.Contain("row.DisplayMetadata")
                .And.Contain("row.ReplacementDisplayText")
                .And.Contain("item.RoleDisplayText")
                .And.Contain("field.DisplayLabel")
                .And.Contain("field.ToolTip")
                .And.Contain("choice.DisplayLabel")
                .And.Contain("choice.AutomationId")
                .And.Contain("FreePApplicationFrameDescriptor.Title")
                .And.NotContain("No comments on this slide.")
                .And.NotContain("Assistant row")
                .And.NotContain("Root row")
                .And.NotContain("choice.IsDefault ?")
                .And.NotContain("$\"table-")
                .And.NotContain("$\"layout-");
        }
    }

    [Fact]
    public void Renderer_adapters_and_section_prompts_only_apply_native_controls()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var adapters = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "PresentationPaneAccessibilityAdapter.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "PresentationPaneAccessibilityAdapter.cs"),
        };
        var promptSources = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "SlidePane.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"),
        };

        foreach (var source in adapters)
        {
            source.Should().Contain("PresentationPaneAccessibilityItemPlan plan")
                .And.Contain("PresentationPaneAccessibilityPlanner.ProjectItem(plan)")
                .And.NotContain("string? state")
                .And.NotContain("string? stableKey");
        }

        foreach (var source in promptSources)
        {
            source.Should().Contain("prompt.PromptTitle")
                .And.Contain("prompt.PromptLabel")
                .And.Contain("prompt.PromptAcceptText")
                .And.Contain("prompt.PromptCancelText")
                .And.NotContain("\"Section name:\"");
        }
    }

    [Fact]
    public void Section_prompt_contract_resolves_complete_neutral_and_french_resources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var planner = Read(root, "freep", "FreeP.App.Presentation", "PresentationPaneTextResources.cs");
        var neutral = Read(root, "freep", "FreeP.App.Localization", "Resources", "Strings.resx");
        var french = Read(root, "freep", "FreeP.App.Localization", "Resources", "Strings.fr-FR.resx");
        var keys = new[]
        {
            "Pane_SlideSection_AddPromptTitle",
            "Pane_SlideSection_RenamePromptTitle",
            "Pane_SlideSection_NamePromptLabel",
            "Pane_SlideSection_NamePromptAccept",
            "Pane_SlideSection_NamePromptCancel",
        };

        foreach (var key in keys)
        {
            planner.Should().Contain($"Loc.Get(\"{key}\")");
            neutral.Should().Contain($"name=\"{key}\"");
            french.Should().Contain($"name=\"{key}\"");
        }

        french.Should().Contain("Ajouter une section")
            .And.Contain("Renommer la section")
            .And.Contain("Nom de la section :")
            .And.Contain("Annuler");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
