using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R47-io-pivot-cache-shared-items-3-3: a pivotCacheDefinition's &lt;cacheField&gt; numFmtId was written
/// raw/unmapped by XlsxPivotTableWriter.Cache.cs's ToPivotCacheFieldXml, bypassing the numberFormatIdMap
/// remap that XlsxNumberFormatCatalogWriter computes whenever a custom numFmtId collides with another
/// format during a save (the same remap the sibling pivotTable &lt;dataField&gt; numFmtId already goes
/// through via ToPivotNumberFormatAttribute). Fixed by threading the already-computed numberFormatIdMap
/// from XlsxPivotTableWriter.cs's Save() (ToPivotCacheDefinitionXml call) into ToPivotCacheFieldXml, which
/// now applies the identical remap.
///
/// The collision must be exercised on a single, from-scratch save (not a load-then-resave patch save):
/// XlsxPivotTableWriter.Save (and therefore ToPivotCacheDefinitionXml) is only invoked when the workbook
/// has no tracked source package (see XlsxFileAdapter.SavePostProcessing.cs's `!hasSourcePackage` guard);
/// a genuine cell style whose custom number format collides with the pivot's declared numFmtId, both
/// present on the SAME from-scratch workbook, reproduces the same id collision within that single save.
/// </summary>
public sealed class R47_PivotCacheFieldNumberFormatRemapTests
{
    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Workbook BuildWorkbookWithCollidingPivotCacheCustomNumberFormat()
    {
        var workbook = new Workbook("PivotCacheNumberFormatCollisionTest");
        // Registered first so the base cell-style writer claims numFmtId 164 for this format.
        var conflictingStyle = CellStyle.Default.Clone();
        conflictingStyle.NumberFormat = "0.0000";
        var conflictingStyleId = workbook.RegisterStyle(conflictingStyle);

        // The pivot's own custom format ("kg"), explicitly declared at the SAME id (164) the pivot model
        // was authored against -- mirroring a file where the pivot cache/data field's numFmtId was set
        // before the cell-style catalog independently claimed the same id for a different format.
        workbook.NumberFormatCatalog[164] = "#,##0.0 \"kg\"";

        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        var styledCell = Cell.FromValue(new NumberValue(10));
        styledCell.StyleId = conflictingStyleId;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), styledCell);

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount", NumberFormatId: 164));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(
            1,
            "Sum of Amount",
            "sum",
            NumberFormatId: 164,
            NumberFormatCode: "#,##0.0 \"kg\""));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    [Fact]
    public void Save_CacheFieldNumFmtIdCollidesWithCellStyle_RemapsCacheFieldSameAsDataField()
    {
        var workbook = BuildWorkbookWithCollidingPivotCacheCustomNumberFormat();

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var stylesText = LoadPackageXml(archive.GetEntry("xl/styles.xml")!).ToString();
            stylesText.Should().Contain("formatCode=\"0.0000\"");
            stylesText.Should().Contain("formatCode=\"#,##0.0 &quot;kg&quot;\"");

            var pivotText = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!).ToString();
            pivotText.Should().NotContain(
                "numFmtId=\"164\"",
                "the sibling pivotTable dataField must already be remapped away from the colliding id");

            // The actual finding: the cacheField numFmtId must go through the SAME remap, not be left
            // pointing at whatever unrelated format now occupies the old id 164.
            var cacheText = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!).ToString();
            cacheText.Should().NotContain(
                "numFmtId=\"164\"",
                "cacheField numFmtId must be remapped identically to the sibling dataField numFmtId");
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedCacheField = reloaded.PivotCaches.Should().ContainSingle().Subject.Fields
            .Should().Contain(field => field.Name == "Amount").Subject;
        reloadedCacheField.NumberFormatId.Should().NotBe(164);
        reloaded.NumberFormatCatalog[reloadedCacheField.NumberFormatId!.Value].Should().Be("#,##0.0 \"kg\"");
    }

    [Fact]
    public void Save_CacheFieldNumFmtIdWithNoCollision_KeepsOriginalIdNoRegression()
    {
        // Sibling no-regression case: when there is no id collision at all (no competing cell style using
        // the same custom numFmtId), the cacheField numFmtId must still round-trip to the SAME id it
        // started with (the remap map is effectively an identity map).
        var workbook = new Workbook("PivotCacheNumberFormatNoCollisionTest");
        workbook.NumberFormatCatalog[164] = "#,##0.0 \"kg\"";
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount", NumberFormatId: 164));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(
            1,
            "Sum of Amount",
            "sum",
            NumberFormatId: 164,
            NumberFormatCode: "#,##0.0 \"kg\""));
        sheet.PivotTables.Add(pivot);

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var cacheText = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!).ToString();
            cacheText.Should().Contain("numFmtId=\"164\"");
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedCacheField = reloaded.PivotCaches.Should().ContainSingle().Subject.Fields
            .Should().Contain(field => field.Name == "Amount").Subject;
        reloadedCacheField.NumberFormatId.Should().Be(164);
        reloaded.NumberFormatCatalog[164].Should().Be("#,##0.0 \"kg\"");
    }
}
