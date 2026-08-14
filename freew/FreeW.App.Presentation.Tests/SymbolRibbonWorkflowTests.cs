using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class SymbolRibbonWorkflowTests
{
    [Fact]
    public void RegistersEveryCatalogRoute()
    {
        var registry = new RibbonCommandRegistry();
        SymbolRibbonWorkflow.Register(registry, CreatePorts([]));

        SymbolRibbonWorkflow.Choices.Should().HaveCount(20);
        SymbolRibbonWorkflow.Choices.Select(choice => choice.CommandId)
            .Should().OnlyHaveUniqueItems();
        foreach (var choice in SymbolRibbonWorkflow.Choices)
            Command(registry, choice.CommandId);
    }

    [Fact]
    public void SharedMappingsPrepareThenInsertExactCatalogPayloads()
    {
        var registry = new RibbonCommandRegistry();
        var events = new List<string>();
        SymbolRibbonWorkflow.Register(registry, CreatePorts(events));

        Execute("freew.symbol.euro");
        Execute("freew.symbol.copyright");
        Execute("freew.symbol.notequal");
        Execute("freew.symbol.emdash");
        Execute("freew.symbol.arrow-right");

        events.Should().Equal(
            "prepare", "insert:€",
            "prepare", "insert:©",
            "prepare", "insert:≠",
            "prepare", "insert:—",
            "prepare", "insert:→");

        void Execute(string id) => Command(registry, id).Execute(RibbonCommandContext.Empty);
    }

    [Fact]
    public void BothRenderersAndDefinitionsDelegateSymbolIdentityToPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");
        var canonical = Read(root, "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs");
        var definitionsTest = Read(root, "freew", "FreeW.Ribbon.Definitions.Tests", "FreeWRibbonDefinitionProfileTests.cs");
        var definitionData = Read(root, "freew", "FreeW.Ribbon.Definitions", "FreeWRibbonDefinitionData.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("SymbolRibbonWorkflow.Register(");
            source.Should().Contain("new SymbolRibbonPorts(");
        }

        avalonia.Should().NotContain("RegisterSymbolPalette(");
        avalonia.Should().NotContain("FreeWRibbonDefinitionData.Symbols");
        canonical.Should().Contain("SymbolRibbonWorkflow.Choices");
        canonical.Should().Contain("BuildSymbolMenu()");
        canonical.Should().Contain("g.SplitButton(\"freew.symbol\"");
        definitionsTest.Should().Contain("SymbolRibbonWorkflow.Choices");
        definitionData.Should().NotContain("freew.symbol.euro");
    }

    private static SymbolRibbonPorts CreatePorts(ICollection<string> events) =>
        new(
            PrepareExecution: () => events.Add("prepare"),
            InsertSymbol: glyph => events.Add($"insert:{glyph}"));

    private static IRibbonCommand Command(IRibbonCommandRegistry registry, string id)
    {
        registry.TryGet(id, out var command).Should().BeTrue($"{id} should be registered");
        return command!;
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));
}
