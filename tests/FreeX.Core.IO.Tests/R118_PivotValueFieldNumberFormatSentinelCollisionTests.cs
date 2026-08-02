using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R118-io-numfmt-pivot-sentinel-collision: PivotValueFieldPlanner.ResolveNumberFormatState (App.Presentation)
/// hardcodes the SAME sentinel NumberFormatId (164 -- DefaultCustomNumberFormatId) for EVERY distinct custom
/// format string a user types into the pivot "Value Field Settings" number-format dialog. When two (or more)
/// pivot value/data fields are each given a DIFFERENT custom format this way, their PivotDataFieldModel
/// instances end up with the SAME NumberFormatId but DIFFERENT NumberFormatCode strings.
///
/// XlsxNumberFormatCatalogWriter.BuildNumberFormatCatalog used to build its id-&gt;code catalog with a plain
/// `catalog[numberFormatId] = field.NumberFormatCode` dictionary-indexer assignment -- no collision check --
/// so the SECOND data field processed silently overwrote the first field's entry. XlsxPivotTableWriter's
/// ToPivotNumberFormatAttribute and XlsxFileAdapter.SavePostProcessing's RewritePreservedPivotDataFieldSummaries
/// then both resolved each dataField's final numFmtId by looking up the SAME sentinel key, so BOTH data
/// fields ended up referencing the SAME final numFmtId -- whichever field's format code happened to "win" the
/// dictionary collision. The other field's saved pivot values silently inherited the wrong number format.
///
/// This must be exercised on a single, from-scratch save (no source package): XlsxPivotTableWriter.Save (and
/// therefore the fresh <c>dataField</c>/<c>numFmts</c> XML this test inspects) is only invoked when the
/// workbook has no tracked source package (XlsxFileAdapter.SavePostProcessing.cs's `!hasSourcePackage` guard).
/// </summary>
public sealed class R118_PivotValueFieldNumberFormatSentinelCollisionTests
{
    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Workbook BuildWorkbookWithTwoCollidingCustomPivotValueFields(string codeA, string codeB)
    {
        var workbook = new Workbook("PivotValueFieldNumberFormatCollisionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(20));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:C2"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount1"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount2"));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        // Both fields are hardcoded to the same sentinel id (164), exactly as
        // PivotValueFieldPlanner.ResolveNumberFormatState does for every distinct custom format string.
        pivot.DataFields.Add(new PivotDataFieldModel(
            1, "Sum of Amount1", "sum", NumberFormatId: 164, NumberFormatCode: codeA));
        pivot.DataFields.Add(new PivotDataFieldModel(
            2, "Sum of Amount2", "sum", NumberFormatId: 164, NumberFormatCode: codeB));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    [Fact]
    public void Save_TwoPivotValueFieldsWithDifferentCustomFormatsSharingSentinelId_KeepDistinctFormats()
    {
        var workbook = BuildWorkbookWithTwoCollidingCustomPivotValueFields("0.0\"kg\"", "0.0\"lb\"");

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        string? kgId, lbId;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var stylesRoot = LoadPackageXml(archive.GetEntry("xl/styles.xml")!).Root!;
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var numFmtElements = stylesRoot.Element(ns + "numFmts")!.Elements(ns + "numFmt").ToList();

            // The finding: BOTH custom format codes must survive into the styles catalog, each under its
            // own numFmtId -- not one silently clobbering the other via the shared sentinel key.
            var kgElement = numFmtElements.Should().ContainSingle(e => e.Attribute("formatCode")!.Value == "0.0\"kg\"",
                "the first data field's custom format must not be dropped by the second field's collision on the same sentinel id").Subject;
            var lbElement = numFmtElements.Should().ContainSingle(e => e.Attribute("formatCode")!.Value == "0.0\"lb\"").Subject;
            kgId = kgElement.Attribute("numFmtId")!.Value;
            lbId = lbElement.Attribute("numFmtId")!.Value;
            kgId.Should().NotBe(lbId, "each distinct custom format must get its own numFmtId");

            var pivotRoot = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!).Root!;
            var dataFieldElements = pivotRoot.Element(ns + "dataFields")!.Elements(ns + "dataField").ToList();
            dataFieldElements.Should().HaveCount(2);

            var amount1Field = dataFieldElements.Should().ContainSingle(e => e.Attribute("name")!.Value == "Sum of Amount1").Subject;
            var amount2Field = dataFieldElements.Should().ContainSingle(e => e.Attribute("name")!.Value == "Sum of Amount2").Subject;

            // THE regression this test guards: before the fix, both dataField elements pointed at the SAME
            // numFmtId (whichever field's code happened to "win" the catalog-dictionary collision).
            amount1Field.Attribute("numFmtId")!.Value.Should().Be(kgId, "Amount1 was given the \"kg\" format");
            amount2Field.Attribute("numFmtId")!.Value.Should().Be(lbId, "Amount2 was given the \"lb\" format");
            amount1Field.Attribute("numFmtId")!.Value.Should().NotBe(amount2Field.Attribute("numFmtId")!.Value);
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedPivot = reloaded.Sheets.SelectMany(s => s.PivotTables).Should().ContainSingle().Subject;
        var reloadedAmount1 = reloadedPivot.DataFields.Should().Contain(f => f.Name == "Sum of Amount1").Subject;
        var reloadedAmount2 = reloadedPivot.DataFields.Should().Contain(f => f.Name == "Sum of Amount2").Subject;

        reloadedAmount1.NumberFormatCode.Should().Be("0.0\"kg\"", "Amount1 must keep its OWN configured format after a save + reload");
        reloadedAmount2.NumberFormatCode.Should().Be("0.0\"lb\"", "Amount2 must keep its OWN configured format after a save + reload, not silently inherit Amount1's");
    }

    [Fact]
    public void Save_TwoPivotValueFieldsWithSameCustomFormatSharingSentinelId_NoRegressionSingleEntry()
    {
        // Sibling no-regression case: when two data fields legitimately share the SAME custom format text
        // (the ordinary, non-colliding case the pre-existing behavior already handled), the fix must not
        // start minting a spurious duplicate numFmt entry or splitting them onto different ids.
        var workbook = BuildWorkbookWithTwoCollidingCustomPivotValueFields("0.0\"kg\"", "0.0\"kg\"");

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var stylesRoot = LoadPackageXml(archive.GetEntry("xl/styles.xml")!).Root!;
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var numFmtElements = stylesRoot.Element(ns + "numFmts")!.Elements(ns + "numFmt").ToList();
            numFmtElements.Should().ContainSingle(e => e.Attribute("formatCode")!.Value == "0.0\"kg\"",
                "two data fields sharing the identical format code must not produce a duplicate numFmt entry");

            var pivotRoot = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!).Root!;
            var dataFieldElements = pivotRoot.Element(ns + "dataFields")!.Elements(ns + "dataField").ToList();
            var amount1Field = dataFieldElements.Should().ContainSingle(e => e.Attribute("name")!.Value == "Sum of Amount1").Subject;
            var amount2Field = dataFieldElements.Should().ContainSingle(e => e.Attribute("name")!.Value == "Sum of Amount2").Subject;
            amount1Field.Attribute("numFmtId")!.Value.Should().Be(amount2Field.Attribute("numFmtId")!.Value,
                "identical custom formats should still share one numFmtId");
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedPivot = reloaded.Sheets.SelectMany(s => s.PivotTables).Should().ContainSingle().Subject;
        reloadedPivot.DataFields.Should().OnlyContain(f => f.NumberFormatCode == "0.0\"kg\"");
    }
}
