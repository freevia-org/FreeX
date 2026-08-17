using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R140-freex-external-links "extlink-1":
/// <see cref="XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo"/> only recognized the
/// numeric-ordinal external-workbook bracket form (<c>[1]Sheet1!$A$1</c>, where the bracket
/// content parses as an <c>int</c>) as an external-workbook reference. A bare defined name whose
/// RefersTo uses the filename-bracket form Excel/other tools also write (e.g.
/// <c>'[Budget.xlsx]Sheet1'!$A$1</c>) fell through to <c>return false</c> ("modelable"), even
/// though it was never actually loaded into the model. That false "modelable" verdict makes the
/// liveness gates in <c>XlsxWorkbookMetadataPreserver.MergeDefinedNames</c> and
/// <c>XlsxFileAdapter.SourcePackageSnapshot.RestorePatchWorkbookDefinedNames</c> treat the name's
/// absence from the live model as a user deletion, so the pristine &lt;definedName&gt; element
/// would be silently dropped from the very next save instead of being resurrected verbatim. The
/// classification predicate is covered directly below (it is the exact decision point both call
/// sites depend on). A separate, ALREADY-PRE-EXISTING defect independent of this fix was found
/// while probing the full load+save round trip for the filename-bracket form: ClosedXML's own
/// formula parser (invoked eagerly for every &lt;definedName&gt; while constructing
/// <c>XLWorkbook</c>) throws an unhandled <c>ClosedXML.Parser.ParsingException</c> for a
/// filename-bracket RefersTo (quoted or unquoted) before <c>XlsxNamedRangeMapper</c> ever runs,
/// and <c>XlsxFileAdapter.OpenClosedXmlWorkbookWithSanitizationFallback</c>'s fallback chain only
/// special-cases conditional-formatting/pivot/shared-formula failures, so the exception propagates
/// out of <c>XlsxFileAdapter.Load</c> uncaught -- this masks the resurrection bug behind a full
/// load crash for that exact input and is out of this fix's file scope (XlsxFileAdapter.cs, not
/// XlsxNamedRangeMapper.cs); it is flagged separately. The classification fix below still stands
/// on its own merits (a correct predicate is required regardless, and remains reachable for any
/// caller/config where the source text does not also trip that separate parser limitation) and is
/// exercised at the classification level plus, for the regression side, at the full round-trip
/// level using the numeric-ordinal external-reference form ClosedXML *can* parse.
/// </summary>
public sealed class R140_ExternalLinkFilenameBracketDefinedNameTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── unit-level: the classification predicate itself ────────────────────────────────────────

    [Theory]
    [InlineData("=[1]Sheet1!$B$2", "numeric-ordinal external reference, unquoted")]
    [InlineData("='[1]Sheet1'!$B$2", "numeric-ordinal external reference, quoted sheet name")]
    [InlineData("=[Budget.xlsx]Sheet1!$A$1", "filename-bracket external reference, unquoted")]
    [InlineData("='[Budget.xlsx]Sheet1'!$A$1", "filename-bracket external reference, quoted sheet name")]
    [InlineData("='[Budget.xlsx]Sheet1'!$A$1:$B$5", "filename-bracket external reference, range")]
    [InlineData("='[Report v2 (final).xlsx]Sheet1'!$A$1", "filename-bracket, filename has spaces/parens, whole span quoted")]
    public void IsUnmodelableDefinedNameRefersTo_ReturnsTrue_ForBareExternalReference(
        string refersTo, string because)
    {
        XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo(refersTo).Should().BeTrue(
            $"a bare external-workbook reference ({because}) was never loaded into the model and " +
            "must be classified as unmodelable so its pristine <definedName> is preserved on save");
    }

    [Theory]
    [InlineData("=Sheet1!$A$1", "ordinary local sheet-qualified single cell")]
    [InlineData("=Sheet1!$A$1:$B$10", "ordinary local sheet-qualified range")]
    [InlineData("='My Sheet'!$A$1", "local range with a quoted (spaced) sheet name")]
    [InlineData("=$A$1", "bare cell address, no sheet qualifier")]
    [InlineData("=A1:B10", "bare range, no sheet qualifier")]
    [InlineData("=Sheet1:Sheet3!$A$1", "sheet-span (3-D) reference")]
    public void IsUnmodelableDefinedNameRefersTo_ReturnsFalse_ForOrdinaryLocalRange(
        string refersTo, string because)
    {
        XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo(refersTo).Should().BeFalse(
            $"an ordinary local reference ({because}) IS modeled and must not be misclassified as " +
            "an external-workbook reference");
    }

    [Theory]
    [InlineData("=[1]Sheet1!$B$2*2", "formula embedding a numeric-ordinal external reference")]
    [InlineData("=SUM([Budget.xlsx]Sheet1!A1:A10)+Local!B1", "formula embedding a filename-bracket external reference")]
    public void IsUnmodelableDefinedNameRefersTo_ReturnsFalse_ForFormulaThatEmbedsExternalReference(
        string refersTo, string because)
    {
        // A formula body (operator/paren outside a quoted sheet name) is routed into
        // NamedFormulas/ScopedNamedFormulas as a live, opaque formula regardless of what external
        // reference it embeds -- it must not be classified as unmodelable, or the liveness gate
        // would resurrect a genuinely-deleted live name from the pristine source on every save.
        XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo(refersTo).Should().BeFalse(
            $"a live formula ({because}) is modeled via IsFormulaExpression and must not be " +
            "misclassified as an unmodelable bare external reference");
    }

    [Fact]
    public void IsUnmodelableDefinedNameRefersTo_ReturnsFalse_ForStructuredTableReference()
    {
        // Sibling-behaviour guard: a structured table reference like Table1[Column1] also contains
        // '[' / ']', but the bracket is NOT the first character of the (possibly quoted) body -- it
        // must not be swept up by the external-reference bracket check.
        XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo("=Table1[Column1]").Should().BeFalse(
            "a structured table reference's bracket follows the table identifier, unlike a genuine " +
            "external-workbook reference whose bracket opens the body, and must not be misclassified");
    }

    // ── integration-level: the actual save round trip a user hits ──────────────────────────────
    //
    // The filename-bracket form itself cannot be exercised end-to-end here: ClosedXML's own
    // formula parser rejects that syntax outright (throws while constructing XLWorkbook, before
    // XlsxNamedRangeMapper ever runs) for both the quoted and unquoted spellings -- confirmed via
    // a standalone probe against ClosedXML directly. That is a separate, pre-existing defect in
    // the load path (XlsxFileAdapter.cs), not something this classification fix can address or
    // that a test against XlsxNamedRangeMapper.cs should paper over. The genuine external-
    // reference form ClosedXML *does* load successfully -- the numeric-ordinal bracket, e.g.
    // '[1]Sheet1'!$A$1 -- is used below to prove the resurrection round trip for a real external
    // reference still works after the classification broadening (regression sibling for "genuine
    // external references all still map correctly").

    [Fact]
    public void Save_AfterUnrelatedSheetRename_NumericOrdinalExternalDefinedName_SurvivesResurrection()
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddDefinedName(source, "TaxRate", "'[1]Sheet1'!$A$1", localSheetId: null);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        // Confirm the premise: FreeX never actually models a bare external-workbook reference.
        loaded.NamedRanges.Should().NotContainKey("TaxRate");
        loaded.NamedFormulas.Should().NotContainKey("TaxRate");

        // Force a full (non-patch) save via an unrelated sheet rename, exactly as
        // R27_WorkbookPartsDefinedNameResurrectionTests does for the constant-literal case.
        loaded.Sheets.Single(s => s.Name == "Sheet1").Name = "Data";

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = ReadWorkbookRoot(saved);
        var resurrected = root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .Where(name => name.Attribute("name")?.Value == "TaxRate")
            .ToList() ?? [];

        resurrected.Should().ContainSingle(
            "a genuine (numeric-ordinal) external-workbook reference was never loaded into the " +
            "model, so it must still be resurrected verbatim from the pristine source on save -- " +
            "unaffected by broadening the classification to also catch the filename-bracket form");
        resurrected[0].Value.Should().Be("'[1]Sheet1'!$A$1");
    }

    [Fact]
    public void Save_AfterUnrelatedSheetRename_OrdinaryWorkbookScopedName_StillSurvivesUnaffected()
    {
        // No-regression sibling: an ordinary constant-literal name (the R27 case the resurrection
        // path already handled) must keep round-tripping exactly as before this fix.
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Other");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddDefinedName(source, "LocalRate", "0.0825", localSheetId: null);

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
            .Where(name => name.Attribute("name")?.Value == "LocalRate")
            .ToList() ?? [];

        resurrected.Should().ContainSingle(
            "an ordinary unmodelable constant-literal name must still be resurrected, unaffected by " +
            "the external-reference bracket fix");
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
    /// R27_WorkbookPartsDefinedNameResurrectionTests's fixture convention.
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
