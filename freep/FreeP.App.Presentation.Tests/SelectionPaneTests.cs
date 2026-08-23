using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SelectionPaneTests
{
    [Fact]
    public void VisualMetrics_DefineSharedSelectionPaneGeometryAndColors()
    {
        PresentationSelectionPaneVisualMetrics.PaneWidth.Should().Be(320);
        PresentationSelectionPaneVisualMetrics.PaneBorderThickness.Should().Be(1);
        PresentationSelectionPaneVisualMetrics.HeadingFontSize.Should().Be(15);
        PresentationSelectionPaneVisualMetrics.ContentSideMargin.Should().Be(12);
        PresentationSelectionPaneVisualMetrics.HeadingTopMargin.Should().Be(12);
        PresentationSelectionPaneVisualMetrics.HeadingBottomMargin.Should().Be(4);
        PresentationSelectionPaneVisualMetrics.MessageBottomMargin.Should().Be(8);
        PresentationSelectionPaneVisualMetrics.SelectHorizontalPadding.Should().Be(8);
        PresentationSelectionPaneVisualMetrics.SelectVerticalPadding.Should().Be(5);
        PresentationSelectionPaneVisualMetrics.ItemVerticalMargin.Should().Be(1);
        PresentationSelectionPaneVisualMetrics.SelectRightMargin.Should().Be(4);
        PresentationSelectionPaneVisualMetrics.NestingIndent.Should().Be(16);
        PresentationSelectionPaneVisualMetrics.RenameMinimumWidth.Should().Be(170);
        PresentationSelectionPaneVisualMetrics.FieldHorizontalPadding.Should().Be(4);
        PresentationSelectionPaneVisualMetrics.FieldVerticalPadding.Should().Be(3);
        PresentationSelectionPaneVisualMetrics.RenameRightMargin.Should().Be(4);
        PresentationSelectionPaneVisualMetrics.VisibilityMinimumWidth.Should().Be(50);
        PresentationSelectionPaneVisualMetrics.VisibilityHorizontalPadding.Should().Be(5);
        PresentationSelectionPaneVisualMetrics.VisibilityVerticalPadding.Should().Be(3);
        PresentationSelectionPaneVisualMetrics.VisibilityRightMargin.Should().Be(8);
        PresentationSelectionPaneVisualMetrics.MoveButtonWidth.Should().Be(22);
        PresentationSelectionPaneVisualMetrics.MoveButtonRightMargin.Should().Be(2);
        PresentationSelectionPaneVisualMetrics.PaneBackgroundColor.ToString().Should().Be("#FFFFFF");
        PresentationSelectionPaneVisualMetrics.PaneBorderColor.ToString().Should().Be("#C0C0C0");
        PresentationSelectionPaneVisualMetrics.MessageColor.ToString().Should().Be("#555555");
    }

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
        plan.Items[0].SelectText.Should().Be("1.");
        plan.Items[0].VisibilityActionText.Should().Be("Hide");
        plan.Items[0].VisibilityToolTipText.Should().Be("Hide object");
        plan.Items[0].MoveUpText.Should().Be("\u25B2");
        plan.Items[0].MoveDownText.Should().Be("\u25BC");
        plan.Items[0].AccessibilityStateText.Should().Be("Selected");
        plan.Items[1].AccessibilityStateText.Should().Be("Not selected");
        plan.SelectedItemIndex.Should().Be(0);
        plan.TitleText.Should().Be("Selection Pane");
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

        var moved = session.MoveShapeInReadingOrder(
            2,
            PresentationSelectionPaneMoveDirection.TowardBack);

        events.Should().Equal("selection", "changed");
        moved.Action.Should().Be(PresentationSelectionPaneActionKind.MoveInReadingOrder);
        moved.ActionApplied.Should().BeTrue();
        moved.ShouldRefreshPane.Should().BeTrue();
        moved.PanePlan.SelectedShapeId.Should().Be(2);
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u, 1u);
        moved.PanePlan.Items.Select(item => item.ShapeId).Should().Equal(1u, 2u);
    }

    [Fact]
    public void Planner_ValidatesCommandsAndPreservesUnknownShapeKinds()
    {
        var unknownKind = (SlideShapeKind)12345;
        var slide = new Slide { Title = "Unknown shape" };
        slide.Shapes.Clear();
        var unknown = MakeShape(41, "   ");
        unknown.Kind = unknownKind;
        slide.Shapes.Add(MakeShape(40, "Back"));
        slide.Shapes.Add(unknown);
        var panePlan = PresentationSelectionPanePlanner.Build(slide, 0, [41]);
        var unknownItem = panePlan.Items[0];

        unknownItem.ShapeType.Should().Be(unknownKind);
        unknownItem.ShapeTypeLabel.Should().Be("Object 41");
        unknownItem.ShapeName.Should().Be("Object 41");

        var rename = PresentationSelectionPanePlanner.PlanCommand(
            PresentationSelectionPaneActionKind.Rename,
            panePlan,
            41,
            proposedName: "  Quarterly object  ");
        rename.CanExecute.Should().BeTrue();
        rename.NormalizedName.Should().Be("Quarterly object");
        rename.PreviousName.Should().Be("Object 41");

        var blankRename = PresentationSelectionPanePlanner.PlanCommand(
            PresentationSelectionPaneActionKind.Rename,
            panePlan,
            41,
            proposedName: "   ");
        blankRename.CanExecute.Should().BeFalse();
        blankRename.NormalizedName.Should().BeNull();
        blankRename.PreviousName.Should().Be("Object 41");

        var moveBack = PresentationSelectionPanePlanner.PlanCommand(
            PresentationSelectionPaneActionKind.MoveInReadingOrder,
            panePlan,
            41,
            moveDirection: PresentationSelectionPaneMoveDirection.TowardBack);
        moveBack.CanExecute.Should().BeTrue();
        moveBack.ReadingOrderOffset.Should().Be(-1);

        var movePastFront = PresentationSelectionPanePlanner.PlanCommand(
            PresentationSelectionPaneActionKind.MoveInReadingOrder,
            panePlan,
            41,
            moveDirection: PresentationSelectionPaneMoveDirection.TowardFront);
        movePastFront.CanExecute.Should().BeFalse();
        movePastFront.ReadingOrderOffset.Should().Be(1);

        var moveWithoutDirection = PresentationSelectionPanePlanner.PlanCommand(
            PresentationSelectionPaneActionKind.MoveInReadingOrder,
            panePlan,
            41);
        moveWithoutDirection.CanExecute.Should().BeFalse();
        moveWithoutDirection.ReadingOrderOffset.Should().Be(0);

        PresentationSelectionPanePlanner.PlanCommand(
                PresentationSelectionPaneActionKind.Select,
                panePlan,
                999)
            .CanExecute.Should().BeFalse();
    }

    [Fact]
    public void ItemSession_CommitsOrCancelsRenameOnlyOnce()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Rename lifecycle" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(17, "Original"));
        presentation.Slides.Add(slide);
        var paneSession = new PresentationSelectionPaneSession(
            new EditingSession(presentation, new PresentationCommandBus(presentation)));

        var committedItem = paneSession.CreateItemSession(17);
        var committed = committedItem.CommitRename("  First  ");
        var duplicateCommit = committedItem.CommitRename("Second");

        committed.ActionApplied.Should().BeTrue();
        committed.PanePlan.Items.Single().ShapeName.Should().Be("First");
        duplicateCommit.ActionApplied.Should().BeFalse();
        duplicateCommit.ShouldRefreshPane.Should().BeFalse();
        slide.Shapes.Single().Name.Should().Be("First");

        var cancelledItem = paneSession.CreateItemSession(17);
        var cancelled = cancelledItem.CancelRename();
        var commitAfterCancel = cancelledItem.CommitRename("Second");

        cancelled.ShouldRefreshPane.Should().BeTrue();
        cancelled.ActionApplied.Should().BeFalse();
        commitAfterCancel.ActionApplied.Should().BeFalse();
        slide.Shapes.Single().Name.Should().Be("First");

        var rejectedItem = paneSession.CreateItemSession(17);
        var rejected = rejectedItem.CommitRename("   ");
        var commitAfterRejection = rejectedItem.CommitRename("Second");

        rejected.ActionApplied.Should().BeFalse();
        rejected.RestoreNameText.Should().Be("First");
        commitAfterRejection.ActionApplied.Should().BeFalse();
        slide.Shapes.Single().Name.Should().Be("First");
    }

    [Fact]
    public void ItemFormSession_DoesNotPersistUnchangedDisplayPlaceholder()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Placeholder" };
        slide.Shapes.Clear();
        var shape = MakeShape(17, "   ");
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        var paneSession = new PresentationSelectionPaneSession(
            new EditingSession(presentation, new PresentationCommandBus(presentation)));
        var itemPlan = paneSession.CurrentPlan.Items.Single();
        var applyCount = 0;
        var form = new PresentationSelectionPaneItemFormSession(
            paneSession.CreateItemSession(shape.Id),
            itemPlan,
            index: 0,
            (transition, restore) => applyCount++);

        form.CommitRename(itemPlan.ShapeName, _ => { });

        applyCount.Should().Be(0);
        shape.Name.Should().Be("   ");

        form.CommitRename("Named shape", _ => { });

        applyCount.Should().Be(1);
        shape.Name.Should().Be("Named shape");
    }

    [Fact]
    public void ItemSession_MapsMoveIntentAndRejectsCurrentBoundary()
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Move intent" };
        slide.Shapes.Clear();
        slide.Shapes.Add(MakeShape(1, "Back"));
        slide.Shapes.Add(MakeShape(2, "Front"));
        presentation.Slides.Add(slide);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var paneSession = new PresentationSelectionPaneSession(editor);
        var frontItem = paneSession.CreateItemSession(2);

        var rejected = frontItem.MoveTowardFront();

        rejected.ActionApplied.Should().BeFalse();
        rejected.ShouldRefreshPane.Should().BeFalse();
        editor.SelectedShapeIds.Should().BeEmpty();
        slide.Shapes.Select(shape => shape.Id).Should().Equal(1u, 2u);

        var movedBack = frontItem.MoveTowardBack();

        movedBack.ActionApplied.Should().BeTrue();
        movedBack.ShouldRefreshPane.Should().BeTrue();
        editor.SelectedShapeIds.Should().Equal(2u);
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u, 1u);

        var movedFront = frontItem.MoveTowardFront();

        movedFront.ActionApplied.Should().BeTrue();
        slide.Shapes.Select(shape => shape.Id).Should().Equal(1u, 2u);
    }

    [Fact]
    public void WpfAndAvaloniaSelectionPanes_KeepCommandsInSharedSessionAndNativeProjectionLocal()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "SelectionPane.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "SelectionPane.cs"));
        var wpfWindow = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avaloniaWindow = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var itemForm = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationSelectionPaneItemFormSession.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("PresentationSelectionPaneFormSession<");
            source.Should().Contain("PresentationSelectionPaneItemFormSession(");
            source.Should().Contain("PresentationSelectionPaneVisualMetrics.PaneWidth");
            source.Should().Contain("PresentationSelectionPaneVisualMetrics.PaneBackgroundColor");
            source.Should().Contain("PresentationSelectionPaneVisualMetrics.PaneBorderColor");
            source.Should().Contain("PresentationSelectionPaneVisualMetrics.HeadingFontSize");
            source.Should().Contain("PresentationSelectionPaneVisualMetrics.NestingIndent");
            source.Should().Contain("PresentationSelectionPaneVisualMetrics.RenameMinimumWidth");
            source.Should().Contain("PresentationSelectionPaneVisualMetrics.VisibilityMinimumWidth");
            source.Should().Contain("PresentationSelectionPaneVisualMetrics.MoveButtonWidth");
            source.Should().Contain("PresentationSelectionPaneItemSession itemSession");
            source.Should().Contain("itemForm.Select()");
            source.Should().Contain("itemForm.CommitRename(rename.Text,");
            source.Should().Contain("itemForm.CancelRename()");
            source.Should().Contain("itemForm.ToggleVisibility()");
            source.Should().Contain("itemForm.MoveTowardFront()");
            source.Should().Contain("itemForm.MoveTowardBack()");
            source.Should().Contain("item.VisibilityActionText");
            source.Should().Contain("itemForm.AccessibilityPlan");
            source.Should().Contain("BuildItem(");
            source.Should().Contain("rename.LostFocus");
            source.Should().Contain("Key.Enter");
            source.Should().Contain("Key.Escape");
            source.Should().NotContain("PresentationSelectionPanePlanner.Build(");
            source.Should().NotContain("var committed");
            source.Should().NotContain("_session.SelectShape(");
            source.Should().NotContain("_session.RenameShape(");
            source.Should().NotContain("_session.ToggleShapeVisibility(");
            source.Should().NotContain("_session.MoveShapeInReadingOrder(");
            source.Should().NotContain("itemSession.Select()");
            source.Should().NotContain("itemSession.CommitRename(");
            source.Should().NotContain("itemSession.CancelRename()");
            source.Should().NotContain("itemSession.ToggleVisibility()");
            source.Should().NotContain("itemSession.MoveTowardFront()");
            source.Should().NotContain("itemSession.MoveTowardBack()");
            source.Should().NotContain("PresentationPaneAccessibilityPlanner.PlanItem(");
            source.Should().NotContain("PresentationPaneAccessibilityPlanner.BuildShapeKey(");
            source.Should().NotContain("PresentationSelectionPaneMoveDirection");
            source.Should().NotContain("offset:");
            source.Should().NotContain("item.IsHidden ?");
            source.Should().NotContain("item.AccessibilityStateText");
            source.Should().NotContain("Width = 320");
            source.Should().NotContain("FontSize = 15");
            source.Should().NotContain("MinWidth = 170");
            source.Should().NotContain("MinWidth = 50");
            source.Should().NotContain("Width = 22");
            source.Should().NotContain("Color.FromRgb(0xC0, 0xC0, 0xC0)");
            source.Should().NotContain("Color.FromRgb(0x55, 0x55, 0x55)");
            source.Should().NotContain(".SetShapeName(");
            source.Should().NotContain(".ToggleShapeHidden(");
            source.Should().NotContain(".MoveSelectedShapeInReadingOrder(");
            source.Should().NotContain("rename.Text == item.ShapeName");
        }

        itemForm.Should().Contain("_item.Select()");
        itemForm.Should().Contain("_item.CommitRename(name)");
        itemForm.Should().Contain("string.Equals(name, Plan.ShapeName, StringComparison.Ordinal)");
        itemForm.Should().Contain("_item.CancelRename()");
        itemForm.Should().Contain("_item.ToggleVisibility()");
        itemForm.Should().Contain("_item.MoveTowardFront()");
        itemForm.Should().Contain("_item.MoveTowardBack()");
        itemForm.Should().Contain("PresentationPaneAccessibilityPlanner.PlanItem(");
        itemForm.Should().Contain("PresentationPaneAccessibilityPlanner.BuildShapeKey(plan.ShapeId)");

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
