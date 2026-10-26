using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxNativeXmlMergerTests
{
    [Fact]
    public void MergeElementNativeAttributesAndChildren_DenseMissingChildrenPreserveOrderAndIdentity()
    {
        const int childCount = 512;
        var source = new XElement(
            "root",
            Enumerable.Range(0, childCount).Select(index =>
                new XElement("item", new XAttribute("id", index), new XAttribute("value", $"v{index}"))));
        var target = new XElement("root");

        XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(source, target).Should().BeTrue();

        target.Elements("item").Select(element => element.Attribute("id")?.Value)
            .Should().Equal(Enumerable.Range(0, childCount).Select(index => index.ToString()));
        target.Elements("item").Select(element => element.Attribute("value")?.Value)
            .Should().Equal(Enumerable.Range(0, childCount).Select(index => $"v{index}"));
    }

    [Fact]
    public void MergeElementNativeAttributesAndChildren_DuplicateNewIdentityMergesIntoFirstClone()
    {
        var source = new XElement(
            "root",
            new XElement("item", new XAttribute("id", "same"), new XAttribute("first", "kept")),
            new XElement("item", new XAttribute("id", "same"), new XAttribute("second", "merged")));
        var target = new XElement("root");

        XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(source, target).Should().BeTrue();

        var item = target.Elements("item").Should().ContainSingle().Subject;
        item.Attribute("first")!.Value.Should().Be("kept");
        item.Attribute("second")!.Value.Should().Be("merged");
    }

    [Fact]
    public void MergeElementNativeAttributesAndChildren_SourceGuardIndexesTheAppendedCloneDirectly()
    {
        var source = TestWorkspaceFiles.ReadRepoText(
            "shared",
            "Free.Shared.Opc",
            "XlsxNativeXmlMerger.cs");

        source.Should().Contain("var clonedChild = new XElement(sourceChild);")
            .And.Contain("targetElement.Add(clonedChild);")
            .And.Contain("existingChildrenByKey[key] = clonedChild;")
            .And.NotContain("targetElement.Elements().Last()",
                "appending each missing child must not re-enumerate all target children");
    }
}
