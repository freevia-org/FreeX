using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R133-io-slicer-timeline-multipivot: a slicer/timeline connected to SEVERAL
/// pivot tables at once (Excel's "Report Connections") had its <c>&lt;pivotTables&gt;</c> connections
/// collapsed onto a single name on save. <see cref="XlsxSlicerTimelineStateRewriter"/>'s
/// <c>RewriteCachePivotTableBinding</c> stamped EVERY <c>&lt;pivotTable name=".."/&gt;</c> entry the
/// preserved slicerCache/timelineCache carried with the model's single
/// <see cref="SlicerModel.SourcePivotTableName"/>/<see cref="TimelineModel.SourcePivotTableName"/> (the
/// first/primary connection only, since <see cref="XlsxSlicerTimelineMetadataReader"/> only ever captured
/// the FIRST <c>&lt;pivotTable&gt;</c> too) -- so on any patch-save of a source-preserved workbook, EVERY
/// connection but the first was silently overwritten to the same name, breaking the other pivot tables'
/// binding to the control on reopen.
/// </summary>
public sealed class FreeXR133SlicerTimelineMultiPivotTests
{
    [Fact]
    public void Load_SlicerCacheConnectedToTwoPivotTables_CapturesBothConnections()
    {
        using var source = BuildSourceWithSlicerConnectedToTwoPivots();

        var loaded = new XlsxFileAdapter().Load(source);

        var slicer = loaded.Slicers.Single();
        slicer.SourcePivotTableName.Should().Be("PT1", "the primary connection keeps driving live filtering");
        slicer.ConnectedPivotTableNames.Should().Equal(["PT1", "PT2"],
            "the reader must capture EVERY pivotTable this slicer's cache lists, not just the first");
    }

