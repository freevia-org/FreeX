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
    public void BoldToggleChecked_ReflectsAlreadyBoldSelection_NotAClickParityFlag()
    {
        var editor = MakeEditor();
        var shape = MakeShape(7);
        var run = new Run { Text = "hi", Bold = true };
        shape.TextBody = new TextBody { Paragraphs = { new Paragraph { Runs = { run } } } };
        editor.CurrentSlide!.Shapes.Add(shape);
        editor.Select(shape.Id);

        var result = FreePRibbonCommandWorkflow.Build(editor, new RibbonStateStore());
        var command = Stateful(result.Registry, "freep.bold");

        // The selection is already all-bold. Before any click, the ribbon must show it checked --
        // a document-blind click-parity flag starts every command at false regardless of the
        // selection, which is exactly finding F1's bug #1.
        command.GetState().IsChecked.Should().BeTrue(
            "the selected run is already bold, so the ribbon should show Bold as checked before any click");

        // Clicking Bold on an all-bold selection is a majority-rule toggle: it turns bold OFF.
        command.Execute(RibbonCommandContext.Empty);
        run.Bold.Should().BeFalse("EditingSession.ToggleBoldOnSelection un-bolds an all-bold selection");

        // The ribbon button must agree with what just happened to the document, not flip to
        // CHECKED at the exact moment the text became not-bold (finding F1's bug #2).
        command.GetState().IsChecked.Should().BeFalse();
    }

    [Fact]
    public void BoldToggleChecked_UpdatesWhenSelectionMovesBetweenBoldAndPlainRuns()
    {
        var editor = MakeEditor();
        var boldShape = MakeShape(8);
        boldShape.TextBody = new TextBody
        {
            Paragraphs = { new Paragraph { Runs = { new Run { Text = "bold", Bold = true } } } },
        };
        var plainShape = MakeShape(9);
        plainShape.TextBody = new TextBody
        {
            Paragraphs = { new Paragraph { Runs = { new Run { Text = "plain", Bold = false } } } },
        };
        editor.CurrentSlide!.Shapes.Add(boldShape);
        editor.CurrentSlide!.Shapes.Add(plainShape);

        var result = FreePRibbonCommandWorkflow.Build(editor, new RibbonStateStore());
        var command = Stateful(result.Registry, "freep.bold");

        editor.Select(boldShape.Id);
        command.GetState().IsChecked.Should().BeTrue("the selected shape's only run is bold");

        // Moving the selection to a non-bold shape must flip the button without any click --
        // a click-parity flag never re-derives from the new selection (finding F1's bug #3).
        editor.Select(plainShape.Id);
        command.GetState().IsChecked.Should().BeFalse("the newly selected shape's only run is not bold");
    }

    [Fact]
    public void TransitionSoundLoop_IsStatefulAndTracksCurrentSlideSound()
    {
        var editor = MakeEditor();
        var store = new RibbonStateStore();
        var registry = FreePRibbonCommandWorkflow.Build(editor, store).Registry;
        var command = Stateful(registry, "freep.transition.sound-loop");

        command.GetState().Should().Match<RibbonCommandState>(state =>
            !state.IsEnabled && !state.IsChecked);

        editor.SetTransition(new SlideTransition
        {
            Kind = TransitionKind.Fade,
            Sound = new TransitionSound { ContentType = "audio/wav", Loop = false },
        });
        command.GetState().Should().Match<RibbonCommandState>(state =>
            state.IsEnabled && !state.IsChecked);

        command.Execute(RibbonCommandContext.Empty);

        editor.CurrentSlideTransition!.Sound!.Loop.Should().BeTrue();
        command.GetState().Should().Match<RibbonCommandState>(state =>
            state.IsEnabled && state.IsChecked);
        store.GetState("freep.transition.sound-loop").IsChecked.Should().BeTrue();
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
    public void TableMergeAndSplitAvailabilityComesFromTheSharedCellPlanner()
    {
        var editor = MakeEditor();
        var table = editor.InsertTable(1, 2);
        editor.Select(table.Id);
        editor.SetActiveTableCell(0, 0);
        var registry = FreePRibbonCommandWorkflow.Build(editor, new RibbonStateStore()).Registry;

        Stateful(registry, TableCellEditPlanner.MergeCellsCommandId)
            .GetState().IsEnabled.Should().BeTrue();
        Stateful(registry, TableCellEditPlanner.SplitCellCommandId)
            .GetState().IsEnabled.Should().BeFalse();

        Execute(registry, TableCellEditPlanner.MergeCellsCommandId);

        Stateful(registry, TableCellEditPlanner.MergeCellsCommandId)
            .GetState().IsEnabled.Should().BeFalse();
        Stateful(registry, TableCellEditPlanner.SplitCellCommandId)
            .GetState().IsEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(TableCellEditPlanner.DistributeRowsCommandId, PresentationDomainContextActionKind.DistributeTableRows)]
    [InlineData(TableCellEditPlanner.DistributeColumnsCommandId, PresentationDomainContextActionKind.DistributeTableColumns)]
    [InlineData(TableCellEditPlanner.InsertRowAboveCommandId, PresentationDomainContextActionKind.InsertTableRowAbove)]
    [InlineData(TableCellEditPlanner.InsertRowBelowCommandId, PresentationDomainContextActionKind.InsertTableRowBelow)]
    [InlineData(TableCellEditPlanner.InsertColumnLeftCommandId, PresentationDomainContextActionKind.InsertTableColumnLeft)]
    [InlineData(TableCellEditPlanner.InsertColumnRightCommandId, PresentationDomainContextActionKind.InsertTableColumnRight)]
    [InlineData(TableCellEditPlanner.DeleteRowCommandId, PresentationDomainContextActionKind.DeleteTableRow)]
    [InlineData(TableCellEditPlanner.DeleteColumnCommandId, PresentationDomainContextActionKind.DeleteTableColumn)]
    public void TableStructureCommandsPreferTheTypedCommitFirstHostRoute(
        string commandId,
        PresentationDomainContextActionKind expectedKind)
    {
        var editor = MakeEditorWithActiveTable();
        FreePRibbonHostAction? routed = null;
        var registry = FreePRibbonCommandWorkflow.Build(
            editor,
            new RibbonStateStore(),
            new FreePRibbonCommandHostAdapter
            {
                TryExecuteAction = action =>
                {
                    routed = action;
                    return true;
                },
            }).Registry;
        var before = TableSignature(editor);

        Execute(registry, commandId);

        routed.Should().Be(new FreePRibbonHostAction(
            FreePRibbonHostActionKind.ExecuteTableStructureAction,
            expectedKind));
        TableSignature(editor).Should().Be(before, "the native editor accepted the command");
    }

    [Fact]
    public void TableStructureCommandFallsBackToTheModelWhenTheNativeEditorDeclines()
    {
        var editor = MakeEditorWithActiveTable();
        var registry = FreePRibbonCommandWorkflow.Build(
            editor,
            new RibbonStateStore(),
            new FreePRibbonCommandHostAdapter { TryExecuteAction = _ => false }).Registry;

        Execute(registry, TableCellEditPlanner.InsertRowBelowCommandId);

        editor.CurrentSlide!.Shapes.Single().Table!.Rows.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(TableCellEditPlanner.TableFirstRowCommandId, TableStyleFlagKind.FirstRow, true)]
    [InlineData(TableCellEditPlanner.TableLastRowCommandId, TableStyleFlagKind.LastRow, false)]
    [InlineData(TableCellEditPlanner.TableFirstColCommandId, TableStyleFlagKind.FirstCol, false)]
    [InlineData(TableCellEditPlanner.TableLastColCommandId, TableStyleFlagKind.LastCol, false)]
    [InlineData(TableCellEditPlanner.TableBandRowCommandId, TableStyleFlagKind.BandRow, true)]
    [InlineData(TableCellEditPlanner.TableBandColCommandId, TableStyleFlagKind.BandCol, false)]
    public void TableStyleFlagsExposeLiveCheckedAvailabilityAndUndoState(
        string commandId,
        TableStyleFlagKind kind,
        bool initialValue)
    {
        var editor = MakeEditor();
        var result = FreePRibbonCommandWorkflow.Build(editor, new RibbonStateStore());
        var command = Stateful(result.Registry, commandId);

        command.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: false, IsChecked: false));

        var table = editor.InsertTable(2, 2);
        editor.Select(table.Id);
        editor.TryGetSelectedTableStyleFlag(kind, out var selectedValue).Should().BeTrue();
        selectedValue.Should().Be(initialValue);
        command.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: true, IsChecked: initialValue));

        command.Execute(RibbonCommandContext.Empty);

        command.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: true, IsChecked: !initialValue));

        editor.Undo();

        command.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: true, IsChecked: initialValue));

        editor.ClearSelection();
        command.GetState().Should().Be(
            new RibbonCommandState(IsEnabled: false, IsChecked: false));
    }

    [Fact]
    public void AnimationPaneCheckedStateTracksTheLiveRendererQuery()
    {
        var visible = false;
        var result = FreePRibbonCommandWorkflow.Build(
            MakeEditor(),
            new RibbonStateStore(),
            new FreePRibbonCommandHostAdapter
            {
                QueryState = query => query.Kind == FreePRibbonHostQueryKind.AnimationPaneVisible
                    ? visible
                    : null,
            });
        var command = Stateful(result.Registry, "freep.anim.pane");

        command.GetState().IsChecked.Should().BeFalse();

        visible = true;

        command.GetState().IsChecked.Should().BeTrue();
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
        var sharedActionProfile = Read(
            root,
            "freep",
            "RendererShared",
            "MainWindow.RibbonActionProfile.cs");
        var wpfWorkareaEndpoint = Read(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.WorkareaEndpoint.cs");
        var avaloniaRegistry = Slice(
            avalonia,
            "internal RibbonCommandRegistry BuildCommandRegistry()",
            "private void OnCustomSlideSizeRequested");

        wpfMain.Should().Contain("new FreePRibbonBindingSession(")
            .And.Contain("CreateRibbonHostProfile);");
        wpf.Should().Contain("FreePRibbonHostProfileFactory.Create(new FreePRibbonHostPorts")
            .And.Contain("new FreePRibbonOleCommandEndpoints")
            .And.Contain("AnimationPaneVisible = () => IsAnimationPaneVisible")
            .And.Contain("FreePRibbonTextActionTargets");
        sharedActionProfile.Should().Contain("ExecuteTableStructureAction = kind =>")
            .And.Contain("ExecuteCurrentTableAction(kind, TryExecuteInlineTableAction)");
        wpfMain.Should().Contain("internal bool IsAnimationPaneVisible");
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
        avaloniaRegistry.Should().Contain("new FreePRibbonBindingSession(")
            .And.Contain("FreePRibbonHostProfileFactory.Create(new FreePRibbonHostPorts")
            .And.Contain("new FreePRibbonFileCommandEndpoints")
            .And.Contain("new FreePRibbonOleCommandEndpoints")
            .And.Contain("new FreePRibbonHostQueryEndpoints")
            .And.Contain("AnimationPaneVisible = () => IsAnimationPaneVisible")
            .And.Contain("FreePRibbonTextActionTargets");
        var workflow = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "Ribbon",
            "FreePRibbonCommandWorkflow.cs");
        workflow.Should().Contain("TryExecuteAction")
            .And.Contain("ExecuteTableStructureAction, actionKind")
            .And.NotContain("static () => false, execute");
        wpf.Should().NotContain("CanMergeTableCells =")
            .And.NotContain("CanSplitTableCell =");
        avaloniaRegistry.Should().NotContain("CanMergeTableCells =")
            .And.NotContain("CanSplitTableCell =");
        avaloniaRegistry.Should().NotContain("freep.bold")
            .And.NotContain("SmartArtAuthoringPlanner.ThemeAccentsCommandId")
            .And.NotContain("PresentationTransitionCommandPlanner.BuiltInPlans")
            .And.NotContain("registry.Register(")
            .And.NotContain("FreePRibbonHostActionDispatcher.Dispatch(")
            .And.NotContain("new FreePRibbonHostProfile")
            .And.NotContain("BuildRibbonTextActionEndpoints")
            .And.NotContain("DesignRequest =");
        wpfWorkareaEndpoint.Should().Contain("_ribbonBindingSession?.Rebind(editor)")
            .And.Contain("RefreshCommandStates = SyncRibbonCommandStates");
        avaloniaWorkareaEndpoint.Should().Contain("_ribbonBindingSession?.Rebind(editor)")
            .And.Contain("RefreshCommandStates = SyncRibbonCommandStates");
        wpfWorkareaEndpoint.Should().NotContain("FreePRibbonHostRegistryComposer.BindInto(");
        avaloniaWorkareaEndpoint.Should().NotContain("FreePRibbonHostRegistryComposer.BindInto(");
        avalonia.Should().NotContain("TransitionAdvanceOnClickToggleCommand")
            .And.NotContain("AnimationPaneToggleCommand")
            .And.NotContain("ViewShowToggleCommand")
            .And.NotContain("RegisterReviewWorkflowCommands");
    }

    [Fact]
    public void BothRenderersRouteActiveTableParagraphActionsThroughTheNativeSelectionAdapter()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpfProfile = Read(root, "freep", "FreeP.App.Host", "MainWindow.RibbonProfile.cs");
        var wpfEditor = Read(root, "freep", "FreeP.App.Rendering.Wpf", "InCanvasTableCellEditor.cs");
        var wpfImports = Read(root, "freep", "FreeP.App.Host", "MainWindow.AssetImports.cs");
        var avalonia = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");

        string[] endpointMethods =
        [
            "TryApplyActiveTableCellParagraphAlignment",
            "TryApplyActiveTableCellParagraphListPreset",
            "TryApplyActiveTableCellParagraphBulletToggle",
            "TryApplyActiveTableCellParagraphNumberingToggle",
            "TryApplyActiveTableCellParagraphIndent",
            "TryApplyActiveTableCellParagraphOutdent",
        ];

        foreach (var method in endpointMethods)
        {
            wpfProfile.Should().Contain($"canvas.TableCellEditor?.{method}");
            avalonia.Should().Contain($"_textEditor?.{method}");
        }

        wpfEditor.Should().Contain("TextBodyFlowDocumentConverter.LogicalOffsetAt(")
            .And.Contain("InCanvasTextEditPlanner.ApplyParagraphAlignment(")
            .And.Contain("InCanvasTextEditPlanner.ApplyParagraphListPreset(")
            .And.Contain("InCanvasTextEditPlanner.ApplyParagraphBulletToggle(")
            .And.Contain("InCanvasTextEditPlanner.ApplyParagraphNumberingToggle(")
            .And.Contain("InCanvasTextEditPlanner.ApplyParagraphIndent(");
        wpfImports.Should().Contain(
            "SlideCanvas.TableCellEditor?.TryApplyActiveTableCellParagraphPictureBullet(payload)");
    }

    private static EditingSession MakeEditor()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static SlideShape MakeShape(uint id) => new()
    {
        Id          = id,
        Name        = $"S{id}",
        Kind        = SlideShapeKind.AutoShape,
        OffsetXEmu  = 0,
        OffsetYEmu  = 0,
        ExtentCxEmu = 100,
        ExtentCyEmu = 100,
    };

    private static EditingSession MakeEditorWithActiveTable()
    {
        var editor = MakeEditor();
        var table = editor.InsertTable(2, 2);
        editor.Select(table.Id);
        editor.SetActiveTableCell(0, 0);
        return editor;
    }

    private static string TableSignature(EditingSession editor)
    {
        var table = editor.CurrentSlide!.Shapes.Single().Table!;
        return $"{table.Rows.Count}:{table.ColumnWidthsEmu.Count}:"
            + string.Join(";", table.Rows.Select(row => row.Cells.Count));
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

    private static IRibbonStatefulCommand Stateful(
        RibbonCommandRegistry registry,
        string commandId)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue();
        command.Should().BeAssignableTo<IRibbonStatefulCommand>();
        return (IRibbonStatefulCommand)command!;
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
