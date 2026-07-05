using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationListGalleryPlannerTests
{
    [Fact]
    public void BulletGallery_ExposesPowerPointLikeCharacterPresetsAndImageBulletCommand()
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

        plan.EnabledItems.Should().HaveCount(6);
        plan.Items.Last().Kind.Should().Be(PresentationListGalleryItemKind.ImageBullet);
        plan.Items.Last().IsEnabled.Should().BeTrue(
            "image bullets now have a shared authoring contract fed by WPF/Avalonia picker adapters");
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

    [Theory]
    [InlineData("bullet.png", "image/png")]
    [InlineData("bullet.jpeg", "image/jpeg")]
    [InlineData("bullet.gif", "image/gif")]
    [InlineData("bullet.bmp", "image/bmp")]
    [InlineData("bullet.svg", "image/svg+xml")]
    [InlineData("bullet.wmf", "image/x-wmf")]
    [InlineData("bullet.emf", "image/x-emf")]
    [InlineData("bullet.unknown", "image/png")]
    public void PictureBulletPayload_InfersContentTypeAndClonesBytes(
        string fileName,
        string expectedContentType)
    {
        var source = new byte[] { 1, 2, 3 };

        var payload = PresentationPictureBulletAuthoringPlanner.CreatePayloadFromFileName(source, fileName);
        var image = PresentationPictureBulletAuthoringPlanner.CreateImagePart(payload);
        source[0] = 9;

        payload.ContentType.Should().Be(expectedContentType);
        payload.ImageBytes.Should().Equal(1, 2, 3);
        image.ContentType.Should().Be(expectedContentType);
        image.Bytes.Should().Equal(1, 2, 3);

        var paragraph = new Paragraph { BulletKind = BulletKind.Char, BulletChar = "\u2022" };
        PresentationPictureBulletAuthoringPlanner.ApplyToParagraph(paragraph, payload);
        paragraph.BulletKind.Should().Be(BulletKind.Image);
        paragraph.BulletImage.Should().NotBeNull();
        paragraph.BulletImage!.Bytes.Should().Equal(1, 2, 3);
        paragraph.BulletChar.Should().BeNull();
        paragraph.BulletSuppressed.Should().BeFalse();
    }
}
