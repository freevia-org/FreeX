using Free.Shared.Ribbon;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePRibbonHostProfileTests
{
    [Fact]
    public void BuildCombinesCommonFileAndOleInventoriesWithoutDuplicates()
    {
        var result = FreePRibbonHostRegistryComposer.Build(
            MakeEditor(),
            new RibbonStateStore(),
            CompleteNativeProfile());

        result.CommonCommandIds.Should().HaveCountGreaterThanOrEqualTo(221);
        result.NativeCommandIds.Should().Equal(
            FreePRibbonHostRegistryComposer.FileCommandIds
                .Concat(FreePRibbonHostRegistryComposer.OleCommandIds));
        result.AllCommandIds.Should().OnlyHaveUniqueItems();
        result.NativeCommandIds.Should().HaveCount(11);
    }

    [Fact]
    public void PortableInventoryIsSharedAndNativeInventoryContainsOnlyBackedEndpoints()
    {
        var wpf = FreePRibbonHostRegistryComposer.Build(
            MakeEditor(),
            new RibbonStateStore(),
            CreateProfile(new FreePRibbonHostPorts
            {
                OleCommands = new FreePRibbonOleCommandEndpoints
                {
                    InsertEmbeddedObject = () => { },
                    TryOpenInlineEmbeddedObject = () => false,
                },
            }));
        var avalonia = FreePRibbonHostRegistryComposer.Build(
            MakeEditor(),
            new RibbonStateStore(),
            CompleteNativeProfile());

        wpf.CommonCommandIds.Should().Equal(avalonia.CommonCommandIds);
        wpf.NativeCommandIds.Should().Equal(FreePRibbonHostRegistryComposer.OleCommandIds);
        avalonia.NativeCommandIds.Except(wpf.NativeCommandIds)
            .Should().Equal(FreePRibbonHostRegistryComposer.FileCommandIds);
    }

    [Fact]
    public void NullNativeEndpointsStayUnregisteredAndOutOfExecutableInventory()
    {
        var result = FreePRibbonHostRegistryComposer.Build(
            MakeEditor(),
            new RibbonStateStore(),
            CreateProfile(new FreePRibbonHostPorts
            {
                FileCommands = new FreePRibbonFileCommandEndpoints(),
                OleCommands = new FreePRibbonOleCommandEndpoints(),
            }));

        result.NativeCommandIds.Should().BeEmpty();
        foreach (var commandId in FreePRibbonHostRegistryComposer.FileCommandIds
                     .Concat(FreePRibbonHostRegistryComposer.OleCommandIds))
        {
            result.Registry.TryGet(commandId, out _).Should().BeFalse(
                $"{commandId} has no executable endpoint");
        }
    }

    [Fact]
    public void ProfileRoutesHostActionsQueriesAndTextThroughNativeEndpoints()
    {
        var copied = false;
        TableCellTextFormatKind? textFormat = null;
        var profile = CreateProfile(new FreePRibbonHostPorts
        {
            ActionEndpoints = new FreePRibbonHostActionEndpoints { Copy = () => copied = true },
            QueryEndpoints = new FreePRibbonHostQueryEndpoints { EditPointsEnabled = () => true },
            TextActionTargets = new FreePRibbonTextActionTargets
            {
                Notes = new FreePRibbonTextActionEndpoints
                {
                    ToggleFormat = format =>
                    {
                        textFormat = format;
                        return true;
                    },
                },
            },
        });
        var result = FreePRibbonHostRegistryComposer.Build(
            MakeEditor(),
            new RibbonStateStore(),
            profile);

        Execute(result.Registry, "freep.copy");
        Execute(result.Registry, "freep.bold");

        copied.Should().BeTrue();
        textFormat.Should().Be(TableCellTextFormatKind.Bold);
        result.Registry.TryGet(PresentationEditPointsModePlanner.CommandId, out var editPoints).Should().BeTrue();
        editPoints.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsChecked.Should().BeTrue();
    }

    [Fact]
    public void ActionPortProfileFactoryOwnsTheBoundNativeActionInventory()
    {
        var profile = FreePRibbonActionPortProfileFactory.Create(
            new FreePRibbonHostActionEndpoints
            {
                Copy = () => { },
                OpenTablePicker = () => { },
                OpenSlideShowSettings = () => { },
            });

        profile.BoundActions.Should().Equal(
            FreePRibbonHostActionKind.Copy,
            FreePRibbonHostActionKind.OpenTablePicker,
            FreePRibbonHostActionKind.OpenSlideShowSettings);
    }

    [Fact]
    public void RendererActionCompositionIsSharedAndHostProfilesOnlyConsumeIt()
    {
        var shared = TestWorkspaceFileLocator.ReadAllText(
            "freep", "RendererShared", "MainWindow.RibbonActionProfile.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Host", "MainWindow.RibbonProfile.cs");
        var avalonia = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Avalonia", "MainWindow.cs");

        shared.Should().Contain("FreePRibbonActionPortProfileFactory.Create(")
            .And.Contain("PresentationDomainContextActionKind.MergeTableCell")
            .And.Contain("PresentationDomainContextActionKind.SplitTableCell");
        wpf.Should().Contain("ActionProfile = GetRibbonActionPortProfile()")
            .And.NotContain("private FreePRibbonHostActionEndpoints CreateRibbonHostActionEndpoints");
        avalonia.Should().Contain("ActionProfile = GetRibbonActionPortProfile()")
            .And.NotContain("private FreePRibbonHostActionEndpoints GetRibbonHostActionEndpoints");
    }

    [Fact]
    public void OleActivationPrefersInlineBeforeSelectedObjectPort()
    {
        var editor = MakeEditorWithSelectedOle(out _);
        var inlineCalls = 0;
        var selectedCalls = 0;
        var profile = CreateProfile(new FreePRibbonHostPorts
        {
            OleCommands = new FreePRibbonOleCommandEndpoints
            {
                TryOpenInlineEmbeddedObject = () =>
                {
                    inlineCalls++;
                    return true;
                },
                TryOpenSelectedEmbeddedObject = _ =>
                {
                    selectedCalls++;
                    return true;
                },
            },
        });
        var result = FreePRibbonHostRegistryComposer.Build(
            editor,
            new RibbonStateStore(),
            profile);

        Execute(result.Registry, OleActivationPlanner.OpenEmbeddedObjectCommandId);

        inlineCalls.Should().Be(1);
        selectedCalls.Should().Be(0);
    }

    [Fact]
    public void BindIntoRefreshesOleSelectionAgainstReplacementEditor()
    {
        var original = MakeEditorWithSelectedOle(out var originalOle);
        var replacement = MakeEditorWithSelectedOle(out var replacementOle);
        OleObjectInfo? opened = null;
        var profile = CreateProfile(new FreePRibbonHostPorts
        {
            OleCommands = new FreePRibbonOleCommandEndpoints
            {
                TryOpenSelectedEmbeddedObject = ole =>
                {
                    opened = ole;
                    return true;
                },
            },
        });
        var stateStore = new RibbonStateStore();
        var result = FreePRibbonHostRegistryComposer.Build(original, stateStore, profile);

        FreePRibbonHostRegistryComposer.BindInto(result.Registry, replacement, stateStore, profile);
        Execute(result.Registry, OleActivationPlanner.OpenEmbeddedObjectCommandId);

        opened.Should().BeSameAs(replacementOle).And.NotBeSameAs(originalOle);
    }

    [Fact]
    public void BindingSessionRebindsExistingRendererRegistryToReplacementEditor()
    {
        var original = MakeEditor();
        var replacement = MakeEditor();
        var session = new FreePRibbonBindingSession(
            original,
            new RibbonStateStore(),
            () => CreateProfile(new FreePRibbonHostPorts()));

        session.Rebind(replacement);
        Execute(session.Registry, "freep.new-slide");

        original.Presentation.Slides.Should().ContainSingle();
        replacement.Presentation.Slides.Should().HaveCount(2);
    }

    [Fact]
    public void BindingSessionProjectsLiveStatefulCommandsIntoRendererStore()
    {
        var showState = new PresentationViewShowState(
            ShowGridlines: false,
            ShowGuides: false);
        var stateStore = new RibbonStateStore();
        var session = new FreePRibbonBindingSession(
            MakeEditor(),
            stateStore,
            () => CreateProfile(new FreePRibbonHostPorts
            {
                QueryEndpoints = new FreePRibbonHostQueryEndpoints
                {
                    ViewShowState = () => showState,
                },
            }));

        showState = showState with { ShowGridlines = true };
        session.SyncCommandStates();

        stateStore.GetState(PresentationViewShowPlanner.GridlinesCommandId)
            .IsChecked.Should().BeTrue();
        stateStore.GetState(PresentationViewShowPlanner.GuidesCommandId)
            .IsChecked.Should().BeFalse();
    }

    [Fact]
    public void EndpointCatalogsRemainExhaustive()
    {
        typeof(FreePRibbonHostQueryEndpoints).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(Enum.GetNames<FreePRibbonHostQueryKind>());
        typeof(FreePRibbonTextActionEndpoints).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(Enum.GetNames<FreePRibbonTextActionKind>());
        FreePRibbonHostRegistryComposer.FileCommandIds.Select(id => id.Value).Should().Equal(
            "freep.file.new",
            "freep.file.open",
            "freep.file.save",
            "freep.file.save-as",
            PresentationExportPlanner.PdfExportCommandId,
            PresentationExportPlanner.NotesPagePdfExportCommandId,
            PresentationExportPlanner.ImageExportCommandId,
            PresentationExportPlanner.PrintCommandId,
            PresentationExportPlanner.VideoExportCommandId);
        FreePRibbonHostRegistryComposer.OleCommandIds.Select(id => id.Value).Should().Equal(
            OleInsertionPlanner.InsertEmbeddedObjectCommandId,
            OleActivationPlanner.OpenEmbeddedObjectCommandId);
    }

    [Fact]
    public void TextDispatcherRejectsMismatchedPayloadsWithoutCallingNativeEndpoints()
    {
        var calls = 0;
        var endpoints = new FreePRibbonTextActionEndpoints
        {
            SetFontSize = _ =>
            {
                calls++;
                return true;
            },
            SetTableCellInset = (_, _) =>
            {
                calls++;
                return true;
            },
        };

        FreePRibbonTextActionDispatcher.Dispatch(
                new FreePRibbonTextAction(FreePRibbonTextActionKind.SetFontSize, "12"),
                endpoints)
            .Should().BeFalse();
        FreePRibbonTextActionDispatcher.Dispatch(
                new FreePRibbonTextAction(
                    FreePRibbonTextActionKind.SetTableCellInset,
                    TableCellInsetSide.Left,
                    "0.1"),
                endpoints)
            .Should().BeFalse();
        calls.Should().Be(0);
    }

    [Fact]
    public void TextTargetRouterUsesNotesThenShapeThenTablePrecedence()
    {
        var calls = new List<string>();
        var action = new FreePRibbonTextAction(
            FreePRibbonTextActionKind.ToggleFormat,
            TableCellTextFormatKind.Bold);
        var targets = new FreePRibbonTextActionTargets
        {
            Notes = new FreePRibbonTextActionEndpoints
            {
                ToggleFormat = _ =>
                {
                    calls.Add("notes");
                    return false;
                },
            },
            Shape = new FreePRibbonTextActionEndpoints
            {
                ToggleFormat = _ =>
                {
                    calls.Add("shape");
                    return true;
                },
            },
            Table = new FreePRibbonTextActionEndpoints
            {
                ToggleFormat = _ =>
                {
                    calls.Add("table");
                    return true;
                },
            },
        };

        FreePRibbonTextActionTargetRouter.Dispatch(action, targets).Should().BeTrue();
        calls.Should().Equal("notes", "shape");
    }

    [Fact]
    public void TextTargetRouterFallsThroughToTableWhenEarlierTargetsDecline()
    {
        var calls = new List<string>();
        var action = new FreePRibbonTextAction(
            FreePRibbonTextActionKind.SetFontFamily,
            "Aptos");
        var targets = new FreePRibbonTextActionTargets
        {
            Notes = new FreePRibbonTextActionEndpoints
            {
                SetFontFamily = _ =>
                {
                    calls.Add("notes");
                    return false;
                },
            },
            Shape = new FreePRibbonTextActionEndpoints
            {
                SetFontFamily = _ =>
                {
                    calls.Add("shape");
                    return false;
                },
            },
            Table = new FreePRibbonTextActionEndpoints
            {
                SetFontFamily = _ =>
                {
                    calls.Add("table");
                    return true;
                },
            },
        };

        FreePRibbonTextActionTargetRouter.Dispatch(action, targets).Should().BeTrue();
        calls.Should().Equal("notes", "shape", "table");
    }

    private static FreePRibbonHostProfile CompleteNativeProfile() => CreateProfile(
        new FreePRibbonHostPorts
        {
            FileCommands = new FreePRibbonFileCommandEndpoints
            {
                New = () => { },
                Open = () => { },
                Save = () => { },
                SaveAs = () => { },
                ExportPdf = () => { },
                ExportNotesPagePdf = () => { },
                ExportImages = () => { },
                Print = () => { },
                ExportVideo = () => { },
            },
            OleCommands = new FreePRibbonOleCommandEndpoints
            {
                InsertEmbeddedObject = () => { },
                TryOpenInlineEmbeddedObject = () => false,
            },
        });

    private static FreePRibbonHostProfile CreateProfile(FreePRibbonHostPorts ports) =>
        FreePRibbonHostProfileFactory.Create(ports);

    private static EditingSession MakeEditor()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static EditingSession MakeEditorWithSelectedOle(out OleObjectInfo ole)
    {
        var editor = MakeEditor();
        ole = new OleObjectInfo
        {
            ProgId = "FreeP.Test",
            EmbeddedBytes = [1, 2, 3],
        };
        var shape = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Ole,
            OleObject = ole,
        };
        editor.Presentation.Slides[0].Shapes.Add(shape);
        editor.Select(shape.Id);
        return editor;
    }

    private static void Execute(RibbonCommandRegistry registry, RibbonCommandId commandId)
    {
        registry.TryGet(commandId, out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }
}
