using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Unit tests for every <see cref="IPresentationCommand"/> implementation and
/// <see cref="PresentationCommandBus"/> mechanics.
/// </summary>
public sealed class PresentationCommandTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────────

    /// <summary>Creates a presentation with <paramref name="slideCount"/> blank slides (no pre-existing shapes).</summary>
    private static (Presentation p, PresentationCommandBus bus) Make(int slideCount = 1)
    {
        var p = new Presentation();
        for (int i = 0; i < slideCount; i++)
            p.Slides.Add(new Slide { Title = $"S{i + 1}" });
        var bus = new PresentationCommandBus(p);
        // Clear any shapes added by the Title setter.
        foreach (var s in p.Slides) s.Shapes.Clear();
        return (p, bus);
    }

    private static SlideShape MakeShape(uint id = 1) => new()
    {
        Id          = id,
        Name        = $"Shape{id}",
        Kind        = SlideShapeKind.AutoShape,
        OffsetXEmu  = 100,
        OffsetYEmu  = 200,
        ExtentCxEmu = 300,
        ExtentCyEmu = 400,
        RotationDeg = 0,
    };

    private static SlideShape MakeChart(uint id = 1, bool protectedObject = true) => new()
    {
        Id = id,
        Name = $"Chart{id}",
        Kind = SlideShapeKind.Chart,
        OffsetXEmu = 100,
        OffsetYEmu = 200,
        ExtentCxEmu = 300,
        ExtentCyEmu = 400,
        RotationDeg = 15,
        Chart = new ChartShape { ChartObjectProtected = protectedObject }
    };

    // ════════════════════════════════════════════════════════════════════════════
    // SLIDE COMMANDS
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertSlideCommand_Apply_InsertsAtCorrectIndex()
    {
        var (p, bus) = Make();
        var newSlide = new Slide { Title = "New" };
        bus.Execute(new InsertSlideCommand(0, newSlide));
        p.Slides.Should().HaveCount(2);
        p.Slides[0].Should().BeSameAs(newSlide);
    }

    [Fact]
    public void InsertSlideCommand_Revert_RemovesInsertedSlide()
    {
        var (p, bus) = Make();
        var original = p.Slides[0];
        var newSlide = new Slide { Title = "New" };
        bus.Execute(new InsertSlideCommand(0, newSlide));
        bus.Undo();
        p.Slides.Should().HaveCount(1);
        p.Slides[0].Should().BeSameAs(original);
    }

    [Fact]
    public void InsertSlideCommand_Redo_ReappliesInsert()
    {
        var (p, bus) = Make();
        var newSlide = new Slide { Title = "New" };
        bus.Execute(new InsertSlideCommand(1, newSlide));
        bus.Undo();
        bus.Redo();
        p.Slides.Should().HaveCount(2);
        p.Slides[1].Should().BeSameAs(newSlide);
    }

    [Fact]
    public void InsertSlideCommand_InheritsNeighborSectionAcrossUndoAndRedo()
    {
        var (p, bus) = Make(3);
        var firstId = p.Slides[0].Id;
        var secondId = p.Slides[1].Id;
        var thirdId = p.Slides[2].Id;
        var section = new PresentationSection { Id = "section-1", Name = "Intro" };
        section.SlideIds.AddRange(new[] { firstId, secondId, thirdId });
        p.Sections.Add(section);

        var inserted = new Slide { Title = "Inserted" };
        bus.Execute(new InsertSlideCommand(1, inserted));

        p.Sections[0].SlideIds.Should().Equal(firstId, inserted.Id, secondId, thirdId);

        bus.Undo();
        p.Slides.Select(slide => slide.Id).Should().Equal(firstId, secondId, thirdId);
        p.Sections[0].SlideIds.Should().Equal(firstId, secondId, thirdId);

        bus.Redo();
        p.Slides[1].Should().BeSameAs(inserted);
        p.Sections[0].SlideIds.Should().Equal(firstId, inserted.Id, secondId, thirdId);
    }

    [Fact]
    public void AddSlideCommand_Apply_AppendsSlide()
    {
        var (p, bus) = Make();
        var s = new Slide { Title = "Appended" };
        bus.Execute(new AddSlideCommand(s));
        p.Slides.Should().HaveCount(2);
        p.Slides[1].Should().BeSameAs(s);
    }

    [Fact]
    public void AddSlideCommand_PreservesSectionMembershipAcrossUndoAndRedo()
    {
        var (p, bus) = Make(2);
        var lastSlideId = p.Slides[^1].Id;
        var section = new PresentationSection { Id = "section-1", Name = "Closing" };
        section.SlideIds.Add(lastSlideId);
        p.Sections.Add(section);
        var added = new Slide { Id = "new-slide" };

        bus.Execute(new AddSlideCommand(added));

        p.Slides[^1].Should().BeSameAs(added);
        p.Sections[0].SlideIds.Should().Equal(lastSlideId, added.Id);

        bus.Undo();
        p.Sections[0].SlideIds.Should().Equal(lastSlideId);

        bus.Redo();
        p.Sections[0].SlideIds.Should().Equal(lastSlideId, added.Id);
    }

    [Fact]
    public void AddSlideCommand_Revert_RemovesSlide()
    {
        var (p, bus) = Make();
        var s = new Slide { Title = "Appended" };
        bus.Execute(new AddSlideCommand(s));
        bus.Undo();
        p.Slides.Should().HaveCount(1);
    }

    [Fact]
    public void PasteSlideCommand_PreservesSectionMembershipAcrossUndoAndRedo()
    {
        var (p, bus) = Make(3);
        var firstId = p.Slides[0].Id;
        var secondId = p.Slides[1].Id;
        var thirdId = p.Slides[2].Id;
        var section = new PresentationSection { Id = "section-1", Name = "Middle" };
        section.SlideIds.AddRange(new[] { firstId, secondId, thirdId });
        p.Sections.Add(section);
        var pasted = new Slide { Id = "pasted-slide" };

        bus.Execute(new PasteSlideCommand(1, pasted));

        p.Slides.Select(slide => slide.Id).Should().Equal(firstId, pasted.Id, secondId, thirdId);
        p.Sections[0].SlideIds.Should().Equal(firstId, pasted.Id, secondId, thirdId);

        bus.Undo();
        p.Slides.Select(slide => slide.Id).Should().Equal(firstId, secondId, thirdId);
        p.Sections[0].SlideIds.Should().Equal(firstId, secondId, thirdId);

        bus.Redo();
        p.Sections[0].SlideIds.Should().Equal(firstId, pasted.Id, secondId, thirdId);
    }

    [Fact]
    public void DeleteSlideCommand_Apply_RemovesSlide()
    {
        var (p, bus) = Make(2);
        var second = p.Slides[1];
        bus.Execute(new DeleteSlideCommand(1));
        p.Slides.Should().HaveCount(1);
        p.Slides.Should().NotContain(second);
    }

    [Fact]
    public void DeleteSlideCommand_Revert_RestoresAtOriginalIndex()
    {
        var (p, bus) = Make(3);
        var middle = p.Slides[1];
        bus.Execute(new DeleteSlideCommand(1));
        bus.Undo();
        p.Slides.Should().HaveCount(3);
        p.Slides[1].Should().BeSameAs(middle);
    }

    [Fact]
    public void DeleteSlideCommand_PrunesAndRestoresSectionAndCustomShowReferences()
    {
        var (p, bus) = Make(3);
        var firstId = p.Slides[0].Id;
        var deletedId = p.Slides[1].Id;
        var lastId = p.Slides[2].Id;

        var section = new PresentationSection { Id = "section-1", Name = "Main" };
        section.SlideIds.AddRange(new[] { firstId, deletedId, deletedId, lastId });
        p.Sections.Add(section);

        var customShow = new PresentationCustomShow { Id = 4, Name = "Review" };
        customShow.SlideIds.AddRange(new[] { deletedId, firstId, deletedId, lastId });
        p.CustomShows.Add(customShow);

        bus.Execute(new DeleteSlideCommand(1));

        p.Sections[0].SlideIds.Should().Equal(firstId, lastId);
        p.CustomShows[0].SlideIds.Should().Equal(firstId, lastId);

        bus.Undo();

        p.Slides[1].Id.Should().Be(deletedId);
        p.Sections[0].SlideIds.Should().Equal(firstId, deletedId, deletedId, lastId);
        p.CustomShows[0].SlideIds.Should().Equal(deletedId, firstId, deletedId, lastId);

        bus.Redo();

        p.Sections[0].SlideIds.Should().Equal(firstId, lastId);
        p.CustomShows[0].SlideIds.Should().Equal(firstId, lastId);
    }

    [Fact]
    public void DuplicateSlideCommand_Apply_InsertsDeepCloneAfterSource()
    {
        var (p, bus) = Make(1);
        p.Slides[0].Shapes.Clear(); // ensure clean
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new DuplicateSlideCommand(0));

        p.Slides.Should().HaveCount(2);
        // Clone has 1 shape (the one we added) but is a different object.
        p.Slides[1].Shapes.Should().HaveCount(1);
        p.Slides[1].Shapes[0].Should().NotBeSameAs(shape);
        p.Slides[1].Should().NotBeSameAs(p.Slides[0]);
    }

    [Fact]
    public void DuplicateSlideCommand_InheritsSectionMembershipAcrossUndoAndRedo()
    {
        var (p, bus) = Make(2);
        var sourceId = p.Slides[0].Id;
        var followingId = p.Slides[1].Id;
        var section = new PresentationSection { Id = "section-1", Name = "Intro" };
        section.SlideIds.AddRange(new[] { sourceId, followingId });
        p.Sections.Add(section);

        bus.Execute(new DuplicateSlideCommand(0));

        var firstDuplicateId = p.Slides[1].Id;
        p.Sections[0].SlideIds.Should().Equal(sourceId, firstDuplicateId, followingId);

        bus.Undo();
        p.Slides.Select(slide => slide.Id).Should().Equal(sourceId, followingId);
        p.Sections[0].SlideIds.Should().Equal(sourceId, followingId);

        bus.Redo();

        var redoDuplicateId = p.Slides[1].Id;
        redoDuplicateId.Should().NotBe(firstDuplicateId);
        p.Sections[0].SlideIds.Should().Equal(sourceId, redoDuplicateId, followingId);
    }

    [Fact]
    public void DuplicateSlideCommand_DeepClone_PreservesTransitionSplitOrientation()
    {
        var (p, bus) = Make(1);
        p.Slides[0].Transition = new SlideTransition
        {
            Kind = TransitionKind.Split,
            Direction = TransitionDirection.In,
            SplitOrientation = TransitionDirection.Vertical,
        };

        bus.Execute(new DuplicateSlideCommand(0));

        var transition = p.Slides[1].Transition;
        transition.Should().NotBeNull();
        transition!.Kind.Should().Be(TransitionKind.Split);
        transition.Direction.Should().Be(TransitionDirection.In);
        transition.SplitOrientation.Should().Be(TransitionDirection.Vertical);
    }

    [Fact]
    public void DuplicateSlideCommand_DeepClone_MutatingDuplicateDoesNotTouchOriginal()
    {
        var (p, bus) = Make(1);
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new DuplicateSlideCommand(0));
        p.Slides[1].Shapes[0].OffsetXEmu = 99999;
        shape.OffsetXEmu.Should().Be(100, "original must not be affected");
    }

    [Fact]
    public void DuplicateSlideCommand_Revert_RemovesDuplicate()
    {
        var (p, bus) = Make(1);
        bus.Execute(new DuplicateSlideCommand(0));
        bus.Undo();
        p.Slides.Should().HaveCount(1);
    }

    [Fact]
    public void MoveSlideCommand_Apply_MovesSlideToNewPosition()
    {
        // 3 slides: [A, B, C] — move A (index 0) to index 2 => [B, C, A]
        var (p, bus) = Make(3);
        var first = p.Slides[0]; // A
        bus.Execute(new MoveSlideCommand(0, 2));
        // After removal of A, list is [B,C]; insert at 2 => [B,C,A]
        p.Slides[2].Should().BeSameAs(first);
    }

    [Fact]
    public void MoveSlideCommand_SynchronizesSectionOrderAcrossUndoAndRedo()
    {
        var (p, bus) = Make(3);
        var firstId = p.Slides[0].Id;
        var secondId = p.Slides[1].Id;
        var thirdId = p.Slides[2].Id;
        var section = new PresentationSection { Id = "section-1", Name = "Intro" };
        section.SlideIds.AddRange(new[] { firstId, secondId, thirdId });
        p.Sections.Add(section);

        bus.Execute(new MoveSlideCommand(0, 2));

        p.Slides.Select(slide => slide.Id).Should().Equal(secondId, thirdId, firstId);
        p.Sections[0].SlideIds.Should().Equal(secondId, thirdId, firstId);

        bus.Undo();
        p.Slides.Select(slide => slide.Id).Should().Equal(firstId, secondId, thirdId);
        p.Sections[0].SlideIds.Should().Equal(firstId, secondId, thirdId);

        bus.Redo();
        p.Sections[0].SlideIds.Should().Equal(secondId, thirdId, firstId);
    }

    [Fact]
    public void MoveSlideCommand_Revert_RestoresOriginalOrder()
    {
        var (p, bus) = Make(3);
        var originalOrder = p.Slides.ToList();
        bus.Execute(new MoveSlideCommand(0, 2));
        bus.Undo();
        p.Slides.Should().Equal(originalOrder);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // SHAPE COMMANDS
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AddShapeCommand_Apply_AddsShapeToSlide()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        bus.Execute(new AddShapeCommand(0, shape));
        p.Slides[0].Shapes.Should().Contain(shape);
    }

    [Fact]
    public void AddShapeCommand_Revert_RemovesShape()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        bus.Execute(new AddShapeCommand(0, shape));
        bus.Undo();
        p.Slides[0].Shapes.Should().NotContain(shape);
    }

    [Fact]
    public void ChangeAutoShapeKindCommand_PreservesFrameAndRestoresGeometryOnUndo()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        shape.AutoShapeKind = DrawingShapeKind.RoundedRectangle;
        shape.PresetGeometryAdjustments["adj"] = 24000;
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, 0, 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, 100, 100));
        shape.CustomGeometry.Add(path);
        shape.CustomConnectionSites.Add(new CustomGeometryConnectionSite
        {
            X = "hc", Y = "t", Angle = "0"
        });
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new ChangeAutoShapeKindCommand(0, shape.Id, DrawingShapeKind.Diamond));

        shape.AutoShapeKind.Should().Be(DrawingShapeKind.Diamond);
        shape.OffsetXEmu.Should().Be(100);
        shape.ExtentCyEmu.Should().Be(400);
        shape.PresetGeometryAdjustments.Should().BeEmpty();
        shape.CustomGeometry.Should().BeEmpty();
        shape.CustomConnectionSites.Should().BeEmpty();

        bus.Undo();

        shape.AutoShapeKind.Should().Be(DrawingShapeKind.RoundedRectangle);
        shape.PresetGeometryAdjustments["adj"].Should().Be(24000);
        shape.CustomGeometry.Should().HaveCount(1);
        shape.CustomGeometry[0].Segments.Should().HaveCount(2);
        shape.CustomConnectionSites.Should().ContainSingle();
        shape.CustomConnectionSites[0].X.Should().Be("hc");
    }

    [Fact]
    public void DeleteShapeCommand_Apply_RemovesShape()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new DeleteShapeCommand(0, 1));
        p.Slides[0].Shapes.Should().NotContain(shape);
    }

    [Fact]
    public void DeleteShapeCommand_RemovesAnimationReferences_AndUndoRestoresThem()
    {
        var (p, bus) = Make();
        var deleted = MakeShape(1);
        var retained = MakeShape(2);
        p.Slides[0].Shapes.Add(deleted);
        p.Slides[0].Shapes.Add(retained);

        var deletedAnimation = new ShapeAnimation { ShapeId = deleted.Id };
        var retainedAnimation = new ShapeAnimation { ShapeId = retained.Id };
        p.Slides[0].Animations.Add(deletedAnimation);
        p.Slides[0].Animations.Add(retainedAnimation);
        p.Slides[0].AnimationBuildListXml =
            "<p:bldLst xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
            "<p:bldP spid=\"1\" grpId=\"0\" build=\"p\" />" +
            "<p:bldP spid=\"2\" grpId=\"0\" build=\"all\" />" +
            "</p:bldLst>";
        var originalBuildList = p.Slides[0].AnimationBuildListXml;

        bus.Execute(new DeleteShapeCommand(0, deleted.Id));

        p.Slides[0].Animations.Should().ContainSingle().Which.Should().BeSameAs(retainedAnimation);
        p.Slides[0].AnimationBuildListXml.Should().NotContain("spid=\"1\"");
        p.Slides[0].AnimationBuildListXml.Should().Contain("spid=\"2\"");

        bus.Undo();

        p.Slides[0].Shapes[0].Should().BeSameAs(deleted);
        p.Slides[0].Animations.Should().ContainInOrder(deletedAnimation, retainedAnimation);
        p.Slides[0].AnimationBuildListXml.Should().Be(originalBuildList);

        bus.Redo();

        p.Slides[0].Animations.Should().ContainSingle().Which.Should().BeSameAs(retainedAnimation);
        p.Slides[0].AnimationBuildListXml.Should().NotContain("spid=\"1\"");
    }

    [Fact]
    public void DeleteShapeCommand_Revert_RestoresShapeAtOriginalIndex()
    {
        var (p, bus) = Make();
        var s1 = MakeShape(10);
        var s2 = MakeShape(20);
        p.Slides[0].Shapes.Add(s1);
        p.Slides[0].Shapes.Add(s2);
        bus.Execute(new DeleteShapeCommand(0, 10)); // delete s1 (index 0)
        bus.Undo();
        p.Slides[0].Shapes[0].Should().BeSameAs(s1);
    }

    [Fact]
    public void DeleteShapeCommand_ProtectedChart_DoesNotRemoveChart()
    {
        var (p, bus) = Make();
        var chart = MakeChart();
        p.Slides[0].Shapes.Add(chart);

        bus.Execute(new DeleteShapeCommand(0, chart.Id));

        p.Slides[0].Shapes.Should().ContainSingle().Which.Should().BeSameAs(chart);
        bus.Undo();
        p.Slides[0].Shapes.Should().ContainSingle().Which.Should().BeSameAs(chart);
    }

    [Fact]
    public void EditingSession_ProtectedChart_CannotBeSelected()
    {
        var (p, bus) = Make();
        var chart = MakeChart();
        chart.Chart!.ChartSelectionProtected = true;
        p.Slides[0].Shapes.Add(chart);
        p.Slides[0].Shapes.Add(MakeShape(2));
        var session = new EditingSession(p, bus);

        session.Select(2);
        session.Select(chart.Id);
        session.SelectedShapeIds.Should().ContainSingle().Which.Should().Be(2);

        session.ClearSelection();
        session.SelectAll();
        session.SelectedShapeIds.Should().ContainSingle().Which.Should().Be(2);
    }

    [Fact]
    public void MoveShapeCommand_Apply_TranslatesShape()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new MoveShapeCommand(0, 1, 500, 300));
        shape.OffsetXEmu.Should().Be(600);
        shape.OffsetYEmu.Should().Be(500);
    }

    [Fact]
    public void MoveShapeCommand_Revert_RestoresPosition()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new MoveShapeCommand(0, 1, 500, 300));
        bus.Undo();
        shape.OffsetXEmu.Should().Be(100);
        shape.OffsetYEmu.Should().Be(200);
    }

    [Fact]
    public void MoveShapeCommand_ProtectedChart_DoesNotChangeGeometry()
    {
        var (p, bus) = Make();
        var chart = MakeChart();
        p.Slides[0].Shapes.Add(chart);

        bus.Execute(new MoveShapeCommand(0, chart.Id, 500, 300));

        chart.OffsetXEmu.Should().Be(100);
        chart.OffsetYEmu.Should().Be(200);

        bus.Undo();
        chart.OffsetXEmu.Should().Be(100);
        chart.OffsetYEmu.Should().Be(200);
        bus.Redo();
        chart.OffsetXEmu.Should().Be(100);
        chart.OffsetYEmu.Should().Be(200);
    }

    [Fact]
    public void MoveShapeCommand_UnprotectedChart_StillChangesGeometry()
    {
        var (p, bus) = Make();
        var chart = MakeChart(protectedObject: false);
        p.Slides[0].Shapes.Add(chart);

        bus.Execute(new MoveShapeCommand(0, chart.Id, 500, 300));

        chart.OffsetXEmu.Should().Be(600);
        chart.OffsetYEmu.Should().Be(500);
    }

    [Fact]
    public void ResizeShapeCommand_Apply_SetsNewGeometry()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new ResizeShapeCommand(0, 1, 10, 20, 500, 600));
        shape.OffsetXEmu.Should().Be(10);
        shape.OffsetYEmu.Should().Be(20);
        shape.ExtentCxEmu.Should().Be(500);
        shape.ExtentCyEmu.Should().Be(600);
    }

    [Fact]
    public void ResizeShapeCommand_Revert_RestoresOldGeometry()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new ResizeShapeCommand(0, 1, 10, 20, 500, 600));
        bus.Undo();
        shape.OffsetXEmu.Should().Be(100);
        shape.OffsetYEmu.Should().Be(200);
        shape.ExtentCxEmu.Should().Be(300);
        shape.ExtentCyEmu.Should().Be(400);
    }

    [Fact]
    public void ResizeShapeCommand_ProtectedChart_DoesNotChangeGeometry()
    {
        var (p, bus) = Make();
        var chart = MakeChart();
        p.Slides[0].Shapes.Add(chart);

        bus.Execute(new ResizeShapeCommand(0, chart.Id, 10, 20, 500, 600));

        chart.OffsetXEmu.Should().Be(100);
        chart.OffsetYEmu.Should().Be(200);
        chart.ExtentCxEmu.Should().Be(300);
        chart.ExtentCyEmu.Should().Be(400);

        bus.Undo();
        chart.OffsetXEmu.Should().Be(100);
        chart.OffsetYEmu.Should().Be(200);
        chart.ExtentCxEmu.Should().Be(300);
        chart.ExtentCyEmu.Should().Be(400);
    }

    [Fact]
    public void SetPictureCropCommand_ApplyUndoRedo_PreservesFormatAndCrop()
    {
        var (p, bus) = Make();
        var picture = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1, 2, 3] },
            PictureFormat = new PictureFormat { Grayscale = true, CropLeft = 0.05 }
        };
        p.Slides[0].Shapes.Add(picture);

        bus.Execute(new SetPictureCropCommand(0, 1, 0.1, 0.2, 0.3, 0.05));
        picture.PictureFormat!.CropLeft.Should().Be(0.1);
        picture.PictureFormat.CropTop.Should().Be(0.2);
        picture.PictureFormat.CropRight.Should().Be(0.3);
        picture.PictureFormat.CropBottom.Should().Be(0.05);
        picture.PictureFormat.Grayscale.Should().BeTrue();

        bus.Undo();
        picture.PictureFormat!.CropLeft.Should().Be(0.05);
        picture.PictureFormat.CropTop.Should().Be(0);
        picture.PictureFormat.CropRight.Should().Be(0);
        picture.PictureFormat.CropBottom.Should().Be(0);
        picture.PictureFormat.Grayscale.Should().BeTrue();

        bus.Redo();
        picture.PictureFormat.CropRight.Should().Be(0.3);
    }

    [Fact]
    public void SetPictureCropCommand_Reset_RemovesNewFormatWithoutEffects()
    {
        var (p, bus) = Make();
        var picture = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1] }
        };
        p.Slides[0].Shapes.Add(picture);

        bus.Execute(new SetPictureCropCommand(0, 1, 0.1, 0.1, 0.1, 0.1));
        picture.PictureFormat.Should().NotBeNull();
        bus.Undo();
        picture.PictureFormat.Should().BeNull();
    }

    [Fact]
    public void SetPictureColorEffectsCommand_ApplyUndoRedo_PreservesCrop()
    {
        var (p, bus) = Make();
        var picture = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1, 2, 3] },
            PictureFormat = new PictureFormat { CropLeft = 0.15, Brightness = 0.2 }
        };
        p.Slides[0].Shapes.Add(picture);

        bus.Execute(new SetPictureColorEffectsCommand(
            0, 1, new PictureColorEffectValues(true, null, null, null, null)));
        picture.PictureFormat!.Grayscale.Should().BeTrue();
        picture.PictureFormat.Brightness.Should().BeNull();
        picture.PictureFormat.CropLeft.Should().BeApproximately(0.15, 0.0001);

        bus.Undo();
        picture.PictureFormat!.Grayscale.Should().BeFalse();
        picture.PictureFormat.Brightness.Should().BeApproximately(0.2, 0.0001);
        picture.PictureFormat.CropLeft.Should().BeApproximately(0.15, 0.0001);

        bus.Redo();
        picture.PictureFormat!.Grayscale.Should().BeTrue();
        picture.PictureFormat.Brightness.Should().BeNull();
    }

    [Fact]
    public void SetPictureColorEffectsCommand_Reset_RemovesFormatWhenPictureHasNoCrop()
    {
        var (p, bus) = Make();
        var picture = new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { Bytes = [1] },
            PictureFormat = new PictureFormat { Grayscale = true, Contrast = -0.2 }
        };
        p.Slides[0].Shapes.Add(picture);

        bus.Execute(new SetPictureColorEffectsCommand(0, 1, PictureColorEffectValues.Reset));
        picture.PictureFormat.Should().BeNull();

        bus.Undo();
        picture.PictureFormat!.Grayscale.Should().BeTrue();
        picture.PictureFormat.Contrast.Should().BeApproximately(-0.2, 0.0001);
    }

    [Fact]
    public void SetShapeGeometryAdjustmentCommand_SetsAndUndoRestoresMissingValue()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetShapeGeometryAdjustmentCommand(0, 1, "adj", 0.42));

        shape.PresetGeometryAdjustments["adj"].Should().BeApproximately(0.42, 0.0001);
        bus.Undo();
        shape.PresetGeometryAdjustments.Should().NotContainKey("adj");
    }

    [Fact]
    public void SetShapeGeometryAdjustmentCommand_RemoveAndUndoRestoresAuthoredValue()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        shape.PresetGeometryAdjustments["adj"] = 0.18;
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetShapeGeometryAdjustmentCommand(0, 1, "adj", null));

        shape.PresetGeometryAdjustments.Should().NotContainKey("adj");
        bus.Undo();
        shape.PresetGeometryAdjustments["adj"].Should().BeApproximately(0.18, 0.0001);
    }

    [Fact]
    public void SetCustomGeometryPointCommand_ApplyUndoAndRedoPreservesVertex()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 10, Y: 20));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 90, Y: 20));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        shape.CustomGeometry.Add(path);
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetCustomGeometryPointCommand(0, 1, 0, 1, 60, 70));
        path.Segments[1].X.Should().Be(60);
        path.Segments[1].Y.Should().Be(70);

        bus.Undo();
        path.Segments[1].X.Should().Be(90);
        path.Segments[1].Y.Should().Be(20);

        bus.Redo();
        path.Segments[1].X.Should().Be(60);
        path.Segments[1].Y.Should().Be(70);
    }

    [Fact]
    public void SetCustomGeometryPointCommand_ApplyUndoAndRedoPreservesCurveControl()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 50));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.CubicBezTo,
            X: 20, Y: 0, X1: 80, Y1: 0, X2: 100, Y2: 50));
        shape.CustomGeometry.Add(path);
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetCustomGeometryPointCommand(
            0, 1, 0, 1, 60, 70, CustomGeometryPointSlot.Control2));
        path.Segments[1].X.Should().Be(20);
        path.Segments[1].X1.Should().Be(60);
        path.Segments[1].Y1.Should().Be(70);

        bus.Undo();
        path.Segments[1].X1.Should().Be(80);
        path.Segments[1].Y1.Should().Be(0);

        bus.Redo();
        path.Segments[1].X1.Should().Be(60);
        path.Segments[1].Y1.Should().Be(70);
    }

    [Fact]
    public void SetCustomGeometryArcPointCommand_ApplyUndoAndRedoPreservesArcFields()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 40, Y: 0));
        path.Segments.Add(new CustomSegment(
            CustomSegmentKind.ArcTo, WR: 40, HR: 30, StAng: 0, SwAng: 90));
        shape.CustomGeometry.Add(path);
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetCustomGeometryArcPointCommand(
            0, 1, 0, 1, 180, CustomGeometryArcPointSlot.EndAngle));
        path.Segments[1].SwAng.Should().Be(180);

        bus.Undo();
        path.Segments[1].SwAng.Should().Be(90);

        bus.Redo();
        path.Segments[1].SwAng.Should().Be(180);

        bus.Execute(new SetCustomGeometryArcPointCommand(
            0, 1, 0, 1, 25, CustomGeometryArcPointSlot.RadiusX));
        path.Segments[1].WR.Should().Be(25);
        bus.Undo();
        path.Segments[1].WR.Should().Be(40);
    }

    [Fact]
    public void CustomGeometryPointCommands_ApplyUndoAndRedoInsertAndDelete()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        var path = new CustomGeometryPath { PathW = 100, PathH = 100 };
        path.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, X: 0, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 100, Y: 0));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, X: 50, Y: 100));
        path.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        shape.CustomGeometry.Add(path);
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new InsertCustomGeometryPointCommand(0, 1, 0, 1, 75, 50));
        path.Segments.Should().HaveCount(5);
        path.Segments[2].X.Should().Be(75);
        path.Segments[2].Y.Should().Be(50);
        bus.Undo();
        path.Segments.Should().HaveCount(4);
        bus.Redo();
        path.Segments.Should().HaveCount(5);

        bus.Execute(new DeleteCustomGeometryPointCommand(0, 1, 0, 2));
        path.Segments.Should().HaveCount(4);
        bus.Undo();
        path.Segments.Should().HaveCount(5);
        path.Segments[2].X.Should().Be(75);
        bus.Redo();
        path.Segments.Should().HaveCount(4);
    }

    [Fact]
    public void RotateShapeCommand_Apply_SetsRotation()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new RotateShapeCommand(0, 1, 45.0));
        shape.RotationDeg.Should().Be(45.0);
    }

    [Fact]
    public void RotateShapeCommand_Revert_RestoresOldRotation()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        shape.RotationDeg = 30;
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new RotateShapeCommand(0, 1, 90.0));
        bus.Undo();
        shape.RotationDeg.Should().Be(30.0);
    }

    [Fact]
    public void RotateShapeCommand_ProtectedChart_DoesNotChangeRotation()
    {
        var (p, bus) = Make();
        var chart = MakeChart();
        p.Slides[0].Shapes.Add(chart);

        bus.Execute(new RotateShapeCommand(0, chart.Id, 90.0));

        chart.RotationDeg.Should().Be(15.0);
        bus.Undo();
        chart.RotationDeg.Should().Be(15.0);
    }

    [Fact]
    public void SetShapeFillCommand_Apply_SetsFill()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        var fill = new ShapeFill.Solid(new SrgbColor(0xFF, 0, 0));
        bus.Execute(new SetShapeFillCommand(0, 1, fill));
        shape.Fill.Should().BeSameAs(fill);
    }

    [Fact]
    public void SetShapeFillCommand_Revert_RestoresOldFill()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        var oldFill = ShapeFill.None.Instance;
        shape.Fill = oldFill;
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new SetShapeFillCommand(0, 1, new ShapeFill.Solid(new SrgbColor(0, 0xFF, 0))));
        bus.Undo();
        shape.Fill.Should().BeSameAs(oldFill);
    }

    [Fact]
    public void SetShapeOutlineCommand_Apply_SetsOutline()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        var outline = new ShapeOutline.Visible(SrgbColor.Black, 2.0);
        bus.Execute(new SetShapeOutlineCommand(0, 1, outline));
        shape.Outline.Should().BeSameAs(outline);
    }

    [Fact]
    public void SetShapeOutlineCommand_Revert_RestoresOldOutline()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        shape.Outline = ShapeOutline.None.Instance;
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new SetShapeOutlineCommand(0, 1, new ShapeOutline.Visible(SrgbColor.Black)));
        bus.Undo();
        shape.Outline.Should().BeSameAs(ShapeOutline.None.Instance);
    }

    [Fact]
    public void ReorderShapeCommand_Apply_MovesShapeToNewZIndex()
    {
        var (p, bus) = Make();
        var s1 = MakeShape(1); var s2 = MakeShape(2); var s3 = MakeShape(3);
        p.Slides[0].Shapes.AddRange([s1, s2, s3]);
        // Move s1 from index 0 to index 2. After removal [s2,s3], insert at 2 => [s2,s3,s1]
        bus.Execute(new ReorderShapeCommand(0, s1.Id, 2));
        p.Slides[0].Shapes[2].Should().BeSameAs(s1);
    }

    [Fact]
    public void ReorderShapeCommand_Revert_RestoresOriginalZOrder()
    {
        var (p, bus) = Make();
        var s1 = MakeShape(1); var s2 = MakeShape(2); var s3 = MakeShape(3);
        p.Slides[0].Shapes.AddRange([s1, s2, s3]);
        bus.Execute(new ReorderShapeCommand(0, s1.Id, 2));
        bus.Undo();
        p.Slides[0].Shapes[0].Should().BeSameAs(s1);
    }

    [Fact]
    public void ReorderShapeCommand_ApplyAndRevert_MovesNestedChildWithinGroup()
    {
        var (p, bus) = Make();
        var first = MakeShape(1);
        var group = new SlideShape
        {
            Id = 2,
            Kind = SlideShapeKind.Group,
            Children =
            {
                MakeShape(3),
                MakeShape(4),
                MakeShape(5)
            }
        };
        p.Slides[0].Shapes.AddRange([first, group]);

        bus.Execute(new ReorderShapeCommand(0, 3, 2));
        group.Children.Select(shape => shape.Id).Should().Equal(4u, 5u, 3u);
        p.Slides[0].Shapes.Select(shape => shape.Id).Should().Equal(1u, 2u);

        bus.Undo();
        group.Children.Select(shape => shape.Id).Should().Equal(3u, 4u, 5u);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // TEXT / RUN-FORMAT COMMANDS
    // ════════════════════════════════════════════════════════════════════════════

    private static (Presentation p, PresentationCommandBus bus, SlideShape shape, Run run) MakeShapeWithRun()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        var tb = new TextBody();
        var para = new Paragraph();
        var run  = new Run { Text = "Hello", Bold = false };
        para.Runs.Add(run);
        tb.Paragraphs.Add(para);
        shape.TextBody = tb;
        p.Slides[0].Shapes.Add(shape);
        return (p, bus, shape, run);
    }

    [Fact]
    public void SetShapeTextCommand_Apply_ReplacesTextBody()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        p.Slides[0].Shapes.Add(shape);
        var newBody = new TextBody();
        bus.Execute(new SetShapeTextCommand(0, 1, newBody));
        shape.TextBody.Should().BeSameAs(newBody);
    }

    [Fact]
    public void SetShapeTextCommand_Revert_RestoresOldBody()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        var oldBody = new TextBody();
        shape.TextBody = oldBody;
        p.Slides[0].Shapes.Add(shape);
        bus.Execute(new SetShapeTextCommand(0, 1, new TextBody()));
        bus.Undo();
        shape.TextBody.Should().BeSameAs(oldBody);
    }

    [Fact]
    public void SetShapeTextAutoFitCommand_ApplyUndoAndRedo_PreservesThreeStateMode()
    {
        var (p, bus) = Make();
        var shape = MakeShape();
        shape.TextBody = new TextBody { AutoFitKind = TextAutoFitKind.None };
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetShapeTextAutoFitCommand(0, shape.Id, TextAutoFitKind.Normal));
        shape.TextBody!.AutoFitKind.Should().Be(TextAutoFitKind.Normal);

        bus.Undo();
        shape.TextBody.AutoFitKind.Should().Be(TextAutoFitKind.None);

        bus.Redo();
        shape.TextBody.AutoFitKind.Should().Be(TextAutoFitKind.Normal);
    }

    [Fact]
    public void SetShapeTextAutoFitCommand_NoOp_DoesNotAddUndoEntry()
    {
        var (p, bus) = Make();
        var shape = MakeShape();
        shape.TextBody = new TextBody { AutoFitKind = TextAutoFitKind.Shape };
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetShapeTextAutoFitCommand(0, shape.Id, TextAutoFitKind.Shape));
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetShapeTextVerticalTypeCommand_ApplyUndoAndRedo_PreservesOrientation()
    {
        var (p, bus) = Make();
        var shape = MakeShape();
        shape.TextBody = new TextBody { VerticalType = TextVerticalType.Horizontal };
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetShapeTextVerticalTypeCommand(0, shape.Id, TextVerticalType.Vertical270));
        shape.TextBody!.VerticalType.Should().Be(TextVerticalType.Vertical270);

        bus.Undo();
        shape.TextBody.VerticalType.Should().Be(TextVerticalType.Horizontal);

        bus.Redo();
        shape.TextBody.VerticalType.Should().Be(TextVerticalType.Vertical270);
    }

    [Fact]
    public void SetShapeTextColumnCountCommand_ApplyUndoAndRedo_PreservesCountAndSpacing()
    {
        var (p, bus) = Make();
        var shape = MakeShape();
        shape.TextBody = new TextBody { ColumnCount = 1, ColumnSpacingEmu = 457200 };
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetShapeTextColumnCountCommand(0, shape.Id, 3));
        shape.TextBody!.ColumnCount.Should().Be(3);
        shape.TextBody.ColumnSpacingEmu.Should().Be(457200);

        bus.Undo();
        shape.TextBody.ColumnCount.Should().Be(1);
        shape.TextBody.ColumnSpacingEmu.Should().Be(457200);

        bus.Redo();
        shape.TextBody.ColumnCount.Should().Be(3);
    }

    [Fact]
    public void SetShapeTextColumnCountCommand_NoOp_DoesNotAddUndoEntry()
    {
        var (p, bus) = Make();
        var shape = MakeShape();
        shape.TextBody = new TextBody { ColumnCount = 2 };
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetShapeTextColumnCountCommand(0, shape.Id, 2));
        bus.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SetShapeTextColumnSpacingCommand_ApplyUndoAndRedo()
    {
        var (p, bus) = Make();
        var shape = MakeShape();
        shape.TextBody = new TextBody { ColumnCount = 3, ColumnSpacingEmu = 50_800 };
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new SetShapeTextColumnSpacingCommand(0, shape.Id, 152_400));
        shape.TextBody!.ColumnSpacingEmu.Should().Be(152_400);

        bus.Undo();
        shape.TextBody.ColumnSpacingEmu.Should().Be(50_800);

        bus.Redo();
        shape.TextBody.ColumnSpacingEmu.Should().Be(152_400);
        shape.TextBody.ColumnCount.Should().Be(3);
    }

    [Fact]
    public void SetTableCellTextVerticalTypeCommand_ApplyUndoAndRedo_PreservesOrientation()
    {
        var (p, bus) = Make();
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(DrawingMlCoordinateUnits.EmuPerInch);
        table.Rows.Add(new TableRow
        {
            HeightEmu = DrawingMlCoordinateUnits.EmuPerInch / 2,
            Cells = { new TableCell { TextBody = new TextBody() } }
        });
        p.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.Table,
            Table = table
        });

        var cellBody = table.Rows[0].Cells[0].TextBody!;
        bus.Execute(new SetTableCellTextVerticalTypeCommand(0, 7, 0, 0, TextVerticalType.EastAsianVertical));
        cellBody.VerticalType.Should().Be(TextVerticalType.EastAsianVertical);

        bus.Undo();
        cellBody.VerticalType.Should().Be(TextVerticalType.Horizontal);

        bus.Redo();
        cellBody.VerticalType.Should().Be(TextVerticalType.EastAsianVertical);
    }

    [Fact]
    public void ToggleRunBoldCommand_Apply_TogglesBold()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        bus.Execute(new ToggleRunBoldCommand(0, 1, 0, 0));
        run.Bold.Should().BeTrue();
    }

    [Fact]
    public void ToggleRunBoldCommand_Revert_RestoresBold()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        bus.Execute(new ToggleRunBoldCommand(0, 1, 0, 0));
        bus.Undo();
        run.Bold.Should().BeFalse();
    }

    // ── RR1: inherited-bold undo must restore inherited state (BoldSet=false) ──────────────────────

    /// <summary>
    /// RR1: A run that INHERITS bold (BoldSet=false, Bold=false — compositor renders bold via master).
    /// Apply: effective bold was false (run.Bold=false), so toggle makes it Bold=false,BoldSet=true (explicit non-bold).
    /// Undo: must restore BoldSet=false (inherit), not leave it as BoldSet=true (explicit non-bold).
    /// </summary>
    [Fact]
    public void ToggleRunBoldCommand_Revert_RestoresInheritedBold_WhenRunWasInheriting()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        // Set up: run inherits bold (BoldSet=false, Bold=false).
        run.Bold    = false;
        run.BoldSet = false;

        bus.Execute(new ToggleRunBoldCommand(0, 1, 0, 0));
        // After apply: Bold=true (toggled from false), BoldSet=true (explicit).
        run.BoldSet.Should().BeTrue("forward toggle makes the value explicit");
        run.Bold.Should().BeTrue();

        bus.Undo();
        // After undo: prior state exactly restored — BoldSet=false (inherit), Bold=false.
        run.BoldSet.Should().BeFalse("undo must restore inherited state, not bake explicit non-bold");
        run.Bold.Should().BeFalse();
    }

    /// <summary>
    /// RR1: A run that was EXPLICIT bold (BoldSet=true, Bold=true).
    /// Toggle → (Bold=false, BoldSet=true).  Undo → (Bold=true, BoldSet=true).
    /// </summary>
    [Fact]
    public void ToggleRunBoldCommand_Revert_RestoresExplicitBold_WhenRunWasExplicitlyBold()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        run.Bold    = true;
        run.BoldSet = true;

        bus.Execute(new ToggleRunBoldCommand(0, 1, 0, 0));
        run.Bold.Should().BeFalse("toggling explicit-bold gives explicit non-bold");
        run.BoldSet.Should().BeTrue();

        bus.Undo();
        run.Bold.Should().BeTrue("undo restores explicit bold");
        run.BoldSet.Should().BeTrue();
    }

    /// <summary>
    /// RR1: Multi-run scenario — each run's prior (Bold, BoldSet) is restored independently.
    /// We simulate two consecutive commands (one per run) and undo both.
    /// </summary>
    [Fact]
    public void ToggleRunBoldCommand_Revert_RestoresEachRunIndependently()
    {
        var (p, bus) = Make();
        var shape = MakeShape(1);
        var tb    = new TextBody();
        var para  = new Paragraph();

        // run0: inherited bold (BoldSet=false, Bold=false)
        var run0 = new Run { Text = "R0", Bold = false, BoldSet = false };
        // run1: explicit bold (BoldSet=true, Bold=true)
        var run1 = new Run { Text = "R1", Bold = true,  BoldSet = true };
        para.Runs.Add(run0);
        para.Runs.Add(run1);
        tb.Paragraphs.Add(para);
        shape.TextBody = tb;
        p.Slides[0].Shapes.Add(shape);

        bus.Execute(new ToggleRunBoldCommand(0, 1, 0, 0)); // toggle run0
        bus.Execute(new ToggleRunBoldCommand(0, 1, 0, 1)); // toggle run1

        bus.Undo(); // undo run1 toggle
        run1.Bold.Should().BeTrue("run1 undo restores explicit-bold");
        run1.BoldSet.Should().BeTrue();

        bus.Undo(); // undo run0 toggle
        run0.Bold.Should().BeFalse("run0 undo restores inherited-bold (Bold=false)");
        run0.BoldSet.Should().BeFalse("run0 undo restores BoldSet=false (inherit)");
    }

    // ── RR1: same tests for italic ────────────────────────────────────────────────────────────────

    /// <summary>
    /// RR1 italic: run inherits italic (ItalicSet=false, Italic=false).
    /// Undo must restore ItalicSet=false (inherit).
    /// </summary>
    [Fact]
    public void ToggleRunItalicCommand_Revert_RestoresInheritedItalic_WhenRunWasInheriting()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        run.Italic    = false;
        run.ItalicSet = false;

        bus.Execute(new ToggleRunItalicCommand(0, 1, 0, 0));
        run.ItalicSet.Should().BeTrue("forward toggle makes the value explicit");
        run.Italic.Should().BeTrue();

        bus.Undo();
        run.ItalicSet.Should().BeFalse("undo must restore inherited state, not bake explicit non-italic");
        run.Italic.Should().BeFalse();
    }

    /// <summary>
    /// RR1 italic: run was explicit italic (ItalicSet=true, Italic=true).
    /// Undo restores (Italic=true, ItalicSet=true).
    /// </summary>
    [Fact]
    public void ToggleRunItalicCommand_Revert_RestoresExplicitItalic_WhenRunWasExplicitlyItalic()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        run.Italic    = true;
        run.ItalicSet = true;

        bus.Execute(new ToggleRunItalicCommand(0, 1, 0, 0));
        run.Italic.Should().BeFalse();
        run.ItalicSet.Should().BeTrue();

        bus.Undo();
        run.Italic.Should().BeTrue("undo restores explicit italic");
        run.ItalicSet.Should().BeTrue();
    }

    [Fact]
    public void ToggleRunItalicCommand_Apply_TogglesItalic()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        bus.Execute(new ToggleRunItalicCommand(0, 1, 0, 0));
        run.Italic.Should().BeTrue();
    }

    [Fact]
    public void ToggleRunUnderlineCommand_Apply_TogglesUnderline()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        bus.Execute(new ToggleRunUnderlineCommand(0, 1, 0, 0));
        run.Underline.Should().BeTrue();
    }

    [Fact]
    public void SetRunFontCommand_Apply_SetsFont()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        bus.Execute(new SetRunFontCommand(0, 1, 0, 0, "Arial"));
        run.FontFamily.Should().Be("Arial");
    }

    [Fact]
    public void SetRunFontCommand_Revert_RestoresFont()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        run.FontFamily = "Calibri";
        bus.Execute(new SetRunFontCommand(0, 1, 0, 0, "Arial"));
        bus.Undo();
        run.FontFamily.Should().Be("Calibri");
    }

    [Fact]
    public void SetRunFontSizeCommand_Apply_SetsSize()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        bus.Execute(new SetRunFontSizeCommand(0, 1, 0, 0, 24.0));
        run.FontSizePt.Should().Be(24.0);
    }

    [Fact]
    public void SetRunFontSizeCommand_Revert_RestoresOldSize()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        run.FontSizePt = 18.0;
        bus.Execute(new SetRunFontSizeCommand(0, 1, 0, 0, 24.0));
        bus.Undo();
        run.FontSizePt.Should().Be(18.0);
    }

    [Fact]
    public void SetRunColorCommand_Apply_SetsColor()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        var color = ThemeAwareColor.Black;
        bus.Execute(new SetRunColorCommand(0, 1, 0, 0, color));
        run.Color.Should().Be(color);
    }

    [Fact]
    public void SetRunColorCommand_Revert_RestoresOldColor()
    {
        var (p, bus, _, run) = MakeShapeWithRun();
        bus.Execute(new SetRunColorCommand(0, 1, 0, 0, ThemeAwareColor.Black));
        bus.Undo();
        run.Color.Should().BeNull();
    }

    // ════════════════════════════════════════════════════════════════════════════
    // BUS MECHANICS
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Bus_CanUndo_IsTrueAfterExecute()
    {
        var (p, bus) = Make();
        bus.Execute(new AddSlideCommand(new Slide()));
        bus.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void Bus_CanRedo_IsTrueAfterUndo()
    {
        var (p, bus) = Make();
        bus.Execute(new AddSlideCommand(new Slide()));
        bus.Undo();
        bus.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void Bus_CanRedo_IsFalseAfterNewExecute()
    {
        var (p, bus) = Make();
        bus.Execute(new AddSlideCommand(new Slide()));
        bus.Undo();
        bus.Execute(new AddSlideCommand(new Slide()));
        bus.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Bus_Changed_FiresOnExecute()
    {
        var (p, bus) = Make();
        int fired = 0;
        bus.Changed += () => fired++;
        bus.Execute(new AddSlideCommand(new Slide()));
        fired.Should().Be(1);
    }

    [Fact]
    public void Bus_Changed_FiresOnUndoAndRedo()
    {
        var (p, bus) = Make();
        int fired = 0;
        bus.Changed += () => fired++;
        bus.Execute(new AddSlideCommand(new Slide()));
        bus.Undo();
        bus.Redo();
        fired.Should().Be(3);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // DEEP-CLONE
    // ════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SlideCloner_CloneSlide_NewId()
    {
        var slide = new Slide { Title = "Orig" };
        var clone = SlideCloner.CloneSlide(slide);
        clone.Id.Should().NotBe(slide.Id);
    }

    [Fact]
    public void SlideCloner_CloneSlide_SameTitle()
    {
        var slide = new Slide();
        // Set title directly via shape so we don't accidentally pick up CreateEmpty shapes.
        slide.Title = "Orig";
        var clone = SlideCloner.CloneSlide(slide);
        clone.Title.Should().Be("Orig");
    }

    [Fact]
    public void SlideCloner_CloneSlide_PreservesHiddenAndColorMapOverride()
    {
        var slide = new Slide
        {
            IsHidden = true,
            ColorMapOverride = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tx1"] = "lt1",
                ["bg1"] = "dk1",
            },
        };

        var clone = SlideCloner.CloneSlide(slide);

        clone.IsHidden.Should().BeTrue();
        clone.ColorMapOverride.Should().NotBeNull();
        clone.ColorMapOverride.Should().NotBeSameAs(slide.ColorMapOverride);
        clone.ColorMapOverride!["tx1"].Should().Be("lt1");
        clone.ColorMapOverride["BG1"].Should().Be("dk1");

        clone.ColorMapOverride["tx1"] = "dk2";
        slide.ColorMapOverride!["tx1"].Should().Be("lt1");
    }

    [Fact]
    public void SlideCloner_MutatingCloneDoesNotTouchOriginal()
    {
        var slide = new Slide();
        var shape = MakeShape(1);
        slide.Shapes.Add(shape);
        var clone = SlideCloner.CloneSlide(slide);
        clone.Shapes[0].OffsetXEmu = 99999;
        shape.OffsetXEmu.Should().Be(100, "original must not be affected");
    }

    [Fact]
    public void SlideCloner_CloneShape_PreservesPiePointExplosion()
    {
        var shape = MakeChart(1, protectedObject: false);
        shape.Chart!.ChartType = ChartType.Pie;
        var series = new ChartSeries { Name = "Share" };
        series.Values.AddRange(new double?[] { 2, 3 });
        series.PointStyles[1] = new ChartPointStyle { ExplosionPercent = 40 };
        shape.Chart.Series.Add(series);

        var clone = SlideCloner.CloneShape(shape);

        clone.Chart!.Series[0].PointStyles[1].ExplosionPercent.Should().Be(40);
        clone.Chart.Series[0].PointStyles[1].ExplosionPercent = 10;
        shape.Chart.Series[0].PointStyles[1].ExplosionPercent.Should().Be(40);
    }

    [Fact]
    public void SlideCloner_CloneShape_ClonesMediaAndCaptionTracks()
    {
        var shape = MakeShape(1);
        shape.Media = new MediaInfo
        {
            IsVideo = true,
            Bytes = new byte[] { 1, 2, 3 },
            ContentType = "video/mp4",
            SourcePackagePath = "ppt/media/video1.mp4",
            LinkUrl = "https://example.invalid/video.mp4",
        };
        shape.Media.CaptionTracks.Add(new MediaCaptionTrackInfo
        {
            RelationshipId = "rIdCaption1",
            Source = "captions/en.vtt",
            Bytes = new byte[] { 10, 20 },
            ContentType = "text/vtt",
            Language = "en-US",
            Label = "English",
            IsExternal = false,
        });

        var clone = SlideCloner.CloneShape(shape);

        clone.Media.Should().NotBeNull();
        clone.Media.Should().NotBeSameAs(shape.Media);
        clone.Media!.CaptionTracks.Should().HaveCount(1);
        clone.Media.CaptionTracks.Should().NotBeSameAs(shape.Media.CaptionTracks);
        clone.Media.CaptionTracks[0].Label.Should().Be("English");
        clone.Media.CaptionTracks[0].Bytes.Should().Equal(10, 20);

        clone.Media.CaptionTracks[0].Label = "French";
        clone.Media.CaptionTracks[0].Bytes[0] = 99;
        clone.Media.Bytes[0] = 88;
        shape.Media.CaptionTracks[0].Label.Should().Be("English");
        shape.Media.CaptionTracks[0].Bytes[0].Should().Be(10);
        shape.Media.Bytes[0].Should().Be(1);
    }

    [Fact]
    public void SlideCloner_CloneShape_ClonesEmbeddedAndPreservedPackagePayloads()
    {
        var shape = MakeShape(1);
        shape.OleObject = new OleObjectInfo
        {
            EmbeddedBytes = new byte[] { 1, 2, 3 },
            EmbeddedContentType = "application/octet-stream",
            ProgId = "Excel.Sheet.12",
        };
        shape.PreservedObject = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Ink,
            RawXml = "<p:contentPart />",
        };
        shape.PreservedObject.Parts["ppt/ink/ink1.xml"] = new byte[] { 4, 5, 6 };
        shape.PreservedObject.PartRels["ppt/ink/ink1.xml"] = new byte[] { 7, 8 };

        var clone = SlideCloner.CloneShape(shape);

        clone.OleObject.Should().NotBeSameAs(shape.OleObject);
        clone.OleObject!.EmbeddedBytes.Should().NotBeSameAs(shape.OleObject!.EmbeddedBytes);
        clone.PreservedObject.Should().NotBeSameAs(shape.PreservedObject);
        clone.PreservedObject!.Parts["ppt/ink/ink1.xml"]
            .Should().NotBeSameAs(shape.PreservedObject.Parts["ppt/ink/ink1.xml"]);
        clone.PreservedObject.PartRels["ppt/ink/ink1.xml"]
            .Should().NotBeSameAs(shape.PreservedObject.PartRels["ppt/ink/ink1.xml"]);

        clone.OleObject.EmbeddedBytes[0] = 10;
        clone.PreservedObject.Parts["ppt/ink/ink1.xml"][0] = 11;
        clone.PreservedObject.PartRels["ppt/ink/ink1.xml"][0] = 12;

        shape.OleObject.EmbeddedBytes[0].Should().Be(1);
        shape.PreservedObject.Parts["ppt/ink/ink1.xml"][0].Should().Be(4);
        shape.PreservedObject.PartRels["ppt/ink/ink1.xml"][0].Should().Be(7);
    }

    [Fact]
    public void SlideCloner_CloneShape_ClonesTextBody()
    {
        var shape = MakeShape(1);
        shape.TextBody = new TextBody();
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = "hello", Bold = true });
        shape.TextBody.Paragraphs.Add(para);

        var clone = SlideCloner.CloneShape(shape);
        clone.TextBody.Should().NotBeNull();
        clone.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("hello");
        clone.TextBody.Paragraphs[0].Runs[0].Bold.Should().BeTrue();

        // Mutating clone's run should not affect original.
        clone.TextBody.Paragraphs[0].Runs[0].Bold = false;
        para.Runs[0].Bold.Should().BeTrue();
    }

    [Fact]
    public void SlideCloner_CloneShape_PreservesShapeEffectsAndZeroExtentFlag()
    {
        var shape = MakeShape(1);
        shape.HasExplicitZeroExtentTransform = true;
        shape.Effects = new ShapeEffects
        {
            HasOuterShadow = true,
            OuterShadowAlpha = 166,
            OuterShadowBlurRadEmu = 50800,
            OuterShadowDistEmu = 38100,
            OuterShadowDirDeg = 45,
            HasGlow = true,
            GlowRadiusEmu = 25400,
        };

        var clone = SlideCloner.CloneShape(shape);

        clone.HasExplicitZeroExtentTransform.Should().BeTrue();
        clone.Effects.Should().NotBeNull();
        clone.Effects.Should().NotBeSameAs(shape.Effects);
        clone.Effects!.HasOuterShadow.Should().BeTrue();
        clone.Effects.OuterShadowBlurRadEmu.Should().Be(50800);
        clone.Effects.HasGlow.Should().BeTrue();
        clone.Effects.GlowRadiusEmu.Should().Be(25400);
    }

    [Fact]
    public void SlideCloner_CloneShape_ClonesGroupChildren()
    {
        var group = new SlideShape { Id = 10, Kind = SlideShapeKind.Group };
        group.Children.Add(MakeShape(1));
        var clone = SlideCloner.CloneShape(group);
        clone.Children.Should().HaveCount(1);
        clone.Children[0].Should().NotBeSameAs(group.Children[0]);
    }
}