    [Fact]
    public void PatchSave_SlicerConnectedToTwoPivotTables_PreservesBothConnections()
    {
        using var source = BuildSourceWithSlicerConnectedToTwoPivots();

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        // Trivial unrelated cell edit to force the hasSourcePackage ("patch save") path, mirroring the
        // sibling R69 tests -- this is the path XlsxSlicerTimelineStateRewriter runs on.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 20, 20), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        // Any slicer/timeline on the workbook always disqualifies the fast cell-value-patch path (see
        // WorkbookHasPatchUnsafePivotFeatures) -- every sibling slicer/timeline save test in this project
        // (e.g. FreeXR37SlicerTimelineTests, FreeXR58SlicerTimelineCacheScopeTests) expects FullSave here
        // too. FullSave still runs on the source-preserved package (PreserveSourcePackageParts copies the
        // original parts forward), so XlsxSlicerTimelineStateRewriter still runs against them -- this is
        // exactly the save path under test.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        ReadAllPivotTableNames(saved, "xl/slicerCaches/slicerCache1.xml").Should().Equal(["PT1", "PT2"],
            "both pivot connections must survive a patch-save, not collapse onto a single name");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.Slicers.Single().ConnectedPivotTableNames.Should().Equal(["PT1", "PT2"],
            "the second connection must still be discoverable (and so still able to filter) after a full round-trip");
    }

    [Fact]
    public void PatchSave_RenamingOnlyOneOfTwoConnectedPivotTables_UpdatesOnlyThatConnection()
    {
        using var source = BuildSourceWithSlicerConnectedToTwoPivots();

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        var sheet = loaded.GetSheetAt(0);
        var rename = new RenamePivotTableCommand(sheet.Id, "PT1", "PT1_Renamed");
        rename.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();

        var slicer = loaded.Slicers.Single();
        slicer.SourcePivotTableName.Should().Be("PT1_Renamed");
        slicer.ConnectedPivotTableNames.Should().Equal(["PT1_Renamed", "PT2"],
            "the renamed connection updates in place; the OTHER (untouched) connection must be left alone");

        sheet.SetCell(new CellAddress(sheet.Id, 20, 20), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ReadAllPivotTableNames(saved, "xl/slicerCaches/slicerCache1.xml").Should().Equal(["PT1_Renamed", "PT2"],
            "the saved cache must reflect the rename for the renamed connection only -- PT2 must not be " +
            "silently stomped with the renamed name (the exact collapse bug this fix addresses)");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a workbook with a slicer connected to a SINGLE pivot table (PT1, all FreeX's own writer
    /// currently supports authoring), saves it, then patches the saved package's slicerCache to add a
    /// SECOND <c>&lt;pivotTable name="PT2"/&gt;</c> connection -- mirroring the package shape Excel itself
    /// produces for a slicer with multiple "Report Connections" (a shape FreeX can load but not yet author
    /// from scratch). Loading this patched package is what exercises
    /// <see cref="XlsxSlicerTimelineMetadataReader"/>'s multi-connection read path; re-saving it is what
    /// exercises <see cref="XlsxSlicerTimelineStateRewriter"/>'s multi-connection write path.
    /// </summary>
    private static MemoryStream BuildSourceWithSlicerConnectedToTwoPivots()
    {
        var workbook = new Workbook("MultiPivotSlicerR133");
        var sheet = workbook.AddSheet("Data1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        string[] regions = ["East", "West", "North"];
        for (var i = 0; i < regions.Length; i++)
        {
            var row = (uint)(i + 2);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(regions[i]));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((i + 1) * 10));
        }

        var cache = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Data1", SourceReference = "A1:B4" };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West", "North"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot1 = new PivotTableModel
        {
            Name = "PT1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 6, 1), new CellAddress(sheet.Id, 9, 2))
        };
        pivot1.RowFields.Add(new PivotFieldModel(0));
        pivot1.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot1);

        // Second pivot table this same slicer will ALSO drive once the package is patched below -- a
        // distinct real PivotTableModel so a rename (RenamePivotTableCommand) can target either one.
        var pivot2 = new PivotTableModel
        {
            Name = "PT2",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 12, 1), new CellAddress(sheet.Id, 15, 2))
        };
        pivot2.RowFields.Add(new PivotFieldModel(0));
        pivot2.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot2);

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PT1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2"
        });

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        AddSecondPivotTableConnection(stream, "xl/slicerCaches/slicerCache1.xml", "PT2");

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Post-save package surgery: appends a second <c>&lt;pivotTable name="{secondPivotName}"/&gt;</c>
    /// element to the cache part's existing <c>&lt;pivotTables&gt;</c> list, matching the namespace/shape
    /// <see cref="XlsxSlicerTimelineWriter"/> already emitted for the first entry -- exactly what a real
    /// Excel-authored multi-connection slicerCache looks like.
    /// </summary>
    private static void AddSecondPivotTableConnection(MemoryStream package, string entryPath, string secondPivotName)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(entryPath) ?? throw new InvalidOperationException($"{entryPath} not found");
            XDocument xml;
            using (var entryStream = entry.Open())
                xml = XDocument.Load(entryStream);

            var pivotTablesElement = xml.Root!.Elements()
                .Single(e => e.Name.LocalName == "pivotTables");
            var firstPivotTableElement = pivotTablesElement.Elements().Single();
            var ns = firstPivotTableElement.Name.Namespace;
            var tabId = firstPivotTableElement.Attribute("tabId")?.Value ?? "1";
            pivotTablesElement.Add(new XElement(ns + "pivotTable",
                new XAttribute("name", secondPivotName),
                new XAttribute("tabId", tabId)));

            entry.Delete();
            var newEntry = archive.CreateEntry(entryPath);
            using var writer = newEntry.Open();
            xml.Save(writer);
        }
        package.Position = 0;
    }

    private static string[] ReadAllPivotTableNames(MemoryStream package, string entryPath)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(entryPath);
        entry.Should().NotBeNull(entryPath);
        using var entryStream = entry!.Open();
        var xml = XDocument.Load(entryStream);
        var names = xml.Descendants()
            .Where(element => element.Name.LocalName == "pivotTable")
            .Select(element => element.Attribute("name")?.Value ?? "")
            .ToArray();
        package.Position = 0;
        return names;
    }
}
