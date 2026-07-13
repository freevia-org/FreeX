using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R40-io-sheet-tabcolor-visibility-3-1: XlsxWorkbookMetadataWriter must never
/// write a &lt;workbookView&gt; activeTab/firstSheet that points at a hidden or veryHidden sheet.
/// Real Excel treats the active tab as always being a visible sheet; an activeTab pointing at a
/// hidden sheet is either silently corrected or flags the file for repair on open. A workbook whose
/// modeled ActiveSheetIndex points at a hidden sheet (e.g. via hide -> unhide -> undo leaving the
/// model's ActiveSheetIndex stale, per the finding's failure scenario) must be redirected to the
/// first visible sheet on save, while a normal visible activeTab is left completely untouched.
/// </summary>
public sealed class R40_SheetVisibilityActiveTabTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_WithActiveSheetIndexPointingAtHiddenSheet_RedirectsActiveTabToFirstVisibleSheet()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.IsHidden = true;

        // Simulates the model ending up with ActiveSheetIndex pointing at a sheet that has since
        // been hidden (e.g. the hide -> unhide -> undo sequence from the finding, where the undo of
        // SetSheetHiddenCommand restores IsHidden but never touches ActiveSheetIndex).
        workbook.ActiveSheetIndex = 1;
        workbook.FirstVisibleSheetIndex = 1;

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var primaryView = ReadPrimaryWorkbookView(saved);

        primaryView.Attribute("activeTab")?.Value.Should().Be(
            "0",
            "Sheet2 (index 1) is hidden, so Excel would flag the file for repair if activeTab kept " +
            "pointing at it - the writer must redirect to the first visible sheet (Sheet1, index 0)");
        primaryView.Attribute("firstSheet")?.Value.Should().Be(
            "0",
            "firstSheet must be redirected the same way as activeTab when it points at a hidden sheet");
    }

    [Fact]
    public void Save_WithActiveSheetIndexPointingAtVisibleSheet_LeavesActiveTabUnchanged()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.IsHidden = false;

        workbook.ActiveSheetIndex = 1;

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var primaryView = ReadPrimaryWorkbookView(saved);

        primaryView.Attribute("activeTab")?.Value.Should().Be(
            "1",
            "Sheet2 is visible, so a normal active-tab selection must be written verbatim and not " +
            "redirected");
    }

    [Fact]
    public void Save_WithActiveSheetIndexPointingAtVeryHiddenSheet_RedirectsActiveTabToFirstVisibleSheet()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.IsVeryHidden = true;

        workbook.ActiveSheetIndex = 1;

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var primaryView = ReadPrimaryWorkbookView(saved);

        primaryView.Attribute("activeTab")?.Value.Should().Be(
            "0",
            "a veryHidden sheet must be treated the same as a hidden one - redirect to the first " +
            "visible sheet rather than leaving an invalid activeTab");
    }

    [Fact]
    public void Save_WithFirstSheetHiddenButLaterSheetVisible_RedirectsToFirstVisibleSheetInOrder()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.IsHidden = true;
        workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        sheet3.IsHidden = true;

        // Model somehow ends up pointing at the very-hidden Sheet3 - redirect must scan forward from
        // the start and land on Sheet2 (index 1), the first visible sheet in document order.
        workbook.ActiveSheetIndex = 2;

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var primaryView = ReadPrimaryWorkbookView(saved);

        primaryView.Attribute("activeTab")?.Value.Should().Be(
            "1",
            "Sheet2 (index 1) is the first visible sheet in document order and must be the redirect " +
            "target, not merely 'any' visible sheet");
    }

    private static XElement ReadPrimaryWorkbookView(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        return root.Element(WorkbookNs + "bookViews")!
            .Elements(WorkbookNs + "workbookView")
            .First();
    }
}
