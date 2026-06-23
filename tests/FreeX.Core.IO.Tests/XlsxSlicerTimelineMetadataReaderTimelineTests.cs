using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSlicerTimelineMetadataReaderTimelineTests
{
    [Fact]
    public void Load_ParsesExcelTimeslicerDrawingAnchorAndNestedTimelineState()
    {
        using var package = BuildTimelinePackage();
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var metadata = XlsxSlicerTimelineMetadataReader.Load(archive);

        var timeline = metadata.Timelines.Should().ContainSingle().Subject;
        timeline.Name.Should().Be("NativePivotSaleDateTimeline");
        timeline.Caption.Should().Be("SaleDate");
        timeline.CacheName.Should().Be("NativePivotSaleDateTimeline");
        timeline.SourcePivotTableName.Should().Be("NativePivotSlicerTimeline");
        timeline.SourceFieldName.Should().Be("SaleDate");
        timeline.StartDate.Should().Be("2026-01-01");
        timeline.EndDate.Should().Be("2027-01-01");
        timeline.SelectedStartDate.Should().Be("2026-02-01");
        timeline.SelectedEndDate.Should().Be("2026-04-30");
        timeline.DrawingShapeName.Should().Be("NativePivotSaleDateTimeline");
        timeline.DrawingAnchor.Should().NotBeNull();
        timeline.DrawingAnchor!.From.Column.Should().Be(10);
        timeline.DrawingAnchor.From.Row.Should().Be(2);
        timeline.DrawingAnchor.To.Column.Should().Be(15);
        timeline.DrawingAnchor.To.Row.Should().Be(11);
    }

    private static MemoryStream BuildTimelinePackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Pivot Slicer Timeline" sheetId="2" r:id="rId2"/>
                  </sheets>
                </workbook>
                """);

            WriteEntry(archive, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId2"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                                Target="worksheets/sheet2.xml"/>
                </Relationships>
                """);

            WriteEntry(archive, "xl/worksheets/sheet2.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheetData/>
                  <drawing r:id="rIdDrawing"/>
                </worksheet>
                """);

            WriteEntry(archive, "xl/worksheets/_rels/sheet2.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdDrawing"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"
                                Target="../drawings/drawing1.xml"/>
                  <Relationship Id="rIdTimeline"
                                Type="http://schemas.microsoft.com/office/2011/relationships/timeline"
                                Target="../timelines/timeline1.xml"/>
                </Relationships>
                """);

            WriteEntry(archive, "xl/drawings/drawing1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <xdr:twoCellAnchor editAs="oneCell">
                    <xdr:from><xdr:col>10</xdr:col><xdr:colOff>83820</xdr:colOff><xdr:row>2</xdr:row><xdr:rowOff>167640</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>15</xdr:col><xdr:colOff>337820</xdr:colOff><xdr:row>11</xdr:row><xdr:rowOff>45720</xdr:rowOff></xdr:to>
                    <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
                      <mc:Choice xmlns:tsle="http://schemas.microsoft.com/office/drawing/2012/timeslicer" Requires="tsle">
                        <xdr:graphicFrame macro="">
                          <xdr:nvGraphicFramePr>
                            <xdr:cNvPr id="3" name="NativePivotSaleDateTimeline"/>
                            <xdr:cNvGraphicFramePr/>
                          </xdr:nvGraphicFramePr>
                          <xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm>
                          <a:graphic>
                            <a:graphicData uri="http://schemas.microsoft.com/office/drawing/2012/timeslicer">
                              <tsle:timeslicer name="NativePivotSaleDateTimeline"/>
                            </a:graphicData>
                          </a:graphic>
                        </xdr:graphicFrame>
                      </mc:Choice>
                      <mc:Fallback>
                        <xdr:sp><xdr:nvSpPr><xdr:cNvPr id="0" name="Fallback"/></xdr:nvSpPr></xdr:sp>
                      </mc:Fallback>
                    </mc:AlternateContent>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """);

            WriteEntry(archive, "xl/timelines/timeline1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <timelines xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main">
                  <timeline name="NativePivotSaleDateTimeline" cache="NativePivotSaleDateTimeline" caption="SaleDate" style="TimeSlicerStyleLight2"/>
                </timelines>
                """);

            WriteEntry(archive, "xl/timelineCaches/timelineCache1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <timelineCacheDefinition xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"
                                         name="NativePivotSaleDateTimeline"
                                         sourceName="SaleDate">
                  <pivotTables>
                    <pivotTable tabId="2" name="NativePivotSlicerTimeline"/>
                  </pivotTables>
                  <state filterType="dateBetween">
                    <selection startDate="2026-02-01T00:00:00" endDate="2026-04-30T00:00:00"/>
                    <bounds startDate="2026-01-01T00:00:00" endDate="2027-01-01T00:00:00"/>
                  </state>
                </timelineCacheDefinition>
                """);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
