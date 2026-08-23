using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Opc;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class PackageStreamDeduplicationTests
{
    [Fact]
    public void XmlStreamingCopy_PreservesRepresentativeNodeKindsAndNamespaces()
    {
        const string source = "<r xmlns=\"urn:r\" xmlns:p=\"urn:p\" p:a=\"v\"><empty /> text<![CDATA[<raw>]]><!--note--><?step go?><full></full></r>";

        CopyXml(source).Should().Be(source);
    }

    [Fact]
    public void XmlStreamingCopy_PreservesDocumentTypeAndEntityReference()
    {
        const string source = "<!DOCTYPE root [<!ENTITY item \"value\">]><root>&item;</root>";
        using var textReader = new StringReader(source);
        using var reader = new XmlTextReader(textReader)
        {
            DtdProcessing = DtdProcessing.Parse,
            EntityHandling = EntityHandling.ExpandCharEntities,
            XmlResolver = null
        };

        CopyXml(reader).Should().Be(source);
    }

    [Fact]
    public void XmlStreamingCopy_OnlyWritesDeclarationWhenRequested()
    {
        const string source = "<?xml version=\"1.0\" encoding=\"utf-8\"?><root />";
        using var ignoredDeclarationReader = new XmlTextReader(new StringReader(source));
        using var writtenDeclarationReader = new XmlTextReader(new StringReader(source));

        CopyXml(ignoredDeclarationReader, writeXmlDeclarationAsProcessingInstruction: false)
            .Should().Be("<root />");
        CopyXml(writtenDeclarationReader, writeXmlDeclarationAsProcessingInstruction: true)
            .Should().Be(source);
    }

    [Theory]
    [InlineData(null, "xl/worksheets/sheet1.xml")]
    [InlineData("", "xl/worksheets/sheet1.xml")]
    [InlineData("  ", "xl/worksheets/sheet1.xml")]
    [InlineData("Internal", "xl/worksheets/sheet1.xml")]
    [InlineData(" internal ", "/xl/worksheets/sheet1.xml")]
    [InlineData("External", "https://example.test/workbook.xlsx")]
    [InlineData(" external ", "mailto:test@example.test")]
    public void RelationshipValidation_AcceptsSupportedTargetModesAndTargets(string? targetMode, string target)
    {
        var relationship = CreateRelationship(target, targetMode);

        OpcRelationships.IsStructurallyValidRelationship(relationship).Should().BeTrue();
    }

    [Fact]
    public void RelationshipValidation_AllowsNamespaceDeclarations()
    {
        var relationship = CreateRelationship("sheet1.xml", null);
        relationship.Add(new XAttribute(XNamespace.Xmlns + "custom", "urn:custom"));

        OpcRelationships.IsStructurallyValidRelationship(relationship).Should().BeTrue();
    }

    [Theory]
    [InlineData("Remote")]
    [InlineData("ExternalFile")]
    public void RelationshipValidation_RejectsUnsupportedTargetMode(string targetMode)
    {
        OpcRelationships.IsStructurallyValidRelationship(CreateRelationship("sheet1.xml", targetMode))
            .Should().BeFalse();
    }

    [Fact]
    public void RelationshipValidation_RejectsMissingBlankUnknownAndNamespacedAttributes()
    {
        var missingId = CreateRelationship("sheet1.xml", null);
        missingId.Attribute("Id")!.Remove();

        var blankType = CreateRelationship("sheet1.xml", null);
        blankType.SetAttributeValue("Type", "  ");

        var blankTarget = CreateRelationship("sheet1.xml", null);
        blankTarget.SetAttributeValue("Target", "");

        var unknown = CreateRelationship("sheet1.xml", null);
        unknown.Add(new XAttribute("Other", "value"));

        XNamespace custom = "urn:custom";
        var namespaced = CreateRelationship("sheet1.xml", null);
        namespaced.Add(new XAttribute(custom + "flag", "value"));

        new[] { missingId, blankType, blankTarget, unknown, namespaced }
            .Should().OnlyContain(relationship =>
                !OpcRelationships.IsStructurallyValidRelationship(relationship));
    }

    [Fact]
    public void PackageStreamCallers_AdoptSharedHelpers()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var stripper = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxClosedXmlStyleOnlyCellStripper.cs"));
        var snapshot = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackageSnapshot.cs"));
        var merger = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxPackageMetadataMerger.cs"));

        stripper.Should().Contain("XmlStreamingCopy.WriteCurrentNode(reader, writer)")
            .And.NotContain("private static void WriteCurrentNode");
        snapshot.Should().Contain("XmlStreamingCopy.WriteCurrentNode(")
            .And.Contain("writeXmlDeclarationAsProcessingInstruction: true")
            .And.Contain("OpcRelationships.IsStructurallyValidRelationship(relationship)")
            .And.NotContain("private static void WriteCurrentXmlNode")
            .And.NotContain("private static bool IsStructurallyValidPackageRelationship");
        merger.Should().Contain("OpcRelationships.IsStructurallyValidRelationship(sourceRelationship)")
            .And.NotContain("private static bool IsStructurallyValidPackageRelationship");
    }

    private static XElement CreateRelationship(string target, string? targetMode)
    {
        var relationship = OpcRelationships.CreateRelationship("rId1", "urn:type", target);
        if (targetMode is not null)
            relationship.SetAttributeValue("TargetMode", targetMode);
        return relationship;
    }

    private static string CopyXml(
        string source,
        bool writeXmlDeclarationAsProcessingInstruction = false)
    {
        using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null
        });
        return CopyXml(reader, writeXmlDeclarationAsProcessingInstruction);
    }

    private static string CopyXml(
        XmlReader reader,
        bool writeXmlDeclarationAsProcessingInstruction = false)
    {
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings
               {
                   Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                   OmitXmlDeclaration = !writeXmlDeclarationAsProcessingInstruction,
                   ConformanceLevel = ConformanceLevel.Auto
               }))
        {
            while (reader.Read())
            {
                XmlStreamingCopy.WriteCurrentNode(
                    reader,
                    writer,
                    writeXmlDeclarationAsProcessingInstruction);
            }
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }
}
