using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public partial class XlsxFeatureInspectorTests
{
    [Fact]
    public void Inspect_CleanWorkbookPackage_HasNoUnsupportedFeatures()
    {
        using var package = CreatePackage(
            "[Content_Types].xml",
            "_rels/.rels",
            "xl/workbook.xml",
            "xl/worksheets/sheet1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.HasUnsupportedFeatures.Should().BeFalse();
        report.Features.Should().BeEmpty();
    }


    [Fact]
    public void Inspect_MacroPackage_DetectsMacros()
    {
        using var package = CreatePackage("xl/vbaProject.bin");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f => f.Kind == XlsxUnsupportedFeatureKind.Macros);
    }


    [Fact]
    public void Inspect_PivotAndChartPackage_DoesNotReportModelFirstPivotParts()
    {
        using var package = CreatePackage(
            "xl/pivotTables/pivotTable1.xml",
            "xl/pivotCache/pivotCacheDefinition1.xml",
            "xl/charts/chart1.xml");

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.Charts);
    }

}
