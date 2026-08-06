using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class DeferredCommandMessageTests
{
    [Theory]
    [InlineData(XlsxUnsupportedFeatureKind.Macros, "VBA macros (excluded)")]
    [InlineData(XlsxUnsupportedFeatureKind.Charts, "XLSX chart package parts")]
    [InlineData(XlsxUnsupportedFeatureKind.EmbeddedObjects, "embedded objects")]
    [InlineData(XlsxUnsupportedFeatureKind.CustomXmlParts, "custom XML parts")]
    [InlineData(XlsxUnsupportedFeatureKind.ConditionalFormats, "unsupported conditional formatting")]
    [InlineData(XlsxUnsupportedFeatureKind.DrawingObjects, "drawing objects")]
    [InlineData(XlsxUnsupportedFeatureKind.PowerQuery, "Power Query queries (excluded)")]
    [InlineData(XlsxUnsupportedFeatureKind.DataModel, "Data Model / Power Pivot (excluded)")]
    [InlineData(XlsxUnsupportedFeatureKind.LinkedDataTypes, "Microsoft linked data types (excluded)")]
    [InlineData(XlsxUnsupportedFeatureKind.ThreadedComments, "threaded comments")]
    [InlineData(XlsxUnsupportedFeatureKind.TrackChanges, "track changes / revision history")]
    [InlineData(XlsxUnsupportedFeatureKind.FormControls, "form controls / ActiveX controls")]
    [InlineData(XlsxUnsupportedFeatureKind.DigitalSignatures, "digital signatures")]
    [InlineData(XlsxUnsupportedFeatureKind.CustomRibbonUi, "custom ribbon UI")]
    [InlineData(XlsxUnsupportedFeatureKind.OfficeAddIns, "Office add-ins")]
    [InlineData(XlsxUnsupportedFeatureKind.LiveWebQueries, "live web queries / web publishing")]
    [InlineData(XlsxUnsupportedFeatureKind.SensitivityLabels, "sensitivity labels / IRM metadata")]
    [InlineData(XlsxUnsupportedFeatureKind.SmartArtDiagrams, "SmartArt diagrams")]
    [InlineData(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes, "chart sheets / dialog sheets / macro sheets")]
    public void FormatUnsupportedXlsxFeatureKind_UsesExpectedDisplayText(
        XlsxUnsupportedFeatureKind kind,
        string expectedDisplayText)
    {
        TestDeferredCommandMessages.FormatUnsupportedXlsxFeatureKind(kind)
            .Should().Be(expectedDisplayText);
    }
}
