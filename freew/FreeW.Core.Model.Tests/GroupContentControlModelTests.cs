namespace FreeW.Core.Model.Tests;

public sealed class GroupContentControlModelTests
{
    [Fact]
    public void Factories_CreateExplicitInlineAndBlockGroupKinds()
    {
        var inline = Run.GroupControl("Grouped text", tag: "InlineGroup", alias: "Inline group");
        var block = BlockContentControl.GroupRegion(tag: "BlockGroup", alias: "Block group");

        inline.Control.Should().Be(new ContentControl(
            ContentControlKind.Group,
            Tag: "InlineGroup",
            Alias: "Inline group"));
        block.Should().Be(new BlockContentControl(
            BlockContentControlKind.Group,
            Tag: "BlockGroup",
            Alias: "Block group"));
    }
}
