using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Guards save fidelity for the load-time table-style materializer: Excel keeps tables dynamic and
/// stores no per-cell banding, so after FreeX paints the loaded table's banding (for display) and the
/// patch-save snapshot is rebased, a load to save round-trip must NOT bake those fills into the file.
/// The saved tables part, styles part, and worksheet must remain byte-identical to the source.
/// </summary>
public sealed class StructuredTableStyleSaveFidelityTests
{
    [Fact]
    public void MaterializeLoadedTableStylesThenSave_LeavesTablesAndStylesPartsUnchanged()
    {
        var sourceBytes = CreateStyledTableSourcePackage();
        var adapter = new XlsxFileAdapter();

        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        // Mirror the open service: materialize the table banding for display, then rebase the
        // patch-save snapshot so the materialized fills are treated as the loaded baseline (and are
        // therefore NOT written back to the file on save).
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();
        adapter.RebaseLoadedPackageSnapshot(workbook).Should().BeTrue();

        // The materializer must have actually painted the header band in-memory (sanity check).
        var sheet = workbook.GetSheetAt(0);
        workbook.GetStyle(sheet.GetCell(1, 1)!.StyleId).FillColor
            .Should().NotBeNull("the loaded table header should be styled for display");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // The file must be unchanged: tables part, styles part, and the worksheet keep no baked fills.
        ReadEntry(savedBytes, "xl/tables/table1.xml")
            .Should().Equal(ReadEntry(sourceBytes, "xl/tables/table1.xml"));
        ReadEntry(savedBytes, "xl/styles.xml")
            .Should().Equal(ReadEntry(sourceBytes, "xl/styles.xml"));
        ReadEntry(savedBytes, "xl/worksheets/sheet1.xml")
            .Should().Equal(ReadEntry(sourceBytes, "xl/worksheets/sheet1.xml"));
    }

    private static byte[] ReadEntry(byte[] packageBytes, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"package should contain {entryName}");
        using var entryStream = entry!.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static byte[] CreateStyledTableSourcePackage()
    {
        var parts = new (string Name, string Content)[]
        {
            ("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/tables/table1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"/>
                </Types>
                """),
            ("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            ("xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            ("xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            ("xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:B3"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Category</t></is></c><c r="B1" t="inlineStr"><is><t>Amount</t></is></c></row>
                    <row r="2"><c r="A2" t="inlineStr"><is><t>East</t></is></c><c r="B2"><v>10</v></c></row>
                    <row r="3"><c r="A3" t="inlineStr"><is><t>West</t></is></c><c r="B3"><v>20</v></c></row>
                  </sheetData>
                  <tableParts count="1"><tablePart r:id="rId1"/></tableParts>
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/table" Target="../tables/table1.xml"/>
                </Relationships>
                """),
            ("xl/tables/table1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" id="1" name="Table1" displayName="Table1" ref="A1:B3" totalsRowShown="0">
                  <autoFilter ref="A1:B3"/>
                  <tableColumns count="2">
                    <tableColumn id="1" name="Category"/>
                    <tableColumn id="2" name="Amount"/>
                  </tableColumns>
                  <tableStyleInfo name="TableStyleMedium2" showFirstColumn="0" showLastColumn="0" showRowStripes="1" showColumnStripes="0"/>
                </table>
                """),
        };

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes, 0, bytes.Length);
            }
        }

        return buffer.ToArray();
    }
}
