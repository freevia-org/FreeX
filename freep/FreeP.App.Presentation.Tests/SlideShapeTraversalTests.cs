namespace FreeP.App.Compositor.Tests;

public sealed class SlideShapeTraversalTests
{
    [Fact]
    public void FindById_FindsShapeInNestedGroup()
    {
        var slide = new Slide();
        var outerGroup = Shape(1, SlideShapeKind.Group);
        var innerGroup = Shape(2, SlideShapeKind.Group);
        var nestedShape = Shape(3);
        innerGroup.Children.Add(nestedShape);
        outerGroup.Children.Add(innerGroup);
        slide.Shapes.Add(outerGroup);

        SlideShapeTraversal.FindById(slide, nestedShape.Id).Should().BeSameAs(nestedShape);
    }

    [Fact]
    public void EnumerateDepthFirst_PreservesParentChildAndSiblingOrder()
    {
        var slide = new Slide();
        var firstGroup = Shape(1, SlideShapeKind.Group);
        var firstChild = Shape(2, SlideShapeKind.Group);
        var grandchild = Shape(3);
        var secondChild = Shape(4);
        var finalShape = Shape(5);
        firstChild.Children.Add(grandchild);
        firstGroup.Children.Add(firstChild);
        firstGroup.Children.Add(secondChild);
        slide.Shapes.Add(firstGroup);
        slide.Shapes.Add(finalShape);

        SlideShapeTraversal.EnumerateDepthFirst(slide)
            .Should().Equal(firstGroup, firstChild, grandchild, secondChild, finalShape);
    }

    [Fact]
    public void EmptySlide_HasNoTraversalItemsOrMatchingShape()
    {
        var slide = new Slide();

        SlideShapeTraversal.EnumerateDepthFirst(slide).Should().BeEmpty();
        SlideShapeTraversal.FindById(slide, 1).Should().BeNull();
    }

    [Fact]
    public void FindById_ReturnsFirstDepthFirstMatch()
    {
        var slide = new Slide();
        var group = Shape(1, SlideShapeKind.Group);
        var firstMatch = Shape(42);
        var laterMatch = Shape(42);
        group.Children.Add(firstMatch);
        slide.Shapes.Add(group);
        slide.Shapes.Add(laterMatch);

        SlideShapeTraversal.FindById(slide, 42).Should().BeSameAs(firstMatch);
    }

    [Fact]
    public void PlatformHosts_UseOnlyTheCanonicalSlideShapeTraversal()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var modelDirectory = Path.Combine(root, "freep", "FreeP.Core.Model");
        var presentationDirectory = Path.Combine(root, "freep", "FreeP.App.Presentation");
        var hostDirectory = Path.Combine(root, "freep", "FreeP.App.Host");
        var avaloniaDirectory = Path.Combine(root, "freep", "FreeP.App.Avalonia");
        var rendererSharedDirectory = Path.Combine(root, "freep", "RendererShared");
        var canonicalPath = Path.Combine(modelDirectory, "SlideShapeTraversal.cs");
        var productionDirectories = new[]
        {
            modelDirectory,
            presentationDirectory,
            hostDirectory,
            avaloniaDirectory,
            rendererSharedDirectory,
        };

        var productionSources = productionDirectories
            .SelectMany(directory => Directory.EnumerateFiles(
                directory,
                "*.cs",
                SearchOption.AllDirectories))
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .ToArray();

        productionSources
            .Where(file => file.Source.Contains(
                "class SlideShapeTraversal",
                StringComparison.Ordinal))
            .Select(file => file.Path)
            .Should().Equal(canonicalPath);

        File.Exists(Path.Combine(hostDirectory, "ShapeTreeLookup.cs")).Should().BeFalse();
        File.Exists(Path.Combine(avaloniaDirectory, "ShapeTreeLookup.cs")).Should().BeFalse();

        var platformSource = string.Join(
            Environment.NewLine,
            productionSources
                .Where(file => file.Path.StartsWith(hostDirectory, StringComparison.OrdinalIgnoreCase)
                    || file.Path.StartsWith(avaloniaDirectory, StringComparison.OrdinalIgnoreCase))
                .Select(file => file.Source));

        platformSource.Should().NotContain("ShapeTreeLookup");

        var consumerSource = string.Join(
            Environment.NewLine,
            productionSources
                .Where(file => !file.Path.Equals(canonicalPath, StringComparison.OrdinalIgnoreCase))
                .Select(file => file.Source));

        consumerSource.Should().Contain("SlideShapeTraversal.FindById");
        consumerSource.Should().Contain("SlideShapeTraversal.EnumerateDepthFirst");
    }

    private static SlideShape Shape(uint id, SlideShapeKind kind = SlideShapeKind.AutoShape) =>
        new()
        {
            Id = id,
            Kind = kind,
        };
}
