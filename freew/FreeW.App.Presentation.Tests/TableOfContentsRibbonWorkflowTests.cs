using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class TableOfContentsRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowOwnsInsertRefreshAndCompatibilityAliases()
    {
        var calls = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        TableOfContentsRibbonWorkflow.Register(
            bindings,
            new TableOfContentsRibbonPorts(
                () => calls.Add("insert"),
                () => calls.Add("refresh"),
                styleId => calls.Add($"style:{styleId}")));

        Execute(bindings, FreeWRibbonCommandAction.Toc);
        Execute(bindings, FreeWRibbonCommandAction.TocRefresh);
        calls.Should().Equal("insert", "refresh");

        bindings.TryGet("freew.insert-toc", out var insertAlias).Should().BeTrue();
        bindings.TryGet("freew.update-toc", out var refreshAlias).Should().BeTrue();
        insertAlias.Should().BeSameAs(Command(bindings, FreeWRibbonCommandAction.Toc));
        refreshAlias.Should().BeSameAs(Command(bindings, FreeWRibbonCommandAction.TocRefresh));
    }

    [Fact]
    public void AddTextChoicesApplyTheWpfAuthorityParagraphStyles()
    {
        var styles = new List<string>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        TableOfContentsRibbonWorkflow.Register(
            bindings,
            new TableOfContentsRibbonPorts(
                () => { },
                () => { },
                styles.Add));

        TableOfContentsRibbonWorkflow.StyleChoices.Should().Equal(
            new TableOfContentsStyleChoice("freew.toc-add-text", "Heading1"),
            new TableOfContentsStyleChoice("freew.toc-addtext-none", "Normal"),
            new TableOfContentsStyleChoice("freew.toc-addtext-level1", "Heading1"),
            new TableOfContentsStyleChoice("freew.toc-addtext-level2", "Heading2"),
            new TableOfContentsStyleChoice("freew.toc-addtext-level3", "Heading3"));

        foreach (var choice in TableOfContentsRibbonWorkflow.StyleChoices)
        {
            bindings.TryGet(choice.CommandId, out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }

        styles.Should().Equal("Heading1", "Normal", "Heading1", "Heading2", "Heading3");
    }

    [Fact]
    public void EditorFamilyBuilderRetainsCanonicalAndAdapterOwnership()
    {
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();

        TableOfContentsRibbonWorkflow.Register(
            builder,
            new TableOfContentsRibbonPorts(() => { }, () => { }, _ => { }));

        var family = builder.Build();
        family.Commands.Should().ContainKeys(
            FreeWRibbonCommandAction.Toc,
            FreeWRibbonCommandAction.TocRefresh);
        family.AdapterCommands.Should().ContainKeys(
            "freew.insert-toc",
            "freew.update-toc",
            "freew.toc-add-text",
            "freew.toc-addtext-none",
            "freew.toc-addtext-level1",
            "freew.toc-addtext-level2",
            "freew.toc-addtext-level3");
    }

    [Fact]
    public void BothRenderersDelegateTheTocFamilyToTheSharedWorkflow()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        wpf.Should().Contain("TableOfContentsRibbonWorkflow.Register(");
        avalonia.Should().Contain("TableOfContentsRibbonWorkflow.Register(");
        wpf.Should().NotContain("new ApplyTocStyleCommand(");
        avalonia.Should().NotContain("family.Register(\"freew.insert-toc\"");
        avalonia.Should().NotContain("family.Register(\"freew.update-toc\"");
    }

    private static void Execute(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action) =>
        Command(bindings, action).Execute(RibbonCommandContext.Empty);

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        bindings.TryGet(route.CommandId, out var command).Should().BeTrue();
        return command!;
    }
}
