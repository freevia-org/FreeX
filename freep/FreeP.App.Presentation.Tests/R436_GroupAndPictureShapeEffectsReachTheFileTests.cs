using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r436: shape effects must survive on GROUP shapes, which use a different writer than r428 covered.
///
/// <para>Verified before being asserted, unlike r427's premise. Effects are emitted from exactly two
/// places: <c>BuildSpPrEl</c>, which ordinary shapes AND pictures both go through, and
/// <c>BuildGrpSpPrEl</c>, which only groups use. So r428's coverage of a plain shape reaches pictures
/// transitively, and groups are the genuinely untested path. The picture case is still asserted here
/// -- cheaply -- because sharing a builder is only useful if the picture path actually REACHES it.</para>
///
/// <para>The group builder carries a comment warning that omitting its effect element silently drops
/// a group-level shadow on every save. A group is the shape most likely to carry one, since a shadow
/// applied to a group is how an author shadows an assembled diagram as a single object rather than
/// shadowing each piece.</para>
/// </summary>
public sealed class R436_GroupAndPictureShapeEffectsReachTheFileTests
{
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static ShapeEffects ShadowEffects() => new()
    {
        HasOuterShadow = true,
        OuterShadowColor = SrgbColor.FromRgb(0x336699),
        OuterShadowAlpha = 0x40,
        OuterShadowBlurRadEmu = 50800,
        OuterShadowDistEmu = 38100,
    };

    private static SlideShape RoundTrip(SlideShape shape, params SlideShape[] children)
    {
        var presentation = new Presentation();
        var slide = new Slide();

        foreach (var child in children)
            shape.Children.Add(child);

        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var reloaded = PptxPackageReader.Read(stream).Slides[0].Shapes.FirstOrDefault();
        reloaded.Should().NotBeNull("the shape must survive before its effects can be judged");
        return reloaded!;
    }

    [Fact]
    public void AGroupShapeKeepsItsShadow()
    {
        var reloaded = RoundTrip(
            new SlideShape
            {
                Id = 2,
                Name = "Group",
                Kind = SlideShapeKind.Group,
                OffsetXEmu = 100000,
                OffsetYEmu = 200000,
                ExtentCxEmu = 2000000,
                ExtentCyEmu = 1000000,
                Effects = ShadowEffects(),
            },
            new SlideShape
            {
                Id = 3,
                Name = "Child",
                OffsetXEmu = 100000,
                OffsetYEmu = 200000,
                ExtentCxEmu = 500000,
                ExtentCyEmu = 400000,
            });

        reloaded.Effects.Should().NotBeNull(
            "a shadow on a group is how an author shadows an assembled diagram as one object");
        reloaded.Effects!.HasOuterShadow.Should().BeTrue();
        reloaded.Effects.OuterShadowBlurRadEmu.Should().Be(50800, "the group's own effect path must carry the geometry");
        reloaded.Effects.OuterShadowAlpha.Should().Be(0x40, "alpha defaults to 0x80, so this proves it was written");
    }

    [Fact]
    public void APictureKeepsItsShadow()
    {
        // Pictures share BuildSpPrEl with ordinary shapes, so this is not a separate serialisation
        // path -- it checks the picture path REACHES that builder, which sharing alone does not
        // guarantee.
        var reloaded = RoundTrip(new SlideShape
        {
            Id = 2,
            Name = "Pic",
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 1000000,
            Picture = new ImagePart { Bytes = MinimalPng(), ContentType = "image/png" },
            Effects = ShadowEffects(),
        });

        reloaded.Effects.Should().NotBeNull("a picture's shadow must reach the file like any other shape's");
        reloaded.Effects!.HasOuterShadow.Should().BeTrue();
        reloaded.Effects.OuterShadowDistEmu.Should().Be(38100);
    }

    [Fact]
    public void AnUneffectedGroupGainsNoShadow()
    {
        // Every assertion above checks that something set survives, so a reader that invented a
        // shadow would satisfy them -- and an invented group shadow darkens an entire diagram.
        var reloaded = RoundTrip(
            new SlideShape
            {
                Id = 2,
                Name = "Group",
                Kind = SlideShapeKind.Group,
                OffsetXEmu = 100000,
                OffsetYEmu = 200000,
                ExtentCxEmu = 2000000,
                ExtentCyEmu = 1000000,
            },
            new SlideShape
            {
                Id = 3,
                Name = "Child",
                OffsetXEmu = 100000,
                OffsetYEmu = 200000,
                ExtentCxEmu = 500000,
                ExtentCyEmu = 400000,
            });

        (reloaded.Effects?.HasOuterShadow ?? false).Should().BeFalse("a plain group must not acquire a shadow");
    }
}
