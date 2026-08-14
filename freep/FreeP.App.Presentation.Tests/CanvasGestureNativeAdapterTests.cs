namespace FreeP.App.Compositor.Tests;

public sealed class CanvasGestureNativeAdapterTests
{
    [Theory]
    [InlineData("Escape", CanvasGestureKey.Escape)]
    [InlineData("Back", CanvasGestureKey.Backspace)]
    [InlineData("Backspace", CanvasGestureKey.Backspace)]
    [InlineData("Insert", CanvasGestureKey.Insert)]
    [InlineData("Unknown", CanvasGestureKey.None)]
    public void MapKeyName_NormalizesNativeKeyNames(
        string keyName,
        CanvasGestureKey expected)
    {
        CanvasGestureNativeInputMapper.MapKeyName(keyName).Should().Be(expected);
    }

    [Fact]
    public void MapModifiers_CombinesPortableFlags()
    {
        CanvasGestureNativeInputMapper.MapModifiers(
                shift: true,
                control: false,
                alt: true,
                meta: true)
            .Should().Be(
                CanvasGestureModifiers.Shift |
                CanvasGestureModifiers.Alt |
                CanvasGestureModifiers.Meta);
    }

    [Fact]
    public void PreviewSurfaceAdapter_ForwardsEveryTransition()
    {
        var calls = new List<string>();
        var adapter = new CanvasGesturePreviewSurfaceAdapter(
            (bounds, rotation) => calls.Add($"preview:{rotation}"),
            (guides, transform) => calls.Add("guides"),
            plan => calls.Add("transform"),
            (name, point) => calls.Add($"geometry:{name}"),
            bounds => calls.Add("marquee"));

        adapter.UpdatePreview(null, 15);
        adapter.UpdateSnapGuides(null, SlideTransformCore.Identity);
        adapter.UpdateTransformPreview(CanvasMultiTransformPlan.Empty);
        adapter.UpdateGeometryPreview("adj1", new CanvasGesturePoint(1, 2));
        adapter.UpdateMarquee(new SlideScreenRect(0, 0, 10, 20));

        calls.Should().Equal(
            "preview:15",
            "guides",
            "transform",
            "geometry:adj1",
            "marquee");
    }
}
