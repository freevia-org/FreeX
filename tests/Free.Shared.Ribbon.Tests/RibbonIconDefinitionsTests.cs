using Free.Shared.Ribbon.Icons;

namespace Free.Shared.Ribbon.Tests;

public class RibbonIconDefinitionsTests
{
    public static IEnumerable<object[]> AllKinds() =>
        Enum.GetValues<RibbonCommandIconKind>().Select(k => new object[] { k });

    public static IEnumerable<object[]> AllAccents() =>
        Enum.GetValues<RibbonCommandIconAccent>().Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Resolve_ReturnsNonEmptyDefinition_ForEveryKind(RibbonCommandIconKind kind)
    {
        var geometry = RibbonIconDefinitions.Resolve(kind);

        geometry.Should().NotBeNull();
        geometry.Kind.Should().Be(kind);
        geometry.Elements.Should().NotBeEmpty($"every icon kind must resolve to at least one drawable element ({kind})");
    }

    [Fact]
    public void EveryKind_Resolves_NoExceptions()
    {
        foreach (var kind in Enum.GetValues<RibbonCommandIconKind>())
        {
            var act = () => RibbonIconDefinitions.Resolve(kind);
            act.Should().NotThrow($"resolving {kind} must never throw");
        }
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Resolve_Elements_AreInternallyValid(RibbonCommandIconKind kind)
    {
        var geometry = RibbonIconDefinitions.Resolve(kind);

        foreach (var element in geometry.Elements)
        {
            switch (element.Kind)
            {
                case RibbonIconElementKind.Path:
                    element.PathData.Should().NotBeNullOrWhiteSpace($"path elements need geometry ({kind})");
                    element.StrokeThickness.Should().BeGreaterThan(0);
                    break;
                case RibbonIconElementKind.Text:
                    element.Text.Should().NotBeNullOrEmpty($"text elements need text ({kind})");
                    element.Width.Should().BeGreaterThan(0, "font size must be positive");
                    break;
                case RibbonIconElementKind.Rectangle:
                case RibbonIconElementKind.FilledRectangle:
                case RibbonIconElementKind.Ellipse:
                    element.Width.Should().BeGreaterThan(0);
                    element.Height.Should().BeGreaterThan(0);
                    break;
                case RibbonIconElementKind.FilledCircle:
                    element.Width.Should().BeGreaterThan(0, "circle diameter must be positive");
                    break;
            }
        }
    }

    [Fact]
    public void Generic_Kind_HasDedicatedShape()
    {
        RibbonIconDefinitions.HasDedicatedShape(RibbonCommandIconKind.Generic).Should().BeTrue();
    }

    [Fact]
    public void UnshapedKinds_FallBackToGenericGlyph()
    {
        // These kinds have no dedicated drawing and must reuse the shared Generic glyph
        // so WPF and Avalonia stay identical.
        var generic = RibbonIconDefinitions.Resolve(RibbonCommandIconKind.Generic).Elements;

        foreach (var kind in new[]
                 {
                     RibbonCommandIconKind.Math,
                 })
        {
            RibbonIconDefinitions.HasDedicatedShape(kind).Should().BeFalse($"{kind} has no dedicated drawing");
            RibbonIconDefinitions.Resolve(kind).Elements.Should().BeSameAs(generic);
        }
    }

    [Theory]
    [MemberData(nameof(AllAccents))]
    public void Accent_Resolves_WithoutThrowing(RibbonCommandIconAccent accent)
    {
        var act = () => RibbonIconAccents.Resolve(accent);
        act.Should().NotThrow();
    }

    [Fact]
    public void Accent_None_ResolvesToNull()
    {
        RibbonIconAccents.Resolve(RibbonCommandIconAccent.None).Should().BeNull();
    }

    [Fact]
    public void Accent_NonNone_ResolvesToColor()
    {
        foreach (var accent in Enum.GetValues<RibbonCommandIconAccent>())
        {
            if (accent == RibbonCommandIconAccent.None)
                continue;

            RibbonIconAccents.Resolve(accent).Should().NotBeNull($"{accent} maps to a color");
        }
    }

    [Fact]
    public void Color_FromHex_ParsesRrggbbAndAarrggbb()
    {
        RibbonIconColor.FromHex("#107C10").Should().Be(new RibbonIconColor(0x10, 0x7C, 0x10));
        RibbonIconColor.FromHex("#80112233").Should().Be(new RibbonIconColor(0x11, 0x22, 0x33, 0x80));
    }
}
