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

    [Fact]
    public void SerializeDeserialize_NamespacedAttributes_CanBeAppliedToElement()
    {
        var namespacedAttribute = XName.Get("flag", "urn:freexcel:test").ToString();
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["plainFlag"] = "plain",
                [namespacedAttribute] = "namespaced"
            });
        var target = new XElement("root");

        var (roundTripAttrs, roundTripChildren) = XmlNativeBagSerializer.Deserialize(bagValue);
        var changed = XmlNativeBagSerializer.ApplyToElement(target, bagValue, []);

        roundTripAttrs.Should().ContainKey("plainFlag").WhoseValue.Should().Be("plain");
        roundTripAttrs.Should().ContainKey(namespacedAttribute).WhoseValue.Should().Be("namespaced");
        roundTripChildren.Should().BeEmpty();
        changed.Should().BeTrue();
        target.Attribute("plainFlag")?.Value.Should().Be("plain");
        target.Attribute(XName.Get("flag", "urn:freexcel:test"))?.Value.Should().Be("namespaced");
    }

    [Fact]
    public void SerializeDeserialize_AttributeValues_PreservesXmlSensitiveCharacters()
    {
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["formula"] = "A1<B1 && \"quoted\"",
                ["apostrophe"] = "O'Brien"
            },
            ["<ext note=\"keep\" />"]);

        var (roundTripAttrs, roundTripChildren) = XmlNativeBagSerializer.Deserialize(bagValue);

        roundTripAttrs.Should().ContainKey("formula").WhoseValue.Should().Be("A1<B1 && \"quoted\"");
        roundTripAttrs.Should().ContainKey("apostrophe").WhoseValue.Should().Be("O'Brien");
        roundTripChildren.Should().Equal("<ext note=\"keep\" />");
    }

    [Fact]
    public void Deserialize_NamespaceDeclarations_DoesNotReturnDeclarationsAsNativeAttributes()
    {
        const string bagValue = """
            <e xmlns="urn:freexcel:default" xmlns:fx="urn:freexcel:test" fx:flag="keep" plainFlag="plain"><fx:child /></e>
            """;

        var (roundTripAttrs, roundTripChildren) = XmlNativeBagSerializer.Deserialize(bagValue);

        roundTripAttrs.Should().ContainKey(XName.Get("flag", "urn:freexcel:test").ToString()).WhoseValue.Should().Be("keep");
        roundTripAttrs.Should().ContainKey("plainFlag").WhoseValue.Should().Be("plain");
        roundTripAttrs.Keys.Should().NotContain(["xmlns", "{http://www.w3.org/2000/xmlns/}fx"]);
        roundTripChildren.Should().ContainSingle()
            .Which.Should().Be("<fx:child xmlns:fx=\"urn:freexcel:test\" />");
    }

    [Fact]
    public void ApplyToElement_NativeAttributes_DoesNotOverwriteModeledAttributes()
    {
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["modeled"] = "native-copy",
                ["nativeOnly"] = "preserved"
            });
        var target = new XElement("root", new XAttribute("modeled", "model-value"));

        var changed = XmlNativeBagSerializer.ApplyToElement(target, bagValue, ["modeled"]);

        changed.Should().BeTrue();
        target.Attribute("modeled")?.Value.Should().Be("model-value");
        target.Attribute("nativeOnly")?.Value.Should().Be("preserved");
    }

    [Fact]
    public void Serialize_InvalidChildXml_PreservesValidNativeData()
    {
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nativeOnly"] = "preserved"
            },
            [
                "<valid id=\"1\" />",
                "<invalid>",
                "<alsoValid><child /></alsoValid>"
            ]);

        var (roundTripAttrs, roundTripChildren) = XmlNativeBagSerializer.Deserialize(bagValue);

        roundTripAttrs.Should().ContainKey("nativeOnly").WhoseValue.Should().Be("preserved");
        roundTripChildren.Should().Equal("<valid id=\"1\" />", "<alsoValid><child /></alsoValid>");
    }

    [Fact]
    public void Serialize_InvalidAttributeName_PreservesValidNativeData()
    {
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nativeOnly"] = "preserved",
                ["invalid name"] = "skipped"
            },
            ["<valid id=\"1\" />"]);

        var (roundTripAttrs, roundTripChildren) = XmlNativeBagSerializer.Deserialize(bagValue);

        roundTripAttrs.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>("nativeOnly", "preserved"));
        roundTripChildren.Should().Equal("<valid id=\"1\" />");
    }

    [Fact]
    public void ApplyToElement_InvalidBagValue_DoesNotMutateExistingElement()
    {
        var target = new XElement(
            "root",
            new XAttribute("nativeOnly", "current"),
            new XElement("existing", new XAttribute("id", "1")));

        var changed = XmlNativeBagSerializer.ApplyToElement(target, "<e><invalid></e>", []);

        changed.Should().BeFalse();
        target.ToString(SaveOptions.DisableFormatting)
            .Should().Be("<root nativeOnly=\"current\"><existing id=\"1\" /></root>");
    }

    [Fact]
    public void ApplyToElement_AttributeOnlyBag_DoesNotRemoveExistingChildren()
    {
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nativeOnly"] = "preserved"
            });
        var target = new XElement("root", new XElement("existing", new XAttribute("id", "1")));

        var changed = XmlNativeBagSerializer.ApplyToElement(target, bagValue, []);

        changed.Should().BeTrue();
        target.ToString(SaveOptions.DisableFormatting)
            .Should().Be("<root nativeOnly=\"preserved\"><existing id=\"1\" /></root>");
    }

    [Fact]
    public void ApplyToElement_AttributeOnlyBagWithMatchingValues_DoesNotReportChange()
    {
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nativeOnly"] = "preserved"
            });
        var target = new XElement(
            "root",
            new XAttribute("nativeOnly", "preserved"),
            new XElement("existing", new XAttribute("id", "1")));

        var changed = XmlNativeBagSerializer.ApplyToElement(target, bagValue, []);

        changed.Should().BeFalse();
        target.ToString(SaveOptions.DisableFormatting)
            .Should().Be("<root nativeOnly=\"preserved\"><existing id=\"1\" /></root>");
    }

    [Fact]
    public void ApplyToElement_ChildXmlBag_DoesNotRemoveExistingAttributes()
    {
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal),
            ["<replacement id=\"2\" />"]);
        var target = new XElement(
            "root",
            new XAttribute("nativeOnly", "current"),
            new XElement("existing", new XAttribute("id", "1")));

        var changed = XmlNativeBagSerializer.ApplyToElement(target, bagValue, []);

        changed.Should().BeTrue();
        target.ToString(SaveOptions.DisableFormatting)
            .Should().Be("<root nativeOnly=\"current\"><replacement id=\"2\" /></root>");
    }

    [Fact]
    public void ApplyToElement_PreservedChildXml_ReplacesChildrenInNativeOrder()
    {
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal),
            [
                "<first id=\"1\" />",
                "<second id=\"2\"><leaf /></second>",
                "<third id=\"3\" />"
            ]);
        var target = new XElement(
            "root",
            new XElement("old", new XAttribute("id", "0")));

        var changed = XmlNativeBagSerializer.ApplyToElement(target, bagValue, []);

        changed.Should().BeTrue();
        target.ToString(SaveOptions.DisableFormatting)
            .Should().Be("<root><first id=\"1\" /><second id=\"2\"><leaf /></second><third id=\"3\" /></root>");
    }

    [Fact]
    public void ApplyToElement_PreservedChildXml_RetainsCommentsAndProcessingInstructions()
    {
        var childXml = "<ext><?freexcel keep=\"true\"?><!--keep me--><inner /></ext>";
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal),
            [childXml]);
        var target = new XElement("root");

        var changed = XmlNativeBagSerializer.ApplyToElement(target, bagValue, []);

        changed.Should().BeTrue();
        target.Elements().Should().ContainSingle();
        target.Elements().Single()
            .ToString(SaveOptions.DisableFormatting)
            .Should().Be(childXml);
    }

    [Fact]
    public void ApplyToElement_PreservedChildXml_RetainsCDataSections()
    {
        var childXml = "<ext><formula><![CDATA[A1<B1 && C1>D1]]></formula></ext>";
        var bagValue = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal),
            [childXml]);
        var target = new XElement("root");

        var changed = XmlNativeBagSerializer.ApplyToElement(target, bagValue, []);

        changed.Should().BeTrue();
        target.Elements().Should().ContainSingle();
        target.Elements().Single()
            .ToString(SaveOptions.DisableFormatting)
            .Should().Be(childXml);
    }
}
