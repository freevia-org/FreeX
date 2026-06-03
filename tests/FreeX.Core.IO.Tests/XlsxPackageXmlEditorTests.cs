using System.Text;
using System.Xml;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxPackageXmlEditorTests
{
    [Fact]
    public void LoadXml_RejectsDtds()
    {
        using var stream = ToStream("""
            <!DOCTYPE root [ <!ENTITY x "blocked"> ]>
            <root>&x;</root>
            """);

        Action act = () => XlsxPackageXmlEditor.LoadXml(stream);

        act.Should().Throw<XmlException>();
    }

    [Fact]
    public void LoadXml_RejectsDocumentsOverCharacterCap()
    {
        using var stream = ToStream("<root>1234567890</root>");

        Action act = () => XlsxPackageXmlEditor.LoadXml(stream, maxCharactersInDocument: 8);

        act.Should().Throw<XmlException>();
    }

    [Fact]
    public void LoadXml_LoadsPackageXmlUnderCharacterCap()
    {
        using var stream = ToStream("<root><child /></root>");

        var xml = XlsxPackageXmlEditor.LoadXml(stream, maxCharactersInDocument: 128);

        xml.Root!.Name.LocalName.Should().Be("root");
    }

    private static MemoryStream ToStream(string xml) =>
        new(Encoding.UTF8.GetBytes(xml), writable: false);
}
