namespace FreeW.Core.Model.Tests;

public sealed class RepeatingSectionContentControlModelTests
{
    [Fact]
    public void Factories_CreateNestedRepeatingSectionRoles()
    {
        var section = BlockContentControl.RepeatingSection(
            title: "Line items",
            doNotAllowInsertDeleteSection: true,
            tag: "Orders",
            alias: "Order lines");
        var item = BlockContentControl.RepeatingSectionItem(
            section,
            tag: "Order",
            alias: "Order line");

        section.Kind.Should().Be(BlockContentControlKind.RepeatingSection);
        section.RepeatingSectionTitle.Should().Be("Line items");
        section.DoNotAllowInsertDeleteSection.Should().BeTrue();
        section.Parent.Should().BeNull();

        item.Kind.Should().Be(BlockContentControlKind.RepeatingSectionItem);
        item.Parent.Should().BeSameAs(section);
        item.Tag.Should().Be("Order");
        item.Alias.Should().Be("Order line");
    }

    [Fact]
    public void RepeatingSectionItem_RejectsOrdinaryParent()
    {
        var ordinary = new BlockContentControl(BlockContentControlKind.RichText);

        var act = () => BlockContentControl.RepeatingSectionItem(ordinary);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("parent");
    }
}
