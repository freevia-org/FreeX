using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SelectionPaneTests
{
    [Fact]
    public void Planner_ListsFrontMostObjectsAndPreservesVisibilityState()
    {
        var slide = new Slide { Title = "Selection" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(1, "Back"));
        var middle = MakeShape(2, "Middle");
        middle.IsHidden = true;
        slide.Shapes.Add(middle);
        slide.Shapes.Add(MakeShape(3, "Front"));

        var plan = PresentationSelectionPanePlanner.Build(slide, 2, [3]);

        plan.HasSlide.Should().BeTrue();
        plan.SlideIndex.Should().Be(2);
        plan.Items.Select(item => item.ShapeName).Should().Equal("Front", "Middle", "Back");
        plan.Items[0].IsSelected.Should().BeTrue();
        plan.Items[1].IsHidden.Should().BeTrue();
        plan.Items[1].SelectionIndex.Should().Be(1);
        plan.Items[0].CanMoveUp.Should().BeFalse();
        plan.Items[0].CanMoveDown.Should().BeTrue();
        plan.Items[1].CanMoveUp.Should().BeTrue();
        plan.Items[1].CanMoveDown.Should().BeTrue();
        plan.Items[2].CanMoveUp.Should().BeTrue();
        plan.Items[2].CanMoveDown.Should().BeFalse();
        plan.Items[0].SelectToolTipText.Should().Be("Select Shape");
        plan.Items[0].VisibilityToolTipText.Should().Be("Hide object");
        plan.SelectedItemIndex.Should().Be(0);
        plan.StatusText.Should().Be("Slide 3 (3 objects)");
        PresentationSelectionPaneItemPlan.RenameToolTipText.Should().Be("Rename object");
        PresentationSelectionPaneItemPlan.MoveUpToolTipText.Should().Be("Move toward front");
        PresentationSelectionPaneItemPlan.MoveDownToolTipText.Should().Be("Move toward back");
        plan.Items[1].VisibilityToolTipText.Should().Be("Show object");
    }

    [Fact]
    public void Session_PlansVisibilityAndRenameTransitionsFromCurrentState()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Transitions" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(17, "Original"));
        presentation.Slides.Add(slide);
        var session = new PresentationSelectionPaneSession(
            new EditingSession(presentation, new PresentationCommandBus(presentation)));

        var visibility = session.ToggleShapeVisibility(17);

        visibility.Action.Should().Be(PresentationSelectionPaneActionKind.ToggleVisibility);
        visibility.ActionApplied.Should().BeTrue();
        visibility.ShouldRefreshPane.Should().BeTrue();
        visibility.RestoreNameText.Should().BeNull();
        visibility.PanePlan.Should().BeSameAs(session.CurrentPlan);
        visibility.PanePlan.Items.Single().IsHidden.Should().BeTrue();

        var renamed = session.RenameShape(17, "  Renamed  ");

        renamed.Action.Should().Be(PresentationSelectionPaneActionKind.Rename);
        renamed.ActionApplied.Should().BeTrue();
        renamed.ShouldRefreshPane.Should().BeFalse();
        renamed.RestoreNameText.Should().BeNull();
        renamed.PanePlan.Items.Single().ShapeName.Should().Be("Renamed");

        var rejected = session.RenameShape(17, "   ");

        rejected.ActionApplied.Should().BeFalse();
        rejected.ShouldRefreshPane.Should().BeFalse();
        rejected.RestoreNameText.Should().Be("Renamed");
        rejected.PanePlan.Items.Single().ShapeName.Should().Be("Renamed");
    }

    [Fact]
    public void Session_SelectsBeforeMovingAndPreservesEditorEventOrder()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Order" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(1, "Back"));
        slide.Shapes.Add(MakeShape(2, "Front"));
        presentation.Slides.Add(slide);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = new PresentationSelectionPaneSession(editor);
        var events = new List<string>();
        editor.SelectionChanged += (_, _) => events.Add("selection");
        editor.Changed += () => events.Add("changed");

        var selected = session.SelectShape(1);

        selected.Action.Should().Be(PresentationSelectionPaneActionKind.Select);
        selected.ActionApplied.Should().BeTrue();
        selected.ShouldRefreshPane.Should().BeFalse();
        selected.PanePlan.SelectedShapeId.Should().Be(1);
        events.Should().Equal("selection");
        events.Clear();

        var moved = session.MoveShapeInReadingOrder(2, -1);

        events.Should().Equal("selection", "changed");
        moved.Action.Should().Be(PresentationSelectionPaneActionKind.MoveInReadingOrder);
        moved.ActionApplied.Should().BeTrue();
        moved.ShouldRefreshPane.Should().BeTrue();
        moved.PanePlan.SelectedShapeId.Should().Be(2);
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u, 1u);
        moved.PanePlan.Items.Select(item => item.ShapeId).Should().Equal(1u, 2u);
    }

    [Fact]
    public void WpfAndAvaloniaSelectionPanes_KeepCommandsInSharedSessionAndNativeProjectionLocal()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "SelectionPane.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "SelectionPane.cs"));
        var wpfWindow = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avaloniaWindow = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("PresentationSelectionPaneSession");
            source.Should().Contain("_session.SelectShape(");
            source.Should().Contain("_session.RenameShape(");
            source.Should().Contain("_session.ToggleShapeVisibility(");
            source.Should().Contain("_session.MoveShapeInReadingOrder(");
            source.Should().Contain("BuildItem(");
            source.Should().Contain("rename.LostFocus");
            source.Should().Contain("Key.Enter");
            source.Should().Contain("Key.Escape");
            source.Should().NotContain("PresentationSelectionPanePlanner.Build(");
            source.Should().NotContain(".SetShapeName(");
            source.Should().NotContain(".ToggleShapeHidden(");
            source.Should().NotContain(".MoveSelectedShapeInReadingOrder(");
        }

        wpf.Should().Contain("System.Windows.Controls");
        avalonia.Should().Contain("Avalonia.Controls");
        foreach (var source in new[] { wpfWindow, avaloniaWindow })
        {
            source.Should().Contain("_selectionPane.CurrentPlan");
            source.Should().NotContain("PresentationSelectionPanePlanner.Build(");
        }
    }

    [Fact]
    public void SetShapeHiddenCommand_IsUndoableAndRedoable()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Visibility" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(17, "Object"));
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeHiddenCommand(0, 17, true));
        slide.Shapes[0].IsHidden.Should().BeTrue();

        bus.Undo();
        slide.Shapes[0].IsHidden.Should().BeFalse();

        bus.Redo();
        slide.Shapes[0].IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Planner_ListsGroupChildrenInFrontToBackOrderWithDepth()
    {
        var slide = new Slide { Title = "Grouped selection" };
        slide.Shapes.Clear();
        var group = MakeShape(10, "Group");
        group.Kind = SlideShapeKind.Group;
        group.Children.Add(MakeShape(11, "Back child"));
        group.Children.Add(MakeShape(12, "Front child"));
        slide.Shapes.Add(MakeShape(1, "Behind group"));
        slide.Shapes.Add(group);

        var plan = PresentationSelectionPanePlanner.Build(slide, 0, [12]);

        plan.Items.Select(item => item.ShapeName).Should().Equal("Group", "Front child", "Back child", "Behind group");
        plan.Items.Select(item => item.NestingDepth).Should().Equal(0, 1, 1, 0);
        plan.Items[1].IsSelected.Should().BeTrue();
        plan.Items[1].CanMoveUp.Should().BeFalse();
        plan.Items[1].CanMoveDown.Should().BeTrue();
        plan.Items[2].CanMoveUp.Should().BeTrue();
        plan.Items[2].CanMoveDown.Should().BeFalse();
    }

    [Fact]
    public void EditingSession_SelectionPaneMovePreservesGroupAndIsUndoable()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Selection order" };
        slide.Shapes.Clear();
        var group = MakeShape(10, "Group");
        group.Kind = SlideShapeKind.Group;
        group.Children.Add(MakeShape(11, "Back child"));
        group.Children.Add(MakeShape(12, "Front child"));
        slide.Shapes.Add(group);
        presentation.Slides.Add(slide);
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        session.Select(12);
        session.MoveSelectedShapeInReadingOrder(-1).Should().BeTrue();
        group.Children.Select(child => child.Id).Should().Equal(12u, 11u);

        session.Undo();
        group.Children.Select(child => child.Id).Should().Equal(11u, 12u);
        session.Redo();
        group.Children.Select(child => child.Id).Should().Equal(12u, 11u);
    }

    [Fact]
    public void SetShapeHiddenCommand_ResolvesGroupedChildAndIsUndoable()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Grouped visibility" };
        slide.Shapes.Clear();
        var group = MakeShape(10, "Group");
        group.Kind = SlideShapeKind.Group;
        group.Children.Add(MakeShape(11, "Child"));
        slide.Shapes.Add(group);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeHiddenCommand(0, 11, true));
        group.Children[0].IsHidden.Should().BeTrue();

        bus.Undo();
        group.Children[0].IsHidden.Should().BeFalse();
    }

    [Fact]
    public void SetShapeNameCommand_ResolvesGroupedChildAndIsUndoable()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Grouped names" };
        slide.Shapes.Clear();
        var group = MakeShape(10, "Group");
        group.Kind = SlideShapeKind.Group;
        group.Children.Add(MakeShape(11, "Old child name"));
        slide.Shapes.Add(group);
        presentation.Slides.Add(slide);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeNameCommand(0, 11, "  New child name  "));
        group.Children[0].Name.Should().Be("New child name");

        bus.Undo();
        group.Children[0].Name.Should().Be("Old child name");

        bus.Redo();
        group.Children[0].Name.Should().Be("New child name");
    }

    [Fact]
    public void EditingSession_SetShapeNameRejectsBlankAndRoundTripsName()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Names" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(17, "Original"));
        presentation.Slides.Add(slide);
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        session.SetShapeName(17, " Renamed ").Should().BeTrue();
        slide.Shapes[0].Name.Should().Be("Renamed");
        session.SetShapeName(17, "   ").Should().BeFalse();
        slide.Shapes[0].Name.Should().Be("Renamed");

        session.Undo();
        slide.Shapes[0].Name.Should().Be("Original");
        session.Redo();
        slide.Shapes[0].Name.Should().Be("Renamed");
    }

    [Fact]
    public void EditingSession_TogglesGroupedChildVisibilityThroughCommandBus()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Grouped session visibility" };
        slide.Shapes.Clear();
        var group = MakeShape(10, "Group");
        group.Kind = SlideShapeKind.Group;
        group.Children.Add(MakeShape(11, "Child"));
        slide.Shapes.Add(group);
        presentation.Slides.Add(slide);
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        session.ToggleShapeHidden(11).Should().BeTrue();
        group.Children[0].IsHidden.Should().BeTrue();
        session.Undo();
        group.Children[0].IsHidden.Should().BeFalse();
    }

    [Fact]
    public void HiddenState_RoundTripsThroughPowerPointPackage()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Visibility" };
        slide.Shapes.Clear();
        var shape = MakeShape(17, "Object");
        shape.IsHidden = true;
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopened = FreeP.Core.IO.PptxPackageReader.Read(stream);

        reopened.Slides.Should().ContainSingle();
        reopened.Slides[0].Shapes.Should().ContainSingle();
        reopened.Slides[0].Shapes[0].IsHidden.Should().BeTrue();
    }

    [Fact]
    public void RenamedObject_RoundTripsThroughPowerPointPackage()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Names" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(17, "Quarterly revenue"));
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopened = FreeP.Core.IO.PptxPackageReader.Read(stream);

        reopened.Slides[0].Shapes[0].Name.Should().Be("Quarterly revenue");
    }

    private static SlideShape MakeShape(uint id, string name) => new()
    {
        Id = id,
        Name = name,
        Kind = SlideShapeKind.AutoShape,
        OffsetXEmu = 100,
        OffsetYEmu = 200,
        ExtentCxEmu = 300,
        ExtentCyEmu = 400,
    };
}
