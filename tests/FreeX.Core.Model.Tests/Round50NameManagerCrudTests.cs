using FluentAssertions;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-50 findings R50-commands-name-manager-crud-3-3 and -3-4: a case-only rename of a
/// defined name must actually update the stored key casing, and deleting a sheet must leave any
/// defined name that referred to it in place with a "#REF!" refers-to (matching Excel's Name
/// Manager "Names with Errors" repair workflow) instead of silently deleting the name.
/// </summary>
public class Round50NameManagerCrudTests
{
    // ── R50-commands-name-manager-crud-3-3 ────────────────────────────────────

    [Fact]
    public void DefineNamedRange_CaseOnlyRename_UpdatesStoredKeyCasing()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 0, 0),
            new CellAddress(sheet.Id, 9, 0));

        wb.DefineNamedRange("revenue", range);

        // Re-define with only the casing changed (same range), exactly what NamedRangeDialog /
        // DefineNamedRangeCommand does for a pure-casing rename.
        wb.DefineNamedRange("Revenue", range);

        wb.NamedRanges.Should().ContainKey("Revenue");
        var storedKey = wb.NamedRanges.Keys.Single(k => string.Equals(k, "Revenue", StringComparison.OrdinalIgnoreCase));
        storedKey.Should().Be("Revenue", "the stored key text must reflect the user's rename, not the original casing");
    }

    [Fact]
    public void DefineNamedRange_DifferentNameRename_StillWorksAndOldNameRemoved()
    {
        // Sibling no-regression: a normal (different-name, not case-only) rename must still fully
        // replace the old entry with the new one.
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 0, 0),
            new CellAddress(sheet.Id, 9, 0));

        wb.DefineNamedRange("Revenue", range);
        wb.RemoveNamedRange("Revenue");
        wb.DefineNamedRange("TotalRevenue", range);

        wb.NamedRanges.Should().NotContainKey("Revenue");
        wb.NamedRanges.Should().ContainKey("TotalRevenue");
        wb.NamedRanges.Keys.Single(k => string.Equals(k, "TotalRevenue", StringComparison.OrdinalIgnoreCase))
            .Should().Be("TotalRevenue");
    }

    // ── R50-commands-name-manager-crud-3-4 ────────────────────────────────────

    [Fact]
    public void RemoveSheet_NameReferringToDeletedSheet_KeptAsRefError()
    {
        var wb = new Workbook();
        var keep = wb.AddSheet("Keep");
        var remove = wb.AddSheet("Remove");
        var range = new GridRange(
            new CellAddress(remove.Id, 0, 0),
            new CellAddress(remove.Id, 9, 0));
        wb.DefineNamedRange("SalesQ2", range);

        wb.RemoveSheet(remove.Id).Should().BeTrue();

        // Excel keeps the name in the Name Manager with RefersTo rewritten to #REF!, rather than
        // deleting it outright.
        wb.NamedRanges.Should().NotContainKey("SalesQ2");
        wb.NamedFormulas.Should().ContainKey("SalesQ2");
        wb.NamedFormulas["SalesQ2"].Should().Be("#REF!");
    }

    [Fact]
    public void RemoveSheet_NameNotReferringToDeletedSheet_IsUntouched()
    {
        // Sibling no-regression: a name that refers to a surviving sheet must not be touched by
        // deleting an unrelated sheet.
        var wb = new Workbook();
        var keep = wb.AddSheet("Keep");
        var remove = wb.AddSheet("Remove");
        var keepRange = new GridRange(
            new CellAddress(keep.Id, 0, 0),
            new CellAddress(keep.Id, 1, 0));
        wb.DefineNamedRange("KeepRange", keepRange);

        wb.RemoveSheet(remove.Id).Should().BeTrue();

        wb.NamedRanges.Should().ContainKey("KeepRange");
        wb.NamedRanges["KeepRange"].Should().Be(keepRange);
        wb.NamedFormulas.Should().NotContainKey("KeepRange");
    }
}
