using System.IO;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentObjectEditingCoordinatorTests
{
    [Fact]
    public void InvalidTarget_DoesNotEnterUndoHistoryOrRaiseChanged()
    {
        var session = SessionWith(new Paragraph("body"));
        var changed = 0;
        session.Changed += () => changed++;

        var result = session.Objects.SetImageCrop(
            new DocumentObjectTarget(0, 99),
            0.1,
            0.2,
            0.3,
            0.4);

        result.Applied.Should().BeFalse();
        result.Kind.Should().Be(DocumentObjectKind.None);
        changed.Should().Be(0);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void NestedImageSpecificMutation_IsRejectedWithoutHistory()
    {
        var image = new InlineImage([], 80, 40);
        var group = new DrawingGroup();
        group.Children.Add(image);
        group.ChildOffsets.Add((0, 0));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        var session = SessionWith(paragraph);

        var result = session.Objects.SetImageSize(
            new DocumentObjectTarget(0, 0, [0]),
            120,
            60);

        result.Applied.Should().BeFalse();
        (image.WidthPt, image.HeightPt).Should().Be((80, 40));
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Group_IgnoresInlineObjectsWithoutEnteringHistory()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage([], 80, 40)));
        paragraph.Runs.Add(Run.FromImage(new InlineImage([], 60, 30)));
        var session = SessionWith(paragraph);

        var result = session.Objects.Group(
            [new DocumentObjectTarget(0, 0), new DocumentObjectTarget(0, 1)]);

        result.Applied.Should().BeFalse();
        paragraph.Runs.Count.Should().Be(2);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ImageShapeChartAndSmartArtMutations_ShareSessionHistoryAndUndoInOrder()
    {
        var image = new InlineImage([], 80, 40);
        var shape = new Shape(ShapeKind.Rectangle, 72, 36);
        var chart = new Chart();
        var smartArt = SmartArt.Create(SmartArtKind.List, ["One", "Two"]);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        paragraph.Runs.Add(Run.FromShape(shape));
        paragraph.Runs.Add(Run.FromChart(chart));
        paragraph.Runs.Add(Run.FromSmartArt(smartArt));
        var session = SessionWith(paragraph);
        var changed = 0;
        session.Changed += () => changed++;

        session.Objects.SetImageCrop(new DocumentObjectTarget(0, 0), 0.1, 0.2, 0.3, 0.4)
            .Applied.Should().BeTrue();
        session.Objects.SetShapeFill(new DocumentObjectTarget(0, 1), "#112233")
            .Applied.Should().BeTrue();
        session.Objects.SetChartTitle(new DocumentObjectTarget(0, 2), "Revenue")
            .Applied.Should().BeTrue();
        session.Objects.SetSmartArtColor(new DocumentObjectTarget(0, 3), "colorful1")
            .Applied.Should().BeTrue();

        changed.Should().Be(4);
        image.CropLeft.Should().Be(0.1);
        shape.FillColorHex.Should().Be("#112233");
        chart.Title.Should().Be("Revenue");
        smartArt.ColorSchemeId.Should().Be("colorful1");

        session.Commands.Undo().Should().BeTrue();
        smartArt.ColorSchemeId.Should().BeNull();
        session.Commands.Undo().Should().BeTrue();
        chart.Title.Should().BeNull();
        session.Commands.Undo().Should().BeTrue();
        shape.FillColorHex.Should().BeNull();
        session.Commands.Undo().Should().BeTrue();
        image.HasCrop.Should().BeFalse();
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void NestedShapeMutation_PreservesFullChildPathAndUndo()
    {
        var nestedShape = new Shape(ShapeKind.Rectangle, 30, 20);
        var inner = new DrawingGroup();
        inner.Children.Add(nestedShape);
        inner.Children.Add(new InlineImage([], 10, 10));
        inner.ChildOffsets.Add((1, 2));
        inner.ChildOffsets.Add((3, 4));
        var outer = new DrawingGroup();
        outer.Children.Add(new InlineImage([], 12, 12));
        outer.Children.Add(inner);
        outer.ChildOffsets.Add((5, 6));
        outer.ChildOffsets.Add((7, 8));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromDrawingGroup(outer));
        var session = SessionWith(paragraph);
        var target = new DocumentObjectTarget(0, 0, [1, 0]);

        var result = session.Objects.SetShapeKind(target, ShapeKind.Ellipse);

        result.Should().Be(new DocumentObjectEditResult(true, target, DocumentObjectKind.Shape));
        nestedShape.Kind.Should().Be(ShapeKind.Ellipse);
        session.Commands.Undo().Should().BeTrue();
        nestedShape.Kind.Should().Be(ShapeKind.Rectangle);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void ShapePosition_ResolvesDirectPlacementAndNestedGroupOffset()
    {
        var direct = new Shape(ShapeKind.Rectangle, 30, 20)
        {
            Placement = new FloatingPlacement
            {
                HorizontalOffsetPt = 12,
                VerticalOffsetPt = 18,
                HorizontalAnchor = HorizontalAnchor.Page,
                VerticalAnchor = VerticalAnchor.Margin,
            },
        };
        var nested = new Shape(ShapeKind.Ellipse, 10, 10);
        var group = new DrawingGroup();
        group.Children.Add(nested);
        group.ChildOffsets.Add((7, 9));
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(direct));
        paragraph.Runs.Add(Run.FromDrawingGroup(group));
        var session = SessionWith(paragraph);

        session.Objects.GetShapePosition(new DocumentObjectTarget(0, 0)).Should().Be(
            new DocumentShapePositionPlan(12, 18, HorizontalAnchor.Page, VerticalAnchor.Margin, false));
        session.Objects.GetShapePosition(new DocumentObjectTarget(0, 1, [0])).Should().Be(
            new DocumentShapePositionPlan(7, 9, HorizontalAnchor.Column, VerticalAnchor.Paragraph, true));
    }

    [Fact]
    public void ResizeAndMoveFloatingImage_IsOneUndoableEdit()
    {
        var image = new InlineImage([], 80, 40)
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 10,
            VerticalOffsetPt = 20
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        var session = SessionWith(paragraph);
        var changed = 0;
        session.Changed += () => changed++;

        session.Objects.ResizeAndMove(
                new DocumentObjectTarget(0, 0),
                120,
                60,
                3,
                -2)
            .Applied.Should().BeTrue();

        changed.Should().Be(1);
        (image.WidthPt, image.HeightPt).Should().Be((120, 60));
        (image.HorizontalOffsetPt, image.VerticalOffsetPt).Should().Be((13, 18));

        session.Commands.Undo().Should().BeTrue();
        (image.WidthPt, image.HeightPt).Should().Be((80, 40));
        (image.HorizontalOffsetPt, image.VerticalOffsetPt).Should().Be((10, 20));
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void RotateBy_UsesSharedDeltaSemanticsAndUndo()
    {
        var chart = new Chart
        {
            RotationAngle = 350,
            Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square }
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        var session = SessionWith(paragraph);

        session.Objects.RotateBy(new DocumentObjectTarget(0, 0), 20)
            .Applied.Should().BeTrue();

        chart.RotationAngle.Should().Be(10);
        session.Commands.Undo().Should().BeTrue();
        chart.RotationAngle.Should().Be(350);
    }

    private static DocumentEditingSession SessionWith(Paragraph paragraph)
    {
        var document = new TextDocument();
        document.Blocks.Add(paragraph);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }
}

public sealed class DocumentObjectEditingOwnershipSourceTests
{
    private static readonly string[] MigratedCommandTypes =
    [
        "SetImageSizeCommand",
        "SetImageAltTextCommand",
        "SetImageRotationCommand",
        "SetImageCropCommand",
        "SetImageAdjustCommand",
        "SetImageBorderCommand",
        "SetImageEffectCommand",
        "SetImageRecolorCommand",
        "SetImageArtisticEffectCommand",
        "SetImageStyleCommand",
        "ResetImageSizeCommand",
        "SetImagePositionCommand",
        "NudgeImagePositionCommand",
        "SetShapeKindCommand",
        "SetShapeCustomGeometryCommand",
        "MoveShapeEditPointCommand",
        "SetShapeFillCommand",
        "SetShapeOutlineCommand",
        "SetShapeSizeCommand",
        "SetShapeAltTextCommand",
        "SetShapeTextDirectionCommand",
        "SetShapeTextParagraphAlignmentCommand",
        "ApplyShapeStyleCommand",
        "SetShapeExtendedFillCommand",
        "SetShapeEffectsCommand",
        "SetShapeRotationCommand",
        "SetShapePositionCommand",
        "SetChartKindCommand",
        "SetChartStyleCommand",
        "SetChartColorSchemeCommand",
        "SetChartQuickLayoutCommand",
        "SetChartLegendCommand",
        "SetChartTitleCommand",
        "SetChartAxisTitlesCommand",
        "ReplaceChartDataCommand",
        "SetSmartArtLayoutCommand",
        "SetSmartArtColorCommand",
        "SetSmartArtStyleCommand",
        "MutateSmartArtStructureCommand",
        "ReplaceSmartArtContentCommand",
        "SetFloatingPositionCommand",
        "SetFloatingSizeCommand",
        "SetFloatingWrapCommand",
        "SetFloatingRotationCommand",
        "SetDrawingGroupChildPositionCommand",
        "SetDrawingGroupChildSizeCommand",
        "SetDrawingGroupChildRotationCommand",
        "ChangeDrawingGroupChildZOrderCommand",
        "ChangeZOrderCommand",
        "GroupFloatingObjectsCommand",
        "UngroupFloatingObjectsCommand",
        "ArrangeFloatingObjectsCommand"
    ];

    [Fact]
    public void Renderers_ResolveNativeTargetsButDoNotConstructMigratedObjectCommands()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var coordinator = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Editing",
            "DocumentObjectEditingCoordinator.cs");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().Contain("_editingSession.Objects");
            renderer.Should().Contain("ObjectEdits.");
            foreach (var commandType in MigratedCommandTypes)
                renderer.Should().NotContain($"new {commandType}(");
        }

        coordinator.Should().Contain("public sealed class DocumentObjectEditingCoordinator");
        coordinator.Should().Contain("_session.Commands.Execute(command)");
        coordinator.Should().Contain("new SetImageCropCommand(");
        coordinator.Should().Contain("new SetShapeFillCommand(");
        coordinator.Should().Contain("new SetChartKindCommand(");
        coordinator.Should().Contain("new SetSmartArtLayoutCommand(");
        coordinator.Should().Contain("new SetFloatingRotationCommand(");

        avalonia.Should().Contain("new SetShapeTextRunCommand(");
        avalonia.Should().Contain("TryGetShapeTextTarget(");
        wpf.Should().Contain("SelectedImageLocation()");
        wpf.Should().Contain("SelectedShapeLocation()");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(parts.Aggregate(root, Path.Combine));
    }
}
