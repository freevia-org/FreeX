using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 115: CT_CustomSheetView's nested &lt;pageSetup&gt; scale vs
/// fitToWidth/fitToHeight ambiguity was not being resolved against the sibling
/// customSheetView/@fitToPage flag the way the main worksheet's sheetPr/pageSetUpPr/@fitToPage
/// resolves the identical ambiguity for ClosedXML's PagesWide/PagesTall (see XlsxFileAdapter.cs).
/// Excel is known to leave the inactive mode's attribute(s) present-but-stale in the XML (this
/// codebase already documents the identical staleness pattern for firstPageNumber/
/// useFirstPageNumber), so a real file's customSheetView can carry scale="80" together with
/// fitToWidth="1" fitToHeight="1" even though fitToPage="0" means "scale mode, ignore
/// fitToWidth/fitToHeight". Before the fix, XlsxCustomViewMapper.ParseScaleToFit read all three
/// attributes unconditionally, producing an ambiguous WorksheetScaleToFit(80, 1, 1); on save,
/// ToPageSetupXml then unconditionally preferred FitToPagesWide/FitToPagesTall over ScalePercent,
/// silently discarding the true 80% scale and re-emitting fitToWidth/fitToHeight instead, while
/// leaving customSheetView/@fitToPage omitted (since state.FitToPage was false) -- an internally
/// contradictory file Excel would reopen at 100% default scale.
///
/// A hand-authored XML fixture is unavoidable here for the read-side tests: the defect is that our
/// own writer, once fixed, NEVER itself produces the ambiguous scale+fitToWidth+fitToHeight
/// combination the reader must tolerate (that combination only arises from Excel's own documented
/// "leaves the inactive mode's attributes behind" quirk on external files). Testing the reader
/// against our own writer's output would therefore never be able to observe the bug, so this
/// mirrors the R19/R27 precedent of driving XlsxCustomViewMapper.ReadWorksheetViews directly with a
/// synthetic worksheet XML representing that real, externally-authored quirk.
/// </summary>
public sealed class R115_CustomViewScaleToFitFitToPageAmbiguityTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XDocument BuildWorksheetXml(string? fitToPageAttribute, string scale, string fitToWidth, string fitToHeight) =>
        new(
            new XElement(
                WorksheetNs + "worksheet",
                new XElement(
                    WorksheetNs + "customSheetViews",
                    new XElement(
                        WorksheetNs + "customSheetView",
                        new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                        fitToPageAttribute is null ? null : new XAttribute("fitToPage", fitToPageAttribute),
                        new XAttribute("state", "visible"),
                        new XElement(
                            WorksheetNs + "pageSetup",
                            new XAttribute("scale", scale),
                            new XAttribute("fitToWidth", fitToWidth),
                            new XAttribute("fitToHeight", fitToHeight))))));

    [Fact]
    public void ReadWorksheetViews_StaleFitToWidthHeight_WithFitToPageFalse_ResolvesToScaleOnly()
    {
        // customSheetView/@fitToPage="0" (explicit scale mode) with stale fitToWidth/fitToHeight
        // left over alongside the authoritative scale="80" -- the exact quirk described in the
        // finding. Before the fix this produced WorksheetScaleToFit(80, 1, 1) (all three fields
        // populated); after the fix only ScalePercent should survive.
        var worksheetXml = BuildWorksheetXml(fitToPageAttribute: "0", scale: "80", fitToWidth: "1", fitToHeight: "1");

        var views = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, WorksheetNs);

        var state = views.Should().ContainSingle().Subject.State;
        state.FitToPage.Should().BeFalse();
        state.ScaleToFit.Should().Be(new WorksheetScaleToFit(80, null, null));
    }

    [Fact]
    public void ReadWorksheetViews_FitToPageOmitted_WithStaleScaleAndFitToAttributes_DefaultsToScaleOnly()
    {
        // customSheetView/@fitToPage entirely omitted (schema default is false, exactly like the
        // sheetPr/pageSetUpPr/@fitToPage case this codebase already documents) must resolve the same
        // way as an explicit fitToPage="0".
        var worksheetXml = BuildWorksheetXml(fitToPageAttribute: null, scale: "80", fitToWidth: "1", fitToHeight: "1");

        var views = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, WorksheetNs);

        var state = views.Should().ContainSingle().Subject.State;
        state.FitToPage.Should().BeNull();
        state.ScaleToFit.Should().Be(new WorksheetScaleToFit(80, null, null));
    }

    [Fact]
    public void ReadWorksheetViews_FitToPageTrue_WithStaleScaleAttribute_ResolvesToFitToPagesOnly()
    {
        // The mirror-image case: fitToPage="1" (fit-to-page mode) with a stale leftover scale
        // attribute alongside the authoritative fitToWidth/fitToHeight. ScalePercent must be
        // dropped so a subsequent save doesn't resurrect the stale scale.
        var worksheetXml = BuildWorksheetXml(fitToPageAttribute: "1", scale: "55", fitToWidth: "2", fitToHeight: "3");

        var views = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, WorksheetNs);

        var state = views.Should().ContainSingle().Subject.State;
        state.FitToPage.Should().BeTrue();
        state.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 2, 3));
    }

    [Fact]
    public void XlsxFileAdapter_Load_CustomViewStaleFitToWidthHeight_ResolvesScaleOnlyInModel()
    {
        // Full end-to-end through the real production entry point: XlsxFileAdapter.Load reads a
        // synthesized package whose customSheetView carries the same stale-attribute quirk
        // (fitToPage="0" alongside scale="80" AND stale fitToWidth="1" fitToHeight="1"), and the
        // resulting in-memory WorksheetCustomViewState.ScaleToFit must resolve to scale-only. This
        // is the value CustomViewCommands.ApplyState (View &gt; Custom Views &gt; Show) copies
        // straight onto Sheet.ScaleToFit -- see
        // ShowCustomView_StaleScaleWithFitToPageTrue_AppliesFitToPagesNotStaleScale below for proof
        // that an ambiguous value reaching that copy previously produced the wrong live print mode.
        //
        // A re-save of this exact workbook is deliberately NOT asserted on here: this file's
        // customSheetView underwent a real load-from-package, so XlsxWorksheetMetadataPreserver's
        // native-attribute merge (a separate preservation layer, not this fix's target -- see
        // siblingLeads) will legitimately copy the original source's raw scale/fitToWidth/
        // fitToHeight attributes back onto the re-saved element for anything the modeled writer
        // left out, independent of whether ParseScaleToFit resolves the ambiguity. That merge
        // behavior does not by itself resurrect the actual bug (Excel still reads fitToPage="0" as
        // "use scale, ignore the stale siblings"), so it is out of scope for this fix and is not a
        // reliable pre/post-fix discriminator to assert on.
        var workbook = new Workbook("CustomViewScaleAmbiguityTest");
        workbook.AddSheet("Data");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddCustomViewWithAmbiguousPageSetup(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var state = loaded.CustomViews.Should().ContainSingle().Subject
            .Sheets.Should().ContainSingle().Subject;
        state.FitToPage.Should().BeFalse();
        state.ScaleToFit.Should().Be(new WorksheetScaleToFit(80, null, null));
    }

    [Fact]
    public void ShowCustomView_StaleScaleWithFitToPageTrue_AppliesFitToPagesNotStaleScale()
    {
        // Full end-to-end through the real production entry points for the "View > Custom Views >
        // Show" reachability path the finding describes: a custom view whose true mode is
        // fit-to-page (fitToPage="1") but whose <pageSetup> carries a stale leftover scale="55"
        // attribute alongside the authoritative fitToWidth="2"/fitToHeight="3". Before the fix,
        // ParseScaleToFit produced an ambiguous WorksheetScaleToFit(55, 2, 3); copying that onto
        // Sheet.ScaleToFit (mirroring CustomViewCommands.ApplyState, XlsxCustomViewMapper.cs is not
        // the owner of CustomViewCommands.cs so the copy is inlined here rather than taking a
        // FreeX.Core.Commands dependency) made XlsxFileAdapter.Save.cs's real ClosedXML write path
        // pick the stale scale=55 (its "ScalePercent wins" branch fires whenever ScalePercent is
        // non-null, regardless of the true fit-to-page intent) -- silently discarding the intended
        // 2x3 page-fit and applying a completely unrelated 55% scale instead. After the fix,
        // ParseScaleToFit resolves the ambiguity up front to WorksheetScaleToFit(null, 2, 3), so the
        // real write/read-back path ends up applying FitToPages(2, 3) as intended.
        var workbook = new Workbook("ShowCustomViewStaleScaleTest");
        workbook.AddSheet("Data");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddCustomViewWithStaleScaleFitToPageTrue(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var state = loaded.CustomViews.Should().ContainSingle().Subject
            .Sheets.Should().ContainSingle().Subject;

        // Mirrors CustomViewCommands.ApplyState (CustomViewCommands.cs:189-192) copying the
        // captured custom-view print state directly onto the live sheet when the view is shown.
        var sheet = loaded.Sheets.Should().ContainSingle().Subject;
        if (state.ScaleToFit is { } scaleToFit)
            sheet.ScaleToFit = scaleToFit;
        if (state.FitToPage is { } fitToPage)
            sheet.FitToPage = fitToPage;

        var afterShow = new MemoryStream();
        adapter.Save(loaded, afterShow);
        afterShow.Position = 0;
        var reloaded = adapter.Load(afterShow);

        var reloadedSheet = reloaded.Sheets.Should().ContainSingle().Subject;
        reloadedSheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 2, 3),
            "the true fit-to-page mode (fitToWidth=2/fitToHeight=3) must be applied, not the stale scale=55 leftover");
    }

    [Fact]
    public void ToCustomSheetViewXml_StaleFitToPageFlag_DerivesFitToPageAttributeFromScaleToFit()
    {
        // Covers the sibling "capture a NEW custom view from the live sheet" path
        // (CustomViewStatePlanner.CaptureSheetState): Sheet.FitToPage is a load-time flag the Page
        // Setup dialog's scale/fit-to-page toggle never updates (see
        // XlsxWorksheetPageSetupMetadataWriter), so it can disagree with the sheet's actual current
        // ScaleToFit by the time a custom view is captured. The written customSheetView/@fitToPage
        // must follow ScaleToFit (the authoritative, always-unambiguous field), not the possibly
        // stale FitToPage flag, so the two never contradict each other in the saved XML.
        var state = new WorksheetCustomViewState(
            "Data",
            WorksheetViewMode.Normal,
            FrozenRows: 0,
            FrozenCols: 0,
            SplitRow: null,
            SplitColumn: null,
            ScaleToFit: new WorksheetScaleToFit(80, null, null),
            FitToPage: true); // stale: sheet was already back in scale mode when the view was captured

        var workbook = new Workbook("CustomViewCaptureStaleFlagTest");
        workbook.ActiveSheetIndex = 0;
        workbook.AddSheet("Data");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Review",
            [state],
            Id: "{33333333-3333-3333-3333-333333333333}",
            ActiveSheetIndex: 0));

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var worksheetXml = XDocument.Load(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var customSheetView = worksheetXml.Root!
            .Element(WorksheetNs + "customSheetViews")!
            .Element(WorksheetNs + "customSheetView")!;

        customSheetView.Attribute("fitToPage").Should().BeNull("ScaleToFit's ScalePercent is set, so the effective mode is scale, not fit-to-page");

        var pageSetup = customSheetView.Element(WorksheetNs + "pageSetup")!;
        pageSetup.Attribute("scale")!.Value.Should().Be("80");
        pageSetup.Attribute("fitToWidth").Should().BeNull();
        pageSetup.Attribute("fitToHeight").Should().BeNull();
    }

    [Fact]
    public void ReadWorksheetViews_UnambiguousScaleOnly_StillRoundTrips_NoRegression()
    {
        // Sibling no-regression check: the ordinary, non-ambiguous case (only scale present, no
        // fitToWidth/fitToHeight at all, fitToPage omitted) must keep working exactly as before.
        var worksheetXml = new XDocument(
            new XElement(
                WorksheetNs + "worksheet",
                new XElement(
                    WorksheetNs + "customSheetViews",
                    new XElement(
                        WorksheetNs + "customSheetView",
                        new XAttribute("guid", "{44444444-4444-4444-4444-444444444444}"),
                        new XAttribute("state", "visible"),
                        new XElement(
                            WorksheetNs + "pageSetup",
                            new XAttribute("scale", "75"))))));

        var views = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, WorksheetNs);

        var state = views.Should().ContainSingle().Subject.State;
        state.FitToPage.Should().BeNull();
        state.ScaleToFit.Should().Be(new WorksheetScaleToFit(75, null, null));
    }

    [Fact]
    public void ReadWorksheetViews_UnambiguousFitToPageOnly_StillRoundTrips_NoRegression()
    {
        // Sibling no-regression check: the ordinary fit-to-page case (fitToPage="1", only
        // fitToWidth/fitToHeight present, no scale attribute at all) must keep working.
        var worksheetXml = new XDocument(
            new XElement(
                WorksheetNs + "worksheet",
                new XElement(
                    WorksheetNs + "customSheetViews",
                    new XElement(
                        WorksheetNs + "customSheetView",
                        new XAttribute("guid", "{55555555-5555-5555-5555-555555555555}"),
                        new XAttribute("fitToPage", "1"),
                        new XAttribute("state", "visible"),
                        new XElement(
                            WorksheetNs + "pageSetup",
                            new XAttribute("fitToWidth", "1"),
                            new XAttribute("fitToHeight", "2"))))));

        var views = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, WorksheetNs);

        var state = views.Should().ContainSingle().Subject.State;
        state.FitToPage.Should().BeTrue();
        state.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 1, 2));
    }

    private static void AddCustomViewWithStaleScaleFitToPageTrue(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                WorksheetNs + "customWorkbookViews",
                new XElement(
                    WorksheetNs + "customWorkbookView",
                    new XAttribute("name", "FreeXView"),
                    new XAttribute("guid", "{66666666-6666-6666-6666-666666666666}"),
                    new XAttribute("autoUpdate", "0"),
                    new XAttribute("mergeInterval", "0"),
                    new XAttribute("personalView", "0"),
                    new XAttribute("includePrintSettings", "1"))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                WorksheetNs + "customSheetViews",
                new XElement(
                    WorksheetNs + "customSheetView",
                    new XAttribute("guid", "{66666666-6666-6666-6666-666666666666}"),
                    // fitToPage explicitly true: fit-to-page mode is authoritative, but Excel left a
                    // stale leftover scale attribute on the nested pageSetup alongside it.
                    new XAttribute("fitToPage", "1"),
                    new XAttribute("state", "visible"),
                    new XElement(
                        WorksheetNs + "pageSetup",
                        new XAttribute("scale", "55"),
                        new XAttribute("fitToWidth", "2"),
                        new XAttribute("fitToHeight", "3")))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddCustomViewWithAmbiguousPageSetup(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                WorksheetNs + "customWorkbookViews",
                new XElement(
                    WorksheetNs + "customWorkbookView",
                    new XAttribute("name", "FreeXView"),
                    new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                    new XAttribute("autoUpdate", "0"),
                    new XAttribute("mergeInterval", "0"),
                    new XAttribute("personalView", "0"),
                    new XAttribute("includePrintSettings", "1"))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                WorksheetNs + "customSheetViews",
                new XElement(
                    WorksheetNs + "customSheetView",
                    new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                    // fitToPage explicitly false: scale mode is authoritative, but Excel left stale
                    // fitToWidth/fitToHeight attributes on the nested pageSetup alongside it.
                    new XAttribute("fitToPage", "0"),
                    new XAttribute("state", "visible"),
                    new XElement(
                        WorksheetNs + "pageSetup",
                        new XAttribute("scale", "80"),
                        new XAttribute("fitToWidth", "1"),
                        new XAttribute("fitToHeight", "1")))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        var entry = archive.GetEntry(entryName)!;
        entry.Delete();
        var newEntry = archive.CreateEntry(entryName);
        using var stream = newEntry.Open();
        document.Save(stream);
    }
}
