using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for two round-37 defined-name hidden/comment round-trip findings:
///
///  - R37-io-defined-names-3-1: a full-rebuild save must not silently drop the hidden/comment
///    attributes of a pre-existing, still-live FORMULA-based defined name. Before the fix,
///    XlsxWorkbookMetadataPreserver.MergeDefinedNames treated any name already re-emitted by the
///    model round-trip (existingKeys.Contains(key)) as fully handled and never backfilled
///    attributes the model has no metadata slot for (Workbook.NamedFormulas is a plain
///    Dictionary&lt;string,string&gt;, unlike NamedRangeMetadataByName for plain ranges). These
///    tests call XlsxWorkbookMetadataPreserver.Preserve directly (like its sibling
///    XlsxWorkbookMetadataPreserverDefinedNameTests) so the outcome is attributable to
///    MergeDefinedNames alone - end-to-end, XlsxFileAdapter.Save.cs also calls
///    SourcePackage.RestoreWorkbookDefinedNames afterward on the SAME full-save path, which
///    already backfills missing attributes on a matching element and would otherwise mask a
///    MergeDefinedNames-only regression.
///
///  - R37-io-defined-names-3-2: the patch-save (incremental) path must write the hidden/comment
///    attributes of a BRAND-NEW defined name (one that did not exist in the pristine source
///    package) directly, since there is no pristine element to backfill from afterwards. Before
///    the fix, XlsxNamedRangeMapper.CreateDefinedNameEntries/DefinedNameEntry carried only
///    Name/LocalSheetId/Text, so a newly hidden/commented named range was written back fully
///    visible and commentless the first time it was saved through the patch-save path.
/// </summary>
public sealed class R37_DefinedNamesHiddenCommentRoundTripTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── R37-io-defined-names-3-1: full-rebuild must preserve a formula name's hidden/comment ──

    [Fact]
    public void Preserve_LiveFormulaBasedDefinedName_BackfillsHiddenAndCommentOntoRewrittenElement()
    {
        // "Target": the freshly ClosedXML-rebuilt package for the CURRENT model state. The model
        // round-trip re-emits "TaxRate" (it is live in workbook.NamedFormulas) but - since
        // NamedFormulas has no metadata slot - WITHOUT any hidden/comment attribute, exactly like
        // XlsxNamedRangeMapper.Save's formula overload actually behaves.
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["TaxRate"] = "Sheet1!$A$1*2";
        using var target = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // "Source": the pristine pre-edit package, where the SAME live name carries hidden/comment
        // attributes (as if set via Excel's Name Manager on a previous save).
        using var source = BuildSourcePackage(["Sheet1"], "TaxRate", "Sheet1!$A$1*2", hidden: true, comment: "internal", localSheetId: null);

        var sourceSheetIdsByLocalId = new[] { workbook.Sheets[0].Id };
        RunPreserve(source, target, workbook, sourceSheetIdsByLocalId);

        var definedName = ReadDefinedName(target, "TaxRate");
        definedName.Should().NotBeNull("the live formula-based name must still be present after Preserve");
        definedName!.Attribute("hidden").Should().NotBeNull(
            "MergeDefinedNames must backfill the hidden attribute onto the freshly-rewritten element " +
            "for a live, unchanged formula-based defined name, exactly as Excel itself does");
        definedName.Attribute("hidden")!.Value.Should().Be("1");
        definedName.Attribute("comment").Should().NotBeNull(
            "MergeDefinedNames must backfill the comment attribute onto the freshly-rewritten element " +
            "for a live, unchanged formula-based defined name");
        definedName.Attribute("comment")!.Value.Should().Be("internal");
    }

    [Fact]
    public void Preserve_OrdinaryFormulaBasedDefinedName_StaysVisibleAndCommentless()
    {
        // Sibling case: an ordinary (not hidden, no comment) formula-based name must be completely
        // unaffected by the backfill fix - no spurious hidden/comment attribute must appear.
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["PlainFormula"] = "Sheet1!$A$1+1";
        using var target = XlsxPackageTestHelper.SaveWorkbook(workbook);

        using var source = BuildSourcePackage(["Sheet1"], "PlainFormula", "Sheet1!$A$1+1", hidden: false, comment: null, localSheetId: null);

        var sourceSheetIdsByLocalId = new[] { workbook.Sheets[0].Id };
        RunPreserve(source, target, workbook, sourceSheetIdsByLocalId);

        var definedName = ReadDefinedName(target, "PlainFormula");
        definedName.Should().NotBeNull();
        definedName!.Attribute("hidden").Should().BeNull(
            "an ordinary formula-based name must not gain a hidden attribute it never had");
        definedName.Attribute("comment").Should().BeNull(
            "an ordinary formula-based name must not gain a comment attribute it never had");
    }

    // ── R37-io-defined-names-3-2: patch-save must write hidden/comment for a brand-new name ────

    // These exercise XlsxNamedRangeMapper.SaveToPackage directly - the exact incremental
    // (raw-XML-edit) write-back this finding is about, and the same method
    // XlsxFileAdapter.Save.cs:82 calls once the cell-patch path has succeeded - so the test is
    // attributable to SaveToPackage/CreateDefinedNameEntries alone, regardless of the outer
    // save-path eligibility gates.

    [Fact]
    public void SaveToPackage_NewHiddenCommentedNamedRange_WritesHiddenAndComment()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        // No defined names at all in the pristine source package.
        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // Brand-new workbook-scoped named range, marked Hidden with a comment - never existed in
        // the pristine source snapshot, so there is nothing for a later backfill pass to draw from.
        workbook.DefineNamedRange(
            "Secret",
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            new NamedRangeMetadata("Workbook", "do not delete", Hidden: true));

        XlsxNamedRangeMapper.SaveToPackage(workbook, package);

        var definedName = ReadDefinedName(package, "Secret");
        definedName.Should().NotBeNull("the new named range must be written into <definedNames>");
        definedName!.Attribute("hidden").Should().NotBeNull(
            "a brand-new hidden defined name must be written back hidden, not silently made " +
            "visible by the patch-save path");
        definedName.Attribute("hidden")!.Value.Should().Be("1");
        definedName.Attribute("comment").Should().NotBeNull(
            "a brand-new commented defined name must keep its comment through the patch-save path");
        definedName.Attribute("comment")!.Value.Should().Be("do not delete");
    }

    [Fact]
    public void SaveToPackage_NewVisibleCommentlessNamedRange_StaysVisibleAndCommentless()
    {
        // Sibling case: a brand-new ordinary (visible, no comment) named range added through the
        // same patch-save write-back must be completely unaffected by the fix.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        workbook.DefineNamedRange(
            "PlainRange",
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));

        XlsxNamedRangeMapper.SaveToPackage(workbook, package);

        var definedName = ReadDefinedName(package, "PlainRange");
        definedName.Should().NotBeNull();
        definedName!.Attribute("hidden").Should().BeNull(
            "an ordinary new named range must not gain a hidden attribute it was never given");
        definedName.Attribute("comment").Should().BeNull(
            "an ordinary new named range must not gain a comment attribute it was never given");
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    private static void RunPreserve(
        MemoryStream sourcePackage,
        MemoryStream target,
        Workbook workbook,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
    {
        sourcePackage.Position = 0;
        target.Position = 0;
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(target, ZipArchiveMode.Update, leaveOpen: true);
        XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook, sourceSheetIdsByLocalId);
    }

    /// <summary>
    /// Builds a real package whose pristine &lt;sheets&gt; carries <paramref name="pristineSheetNames"/>
    /// plus a single injected defined name (workbook- or sheet-scoped), optionally hidden/commented -
    /// matching the P112/R27/R28 fixture convention used by the sibling
    /// XlsxWorkbookMetadataPreserverDefinedNameTests.
    /// </summary>
    private static MemoryStream BuildSourcePackage(
        string[] pristineSheetNames,
        string name,
        string refersTo,
        bool hidden,
        string? comment,
        int? localSheetId)
    {
        var pristine = new Workbook("Test");
        foreach (var sheetName in pristineSheetNames)
            pristine.AddSheet(sheetName);

        var package = XlsxPackageTestHelper.SaveWorkbook(pristine);
        AddDefinedName(package, name, refersTo, hidden, comment, localSheetId);
        return package;
    }

    private static void AddDefinedName(
        MemoryStream package,
        string name,
        string refersTo,
        bool hidden,
        string? comment,
        int? localSheetId)
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
            if (hidden)
                definedName.SetAttributeValue("hidden", "1");
            if (comment is not null)
                definedName.SetAttributeValue("comment", comment);
            definedNames.Add(definedName);

            entry.Delete();
            var replacement = archive.CreateEntry("xl/workbook.xml");
            using var replacementStream = replacement.Open();
            workbookXml.Save(replacementStream, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }

    private static XElement? ReadDefinedName(MemoryStream package, string name)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml")!;
        using var stream = entry.Open();
        var root = XDocument.Load(stream).Root!;
        return root.Element(WorkbookNs + "definedNames")?
            .Elements(WorkbookNs + "definedName")
            .FirstOrDefault(element => element.Attribute("name")?.Value == name);
    }
}
