namespace FreeW.Core.Model.Tests;

public sealed class BuildingBlockGalleryContentControlModelTests
{
    [Fact]
    public void Factories_PreserveInlineAndBlockGallerySemantics()
    {
        var inline = Run.BuildingBlockGalleryControl(
            "Insert a quick part",
            "Quick Parts",
            category: "General",
            unique: true,
            tag: "InlineBuildingBlock",
            alias: "Inline building block");
        var block = BlockContentControl.BuildingBlockGalleryRegion(
            "Cover Pages",
            category: "Built-In",
            unique: false,
            tag: "CoverPage",
            alias: "Cover page gallery");

        inline.Control.Should().Be(new ContentControl(
            ContentControlKind.BuildingBlockGallery,
            Tag: "InlineBuildingBlock",
            Alias: "Inline building block",
            DocPartGallery: "Quick Parts",
            DocPartCategory: "General",
            DocPartUnique: true));
        block.Should().Be(new BlockContentControl(
            BlockContentControlKind.BuildingBlockGallery,
            Tag: "CoverPage",
            Alias: "Cover page gallery",
            DocPartGallery: "Cover Pages",
            DocPartCategory: "Built-In",
            DocPartUnique: false));
    }

    [Fact]
    public void Factories_RejectMissingGalleryIdentity()
    {
        var inline = () => Run.BuildingBlockGalleryControl("text", "  ");
        var block = () => BlockContentControl.BuildingBlockGalleryRegion(string.Empty);

        inline.Should().Throw<ArgumentException>().WithParameterName("gallery");
        block.Should().Throw<ArgumentException>().WithParameterName("gallery");
    }
}
