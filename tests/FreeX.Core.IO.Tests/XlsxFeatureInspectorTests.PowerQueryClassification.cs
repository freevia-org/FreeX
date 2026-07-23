using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R76-io-external-data-4-2: a classic (non-Power-Query) external-data queryTable/connection
/// (Text/Database/ODBC/classic-Web, no webPr, no M-code) must NOT be reported as
/// "Power Query queries (excluded)" -- it round-trips byte-for-byte and is not excluded. Only a
/// genuine Power Query signal (xl/queries/ M-code, or a connection referencing the
/// Microsoft.Mashup provider) should be tagged PowerQuery.
/// </summary>
public partial class XlsxFeatureInspectorTests
{
    [Fact]
    public void Inspect_ClassicDatabaseQueryTableAndConnection_DoesNotReportPowerQuery()
    {
        using var package = CreatePackageWithContent(
            ("xl/connections.xml", """
                <connections xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <connection id="1" name="FreeX Database Query" type="1" refreshedVersion="6">
                    <dbPr connection="ODBC;DSN=FreeXDataSource;" command="SELECT * FROM Customers"/>
                  </connection>
                </connections>
                """),
            ("xl/queryTables/queryTable1.xml", """
                <queryTable xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                            name="FreeXQueryTable" connectionId="1"/>
                """));

        var report = XlsxFeatureInspector.Inspect(package);
        var reportedKinds = report.Features.Select(f => f.Kind).ToList();

        reportedKinds.Should().NotContain(XlsxUnsupportedFeatureKind.PowerQuery,
            "a classic Text/Database/ODBC queryTable+connection is preserved byte-for-byte and " +
            "is not Power Query (R76-io-external-data-4-2)");
        reportedKinds.Should().NotContain(XlsxUnsupportedFeatureKind.LiveWebQueries);
    }

    [Fact]
    public void Inspect_GenuinePowerQueryQueriesPart_StillReportsPowerQuery()
    {
        using var package = CreatePackageWithContent(
            ("xl/queries/query1.xml", """
                <Queries xmlns="http://schemas.microsoft.com/DataMashup">
                  <Query Name="FreeXQuery">let Source = Excel.CurrentWorkbook() in Source</Query>
                </Queries>
                """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind).Should().Contain(XlsxUnsupportedFeatureKind.PowerQuery,
            "an xl/queries/ M-code part is a genuine Power Query signal and must still be flagged");
    }

    [Fact]
    public void Inspect_ConnectionWithMashupProviderSignal_ReportsPowerQuery()
    {
        using var package = CreatePackageWithContent(
            ("xl/connections.xml", """
                <connections xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <connection id="1" name="Query - FreeXTable" type="5" refreshedVersion="6">
                    <dbPr connection="Provider=Microsoft.Mashup.OleDb.1;Data Source=$Workbook$;Location=FreeXTable"
                          command="SELECT * FROM [FreeXTable]"/>
                  </connection>
                </connections>
                """));

        var report = XlsxFeatureInspector.Inspect(package);
        var reportedKinds = report.Features.Select(f => f.Kind).ToList();

        reportedKinds.Should().Contain(XlsxUnsupportedFeatureKind.PowerQuery,
            "a connection referencing the Microsoft.Mashup provider is a genuine Power Query " +
            "signal (R76-io-external-data-4-2)");
        reportedKinds.Should().NotContain(XlsxUnsupportedFeatureKind.LiveWebQueries);
    }

    [Fact]
    public void Inspect_WebQueryConnection_StillReportsLiveWebQueries_NoRegression()
    {
        // Sibling no-regression case: a web query (webPr present) must still take its own
        // LiveWebQueries path, unaffected by the classic-connection carve-out.
        using var package = CreatePackageWithContent(
            ("xl/connections.xml", """
                <connections xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <connection id="1" name="FreeX Web Query" type="4" refreshedVersion="6">
                    <webPr sourceData="1" url="https://example.com/freex-web-query.html"/>
                  </connection>
                </connections>
                """));

        var report = XlsxFeatureInspector.Inspect(package);
        var reportedKinds = report.Features.Select(f => f.Kind).ToList();

        reportedKinds.Should().Contain(XlsxUnsupportedFeatureKind.LiveWebQueries);
        reportedKinds.Should().NotContain(XlsxUnsupportedFeatureKind.PowerQuery);
    }
}
