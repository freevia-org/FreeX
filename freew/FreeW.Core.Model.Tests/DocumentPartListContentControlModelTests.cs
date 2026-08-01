namespace FreeW.Core.Model.Tests;

public sealed class DocumentPartListContentControlModelTests
{
    [Fact]
    public void Factories_PreserveInlineAndBlockDocumentPartListSemantics()
    {
        var inline = Run.DocumentPartListControl(
            "Insert an equation",
            "Equations",
            category: "Built-In",
            unique: true,
            tag: "InlineDocumentPartList",
            alias: "Inline document part list");
        var block = BlockContentControl.DocumentPartListRegion(
            "AutoText",
            category: "General",
            unique: false,
            tag: "BlockDocumentPartList",
            alias: "Block document part list");

        inline.Control.Should().Be(new ContentControl(
            ContentControlKind.DocumentPart,
            Tag: "InlineDocumentPartList",
            Alias: "Inline document part list",
            DocPartGallery: "Equations",
            DocPartCategory: "Built-In",
            DocPartUnique: true));
        block.Should().Be(new BlockContentControl(
            BlockContentControlKind.DocumentPart,
            Tag: "BlockDocumentPartList",
            Alias: "Block document part list",
            DocPartGallery: "AutoText",
            DocPartCategory: "General",
            DocPartUnique: false));

        inline.Control.Kind.Should().NotBe(ContentControlKind.BuildingBlockGallery);
        block.Kind.Should().NotBe(BlockContentControlKind.BuildingBlockGallery);
    }

    [Fact]
    public void Factories_RejectMissingGalleryIdentity()
    {
        var inline = () => Run.DocumentPartListControl("text", "  ");
        var block = () => BlockContentControl.DocumentPartListRegion(string.Empty);

        inline.Should().Throw<ArgumentException>().WithParameterName("gallery");
        block.Should().Throw<ArgumentException>().WithParameterName("gallery");
    }
}
