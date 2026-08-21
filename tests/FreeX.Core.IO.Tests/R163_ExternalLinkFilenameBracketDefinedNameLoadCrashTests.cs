using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R163-freex-external-links "extlink-1": the load-path crash that
/// <see cref="R140_ExternalLinkFilenameBracketDefinedNameTests"/> documented (in its own class
/// summary) as a separate, out-of-scope defect. A workbook.xml &lt;definedName&gt; whose RefersTo
/// uses the filename-bracket external-workbook form (e.g. <c>'[Budget.xlsx]Sheet1'!$A$1</c>,
/// quoted or unquoted -- a shape ECMA-376 18.14.4 permits and other producers/hand-edited files
/// legitimately write) is not a token ClosedXML's own formula grammar recognizes at all.
/// <c>XLWorkbook</c>'s constructor parses every &lt;definedName&gt; eagerly
/// (<c>LoadDefinedNames</c>), before <c>XlsxNamedRangeMapper</c>'s classification ever runs, so it
/// throws an unhandled <c>ClosedXML.Parser.ParsingException</c> and previously took the entire
/// workbook load down with it -- even though the rest of the workbook is perfectly ordinary and
/// even though FreeX never models this kind of reference anyway (numeric-ordinal or
/// filename-bracket alike). <see cref="XlsxFileAdapter.OpenClosedXmlWorkbookWithSanitizationFallback"/>
/// now recognizes this failure by exception type and retries with only the unparseable
/// &lt;definedName&gt; element(s) stripped from its ClosedXML-only parse copy of the package,
/// leaving the caller's own pristine package stream (and therefore defined-name resurrection on
/// save) untouched.
/// </summary>
public sealed class R163_ExternalLinkFilenameBracketDefinedNameLoadCrashTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Theory]
    [InlineData("'[Budget.xlsx]Sheet1'!$A$1", "quoted sheet name")]
    [InlineData("[Budget.xlsx]Sheet1!$A$1", "unquoted sheet name")]
    [InlineData("'[Report v2 (final).xlsx]Sheet1'!$A$1", "filename with spaces/parens")]
    public void Load_WorkbookWithFilenameBracketDefinedName_DoesNotThrow(string refersTo, string because)
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddDefinedName(source, "TaxRate", refersTo, localSheetId: null);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;

        // Before the fix: XLWorkbook's own formula parser threw ClosedXML.Parser.ParsingException
        // ("Unable to determine token for ...") while constructing the workbook, and that
        // exception propagated out of Load uncaught -- the whole file failed to open. This must
        // now succeed and simply not model the one unresolvable defined name.
        var act = () => adapter.Load(source);
        act.Should().NotThrow($"a filename-bracket external-workbook reference ({because}) must not " +
            "crash the entire workbook load -- FreeX never models it either way, and Excel opens " +
            "such a file without complaint");

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.Sheets.Select(s => s.Name).Should().BeEquivalentTo(["Sheet1", "Other"],
            "the rest of the workbook must load normally even though one defined name is unmodelable");
        loaded.NamedRanges.Should().NotContainKey("TaxRate");
        loaded.NamedFormulas.Should().NotContainKey("TaxRate");
    }

    [Fact]
    public void Load_WorkbookWithFilenameBracketDefinedName_StillResurrectsItOnUnrelatedSave()
    {
        // Sibling of R140's numeric-ordinal resurrection test, but for the filename-bracket form
        // that previously could not even be loaded: confirms the fix's package-copy scoping is
        // correct -- stripping the unparseable <definedName> from the ClosedXML-only parse copy
        // must not touch the pristine source snapshot that the save-path resurrection logic reads
        // from.
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddDefinedName(source, "TaxRate", "'[Budget.xlsx]Sheet1'!$A$1", localSheetId: null);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        loaded.Sheets.Single(s => s.Name == "Sheet1").Name = "Data";

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadWorkbookRoot(saved);
        var resurrected = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .Where(name => name.Attribute("name")?.Value == "TaxRate")
            .ToList() ?? [];

        resurrected.Should().ContainSingle(
            "the filename-bracket external-workbook reference was never loaded into the model, so " +
            "it must still be resurrected verbatim from the pristine source on save, exactly like " +
            "the numeric-ordinal form");
        resurrected[0].Value.Should().Be("'[Budget.xlsx]Sheet1'!$A$1");
    }

    [Fact]
    public void Load_WorkbookWithNumericOrdinalExternalDefinedName_StillLoadsUnaffected()
    {
        // No-regression sibling: the numeric-ordinal external-workbook form already loaded fine
        // through ClosedXML before this fix (it is a token ClosedXML's grammar does recognize) and
        // must keep doing so -- the new stripping step must not touch it.
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddDefinedName(source, "TaxRate", "'[1]Sheet1'!$A$1", localSheetId: null);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;

        var act = () => adapter.Load(source);
        act.Should().NotThrow("the numeric-ordinal external-workbook form already parsed through " +
            "ClosedXML successfully and must remain unaffected by the new failure-recovery path");

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.Sheets.Select(s => s.Name).Should().BeEquivalentTo("Sheet1", "Other");
        loaded.NamedRanges.Should().NotContainKey("TaxRate");
        loaded.NamedFormulas.Should().NotContainKey("TaxRate");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static XElement ReadWorkbookRoot(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        return XDocument.Load(stream).Root!;
    }

    /// <summary>
    /// Adds a defined name to the SOURCE package's pristine workbook.xml, mirroring
    /// R140_ExternalLinkFilenameBracketDefinedNameTests's fixture convention.
    /// </summary>
    private static void AddDefinedName(MemoryStream package, string name, string refersTo, int? localSheetId)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
                workbookXml = XDocument.Load(entryStream);

            var root = workbookXml.Root!;
            var definedNames = root.Element(WorkbookNs + "definedNames");
            if (definedNames is null)
            {
                definedNames = new XElement(WorkbookNs + "definedNames");
                var precedingSibling = root.Element(WorkbookNs + "externalReferences")
                    ?? root.Element(WorkbookNs + "sheets");
                if (precedingSibling is not null)
                    precedingSibling.AddAfterSelf(definedNames);
                else
                    root.Add(definedNames);
            }

            var definedName = new XElement(WorkbookNs + "definedName", new XAttribute("name", name), refersTo);
            if (localSheetId is { } id)
                definedName.SetAttributeValue("localSheetId", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            definedNames.Add(definedName);

            entry.Delete();
            var replacement = archive.CreateEntry("xl/workbook.xml");
            using var replacementStream = replacement.Open();
            workbookXml.Save(replacementStream, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }
}
