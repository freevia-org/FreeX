using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.Core.IO.Tests;

public sealed class XmlNativeBagSerializerTests
{
    [Fact]
    public void ApplyToElement_PreservedChildXml_RetainsWhitespaceTextNodes()
    {
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal),
            ["<ext><inner>  spaced  </inner><inner xml:space=\"preserve\"> keep  </inner></ext>"]);
        var target = new XElement("root", new XElement("old"));

        var changed = XmlNativeBagSerializer.ApplyToElement(target, bagValue, []);

        changed.Should().BeTrue();
        target.Elements().Should().ContainSingle();
        target.Elements().Single()
            .ToString(SaveOptions.DisableFormatting)
            .Should().Be("<ext><inner>  spaced  </inner><inner xml:space=\"preserve\"> keep  </inner></ext>");
    }

    [Fact]
    public void SerializeDeserialize_NamespacedChildren_PreservesOrderAndXml()
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customFlag"] = "keep"
        };
        var children = new List<string>
        {
            "<fx:first xmlns:fx=\"urn:freexcel:test\" id=\"1\"><fx:leaf /></fx:first>",
            "<second xmlns=\"urn:freexcel:test-default\" id=\"2\" />"
        };

        var bagValue = XmlNativeBagSerializer.Serialize(attrs, children);

        var (roundTripAttrs, roundTripChildren) = XmlNativeBagSerializer.Deserialize(bagValue);
        roundTripAttrs.Should().ContainKey("customFlag").WhoseValue.Should().Be("keep");
        roundTripChildren.Should().HaveCount(2);

        var first = XElement.Parse(roundTripChildren[0], LoadOptions.PreserveWhitespace);
        var second = XElement.Parse(roundTripChildren[1], LoadOptions.PreserveWhitespace);

        first.Name.Should().Be(XName.Get("first", "urn:freexcel:test"));
        first.Attribute("id")?.Value.Should().Be("1");
        first.Elements().Single().Name.Should().Be(XName.Get("leaf", "urn:freexcel:test"));
        second.Name.Should().Be(XName.Get("second", "urn:freexcel:test-default"));
        second.Attribute("id")?.Value.Should().Be("2");
    }
}
