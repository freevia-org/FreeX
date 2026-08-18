using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R144-io-defined-names-83-1 (finding F1): a user-created defined name
/// whose text happens to collide with the BARE (unprefixed) text of an Excel built-in name --
/// "Print_Area", "Print_Titles", "_FilterDatabase" -- used to be silently dropped by
/// <see cref="XlsxNamedRangeMapper"/> on save and refused on load, even though
/// <see cref="Workbook.ValidateNamedRangeName"/> (the validator every Name Manager/Define Name UI
/// consults) has always allowed it: only names starting with the "_xlnm."/"_xlchart." PREFIX are
/// genuinely Excel-reserved (see Workbook.cs's HasReservedExcelPrefix). ClosedXML itself never
/// surfaces the real built-in through <c>IXLDefinedNames</c> either -- Print_Area/Print_Titles are
/// consumed straight into <c>PageSetup</c> and never appear in DefinedNames at all, and
/// _FilterDatabase appears (when it does) still fully prefixed as "_xlnm._FilterDatabase" -- so a
/// bare-text defined name reaching the mapper is, by construction, always an ordinary user name.
/// </summary>
public sealed class R144_DefinedNameBareReservedWordCollisionTests
{
    [Theory]
    [InlineData("Print_Area")]
    [InlineData("Print_Titles")]
    [InlineData("_FilterDatabase")]
    public void SaveThenLoad_RoundTripsUserDefinedNameThatCollidesWithBareReservedWord(string name)
    {
        // Arrange: an ordinary user-created workbook-scoped defined name whose text happens to be
        // one of the bare reserved words. Workbook.ValidateNamedRangeName allows this (it only
        // rejects the "_xlnm."/"_xlchart." PREFIX forms), matching what the live Name Manager UI
        // would let the user create.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ValidateNamedRangeName(name).Should().BeNull(
            "the validator must allow this bare text -- it is not one of the reserved PREFIX forms");

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1));
        workbook.DefineNamedRange(name, range);

        // Act: save through the real production entry point (XlsxFileAdapter.Save ->
        // XlsxNamedRangeMapper.Save -> SaveWorkbookDefinedName) and reload
        // (XlsxFileAdapter.Load -> XlsxNamedRangeMapper.Load -> LoadDefinedNames).
        using var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedSheet = loaded.GetSheet("Sheet1")!;

        // Assert: the name and its range survived the round trip -- it was not silently dropped.
        loaded.NamedRanges.Should().ContainKey(name,
            $"a user-created defined name literally called '{name}' must round-trip like any " +
            "other ordinary name instead of being silently discarded as if it were Excel-reserved");
        loaded.NamedRanges[name].Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 1, 1)));
    }

    [Fact]
    public void SaveThenLoad_UserPrintAreaNameCoexistsWithTheRealPrintArea()
    {
        // Sibling/no-regression case: the genuine Print Area feature (Sheet.PrintAreas, backed by
        // ClosedXML's PageSetup.PrintAreas / the true "_xlnm.Print_Area" built-in name) must keep
        // working correctly even while an unrelated ordinary defined name that happens to be
        // spelled "Print_Area" also exists in the same workbook -- proving the fix did not merely
        // stop dropping the bare name by accident breaking the real print-area round-trip.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var realPrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3));
        sheet.SetPrintAreas([realPrintArea]);

        var userNamedRange = new GridRange(
            new CellAddress(sheet.Id, 10, 10),
            new CellAddress(sheet.Id, 10, 10));
        workbook.DefineNamedRange("Print_Area", userNamedRange);

        using var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedSheet = loaded.GetSheet("Sheet1")!;

        loadedSheet.PrintAreas.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 5, 3)));

        loaded.NamedRanges.Should().ContainKey("Print_Area");
        loaded.NamedRanges["Print_Area"].Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 10, 10),
            new CellAddress(loadedSheet.Id, 10, 10)));
    }

    [Theory]
    [InlineData("_xlnm.Foo")]
    [InlineData("_xlnm.Print_Area")]
    [InlineData("_xlchart.Bar")]
    public void IsExcelReservedDefinedName_StillTreatsPrefixedBuiltInsAsReserved(string name)
    {
        // Sibling coverage for the part of IsExcelReservedDefinedName the fix must NOT change: any
        // name with the genuine "_xlnm."/"_xlchart." prefix is still reserved.
        XlsxNamedRangeMapper.IsExcelReservedDefinedName(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("Print_Area")]
    [InlineData("Print_Titles")]
    [InlineData("_FilterDatabase")]
    [InlineData("Revenue")]
    public void IsExcelReservedDefinedName_NoLongerTreatsBareWordsAsReserved(string name)
    {
        XlsxNamedRangeMapper.IsExcelReservedDefinedName(name).Should().BeFalse();
    }
}
