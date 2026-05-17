using FluentAssertions;
using Freexcel.App.Host;
using Freexcel.Core.IO;

namespace Freexcel.App.Host.Tests;

public sealed class DeferredCommandMessageTests
{
    [Fact]
    public void WorkbookThemeMessage_NamesDeferredThemeModel()
    {
        var message = DeferredCommandMessages.WorkbookTheme("Themes");

        message.Title.Should().Be("Themes");
        message.Body.Should().Contain("deferred");
        message.Body.Should().Contain("workbook theme model");
        message.Body.Should().Contain("documented parity gap");
    }

    [Fact]
    public void MultiWindowMessage_NamesDeferredWindowHosting()
    {
        var message = DeferredCommandMessages.MultiWindow("New Window");

        message.Title.Should().Be("New Window");
        message.Body.Should().Contain("deferred");
        message.Body.Should().Contain("multi-window workbook hosting");
        message.Body.Should().Contain("documented parity gap");
    }

    [Fact]
    public void ShareExcludedMessage_NamesCloudCoauthoringExclusion()
    {
        var message = DeferredCommandMessages.ShareExcluded();

        message.Title.Should().Be("Share Workbook");
        message.Body.Should().Contain("excluded");
        message.Body.Should().Contain("Microsoft 365 Share");
        message.Body.Should().Contain("cloud co-authoring");
    }

    [Fact]
    public void OnlineTemplatesMessage_NamesExternalMicrosoftServiceExclusion()
    {
        var message = DeferredCommandMessages.OnlineTemplatesExcluded();

        message.Title.Should().Be("Online Templates");
        message.Body.Should().Contain("excluded");
        message.Body.Should().Contain("external Microsoft template service");
    }

    [Fact]
    public void AccountMessage_NamesLocalAccountDecision()
    {
        var message = DeferredCommandMessages.LocalAccountInfo();

        message.Title.Should().Be("Account");
        message.Body.Should().Contain("Microsoft account integration");
        message.Body.Should().Contain("not implemented");
        message.Body.Should().Contain("local files");
        message.Body.Should().Contain("Options");
    }

    [Fact]
    public void PivotTableMessage_NamesPivotSubsystemExclusion()
    {
        var message = DeferredCommandMessages.PivotTableExcluded();

        message.Title.Should().Be("PivotTable");
        message.Body.Should().Contain("excluded");
        message.Body.Should().Contain("pivot caches");
        message.Body.Should().Contain("slicers");
        message.Body.Should().Contain("supported local analysis workflows");
    }

    [Fact]
    public void UnsupportedXlsxFeatureSaveWarning_UsesDocumentedExclusionLanguage()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.PivotTables, "xl/pivotTables/pivotTable1.xml"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Slicers, "xl/slicers/slicer1.xml"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.PivotTables, "xl/pivotCache/pivotCacheDefinition1.xml")
        ]);

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureSaveWarning(report);

        message.Title.Should().Be("Unsupported XLSX Features");
        message.Body.Should().Contain("does not preserve yet");
        message.Body.Should().Contain("VBA macros (excluded)");
        message.Body.Should().Contain("PivotTables/pivot caches (excluded)");
        message.Body.Should().Contain("slicers (excluded with PivotTables)");
        message.Body.Should().Contain("Continue saving?");
        message.Body.Should().NotContain("PivotTables/pivot caches (excluded), PivotTables/pivot caches (excluded)");
    }

    [Fact]
    public void UnsupportedXlsxFeatureOpenWarning_DisclosesPotentialLossBeforeSave()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Macros, "xl/vbaProject.bin"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Timelines, "xl/timelines/timeline1.xml")
        ]);

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Title.Should().Be("Unsupported XLSX Features Detected");
        message.Body.Should().Contain("opened this workbook");
        message.Body.Should().Contain("VBA macros (excluded)");
        message.Body.Should().Contain("timelines (excluded with PivotTables)");
        message.Body.Should().Contain("may be removed if you save");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesChartPackageParts()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Charts, "xl/charts/chart1.xml")
        ]);

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("XLSX chart package parts");
        message.Body.Should().NotContain("charts.");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesKnownGapPackageFeatures()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.ConditionalFormats, "xl/worksheets/sheet1.xml"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.DrawingObjects, "xl/drawings/drawing1.xml"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.Sparklines, "xl/worksheets/sheet1.xml")
        ]);

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("unsupported conditional formatting");
        message.Body.Should().Contain("drawing objects");
        message.Body.Should().Contain("sparklines");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesPowerQueryAndDataModel()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.PowerQuery, "xl/queries/query1.xml"),
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.DataModel, "xl/model/item.data")
        ]);

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("Power Query queries (excluded)");
        message.Body.Should().Contain("Data Model / Power Pivot (excluded)");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesLinkedDataTypes()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.LinkedDataTypes, "xl/richData/rdrichvalue.xml")
        ]);

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("Microsoft linked data types (excluded)");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesThreadedComments()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.ThreadedComments, "xl/threadedComments/threadedComment1.xml")
        ]);

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("threaded comments");
    }

    [Fact]
    public void UnsupportedXlsxFeatureWarning_NamesTrackChanges()
    {
        var report = new XlsxFeatureReport([
            new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.TrackChanges, "xl/revisions/revisionLog1.xml")
        ]);

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(report);

        message.Body.Should().Contain("track changes / revision history");
    }
}
