namespace FreeW.Core.Model.Tests;

public sealed class ContentControlTabIndexModelTests
{
    [Fact]
    public void WordMetadata_PreservesExactTabIndexTokenForInlineAndBlockOwners()
    {
        var metadata = new ContentControlWordMetadata(TabIndex: "-0002");
        var inline = new ContentControl(ContentControlKind.RichText, WordMetadata: metadata);
        var block = new BlockContentControl(BlockContentControlKind.RichText, WordMetadata: metadata);

        inline.WordMetadata.Should().BeSameAs(metadata);
        block.WordMetadata.Should().BeSameAs(metadata);
        inline.WordMetadata!.TabIndex.Should().Be("-0002");
        block.WordMetadata!.TabIndex.Should().Be("-0002");
    }

    [Fact]
    public void WordMetadata_LeavesTabIndexAbsentByDefault()
    {
        new ContentControlWordMetadata().TabIndex.Should().BeNull();
    }
}
