using Free.Shared.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class PresetShapeCatalogTests
{
    public static IEnumerable<object[]> AllItems() =>
        PresetShapeCatalog.Items.Select(item => new object[] { item.Id });

    private static PresetShapeCatalogItem Item(string id)
    {
        PresetShapeCatalog.TryGetItem(id, out var item).Should().BeTrue();
        return item!;
    }

    [Fact]
    public void Groups_FollowWordsShapeGalleryOrder()
    {
        PresetShapeCatalog.Groups.Select(group => group.Id).Should().Equal(
            "Lines", "Rectangles", "BasicShapes", "BlockArrows", "EquationShapes", "Flowchart", "StarsAndBanners", "Callouts");
        PresetShapeCatalog.Groups.Select(group => group.Title.FallbackText).Should().Equal(
            "Lines", "Rectangles", "Basic Shapes", "Block Arrows", "Equation Shapes", "Flowchart", "Stars and Banners", "Callouts");
        PresetShapeCatalog.Groups.Should().OnlyContain(group => group.Items.Count >= 6);
    }

    [Fact]
    public void ItemIdsAndResourceKeys_AreUnique()
    {
        PresetShapeCatalog.Items.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        PresetShapeCatalog.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
        PresetShapeCatalog.Items.Should().OnlyContain(item => item.Name.ResourceKey == "ShapeGallery_Shape_" + item.Id);
        PresetShapeCatalog.RequiredResourceKeys.Should().HaveCount(
            PresetShapeCatalog.Items.Count() + PresetShapeCatalog.Groups.Count + 1);
    }

    [Fact]
    public void Names_ResolveThroughTheLocalizer_AndFallBackToEnglish()
    {
        var star = Item("Star5");

        star.Name.Resolve(key => key == "ShapeGallery_Shape_Star5" ? "Étoile" : null).Should().Be("Étoile");
        star.Name.Resolve().Should().Be("Star: 5 Points");
        PresetShapeCatalog.DialogTitle().Should().Be("Shapes");
    }

    [Theory]
    [MemberData(nameof(AllItems))]
    public void EveryItem_HasADrawnThumbnail(string id)
    {
        var thumbnail = PresetShapeCatalog.BuildThumbnail(Item(id));

        thumbnail.Should().NotBeNull();
        CustomShapePathPlanner.Build(thumbnail, new CustomShapePathBounds(0, 0, 32, 32))
            .Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllItems))]
    public void EveryItem_CreatesAShapeThatKeepsItsPresetIdentity(string id)
    {
        var item = Item(id);
        var shape = PresetShapeCatalog.CreateShape(item);

        shape.WidthPt.Should().BeGreaterThan(0);
        shape.HeightPt.Should().BeGreaterThan(0);
        if (PresetShapeGeometry.IsNamedKindPreset(item.Preset))
        {
            shape.PresetName.Should().BeNull();
            shape.HasCustomGeometry.Should().BeFalse();
        }
        else
        {
            shape.PresetName.Should().Be(item.Preset);
            shape.HasCustomGeometry.Should().BeTrue($"'{item.Preset}' must draw as itself, not as a rectangle");
            shape.HasIntactPresetGeometry.Should().BeTrue();
        }
    }

    [Fact]
    public void LineItems_AreStrokedUnfilled_AndCarryTheirArrowheads()
    {
        var plain = PresetShapeCatalog.CreateShape(Item("Line"));
        var arrow = PresetShapeCatalog.CreateShape(Item("LineArrow"));
        var both = PresetShapeCatalog.CreateShape(Item("ElbowDoubleArrow"));

        plain.ExtendedFill!.Kind.Should().Be(ShapeFillKind.NoFill);
        plain.OutlineColorHex.Should().NotBeNullOrEmpty();
        (plain.HeadEnd, plain.TailEnd).Should().Be((null, null));
        (arrow.HeadEnd, arrow.TailEnd).Should().Be((null, new ShapeLineEnd("triangle")));
        (both.HeadEnd, both.TailEnd).Should().Be((new ShapeLineEnd("triangle"), new ShapeLineEnd("triangle")));
        both.PresetName.Should().Be("bentConnector3");
    }

    [Fact]
    public void NamedKindItems_MapOntoTheExistingShapeKinds()
    {
        PresetShapeCatalog.CreateShape(Item("Rectangle")).Kind.Should().Be(ShapeKind.Rectangle);
        PresetShapeCatalog.CreateShape(Item("RoundedRectangle")).Kind.Should().Be(ShapeKind.RoundedRectangle);
        PresetShapeCatalog.CreateShape(Item("Oval")).Kind.Should().Be(ShapeKind.Ellipse);
    }

    [Fact]
    public void GalleryCommand_InsertsTheChosenItem_ThroughTheHostAdapter()
    {
        var inserted = new List<Shape>();
        var bindings = new RibbonCommandRegistry();
        InsertDrawingGalleryWorkflow.Register(
            bindings,
            new InsertDrawingGalleryPorts(inserted.Add, OpenShapeGallery: choose => choose("FlowDecision")));

        bindings.TryGet(InsertDrawingGalleryWorkflow.ShapeGalleryCommandId, out var command).Should().BeTrue();
        ((IRibbonStatefulCommand)command!).GetState().IsEnabled.Should().BeTrue();
        command.Execute(RibbonCommandContext.Empty);

        inserted.Should().ContainSingle().Which.PresetName.Should().Be("flowChartDecision");
    }

    [Fact]
    public void GalleryCommand_InsertsNothing_WhenTheGalleryIsDismissedOrReportsAnUnknownItem()
    {
        var inserted = new List<Shape>();
        var dismissed = new RibbonCommandRegistry();
        InsertDrawingGalleryWorkflow.Register(dismissed, new InsertDrawingGalleryPorts(inserted.Add, OpenShapeGallery: _ => { }));
        var unknown = new RibbonCommandRegistry();
        InsertDrawingGalleryWorkflow.Register(unknown, new InsertDrawingGalleryPorts(inserted.Add, OpenShapeGallery: choose => choose("NoSuchShape")));

        dismissed.TryGet(InsertDrawingGalleryWorkflow.ShapeGalleryCommandId, out var first).Should().BeTrue();
        first!.Execute(RibbonCommandContext.Empty);
        unknown.TryGet(InsertDrawingGalleryWorkflow.ShapeGalleryCommandId, out var second).Should().BeTrue();
        second!.Execute(RibbonCommandContext.Empty);

        inserted.Should().BeEmpty();
    }

    [Fact]
    public void GalleryCommand_IsDisabled_WhenTheHostSuppliesNoGallery()
    {
        var inserted = new List<Shape>();
        var bindings = new RibbonCommandRegistry();
        InsertDrawingGalleryWorkflow.Register(bindings, new InsertDrawingGalleryPorts(inserted.Add));

        bindings.TryGet(InsertDrawingGalleryWorkflow.ShapeGalleryCommandId, out var command).Should().BeTrue();
        ((IRibbonStatefulCommand)command!).GetState().IsEnabled.Should().BeFalse();
        command.Execute(RibbonCommandContext.Empty);
        inserted.Should().BeEmpty();
    }

    [Fact]
    public void BothShells_OpenTheSharedGallery()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "ShapeGalleryDialog.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "ShapeGalleryDialog.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("PresetShapeCatalog.Groups");
            source.Should().Contain("PresetShapeCatalog.BuildThumbnail(");
            source.Should().NotContain("PresetShapeGeometry.Build(");
        }
    }
}
