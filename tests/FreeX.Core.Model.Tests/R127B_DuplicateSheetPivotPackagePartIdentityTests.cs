using System.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R127B (r127 ScopeAudit): the landed R127 fix (see
/// <see cref="R127_DuplicateSheetPivotCacheIdentityTests"/>) gave a duplicated same-sheet pivot its
/// own, independent <see cref="PivotCacheModel"/> instance -- but two sibling fields of that exact
/// "must not share identity with the source" pattern were left copying <c>PackagePart</c> VERBATIM
/// from the source object onto the clone:
/// <list type="bullet">
/// <item><see cref="Sheet.Clone"/>'s ClonePivotTable (Sheet.Clone.cs) copied
/// <see cref="PivotTableModel.PackagePart"/> from the source pivot onto the copy's pivot.</item>
/// <item><see cref="DuplicateSheetCommand"/>'s CloneRedirectedPivotCache (DuplicateSheetCommand.cs)
/// copied <see cref="PivotCacheModel.PackagePart"/> from the source cache onto the copy's cache.</item>
/// </list>
/// PackagePart is the exact archive path (e.g. "xl/pivotTables/pivotTable1.xml") the source object was
/// loaded from/last saved to. Leaving two distinct model objects claiming the identical path means
/// XlsxFileAdapter's patch-save eligibility guard (which keys a dictionary by this path across every
/// pivot table on a sheet, and across ALL of workbook.PivotCaches) throws a duplicate-key
/// ArgumentException the next time ANY sheet's patch-save eligibility is checked -- silently
/// downgrading every subsequent save of the whole workbook to the slow full-regenerate path.
/// The fix: a cloned pivot table/cache must start with an EMPTY PackagePart, exactly like a
/// brand-new pivot table/cache that has never been saved -- both the patch-save guard and the
/// full-write path already tolerate/expect that state gracefully.
/// </summary>
public sealed class R127B_DuplicateSheetPivotPackagePartIdentityTests
{
    [Fact]
    public void DuplicateSheet_SameSheetPivotCache_ClonedCache_DoesNotShareSourcePackagePart()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 2));
        var targetRange = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 5, 7));

        var originalCache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Sheet1",
            SourceReference = sourceRange.ToString(),
            // Mirrors what XlsxPivotCacheReader actually assigns when loading a real workbook.
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        workbook.PivotCaches.Add(originalCache);

        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange,
            // Mirrors what XlsxPivotTableReader actually assigns when loading a real workbook.
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedPivot = copy.PivotTables.Should().ContainSingle().Subject;
        var copiedCache = workbook.PivotCaches.Should().Contain(c => c.CacheId == copiedPivot.CacheId).Subject;

        // The core defect: the clone must not carry the source's exact package-part path forward.
        copiedCache.PackagePart.Should().NotBe(originalCache.PackagePart,
            because: "two PivotCacheModel entries sharing one package-part path collide in " +
                     "XlsxFileAdapter's patch-save eligibility dictionary");
        copiedCache.PackagePart.Should().BeEmpty(
            because: "a freshly-cloned cache has no saved package identity yet, matching a brand-new pivot cache");

        // The original must be completely untouched.
        originalCache.PackagePart.Should().Be("xl/pivotCache/pivotCacheDefinition1.xml");

        // The actual collision-preventing property: no two entries in workbook.PivotCaches share a
        // non-blank PackagePart (this is exactly what XlsxFileAdapter.SourcePackageSnapshot's
        // ToDictionary(cache => NormalizePivotPackagePart(cache.PackagePart)) requires to not throw).
        var nonBlankCacheParts = workbook.PivotCaches
            .Select(c => c.PackagePart)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        nonBlankCacheParts.Should().OnlyHaveUniqueItems(
            because: "a duplicate non-blank PackagePart across workbook.PivotCaches throws a " +
                     "duplicate-key ArgumentException in the patch-save eligibility guard");
    }

    [Fact]
    public void SheetClone_ClonedPivotTable_DoesNotShareSourcePackagePart()
    {
        // R127B-model-pivot-clone-packagepart: exercises Sheet.Clone's ClonePivotTable directly (the
        // only production caller of Sheet.Clone(SheetId, string) is DuplicateSheetCommand, so this
        // goes through the real, single production entry point for a cloned Sheet). Uses a
        // CROSS-sheet-sourced pivot (SourceRange points at a different sheet) deliberately: that path
        // correctly keeps SHARING the original PivotCacheModel (see DuplicateSheetCommand's
        // CloneOwnedPivotCaches "Cross-sheet source" gate), so PivotTableModel.PackagePart clearing on
        // the pivot itself must hold independently of that gate, proving the fix in Sheet.Clone.cs is
        // not merely incidental to the DuplicateSheetCommand-level cache-cloning fix.
        var workbook = new Workbook("test");
        var dataSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("PivotSheet");

        var sourceRange = new GridRange(
            new CellAddress(dataSheet.Id, 1, 1),
            new CellAddress(dataSheet.Id, 10, 2));
        var targetRange = new GridRange(
            new CellAddress(pivotSheet.Id, 1, 1),
            new CellAddress(pivotSheet.Id, 5, 3));

        var pivot = new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange,
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivotSheet.PivotTables.Add(pivot);

        var copy = pivotSheet.Clone(SheetId.New(), "PivotSheet Copy");

        var copiedPivot = copy.PivotTables.Should().ContainSingle().Subject;
        copiedPivot.PackagePart.Should().NotBe(pivot.PackagePart,
            because: "two PivotTableModel entries sharing one package-part path collide in " +
                     "XlsxFileAdapter's patch-save eligibility dictionary");
        copiedPivot.PackagePart.Should().BeEmpty(
            because: "a freshly-cloned pivot table has no saved package identity yet, matching a " +
                     "brand-new pivot table");

        // The source pivot must be completely untouched.
        pivot.PackagePart.Should().Be("xl/pivotTables/pivotTable1.xml");

        // Cross-sheet SourceRange must still travel unchanged (the sibling behavior this fix must not
        // break -- Sheet.Clone only remaps a same-sheet SourceRange).
        copiedPivot.SourceRange.Should().Be(pivot.SourceRange);
        copiedPivot.CacheId.Should().Be(pivot.CacheId);
    }

    [Fact]
    public void DuplicateSheet_MultiplePivotsOnSameSheet_NoTwoClonedObjectsShareNonEmptyPackagePart()
    {
        // No-regression sibling: a sheet with TWO independent same-sheet pivot tables/caches (the
        // ordinary "two separate pivots on one sheet" shape) must still duplicate cleanly -- every
        // cloned pivot/cache gets its own blank PackagePart, and the ALREADY-landed R127 behavior
        // (independent CacheId per clone, correct SourceSheetName rebasing) must still hold alongside
        // this fix.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var range1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var range2 = new GridRange(new CellAddress(sheet.Id, 1, 10), new CellAddress(sheet.Id, 5, 11));
        var target1 = new GridRange(new CellAddress(sheet.Id, 1, 20), new CellAddress(sheet.Id, 5, 22));
        var target2 = new GridRange(new CellAddress(sheet.Id, 1, 30), new CellAddress(sheet.Id, 5, 32));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Sheet1",
            SourceReference = range1.ToString(),
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        });
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 2,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Sheet1",
            SourceReference = range2.ToString(),
            PackagePart = "xl/pivotCache/pivotCacheDefinition2.xml",
        });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1", CacheId = 1, SourceRange = range1, TargetRange = target1,
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot2", CacheId = 2, SourceRange = range2, TargetRange = target2,
            PackagePart = "xl/pivotTables/pivotTable2.xml",
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        copy.PivotTables.Should().HaveCount(2);
        workbook.PivotCaches.Should().HaveCount(4);

        // Already-landed R127 behavior: each clone gets its own CacheId, distinct from both the
        // source's and each other's.
        copy.PivotTables.Select(p => p.CacheId).Should().OnlyHaveUniqueItems();

        // This fix's property: across every pivot table in the workbook (both sheets) and every
        // cache, no two distinct objects share a non-blank PackagePart.
        var allPivotParts = workbook.Sheets
            .SelectMany(s => s.PivotTables)
            .Select(p => p.PackagePart)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        allPivotParts.Should().OnlyHaveUniqueItems();

        var allCacheParts = workbook.PivotCaches
            .Select(c => c.PackagePart)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        allCacheParts.Should().OnlyHaveUniqueItems();

        // Both clones came from real (previously-saved) sources, so both must have been reset to
        // blank -- not merely "not equal to the source" by coincidence.
        copy.PivotTables.Should().OnlyContain(p => p.PackagePart == string.Empty);
        workbook.PivotCaches.Where(c => c.CacheId is 3 or 4)
            .Should().OnlyContain(c => c.PackagePart == string.Empty);
    }
}
