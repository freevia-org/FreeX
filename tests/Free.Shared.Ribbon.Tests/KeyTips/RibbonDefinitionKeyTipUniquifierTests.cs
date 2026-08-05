using Free.Shared.Ribbon.KeyTips;

namespace Free.Shared.Ribbon.Tests.KeyTips;

public sealed class RibbonDefinitionKeyTipUniquifierTests
{
    [Fact]
    public void Normalize_MakesEveryRibbonScopeUniqueAndPreservesBracketedTokens()
    {
        var nestedItems = new[]
        {
            new RibbonMenuItem("Nested one", "nested.one", "N"),
            new RibbonMenuItem("Nested two", "nested.two", "n"),
        };
        var menu = new RibbonMenu(
        [
            new RibbonMenuItem("First", "first", " [[p]] ", Children: nestedItems),
            new RibbonMenuItem("Second", "second", "[[P]]"),
        ]);
        var controls = new RibbonControl[]
        {
            new RibbonButton("one", "One") { KeyTip = " x " },
            new RibbonButton("two", "Two") { KeyTip = "X" },
            new RibbonSplitButton("three", "Three", menu) { KeyTip = "x" },
        };
        var groups = new[]
        {
            new RibbonGroup("g1", "One", " g ", 1, controls, RibbonGroupSizing.Default),
            new RibbonGroup("g2", "Two", "G", 1, [], RibbonGroupSizing.Default),
        };
        var definition = new RibbonDefinition(
        [
            new RibbonTab("t1", "One", " a ", null, groups),
            new RibbonTab("t2", "Two", "A", null, []),
        ]);

        var normalized = RibbonDefinitionKeyTipUniquifier.Normalize(definition);

        normalized.Tabs.Select(tab => tab.KeyTip).Should().Equal("A", "A2");
        normalized.Tabs[0].Groups.Select(group => group.KeyTip).Should().Equal("G", "G2");
        normalized.Tabs[0].Groups[0].Controls.Select(control => control.KeyTip)
            .Should().Equal("X", "X2", "X3");

        var normalizedMenu = ((RibbonSplitButton)normalized.Tabs[0].Groups[0].Controls[2]).Menu;
        normalizedMenu.Items.Select(item => item.KeyTip).Should().Equal("[[P]]", "[[P2]]");
        normalizedMenu.Items[0].Children.Select(item => item.KeyTip).Should().Equal("N", "N2");
    }

    [Fact]
    public void Normalize_PreservesNullAndBlankKeytips()
    {
        var definition = new RibbonDefinition(
        [
            new RibbonTab("one", "One", null, null,
            [
                new RibbonGroup("group", "Group", " ", 1,
                [
                    new RibbonButton("command", "Command"),
                ], RibbonGroupSizing.Default),
            ]),
        ]);

        var normalized = RibbonDefinitionKeyTipUniquifier.Normalize(definition);

        normalized.Tabs[0].KeyTip.Should().BeNull();
        normalized.Tabs[0].Groups[0].KeyTip.Should().Be(" ");
        normalized.Tabs[0].Groups[0].Controls[0].KeyTip.Should().BeNull();
    }
}
