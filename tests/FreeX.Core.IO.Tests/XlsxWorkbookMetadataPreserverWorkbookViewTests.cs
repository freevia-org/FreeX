using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for XlsxWorkbookMetadataPreserver.MergeWorkbookViews (R32-io-workbook-parts-deep-1):
///  - The primary (first) &lt;workbookView&gt;'s merge identity must not be keyed on its mutable
///    firstSheet/activeTab attributes. XlsxWorkbookMetadataWriter.ApplyWorkbookViewProperties always
///    rewrites the FIRST workbookView's activeTab to the workbook's CURRENT ActiveSheetIndex before
///    this preservation pass runs, while the cloned source view still carries the pre-edit value.
///    Keying the merge on those attributes meant the identity keys could never match after an
///    ordinary sheet-tab switch, so the stale source view was appended as a bogus second window on
///    every save from then on (and the corruption became permanent, since the just-written duplicate
///    is recaptured as the new pristine source for the next save). The fix matches the primary view
///    by POSITION (the first &lt;workbookView&gt; on both sides) instead.
///  - A genuine second window (Window &gt; New Window) must still be preserved: it lives at a
///    subsequent position that XlsxWorkbookMetadataWriter never touches, so it keeps using
///    identity-key matching (and falls back to being appended when the target has no corresponding
///    view, which is the common case since the model-driven rebuild only ever emits one view).
/// </summary>
public sealed class XlsxWorkbookMetadataPreserverWorkbookViewTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_AfterChangingActiveSheetIndex_DoesNotDuplicateWorkbookView()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        // The extremely common action this finding is about: switch the active sheet tab, then save.
        loaded.ActiveSheetIndex = 1;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var workbookViews = ReadWorkbookViews(saved);

        workbookViews.Should().ContainSingle(
            "switching the active sheet tab and saving must update the existing primary " +
            "<workbookView> in place, not append a stale second one keyed on the pre-edit " +
            "activeTab");
        workbookViews[0].Attribute("activeTab")!.Value.Should().Be(
            "1",
            "the single surviving workbookView must carry the NEW active tab, not the source's " +
            "stale pre-edit value");
    }

    [Fact]
    public void Preserve_WithGenuineSecondWorkbookView_KeepsBothViews()
    {
        // "Target": a freshly-rebuilt package for the CURRENT model (ClosedXML/full-save only ever
        // emits a single primary workbookView), with its activeTab already updated to the new
        // sheet-selection state - the same shape MergeWorkbookViews sees in the real bug scenario.
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        using var target = XlsxPackageTestHelper.SaveWorkbook(workbook);
        SetPrimaryWorkbookViewActiveTab(target, activeTab: 1);

        // "Source": the pristine pre-edit package, which additionally has a genuine SECOND window
        // (Window > New Window) - a real second <workbookView> with different window geometry, which
        // XlsxWorkbookMetadataWriter never touches and must survive the merge untouched.
        using var sourcePackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddSecondWorkbookView(sourcePackage, activeTab: 0, xWindow: 480);

        RunPreserve(sourcePackage, target, workbook);

        var workbookViews = ReadWorkbookViews(target);

        workbookViews.Should().HaveCount(
            2,
            "a genuine second window from the source file must be preserved alongside the " +
            "(position-matched, in-place-merged) primary view");
        workbookViews[0].Attribute("activeTab")!.Value.Should().Be(
            "1",
            "the primary view keeps the target's current active tab - it must not be reverted to " +
            "the source's stale value by the position-based merge");
        workbookViews[1].Attribute("xWindow")!.Value.Should().Be(
            "480",
            "the genuine second window is appended from the source, carrying its own window geometry");
    }

    [Fact]
    public void Preserve_WithGenuineSecondWorkbookViewSharingPrimaryActiveTab_KeepsBothViews()
    {
        // R33-meta-1: a genuine second window (Window > New Window) that happens to share the SAME
        // firstSheet/activeTab as the primary view (the common case - a new window opens showing
        // whatever sheet tab is currently active) must still be preserved as its own view, not
        // swallowed into the primary just because their identity keys collide.
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        // "Target": a freshly-rebuilt package for the CURRENT model. Its primary view's activeTab
        // (0) matches the source's pristine primary view below, so the primary merge is exercised
        // via a differing NON-modeled attribute (windowWidth) rather than via activeTab.
        using var target = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // "Source": the pristine pre-edit package, carrying window geometry on the primary view plus
        // a real second <workbookView> whose activeTab (0) is the SAME as the primary's.
        using var sourcePackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        SetPrimaryWorkbookViewWindowWidth(sourcePackage, windowWidth: 19200);
        AddSecondWorkbookView(sourcePackage, activeTab: 0, xWindow: 480);

        RunPreserve(sourcePackage, target, workbook);

        var workbookViews = ReadWorkbookViews(target);

        workbookViews.Should().HaveCount(
            2,
            "a genuine second window must survive even when its activeTab collides with the " +
            "primary view's - it must not be merged away as if it were the same window");
        workbookViews[0].Attribute("windowWidth")!.Value.Should().Be(
            "19200",
            "the primary view is still merged in place, picking up the source's window geometry");
        workbookViews[1].Attribute("xWindow")!.Value.Should().Be(
            "480",
            "the genuine second window is appended from the source, carrying its own window geometry");
    }

    [Fact]
    public void Preserve_WithSingleWorkbookView_StaysSingle()
    {
        // Sibling sanity check: when the source has only the primary view (no genuine second
        // window at all), the merge must stay a single view - the fix for R33-meta-1 must not
        // start manufacturing extra views out of nothing.
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");

        using var target = XlsxPackageTestHelper.SaveWorkbook(workbook);
        using var sourcePackage = XlsxPackageTestHelper.SaveWorkbook(workbook);
        SetPrimaryWorkbookViewWindowWidth(sourcePackage, windowWidth: 19200);

        RunPreserve(sourcePackage, target, workbook);

        var workbookViews = ReadWorkbookViews(target);

        workbookViews.Should().ContainSingle(
            "with no genuine second window in the source, the merge must not append anything extra");
        workbookViews[0].Attribute("windowWidth")!.Value.Should().Be(
            "19200",
            "the primary view is still merged in place, picking up the source's window geometry");
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    private static void RunPreserve(MemoryStream sourcePackage, MemoryStream target, Workbook workbook)
    {
        sourcePackage.Position = 0;
        target.Position = 0;
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(target, ZipArchiveMode.Update, leaveOpen: true);
        XlsxWorkbookMetadataPreserver.Preserve(
            sourceArchive,
            targetArchive,
            workbook,
            workbook.Sheets.Select(sheet => sheet.Id).ToArray());
    }

    private static List<XElement> ReadWorkbookViews(MemoryStream package)
    {
        var root = ReadWorkbookRoot(package);
        return root.Element(WorkbookNs + "bookViews")?
            .Elements(WorkbookNs + "workbookView")
            .ToList() ?? [];
    }

    private static XElement ReadWorkbookRoot(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        return XDocument.Load(stream).Root!;
    }

    /// <summary>
    /// Simulates XlsxWorkbookMetadataWriter.ApplyWorkbookViewProperties having already rewritten the
    /// primary (first) workbookView's activeTab to the workbook's current ActiveSheetIndex, which
    /// always runs before XlsxWorkbookMetadataPreserver.Preserve in the real save pipeline.
    /// </summary>
    private static void SetPrimaryWorkbookViewActiveTab(MemoryStream package, int activeTab)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
                workbookXml = XDocument.Load(entryStream);

            var primaryView = workbookXml.Root!
                .Element(WorkbookNs + "bookViews")!
                .Elements(WorkbookNs + "workbookView")
                .First();
            primaryView.SetAttributeValue("activeTab", activeTab.ToString(System.Globalization.CultureInfo.InvariantCulture));

            entry.Delete();
            var replacement = archive.CreateEntry("xl/workbook.xml");
            using var replacementStream = replacement.Open();
            workbookXml.Save(replacementStream, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }

    /// <summary>
    /// Sets a non-modeled window-geometry attribute on the primary workbookView, so a real merge
    /// (not a raw-text no-op skip) is exercised on the primary view without touching its
    /// firstSheet/activeTab identity attributes.
    /// </summary>
    private static void SetPrimaryWorkbookViewWindowWidth(MemoryStream package, int windowWidth)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
                workbookXml = XDocument.Load(entryStream);

            var primaryView = workbookXml.Root!
                .Element(WorkbookNs + "bookViews")!
                .Elements(WorkbookNs + "workbookView")
                .First();
            primaryView.SetAttributeValue("windowWidth", windowWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));

            entry.Delete();
            var replacement = archive.CreateEntry("xl/workbook.xml");
            using var replacementStream = replacement.Open();
            workbookXml.Save(replacementStream, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }

    /// <summary>
    /// Injects a real second &lt;workbookView&gt; (as Excel writes for Window &gt; New Window) into a
    /// package's pristine workbook.xml.
    /// </summary>
    private static void AddSecondWorkbookView(MemoryStream package, int activeTab, int xWindow)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
                workbookXml = XDocument.Load(entryStream);

            var bookViews = workbookXml.Root!.Element(WorkbookNs + "bookViews")!;
            bookViews.Add(new XElement(
                WorkbookNs + "workbookView",
                new XAttribute("xWindow", xWindow),
                new XAttribute("activeTab", activeTab)));

            entry.Delete();
            var replacement = archive.CreateEntry("xl/workbook.xml");
            using var replacementStream = replacement.Open();
            workbookXml.Save(replacementStream, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }
}
