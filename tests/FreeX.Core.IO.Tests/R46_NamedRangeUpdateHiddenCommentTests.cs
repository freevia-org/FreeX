using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R46-applied-not-persisted-sweep-1: the patch-save defined-name UPDATE
/// branch in <see cref="XlsxNamedRangeMapper.SaveToPackage"/> (an existing &lt;definedName&gt;
/// element is found by key) only compared/updated the RefersTo text and never touched the
/// hidden/comment attributes, unlike the sibling NEW-element branch a few lines below it. So
/// editing ONLY the Comment (or Hidden flag) of an already-on-disk named range via Name Manager,
/// then saving through the fast patch-save path, silently discarded the edit.
/// </summary>
public sealed class R46_NamedRangeUpdateHiddenCommentTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void SaveToPackage_ExistingNamedRange_CommentEditedOnly_UpdatesCommentAttribute()
    {
        // Arrange: a named range that already exists on disk (from a prior full save) with no
        // comment/hidden attributes at all - mirroring a plain range defined in Excel's Name
        // Manager with no comment.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        workbook.DefineNamedRange("SalesRegion", range);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        ReadDefinedName(package, "SalesRegion")!.Attribute("comment").Should().BeNull(
            "the range must start out commentless, matching the failure scenario's pre-edit state");

        // Act: the user edits ONLY the Comment via Name Manager (DefineNamedRangeCommand), which
        // updates the live model metadata for the SAME already-existing name/range - then an
        // ordinary edit elsewhere triggers the fast patch-save path, which calls SaveToPackage
        // directly against the same on-disk package (XlsxFileAdapter.Save.cs:82).
        workbook.DefineNamedRange("SalesRegion", range, new NamedRangeMetadata("Workbook", "Q3 territory list"));
        XlsxNamedRangeMapper.SaveToPackage(workbook, package);

        // Assert: the comment edit must survive the patch-save update branch, not be silently
        // dropped because only Value/RefersTo was compared before the fix.
        var definedName = ReadDefinedName(package, "SalesRegion");
        definedName.Should().NotBeNull();
        definedName!.Attribute("comment").Should().NotBeNull(
            "editing only the Comment of an already-existing named range must persist through the " +
            "patch-save UPDATE branch, mirroring the NEW-element branch which already sets it");
        definedName.Attribute("comment")!.Value.Should().Be("Q3 territory list");
    }

    [Fact]
    public void SaveToPackage_ExistingNamedRange_HiddenEditedOnly_UpdatesHiddenAttribute()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        workbook.DefineNamedRange("SalesRegion", range);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // Act: mark the existing name Hidden via Name Manager without touching the address.
        workbook.DefineNamedRange("SalesRegion", range, new NamedRangeMetadata("Workbook", "", Hidden: true));
        XlsxNamedRangeMapper.SaveToPackage(workbook, package);

        var definedName = ReadDefinedName(package, "SalesRegion");
        definedName.Should().NotBeNull();
        definedName!.Attribute("hidden").Should().NotBeNull(
            "toggling Hidden on an already-existing named range must persist through the patch-save " +
            "UPDATE branch");
        definedName.Attribute("hidden")!.Value.Should().Be("1");
    }

    [Fact]
    public void SaveToPackage_ExistingNamedRange_AddressOnlyEdit_LeavesUnrelatedCommentUntouched()
    {
        // Sibling/no-regression case: a named range that already carries a comment/hidden flag on
        // disk (from a prior save) gets its ADDRESS changed but its metadata is untouched in the
        // model. The update branch must still correctly refresh RefersTo while leaving the
        // (unchanged) comment/hidden attributes exactly as they were - i.e. the fix must not
        // spuriously touch attributes that legitimately did not change.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var originalRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1));
        workbook.DefineNamedRange(
            "SalesRegion",
            originalRange,
            new NamedRangeMetadata("Workbook", "Original note", Hidden: true));

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        ReadDefinedName(package, "SalesRegion")!.Attribute("comment")!.Value.Should().Be("Original note");
        ReadDefinedName(package, "SalesRegion")!.Attribute("hidden")!.Value.Should().Be("1");

        // Act: only the range address changes (e.g. via Name Manager's "Refers to" field); the
        // Comment/Hidden metadata for the same name is re-supplied unchanged.
        var widerRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 1));
        workbook.DefineNamedRange(
            "SalesRegion",
            widerRange,
            new NamedRangeMetadata("Workbook", "Original note", Hidden: true));
        XlsxNamedRangeMapper.SaveToPackage(workbook, package);

        var definedName = ReadDefinedName(package, "SalesRegion");
        definedName.Should().NotBeNull();
        definedName!.Value.Should().Contain("$A$20", "the RefersTo text must reflect the new address");
        definedName.Attribute("comment")!.Value.Should().Be(
            "Original note", "an unrelated address-only edit must not disturb the existing comment");
        definedName.Attribute("hidden")!.Value.Should().Be(
            "1", "an unrelated address-only edit must not disturb the existing hidden flag");
    }

    private static XElement? ReadDefinedName(MemoryStream package, string name)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        var result = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .FirstOrDefault(element => element.Attribute("name")?.Value == name);
        package.Position = 0;
        return result;
    }
}
