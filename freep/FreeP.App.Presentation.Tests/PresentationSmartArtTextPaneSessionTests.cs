using System.Text;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationSmartArtTextPaneSessionTests
{
    [Fact]
    public void Refresh_ProjectsRendererReadyPaneStateAndNormalizesSelection()
    {
        var (editor, _) = CreateEditor();
        PresentationSmartArtTextPanePlan? rendered = null;
        var session = CreateSession(editor, plan => rendered = plan);

        var plan = session.Refresh();

        plan.Should().BeSameAs(rendered);
        plan.Heading.Should().Be("SmartArt Text Pane - Roadmap SmartArt");
        plan.Message.Should().Be("Rows mirror the shared SmartArt outline.");
        plan.Rows.Select(row => row.Text).Should().Equal("Plan", "Build");
        plan.SelectedModelId.Should().Be("n1");
        plan.CanApply.Should().BeTrue();
        plan.CanEditSelectedRow.Should().BeTrue();
        plan.CanToggleAssistant.Should().BeFalse();
        session.SelectedModelId.Should().Be("n1");
    }

    [Fact]
    public void OutlineAndKeyboardMutations_OwnCommitSequenceAndRemainUndoable()
    {
        var (editor, shape) = CreateEditor();
        var dirtyCount = 0;
        var canvasCount = 0;
        var hostCount = 0;
        var renderCount = 0;
        var session = CreateSession(
            editor,
            _ => renderCount++,
            () => dirtyCount++,
            () => canvasCount++,
            () => hostCount++);

        var applied = session.ApplyOutline([
            new SmartArtTextPaneOutlineRow("Discover", 0, false, "n1"),
            new SmartArtTextPaneOutlineRow("Build", 0, false, "n2")
        ]);

        applied.Applied.Should().BeTrue(applied.Message);
        shape.SmartArt!.Data!.Nodes[0].Text.Should().Be("Discover");
        session.LastDataPartRewriteResult!.Applied.Should().BeTrue();
        session.LastDrawingCacheRegenerationResult!.Applied.Should().BeTrue();
        dirtyCount.Should().Be(1);
        canvasCount.Should().Be(1);
        hostCount.Should().Be(1);
        renderCount.Should().Be(1);

        editor.Undo();
        shape.SmartArt.Data.Nodes[0].Text.Should().Be("Plan");
        editor.Redo();
        shape.SmartArt.Data.Nodes[0].Text.Should().Be("Discover");

        session.SelectModel("n1");
        var keyboard = session.ApplyKeyboardRoute(
            SmartArtTextPaneShortcutKey.Enter,
            SmartArtTextPaneShortcutModifiers.None);

        keyboard!.Applied.Should().BeTrue(keyboard.Message);
        session.LastKeyboardRoute!.RouteId.Should().Be("smartart.text-pane.enter.add-sibling-after");
        shape.SmartArt.Data.Nodes.Should().HaveCount(3);
        dirtyCount.Should().Be(2);
        canvasCount.Should().Be(2);
        hostCount.Should().Be(2);
        renderCount.Should().Be(2);
    }

    [Fact]
    public void PictureAndAssistantMutations_UseSelectedModelAndRefreshThePane()
    {
        var (pictureEditor, pictureShape) = CreateEditor(pictureLayout: true);
        var pictureRenderCount = 0;
        var pictureSession = CreateSession(pictureEditor, _ => pictureRenderCount++);
        pictureSession.SelectModel("n1");

        var replaced = pictureSession.ApplyPicture([9, 8, 7, 6], "image/png");
        var cleared = pictureSession.ClearPicture();

        replaced.Applied.Should().BeTrue(replaced.Message);
        cleared.Applied.Should().BeTrue(cleared.Message);
        pictureShape.SmartArt!.Data!.Nodes[0].Picture.Should().BeNull();
        pictureRenderCount.Should().Be(2);

        var (hierarchyEditor, hierarchyShape) = CreateEditor(hierarchy: true);
        var hierarchyRenderCount = 0;
        var hierarchySession = CreateSession(hierarchyEditor, _ => hierarchyRenderCount++);
        hierarchySession.SelectModel("n2");

        var toggled = hierarchySession.ToggleAssistant();

        toggled.Applied.Should().BeTrue(toggled.Message);
        hierarchyShape.SmartArt!.Data!.Nodes[0].Children.Single().IsAssistant.Should().BeTrue();
        hierarchyRenderCount.Should().Be(1);
        hierarchyEditor.Undo();
        hierarchyShape.SmartArt.Data.Nodes[0].Children.Single().IsAssistant.Should().BeFalse();
    }

    [Fact]
    public void MainWindowSourceGuards_KeepSmartArtSemanticsInPresentationSession()
    {
        var root = FindWorkspaceRoot();
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain(
                "private readonly PresentationSmartArtTextPaneSession _smartArtTextPaneSession;");
            source.Should().Contain("_smartArtTextPaneSession.ApplyOutline(rows)");
            source.Should().Contain("_smartArtTextPaneSession.ApplyKeyboardRoute(key, modifiers)");
            source.Should().Contain("_smartArtTextPaneSession.ApplyPicture(imageBytes, contentType)");
            source.Should().Contain("_smartArtTextPaneSession.ToggleAssistant()");
            source.Should().Contain("RenderSmartArtTextPane(PresentationSmartArtTextPanePlan plan)");
            source.Should().Contain("BuildSmartArtTextPaneRow(SmartArtNodeOutlineItem item)");
            source.Should().Contain("TryMapSmartArtTextPaneKey(");
            source.Should().NotContain("SmartArtEditingPlanner.ApplyTextPaneOutline(");
            source.Should().NotContain("SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(");
            source.Should().NotContain("SmartArtEditingPlanner.RewriteDataPart(");
            source.Should().NotContain("SmartArtEditingPlanner.RegenerateDrawingCache(");
            source.Should().NotContain("SmartArtEditingPlanner.SynchronizePreservedDrawingText(");
            source.Should().NotContain("Editor.EditSmartArt(");
            source.Should().NotContain("Editor.ReplaceSmartArtNodePicture(");
            source.Should().NotContain("Editor.ClearSmartArtNodePicture(");
            source.Should().NotContain("Editor.ToggleSmartArtAssistant(");
            source.Should().NotContain("private bool CommitSmartArtTextPaneMutation(");
        }
    }

    private static PresentationSmartArtTextPaneSession CreateSession(
        EditingSession editor,
        Action<PresentationSmartArtTextPanePlan>? render = null,
        Action? markDirty = null,
        Action? refreshCanvas = null,
        Action? updateHost = null)
    {
        return new PresentationSmartArtTextPaneSession(
            () => editor,
            new PresentationSmartArtTextPaneSessionCallbacks(
                markDirty ?? (() => { }),
                refreshCanvas ?? (() => { }),
                updateHost ?? (() => { }),
                render ?? (_ => { })));
    }

    private static (EditingSession Editor, SlideShape Shape) CreateEditor(
        bool hierarchy = false,
        bool pictureLayout = false)
    {
        var presentation = Presentation.CreateEmpty();
        var data = new SmartArtData
        {
            Family = hierarchy ? SmartArtFamily.Hierarchy : SmartArtFamily.List,
            LayoutUniqueId = hierarchy
                ? "urn:microsoft.com/office/officeart/2005/8/layout/orgChart"
                : pictureLayout
                    ? "urn:microsoft.com/office/officeart/2005/8/layout/pictureCaptionList"
                    : "urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList",
            IsLiveLayoutSupported = true
        };
        var first = new SmartArtNode { ModelId = "n1", Text = "Plan", Level = 0 };
        var second = new SmartArtNode { ModelId = "n2", Text = "Build", Level = hierarchy ? 1 : 0 };
        if (hierarchy)
            first.Children.Add(second);
        else
            data.Nodes.Add(second);
        data.Nodes.Insert(0, first);

        var smartArt = new SmartArtShape
        {
            Data = data,
            DrawingPartPath = "ppt/diagrams/drawing1.xml"
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes(
                "<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };

        var shape = new SlideShape
        {
            Id = 970,
            Name = "Roadmap SmartArt",
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 4_572_000,
            ExtentCyEmu = 2_743_200,
            SmartArt = smartArt
        };
        presentation.Slides[0].Shapes.Add(shape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(shape.Id);
        return (editor, shape);
    }

    private static string FindWorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
}
