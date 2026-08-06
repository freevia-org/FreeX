using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class DeferredCommandMessageTests
{
    [Fact]
    public void UnsupportedXlsxFeatureSaveWarning_UsesRetainedOpaqueFeatureLanguage()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.SmartArtDiagrams, "xl/diagrams/data1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureSaveWarning(report);

        message.Title.Should().Be("Unsupported XLSX Features");
        message.Body.Should().Contain("retains as opaque package parts");
        message.Body.Should().Contain("does not run, render, author, or deeply edit");
        message.Body.Should().Contain("VBA macros (excluded)");
        message.Body.Should().Contain("SmartArt diagrams");
        message.Body.Should().Contain("Continue saving?");
        message.Body.Should().NotContain("does not preserve yet");
        message.Body.Should().NotContain("may remove");
    }

    [Fact]
    public void UnsupportedXlsxFeatureOpenWarning_DisclosesRetainedOpaqueFeatures()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.LiveWebQueries, "xl/webPublishItems.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Title.Should().Be("Unsupported XLSX Features Detected");
        message.Body.Should().Contain("opened this workbook");
        message.Body.Should().Contain("VBA macros (excluded)");
        message.Body.Should().Contain("live web queries / web publishing");
        message.Body.Should().Contain("retained as opaque package parts");
        message.Body.Should().Contain("will not be executed, refreshed, rendered, or edited");
        message.Body.Should().NotContain("may be removed if you save");
    }

    [Fact]
    public void UnsupportedXlsxFeatureOpenWarning_DisclosesDigitalSignatureInvalidationRisk()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.DigitalSignatures, "_xmlsignatures/sig1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("digital signatures");
        message.Body.Should().Contain("Digital signatures may no longer validate after workbook edits");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesChartPackageParts()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Charts, "xl/charts/chart1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("XLSX chart package parts");
        message.Body.Should().NotContain("charts.");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesKnownGapPackageFeatures()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.ConditionalFormats, "xl/worksheets/sheet1.xml"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.DrawingObjects, "xl/drawings/drawing1.xml"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.CustomXmlParts, "customXml/item1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("unsupported conditional formatting");
        message.Body.Should().Contain("drawing objects");
        message.Body.Should().Contain("custom XML parts");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesPowerQueryAndDataModel()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.PowerQuery, "xl/queries/query1.xml"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.DataModel, "xl/model/item.data")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("Power Query queries (excluded)");
        message.Body.Should().Contain("Data Model / Power Pivot (excluded)");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesLinkedDataTypes()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.LinkedDataTypes, "xl/richData/rdrichvalue.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("Microsoft linked data types (excluded)");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesThreadedComments()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.ThreadedComments, "xl/threadedComments/threadedComment1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("threaded comments");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesTrackChanges()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.TrackChanges, "xl/revisions/revisionLog1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("track changes / revision history");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesFormControls()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.FormControls, "xl/activeX/activeX1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("form controls / ActiveX controls");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesDigitalSignatures()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.DigitalSignatures, "_xmlsignatures/sig1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("digital signatures");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesCustomRibbonUi()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.CustomRibbonUi, "customUI/customUI.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("custom ribbon UI");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesOfficeAddIns()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.OfficeAddIns, "xl/webextensions/taskpanes.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("Office add-ins");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesLiveWebQueries()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.LiveWebQueries, "xl/webPublishItems.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("live web queries / web publishing");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesSensitivityLabels()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.SensitivityLabels, "docProps/custom.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("sensitivity labels / IRM metadata");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesSmartArtDiagrams()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.SmartArtDiagrams, "xl/diagrams/data1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("SmartArt diagrams");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesUnsupportedSheetTypes()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes, "xl/chartsheets/sheet1.xml")
        ]);

        var message = TestDeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("chart sheets / dialog sheets / macro sheets");
    }
}
