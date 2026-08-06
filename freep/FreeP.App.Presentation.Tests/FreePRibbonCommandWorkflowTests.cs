using Free.Shared.Ribbon;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePRibbonCommandWorkflowTests
{
    [Fact]
    public void BuildOwnsAGroupedUniqueCommonCommandInventory()
    {
        var result = FreePRibbonCommandWorkflow.Build(MakeEditor(), new RibbonStateStore());

        result.CommandGroups.Keys.Should().BeEquivalentTo(Enum.GetValues<FreePRibbonCommandGroup>());
        result.CommonCommandIds.Should().OnlyHaveUniqueItems();
        result.CommonCommandIds.Should().HaveCountGreaterThanOrEqualTo(221);
        result.CommonCommandIds.Should().Contain(SmartArtAuthoringPlanner.TableHierarchyLayoutCommandId);
        result.CommonCommandIds.Should().Contain(PresentationDesignCommandPlanner.LayoutCommandId);
        result.CommonCommandIds.Should().Contain("freep.transition.advance-on-click");
        result.CommonCommandIds.Should().Contain(PresentationSelectionPanePlanner.SelectionPaneCommandId);
    }

    [Theory]
    [InlineData(SmartArtAuthoringPlanner.ThemeAccentsCommandId, FreePRibbonHostActionKind.ApplySmartArtColor)]
    [InlineData(ChartDisplayOptionsPlanner.CommandId, FreePRibbonHostActionKind.OpenChartDisplayOptions)]
    [InlineData(PresentationReviewWorkflowPlanner.CommentsPaneCommandId, FreePRibbonHostActionKind.ShowCommentsPane)]
    [InlineData(SlideZoomInsertionPlanner.CommandId, FreePRibbonHostActionKind.InsertSlideZoom)]
    [InlineData(PresentationDesignCommandPlanner.LayoutCommandId, FreePRibbonHostActionKind.DesignRequest)]
    public void HostCommandsUseSharedTypedRouting(string commandId, FreePRibbonHostActionKind expectedKind)
    {
        FreePRibbonHostAction? dispatched = null;
        var result = FreePRibbonCommandWorkflow.Build(
            MakeEditor(),
            new RibbonStateStore(),
            new FreePRibbonCommandHostAdapter { ExecuteAction = action => dispatched = action });

        Execute(result.Registry, commandId);

        dispatched.Should().NotBeNull();
        dispatched!.Kind.Should().Be(expectedKind);
    }

    [Fact]
    public void TextCommandsPreferNativeAdapterAndShareCheckedStatePolicy()
    {
        FreePRibbonTextAction? routed = null;
        var stateStore = new RibbonStateStore();
        var result = FreePRibbonCommandWorkflow.Build(
            MakeEditor(),
            stateStore,
            new FreePRibbonCommandHostAdapter
            {
                TryHandleTextAction = action =>
                {
                    routed = action;
                    return true;
                },
            });

        Execute(result.Registry, "freep.bold");

        routed.Should().Be(new FreePRibbonTextAction(
            FreePRibbonTextActionKind.ToggleFormat,
            TableCellTextFormatKind.Bold));
        stateStore.GetState("freep.bold").IsChecked.Should().BeTrue();
    }

    [Fact]
    public void ListGalleryOwnerCommandsAcceptExistingPresetIds()
    {
        var editor = MakeEditor();
        var table = editor.InsertTable(1, 1);
        table.Table!.Rows[0].Cells[0].TextBody = new TextBody
        {
            Paragraphs = { new Paragraph { Runs = { new Run { Text = "Cell" } } } },
        };
        editor.Select(table.Id);
        editor.SetActiveTableCell(0, 0);
        var result = FreePRibbonCommandWorkflow.Build(editor, new RibbonStateStore());

        result.Registry.TryGet("freep.numbering", out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.ForSelectedValue(TableCellListPresetCatalog.NumberAlphaLowerPeriodId));

        var paragraph = table.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Auto);
        paragraph.AutoNumType.Should().Be(AutoNumType.AlphaLcPeriod);
    }

    [Fact]
    public void BindIntoRetargetsAnExistingRendererRegistryToTheReplacementEditor()
    {
        var original = MakeEditor();
        var replacement = MakeEditor();
        var stateStore = new RibbonStateStore();
        var registry = FreePRibbonCommandWorkflow.Build(original, stateStore).Registry;

        FreePRibbonCommandWorkflow.BindInto(registry, replacement, stateStore);
        Execute(registry, "freep.new-slide");

        original.Presentation.Slides.Should().ContainSingle();
        replacement.Presentation.Slides.Should().HaveCount(2);
    }

    [Fact]
    public void RendererSourcesDelegateCommonRegistrationOwnership()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = Read(root, "freep", "FreeP.App.Host", "FreePRibbonCommands.cs");
        var avalonia = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var avaloniaRegistry = Slice(
            avalonia,
            "internal RibbonCommandRegistry BuildCommandRegistry()",
            "private void ExecuteRibbonHostAction");

        wpf.Should().Contain("FreePRibbonCommandWorkflow.Build(editor, stateStore, host)");
        Count(wpf, "registry.Register(").Should().Be(2, "only native OLE insertion and activation remain local");
        avaloniaRegistry.Should().Contain("FreePRibbonCommandWorkflow.Build(Editor, _ribbonStateStore, host)");
        avaloniaRegistry.Should().NotContain("freep.bold")
            .And.NotContain("SmartArtAuthoringPlanner.ThemeAccentsCommandId")
            .And.NotContain("PresentationTransitionCommandPlanner.BuiltInPlans");
        Count(avaloniaRegistry, "registry.Register(").Should().Be(11, "only file/export and native OLE commands remain local");
        avalonia.Should().Contain("FreePRibbonCommandWorkflow.BindInto(");
        avalonia.Should().NotContain("TransitionAdvanceOnClickToggleCommand")
            .And.NotContain("AnimationPaneToggleCommand")
            .And.NotContain("ViewShowToggleCommand")
            .And.NotContain("RegisterReviewWorkflowCommands");
    }

    private static EditingSession MakeEditor()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static void Execute(RibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
