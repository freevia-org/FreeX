using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R26-io-pivot-deep-1: XlsxPivotCacheReader.ReadSharedItemValues drops any &lt;m/&gt; (missing/blank)
/// OOXML sharedItems child before <see cref="PivotCacheFieldModel.SharedItems"/> is built, shifting every
/// later item's position out of alignment with the raw OOXML index space a native
/// &lt;pageField item="N"/&gt; is defined against. XlsxPivotTableReader.Fields.cs's
/// ReadNativePageFieldSelectedItem indexed straight into that shifted list using the raw @item value,
/// which could silently resolve to a DIFFERENT item's caption than Excel intended whenever the field's
/// declared sharedItems @count (<see cref="PivotCacheFieldModel.SharedItemCount"/>) is larger than the
/// materialized <see cref="PivotCacheFieldModel.SharedItems"/> list (i.e. at least one item was dropped).
/// These tests pin: (1) the fixed behavior declines to guess a caption when a shift is detectable, and
/// (2) the sibling case -- a field with no dropped items, where the declared count matches the
/// materialized list -- still resolves the selected item exactly as before (no regression).
/// </summary>
public sealed class R26_PivotPageFieldSharedItemShiftTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_NativePageFieldItem_WithRawMissingSharedItemShiftingIndices_ReturnsNullNotWrongCaption()
    {
        // Reproduces a real-Excel-authored cacheField whose raw <sharedItems> carries an <m/> (missing/
        // blank) slot the FreeX writer itself never emits: <s v="East"/><m/><s v="West"/><s v="North"/>
        // (raw indices 0..3, count="4"). XlsxPivotCacheReader drops the <m/> when materializing
        // PivotCacheFieldModel.SharedItems, so the field's SharedItems end up ["East","West","North"]
        // (only 3 entries) while its declared SharedItemCount stays 4 -- the exact mismatch the fix keys
        // off of. The native pageField selects raw index 2 (the real "West"); the old code indexed
        // straight into the shifted materialized list (materialized[2] = "North") and would have
        // returned the WRONG caption instead of declining to resolve one.
        using var package = CreatePivotWorkbookPackage(pageFieldItemIndex: 2);

        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml", document =>
        {
            var sharedItems = document.Root!
                .Element(WorkbookNs + "cacheFields")!
                .Elements(WorkbookNs + "cacheField")
                .First(field => string.Equals(field.Attribute("name")?.Value, "Region", StringComparison.Ordinal))
                .Element(WorkbookNs + "sharedItems")!;
            sharedItems.RemoveNodes();
            sharedItems.SetAttributeValue("count", "4");
            sharedItems.Add(
                new XElement(WorkbookNs + "s", new XAttribute("v", "East")),
                new XElement(WorkbookNs + "m"),
                new XElement(WorkbookNs + "s", new XAttribute("v", "West")),
                new XElement(WorkbookNs + "s", new XAttribute("v", "North")));
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var pageField = workbook.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject
            .PageFields.Should().ContainSingle().Subject;
        pageField.SourceFieldIndex.Should().Be(0);
        // Old (buggy) behavior indexed straight into the shifted list and returned "North" here --
        // a caption Excel never selected. The fix declines to guess and leaves it unresolved instead.
        pageField.SelectedItem.Should().BeNull();
    }

    [Fact]
    public void Load_NativePageFieldItem_WithNoMissingSharedItems_StillResolvesSelectedItem()
    {
        // Sibling case: no <m/> was ever dropped, so SharedItemCount matches the materialized list and
        // the raw @item index lines up with it exactly like before the fix -- must keep resolving.
        using var package = CreatePivotWorkbookPackage(pageFieldItemIndex: 1);

        var workbook = new XlsxFileAdapter().Load(package);

        var pageField = workbook.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject
            .PageFields.Should().ContainSingle().Subject;
        pageField.SourceFieldIndex.Should().Be(0);
        pageField.SelectedItem.Should().Be("West");
    }

    private static MemoryStream CreatePivotWorkbookPackage(int pageFieldItemIndex)
    {
        var package = XlsxPackageTestHelper.SaveWorkbook(CreatePivotWorkbook());

        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            var pageField = document.Root!
                .Element(WorkbookNs + "pageFields")!
                .Element(WorkbookNs + "pageField")!;
            pageField.Attribute("name")?.Remove();
            pageField.SetAttributeValue("item", pageFieldItemIndex.ToString());
        });

        return package;
    }

    private static Workbook CreatePivotWorkbook()
    {
        var workbook = new Workbook("PivotPageFieldSharedItemShift");
        var sheet = workbook.AddSheet("PivotData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            SharedItemCount: 3,
            ContainsString: true,
            SharedItems: ["East", "West", "North"],
            SharedItemKinds: ['s', 's', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.PageFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }
}
