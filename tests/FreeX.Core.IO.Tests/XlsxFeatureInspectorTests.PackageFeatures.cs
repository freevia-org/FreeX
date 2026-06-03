using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public partial class XlsxFeatureInspectorTests
{
    [Fact]
    public void Inspect_DigitalSignaturePackage_DetectsDigitalSignatures()
    {
        using var package = CreatePackage(
            "_xmlsignatures/origin.sigs",
            "_xmlsignatures/sig1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.DigitalSignatures);
    }


    [Fact]
    public void Inspect_RelationshipOnlyDigitalSignatureReference_DetectsDigitalSignatures()
    {
        using var package = CreatePackageWithContent(("_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdSignatureOrigin"
                            Type="http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin"
                            Target="_xmlsignatures/origin.sigs"/>
            </Relationships>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.DigitalSignatures);
    }


    [Fact]
    public void Inspect_CustomRibbonUiPackage_DetectsCustomRibbonUi()
    {
        using var package = CreatePackage("customUI/customUI.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.CustomRibbonUi);
    }


    [Fact]
    public void Inspect_OfficeAddInPackage_DetectsOfficeAddIns()
    {
        using var package = CreatePackage(
            "xl/webextensions/taskpanes.xml",
            "xl/webextensions/webextension1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.OfficeAddIns);
    }


    [Fact]
    public void Inspect_RelationshipOnlyWebExtensionTaskPanesReference_DetectsOfficeAddIns()
    {
        using var package = CreatePackageWithContent(("_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdTaskPanes"
                            Type="http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes"
                            Target="xl/webextensions/taskpanes.xml"/>
            </Relationships>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.OfficeAddIns);
    }


    [Fact]
    public void Inspect_WebPublishItemsPackage_DetectsLiveWebQueries()
    {
        using var package = CreatePackage("xl/webPublishItems.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.LiveWebQueries);
    }


    [Fact]
    public void Inspect_WebQueryConnectionPackage_DetectsLiveWebQueries()
    {
        using var package = CreatePackageWithContent(("xl/connections.xml", """
            <connections xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <connection id="1" name="FreeX Web Query" type="4" refreshedVersion="6">
                <webPr sourceData="1" url="https://example.com/freex-web-query.html"/>
              </connection>
            </connections>
            """));

        var report = XlsxFeatureInspector.Inspect(package);
        var reportedKinds = report.Features.Select(f => f.Kind);

        reportedKinds.Should().Contain(XlsxUnsupportedFeatureKind.LiveWebQueries);
        reportedKinds.Should().NotContain(XlsxUnsupportedFeatureKind.PowerQuery);
    }


    [Fact]
    public void Inspect_RelationshipOnlyWebPublishItemsReference_DetectsLiveWebQueries()
    {
        using var package = CreatePackageWithContent(("xl/worksheets/_rels/sheet1.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdWebPublishItems"
                            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/webPublishItems"
                            Target="../webPublishItems.xml"/>
            </Relationships>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.LiveWebQueries);
    }


    [Fact]
    public void Inspect_CustomPropertiesWithSensitivityLabel_DetectsSensitivityLabels()
    {
        using var package = CreatePackageWithContent(("docProps/custom.xml", """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <property name="MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled">
                <vt:lpwstr>true</vt:lpwstr>
              </property>
            </Properties>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.SensitivityLabels);
    }


    [Fact]
    public void Inspect_SensitivityLabelInfoPart_DetectsSensitivityLabels()
    {
        using var package = CreatePackage("docMetadata/LabelInfo.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.SensitivityLabels);
    }


    [Fact]
    public void Inspect_CustomPropertiesWithoutSensitivityLabel_DoesNotWarn()
    {
        using var package = CreatePackageWithContent(("docProps/custom.xml", """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <property name="Department">
                <vt:lpwstr>Finance</vt:lpwstr>
              </property>
            </Properties>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().BeEmpty();
    }


    [Fact]
    public void Inspect_SmartArtDiagramPackage_DetectsSmartArtDiagrams()
    {
        using var package = CreatePackage(
            "xl/diagrams/data1.xml",
            "xl/diagrams/layout1.xml",
            "xl/diagrams/quickStyle1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.SmartArtDiagrams);
    }

}
