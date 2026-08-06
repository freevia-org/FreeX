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
    public void WpfAndAvaloniaProfilesShareTheExactPortableRegistryInventory()
    {
        var wpf = FreePRibbonHostRegistryComposer.Build(
            MakeEditor(),
            new RibbonStateStore(),
            new FreePRibbonHostProfile { OleCommands = new FreePRibbonOleCommandEndpoints() });
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
    public void ProfileRoutesHostActionsQueriesAndTextThroughNativeEndpoints()
    {
        var copied = false;
        FreePRibbonTextAction? textAction = null;
        var profile = new FreePRibbonHostProfile
        {
            ActionEndpoints = new FreePRibbonHostActionEndpoints { Copy = () => copied = true },
            QueryEndpoints = new FreePRibbonHostQueryEndpoints { EditPointsEnabled = () => true },
            TryHandleTextAction = action =>
            {
                textAction = action;
                return true;
            },
        };
        var result = FreePRibbonHostRegistryComposer.Build(
            MakeEditor(),
            new RibbonStateStore(),
            profile);

        Execute(result.Registry, "freep.copy");
        Execute(result.Registry, "freep.bold");

        copied.Should().BeTrue();
        textAction.Should().Be(new FreePRibbonTextAction(
            FreePRibbonTextActionKind.ToggleFormat,
            TableCellTextFormatKind.Bold));
        result.Registry.TryGet(PresentationEditPointsModePlanner.CommandId, out var editPoints).Should().BeTrue();
        editPoints.Should().BeAssignableTo<IRibbonStatefulCommand>()
            .Which.GetState().IsChecked.Should().BeTrue();
    }

    [Fact]
    public void OleActivationPrefersInlineBeforeSelectedObjectPort()
    {
        var editor = MakeEditorWithSelectedOle(out _);
        var inlineCalls = 0;
        var selectedCalls = 0;
        var profile = new FreePRibbonHostProfile
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
        };
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
        var profile = new FreePRibbonHostProfile
        {
            OleCommands = new FreePRibbonOleCommandEndpoints
            {
                TryOpenSelectedEmbeddedObject = ole =>
                {
                    opened = ole;
                    return true;
                },
            },
        };
        var stateStore = new RibbonStateStore();
        var result = FreePRibbonHostRegistryComposer.Build(original, stateStore, profile);

        FreePRibbonHostRegistryComposer.BindInto(result.Registry, replacement, stateStore, profile);
        Execute(result.Registry, OleActivationPlanner.OpenEmbeddedObjectCommandId);

        opened.Should().BeSameAs(replacementOle).And.NotBeSameAs(originalOle);
    }

    [Fact]
    public void EndpointCatalogsRemainExhaustive()
    {
        typeof(FreePRibbonHostQueryEndpoints).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(Enum.GetNames<FreePRibbonHostQueryKind>());
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

    private static FreePRibbonHostProfile CompleteNativeProfile() => new()
    {
        FileCommands = new FreePRibbonFileCommandEndpoints(),
        OleCommands = new FreePRibbonOleCommandEndpoints(),
    };

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
