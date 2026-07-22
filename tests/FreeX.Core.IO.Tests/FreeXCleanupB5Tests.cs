using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for cleanup batch B5:
///  - P109: patch-save must not silently drop a freeze-panes / unfreeze / split-removal change
///    (which XlsxWorksheetViewWriter.UpdateSheetView cannot represent in-place) and then poison
///    the baseline so the change is permanently lost on every later save. It must escalate to a
///    full save instead, matching the full-save writer's correct freeze/unfreeze handling.
///  - P110: a defined name whose refersTo body is a constant literal, an external-workbook
///    reference, or a broken (#REF!) reference must survive a patch save unmodified instead of
///    being permanently deleted, because such names are never loaded into the model in the first
///    place (so the model-liveness resurrection gate must not treat their absence as deletion).
/// </summary>
public sealed class FreeXCleanupB5Tests
{
    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
            workbook.SaveAs(stream);
        }

        return RemoveEmptyWorkbookDefinedNames(stream.ToArray());
    }

    private static byte[] RemoveEmptyWorkbookDefinedNames(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
            var changed = false;
            foreach (var definedNames in workbookXml.Root!.Elements(workbookNs + "definedNames").ToList())
            {
                if (definedNames.HasElements || definedNames.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
                    continue;

                definedNames.Remove();
                changed = true;
            }

            if (changed)
                ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddFrozenPane(byte[] sourceBytes, uint frozenRows, uint frozenCols)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            var sheetView = worksheetXml.Root!
                .Element(worksheetNs + "sheetViews")!
                .Elements(worksheetNs + "sheetView")
                .Single(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));

            sheetView.Elements(worksheetNs + "pane").Remove();
            sheetView.AddFirst(new XElement(
                worksheetNs + "pane",
                frozenCols > 0 ? new XAttribute("xSplit", frozenCols) : null,
                frozenRows > 0 ? new XAttribute("ySplit", frozenRows) : null,
                new XAttribute("topLeftCell", $"{CellAddress.NumberToColumnName(frozenCols + 1)}{frozenRows + 1}"),
                new XAttribute("state", frozenCols > 0 && frozenRows > 0 ? "frozenSplit" : "frozen")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddDefinedName(byte[] sourceBytes, string name, string refersToBody)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
            var root = workbookXml.Root!;
            var definedNames = root.Element(workbookNs + "definedNames");
            if (definedNames is null)
            {
                definedNames = new XElement(workbookNs + "definedNames");
                root.Element(workbookNs + "sheets")!.AddAfterSelf(definedNames);
            }

            definedNames.Add(new XElement(
                workbookNs + "definedName",
                new XAttribute("name", name),
                refersToBody));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        return stream.ToArray();
    }

    private static bool HasFrozenPane(byte[] packageBytes, string worksheetPath, out uint frozenRows, out uint frozenCols)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        var pane = document.Root!
            .Element(ns + "sheetViews")
            ?.Elements(ns + "sheetView")
            .FirstOrDefault(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal))
            ?.Element(ns + "pane");

        var state = pane?.Attribute("state")?.Value;
        if (pane is null || state is not ("frozen" or "frozenSplit"))
        {
            frozenRows = 0;
            frozenCols = 0;
            return false;
        }

        frozenRows = uint.TryParse(pane.Attribute("ySplit")?.Value, out var rows) ? rows : 0;
        frozenCols = uint.TryParse(pane.Attribute("xSplit")?.Value, out var cols) ? cols : 0;
        return true;
    }

    private static List<(string Name, string RefersTo)> ReadDefinedNames(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, "xl/workbook.xml");
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Root!
            .Element(workbookNs + "definedNames")
            ?.Elements(workbookNs + "definedName")
            .Select(element => (element.Attribute("name")?.Value ?? string.Empty, element.Value))
            .ToList()
            ?? [];
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string path) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, path);

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var replacement = archive.CreateEntry(path);
        using var replacementStream = replacement.Open();
        document.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewFreezePanesEdit_EscalatesToFullSaveInsteadOfDroppingFreeze()
    {
        // P109 regression: freezing panes (FrozenRows 0 -> 1) cannot be represented by the
        // in-place worksheet-view patch writer. Before the fix, ApplyWorksheetViewChanges silently
        // reverted FrozenRows/FrozenCols back to the on-disk (unfrozen) values while still letting
        // the save report success as a patch, which then poisoned the baseline so the freeze was
        // permanently unrecoverable on every subsequent save.
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
        sheet.FrozenRows = 1;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // Must escalate to a full save (which correctly writes freeze panes) rather than silently
        // succeeding as a patch save with the freeze dropped.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        HasFrozenPane(savedBytes, "xl/worksheets/sheet1.xml", out var frozenRows, out var frozenCols)
            .Should()
            .BeTrue("the full-save fallback must actually persist the freeze-panes change");
        frozenRows.Should().Be(1);
        frozenCols.Should().Be(0);

        Workbook reloadedWorkbook;
        using (var reloadStream = new MemoryStream(savedBytes, writable: false))
            reloadedWorkbook = adapter.Load(reloadStream);
        reloadedWorkbook.GetSheetAt(0).FrozenRows.Should().Be(1);

        // Critically, a second Ctrl+S with no further changes must not re-lose the freeze: the
        // freeze is now genuinely on disk, so re-saving (even via patch) must keep reporting it.
        PrepareLoadedWorkbookForEdit(reloadedWorkbook);
        using var resaved = new MemoryStream();
        adapter.Save(reloadedWorkbook, resaved);
        using var resavedReloadStream = new MemoryStream(resaved.ToArray(), writable: false);
        adapter.Load(resavedReloadStream).GetSheetAt(0).FrozenRows.Should().Be(1);
    }

    [Fact]
    public void Save_LoadedWorkbookWithUnfreezeEdit_EscalatesToFullSaveInsteadOfKeepingFreeze()
    {
        // P109 regression: unfreezing (FrozenRows 2 -> 0) is equally unrepresentable by the
        // in-place writer (it never removes an existing <pane> element), so it must also escalate
        // to a full save instead of silently keeping the on-disk frozen state.
        var sourceBytes = AddFrozenPane(CreateSourcePackage(), frozenRows: 2, frozenCols: 0);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenRows = 0;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        HasFrozenPane(savedBytes, "xl/worksheets/sheet1.xml", out _, out _)
            .Should()
            .BeFalse("unfreezing must actually remove the on-disk frozen pane, not silently keep it");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream).GetSheetAt(0).FrozenRows.Should().Be(0);
    }

    [Theory]
    [InlineData("BrokenRef", "#REF!")]
    [InlineData("ExternalName", "[1]Sheet1!$A$1")]
    public void Save_LoadedWorkbookWithUnmodelableDefinedNameAndUnrelatedCellEdit_PreservesDefinedName(
        string name,
        string refersToBody)
    {
        // P110 regression: defined names whose refersTo is a broken (#REF!) reference or an
        // external-workbook reference are never loaded into the model at all
        // (XlsxNamedRangeMapper.IsFormulaExpression treats both as "not a formula", and neither
        // resolves as a plain in-workbook range). Before the fix, the resurrection gate keyed only
        // on ValidateNamedRangeName (which inspects just the name text), so these names were
        // wrongly treated as "model-representable but deleted by the user" and permanently dropped
        // from workbook.xml on every save, turning every formula that referenced them into #NAME?.
        // NOTE (R66-io-defined-names-scope-6-2): a constant-literal refersTo (e.g. 0.21 or "Hello")
        // used to be lumped into this same "unmodelable" bucket, but is now actually loaded into
        // NamedFormulas/ScopedNamedFormulas (see IsConstantLiteralRefersTo) and round-trips through
        // the ordinary NamedFormulas save path instead — covered by
        // R66_DefinedNameScopeBareConstantRelativeTests, not this "genuinely unmodelable" test.
        var sourceBytes = AddDefinedName(CreateSourcePackage(), name, refersToBody);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        // Confirm this defined name is indeed invisible to the model, matching the finding's
        // premise (it was never loaded in the first place).
        workbook.NamedRanges.Should().NotContainKey(name);
        workbook.NamedFormulas.Should().NotContainKey(name);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        var definedNames = ReadDefinedNames(savedBytes);
        definedNames.Should().Contain(
            entry => entry.Name == name,
            "a defined name FreeX cannot model must survive a save unmodified, not be silently deleted");

        using (var reloadStream = new MemoryStream(savedBytes, writable: false))
            adapter.Load(reloadStream).GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new TextValue("patched value"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithUnmodelableDefinedNameAcrossTwoSaves_StillPreservesDefinedName()
    {
        // Reinforces P110: the name must keep surviving even after a second edit+save cycle (i.e.
        // the fix is not merely a one-time accident of the first save's code path). Uses "BrokenRef"
        // (a genuinely still-unmodelable #REF! refersTo, R66-io-defined-names-scope-6-2) rather than
        // the "TaxRate" constant-literal case this test originally used, since that one is now
        // actually modeled (see the NOTE on the sibling parametrized test above) and round-trips via
        // the ordinary NamedFormulas save path instead of this resurrection-gate path.
        var sourceBytes = AddDefinedName(CreateSourcePackage(), "BrokenRef", "#REF!");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("first edit"));

        using var firstSave = new MemoryStream();
        adapter.Save(workbook, firstSave);
        ReadDefinedNames(firstSave.ToArray()).Should().Contain(entry => entry.Name == "BrokenRef");

        Workbook reloaded;
        using (var reloadStream = new MemoryStream(firstSave.ToArray(), writable: false))
            reloaded = adapter.Load(reloadStream);
        PrepareLoadedWorkbookForEdit(reloaded);
        reloaded.GetSheetAt(0).SetCell(new CellAddress(reloaded.GetSheetAt(0).Id, 1, 2), new TextValue("second edit"));

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);
        ReadDefinedNames(secondSave.ToArray()).Should().Contain(entry => entry.Name == "BrokenRef");
    }
}
