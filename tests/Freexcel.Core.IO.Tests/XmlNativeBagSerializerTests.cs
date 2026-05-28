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
}
