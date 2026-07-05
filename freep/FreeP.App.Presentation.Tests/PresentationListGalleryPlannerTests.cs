using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationListGalleryPlannerTests
{
    [Fact]
    public void BulletGallery_ExposesPowerPointLikeCharacterPresetsAndDeferredImageBulletSlot()
    {
        var plan = PresentationListGalleryPlanner.BuildBulletGalleryPlan();

        plan.Kind.Should().Be(PresentationListGalleryKind.Bullets);
        plan.OwnerCommandId.Should().Be(PresentationListGalleryPlanner.BulletsCommandId);
        plan.Items.Select(item => item.ListPreset?.Id ?? item.CommandId)
            .Should()
            .Equal(
                TableCellListPresetCatalog.BulletDiscId,
                TableCellListPresetCatalog.BulletHollowCircleId,
                TableCellListPresetCatalog.BulletSquareId,
                TableCellListPresetCatalog.BulletDashId,
                TableCellListPresetCatalog.BulletCheckId,
                PresentationListGalleryPlanner.ImageBulletCommandId);

        plan.EnabledItems.Should().HaveCount(5);
        plan.Items.Last().Kind.Should().Be(PresentationListGalleryItemKind.ImageBulletPlaceholder);
        plan.Items.Last().IsEnabled.Should().BeFalse(
            "image bullets need a visible shared contract without claiming import/render parity yet");
    }

    [Fact]
    public void NumberingGallery_UsesSharedTableCellPresetDescriptors()
    {
        var plan = PresentationListGalleryPlanner.BuildNumberingGalleryPlan();

        plan.Kind.Should().Be(PresentationListGalleryKind.Numbering);
        plan.OwnerCommandId.Should().Be(PresentationListGalleryPlanner.NumberingCommandId);
        foreach (var item in plan.Items)
        {
            item.Kind.Should().Be(PresentationListGalleryItemKind.Numbering);
            item.IsEnabled.Should().BeTrue();
            item.ListPreset.Should().NotBeNull();
            item.ListPreset!.BulletKind.Should().Be(BulletKind.Auto);
        }
        plan.Items.Select(item => item.ListPreset!.AutoNumType)
            .Should()
            .Equal(
                AutoNumType.ArabicPeriod,
                AutoNumType.RomanUcPeriod,
                AutoNumType.RomanLcPeriod,
                AutoNumType.AlphaUcPeriod,
                AutoNumType.AlphaLcPeriod);
    }

    [Fact]
    public void TryGetPresetCommand_MapsVisibleMenuCommandsToMutationPreset()
    {
        var square = PresentationListGalleryPlanner.BuildBulletGalleryPlan()
            .Items.Single(item => item.ListPreset?.Id == TableCellListPresetCatalog.BulletSquareId);

        PresentationListGalleryPlanner.TryGetPresetCommand(square.CommandId, out var preset)
            .Should()
            .BeTrue();

        preset.Should().Be(TableCellListPresetCatalog.BulletSquare);
        preset!.BulletChar.Should().Be("\u25AA");
    }
}
