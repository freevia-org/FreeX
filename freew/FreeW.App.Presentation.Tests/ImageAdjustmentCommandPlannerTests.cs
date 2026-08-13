using FluentAssertions;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Presentation.Tests;

public sealed class ImageAdjustmentCommandPlannerTests
{
    [Fact]
    public void PresetCatalogs_ContainTheSharedPictureFormatRoutes()
    {
        ImageAdjustmentCommandPlanner.AdjustmentPresets.Should().HaveCount(12);
        ImageAdjustmentCommandPlanner.RecolorPresets.Should().HaveCount(8);
        ImageAdjustmentCommandPlanner.EffectPresets.Should().HaveCount(27);
        ImageAdjustmentCommandPlanner.ArtisticEffectPresets.Should().HaveCount(15);
    }

    [Fact]
    public void EffectCatalog_PreservesSpecialCommandSuffixesAndActionRoutes()
    {
        ImageAdjustmentCommandPlanner.EffectPresets.Should().Contain(new ImageEffectPresetDescriptor(
            ImageEffectChannel.SoftEdge, 2.5, CommandId: "freew.image-softedge-2pt5"));
        ImageAdjustmentCommandPlanner.EffectPresets.Should().Contain(new ImageEffectPresetDescriptor(
            ImageEffectChannel.Shadow, 0, Action: FreeWRibbonCommandAction.ImageShadowNone));
    }

    [Fact]
    public void ArtisticCatalog_MapsEveryModelEffectExactlyOnce()
    {
        ImageAdjustmentCommandPlanner.ArtisticEffectPresets.Select(item => item.Effect)
            .Should().BeEquivalentTo(Enum.GetValues<ImageArtisticEffect>());
    }
}
