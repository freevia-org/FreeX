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
        result.CommonCommandIds.Should().Contain(SmartArtAuthoringPlanner.VerticalPictureListLayoutCommandId);
        result.CommonCommandIds.Should().Contain("freep.strikethrough");
        result.CommonCommandIds.Should().Contain(TableCellEditPlanner.DistributeRowsCommandId);
        result.CommonCommandIds.Should().Contain(TableCellEditPlanner.InsertRowAboveCommandId);
        result.CommonCommandIds.Should().Contain(TableCellEditPlanner.DeleteColumnCommandId);
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
    public void TextChoiceCommandsAcceptStableTokensAndTypedDescriptors()
    {
        var editor = MakeEditor();
        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            TextBody = new TextBody(),
        };
        editor.CurrentSlide!.Shapes.Add(shape);
        editor.Select(shape.Id);
        var registry = FreePRibbonCommandWorkflow.Build(editor, new RibbonStateStore()).Registry;

        Execute(registry, "freep.text-autofit", SelectedValue("text-autofit.normal"));
        Execute(registry, "freep.text-direction", SelectedValue(TextVerticalType.Vertical270));
        Execute(registry, "freep.text-columns", SelectedValue("text-columns.4"));
        Execute(registry, "freep.text-column-spacing", SelectedValue(152_400L));

        shape.TextBody.AutoFitKind.Should().Be(TextAutoFitKind.Normal);
        shape.TextBody.VerticalType.Should().Be(TextVerticalType.Vertical270);
        shape.TextBody.ColumnCount.Should().Be(4);
        shape.TextBody.ColumnSpacingEmu.Should().Be(152_400);
    }

    [Fact]
    public void TableChoiceCommandsAcceptStableTokensTypedDescriptorsAndLegacyLabels()
    {
        var editor = MakeEditor();
        var shape = editor.InsertTable(1, 1);
        editor.Select(shape.Id);
        editor.SetActiveTableCell(0, 0);
        var registry = FreePRibbonCommandWorkflow.Build(editor, new RibbonStateStore()).Registry;

        Execute(registry, "freep.table-cell-fill", SelectedValue("color.blue"));
        Execute(registry, "freep.table-cell-anchor", SelectedValue("table-cell-anchor.bottom"));
        Execute(registry, "freep.table-cell-border", SelectedValue("table-cell-border.left.none"));
        Execute(
            registry,
            "freep.table-cell-inset",
            SelectedValue(new FreePRibbonTableCellInsetChoiceDescriptor(TableCellInsetSide.All, 4.0)));
        Execute(registry, "freep.table-row-height", RibbonCommandContext.ForSelectedValue("0.75in"));

        var cell = shape.Table!.Rows[0].Cells[0];
        cell.Fill.Should().BeOfType<ShapeFill.Solid>()
            .Which.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x0000FF));
        cell.Anchor.Should().Be(TableCellAnchor.Bottom);
        cell.Borders!.Left.Should().BeSameAs(ShapeOutline.None.Instance);
        cell.InsetLeftPt.Should().Be(4.0);
        cell.InsetBottomPt.Should().Be(4.0);
        shape.Table.Rows[0].HeightEmu.Should().Be(685_800);
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
        var wpf = Read(root, "freep", "FreeP.App.Host", "MainWindow.RibbonProfile.cs");
        var wpfMain = Read(root, "freep", "FreeP.App.Host", "MainWindow.cs");
        var avalonia = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var avaloniaWorkareaEndpoint = Read(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.WorkareaEndpoint.cs");
        var avaloniaRegistry = Slice(
            avalonia,
            "internal RibbonCommandRegistry BuildCommandRegistry()",
            "private void OnCustomSlideSizeRequested");

        wpfMain.Should().Contain("FreePRibbonHostRegistryComposer.Build(")
            .And.Contain("CreateRibbonHostProfile()");
        wpf.Should().Contain("FreePRibbonHostProfileFactory.Create(new FreePRibbonHostPorts")
            .And.Contain("new FreePRibbonOleCommandEndpoints")
            .And.Contain("FreePRibbonTextActionTargets");
        wpf.Should().NotContain("registry.Register(")
            .And.NotContain("new FreePRibbonCommandHostAdapter")
            .And.NotContain("FreePRibbonHostActionDispatcher.Dispatch(")
            .And.NotContain("new FreePRibbonHostProfile")
            .And.NotContain("BuildTextActionEndpoints")
            .And.NotContain("DesignRequest =")
            .And.NotContain("ApplyBuiltInInsertion")
            .And.NotContain("ExecuteHeaderFooter")
            .And.NotContain("ExecuteDesignRequest");
        File.Exists(Path.Combine(root, "freep", "FreeP.App.Host", "FreePRibbonCommands.cs"))
            .Should().BeFalse("WPF composes the portable host profile directly");
        avaloniaRegistry.Should().Contain("FreePRibbonHostRegistryComposer.Build(")
            .And.Contain("FreePRibbonHostProfileFactory.Create(new FreePRibbonHostPorts")
            .And.Contain("new FreePRibbonFileCommandEndpoints")
            .And.Contain("new FreePRibbonOleCommandEndpoints")
            .And.Contain("new FreePRibbonHostQueryEndpoints")
            .And.Contain("FreePRibbonTextActionTargets");
        avaloniaRegistry.Should().NotContain("freep.bold")
            .And.NotContain("SmartArtAuthoringPlanner.ThemeAccentsCommandId")
            .And.NotContain("PresentationTransitionCommandPlanner.BuiltInPlans")
            .And.NotContain("registry.Register(")
            .And.NotContain("FreePRibbonHostActionDispatcher.Dispatch(")
            .And.NotContain("new FreePRibbonHostProfile")
            .And.NotContain("BuildRibbonTextActionEndpoints")
            .And.NotContain("DesignRequest =");
        avaloniaWorkareaEndpoint.Should().Contain("FreePRibbonHostRegistryComposer.BindInto(");
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
        Execute(registry, commandId, RibbonCommandContext.Empty);
    }

    private static void Execute(
        RibbonCommandRegistry registry,
        string commandId,
        RibbonCommandContext context)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(context);
    }

    private static RibbonCommandContext SelectedValue(object? value) =>
        new(new Dictionary<string, object?>
        {
            [RibbonCommandContext.SelectedValueKey] = value,
        });

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
