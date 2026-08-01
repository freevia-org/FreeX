namespace FreeW.Core.Model.Tests;

public sealed class CitationContentControlModelTests
{
    [Fact]
    public void Factories_CreateExplicitInlineAndBlockCitationKinds()
    {
        var inline = Run.CitationControl("(Lovelace, 1843)", tag: "InlineCitation", alias: "Inline citation");
        var block = BlockContentControl.CitationRegion(tag: "BlockCitation", alias: "Block citation");

        inline.Control.Should().Be(new ContentControl(
            ContentControlKind.Citation,
            Tag: "InlineCitation",
            Alias: "Inline citation"));
        block.Should().Be(new BlockContentControl(
            BlockContentControlKind.Citation,
            Tag: "BlockCitation",
            Alias: "Block citation"));
    }
}
