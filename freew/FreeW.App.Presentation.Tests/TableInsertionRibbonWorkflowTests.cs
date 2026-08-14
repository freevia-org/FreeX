using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class TableInsertionRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRegistersCanonicalChoicesAndLegacyRoutes()
    {
        var inserted = new List<(int Rows, int Columns)>();
        var bindings = new RibbonCommandRegistry();

        TableInsertionRibbonWorkflow.Register(
            bindings,
            new TableInsertionRibbonPorts((rows, columns) => inserted.Add((rows, columns))));

        foreach (var choice in TableInsertionRibbonWorkflow.Choices)
        {
            bindings.TryGet(choice.CommandId, out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }

        inserted.Should().Equal((2, 2), (3, 3), (4, 4), (2, 5));

        bindings.TryGet("freew.table", out var tableFace).Should().BeTrue();
        bindings.TryGet("freew.table-2x2", out var twoByTwo).Should().BeTrue();
        tableFace.Should().BeSameAs(twoByTwo);

        bindings.TryGet("freew.insert-table", out var legacy).Should().BeTrue();
        bindings.TryGet("freew.table-3x3", out var threeByThree).Should().BeTrue();
        legacy.Should().BeSameAs(threeByThree);
    }

    [Fact]
    public void CanonicalChoicesMatchTheSharedRibbonMenuDimensions()
    {
        TableInsertionRibbonWorkflow.Choices.Should().Equal(
            new TableInsertionChoice("freew.table-2x2", 2, 2),
            new TableInsertionChoice("freew.table-3x3", 3, 3),
            new TableInsertionChoice("freew.table-4x4", 4, 4),
            new TableInsertionChoice("freew.table-5x2", 2, 5));
    }

    [Fact]
    public void EditorFamilyBuilderReceivesTheSameCanonicalAndAdapterCommands()
    {
        var inserted = new List<(int Rows, int Columns)>();
        var builder = new FreeWRibbonEditorCommandFamilyBuilder();

        TableInsertionRibbonWorkflow.Register(
            builder,
            new TableInsertionRibbonPorts((rows, columns) => inserted.Add((rows, columns))));

        var family = builder.Build();
        family.Commands[FreeWRibbonCommandAction.Table].Execute(RibbonCommandContext.Empty);
        family.AdapterCommands!["freew.table-4x4"].Execute(RibbonCommandContext.Empty);
        family.AdapterCommands["freew.insert-table"].Execute(RibbonCommandContext.Empty);

        inserted.Should().Equal((2, 2), (4, 4), (3, 3));
        family.Commands[FreeWRibbonCommandAction.Table]
            .Should().BeSameAs(family.AdapterCommands["freew.table-2x2"]);
        family.AdapterCommands["freew.insert-table"]
            .Should().BeSameAs(family.AdapterCommands["freew.table-3x3"]);
    }

    [Fact]
    public void BothRenderersDelegateTableInsertionPolicyToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("TableInsertionRibbonWorkflow.Register(");
            source.Should().NotContain("Register(\"freew.table-2x2\"");
            source.Should().NotContain("Register(\"freew.table-3x3\"");
            source.Should().NotContain("Register(\"freew.table-4x4\"");
            source.Should().NotContain("Register(\"freew.table-5x2\"");
        }
    }
}
