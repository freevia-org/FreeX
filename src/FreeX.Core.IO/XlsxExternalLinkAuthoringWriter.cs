using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// R96-io-external-link-writer-1: FreeX's formula lexer/parser fully accepts a freshly TYPED
/// bracketed external-workbook reference (e.g. <c>='[Budget.xlsx]Sheet1'!A1</c>), but until this
/// writer nothing ever synthesized the supporting OOXML infrastructure Excel always writes
/// alongside such a formula -- the <c>xl/externalLinks/externalLinkN.xml</c> part (with its
/// <c>externalBook</c>/<c>sheetNames</c> scaffolding), that part's own <c>_rels</c>
/// <c>externalLinkPath</c> relationship, the <c>xl/_rels/workbook.xml.rels</c> <c>externalLink</c>
/// relationship, the <c>workbook.xml</c> <c>&lt;externalReferences&gt;</c>/<c>&lt;externalReference&gt;</c>
/// entry, and the <c>[Content_Types].xml</c> Override. Without this, a save emitted the literal
/// bracketed formula text with none of that backing -- a shape real Excel never produces on its own
/// (Excel always writes the externalLink part the moment such a formula is entered) -- leaving Edit
/// Links with nothing to show and the reference permanently orphaned.
/// <para>
/// Scoped to the realistic "typed directly into a cell" shape: the quoted FILENAME form
/// <c>'[Book.xlsx]Sheet1'!A1</c>. The sibling numeric unquoted/quoted form (<c>[1]Sheet1!A1</c> /
/// <c>'[1]Sheet1'!A1</c>) only ever addresses an ALREADY-existing external reference by its 1-based
/// position; with none existing there is no filename to synthesize a backing part for (and if one
/// does exist, it is already backed), so purely-numeric bracket content is left untouched -- still
/// resolving to #REF! exactly as before, same as any other genuinely dangling reference. Likewise
/// the external DEFINED-NAME shape (<c>'[Book.xlsx]'!TaxRate</c>, zero-length sheet segment) is left
/// for a follow-up; only the sheet-qualified cell/range shape is synthesized here.
/// </para>
/// <para>
/// Idempotent by construction: before synthesizing a new part for a given book name, this scans the
/// package's OWN already-written external-link infrastructure (freshly merged in by
/// <see cref="XlsxExternalLinkReferencePreserver"/> when the workbook has a source package, or simply
/// absent for a brand-new workbook) rather than any in-memory model, so a book already backed by a
/// part -- whether carried forward from a prior save or added earlier in this same pass -- is left
/// alone instead of accumulating a duplicate externalLinkN.xml on every subsequent save.
/// </para>
/// </summary>
internal static class XlsxExternalLinkAuthoringWriter
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string ExternalLinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkPathRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath";
    private const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

    // Matches the shape Lexer.ReadQuotedSheetQualifier accepts and reduces to a bracketed
    // "[Book]Sheet" SheetQualifier token: a quoted span opening with "[", closing with "]", a sheet
    // name run (permitting the "''" doubled-apostrophe escape), and the closing quote immediately
    // followed by "!". The book segment deliberately excludes "'" -- a book name containing an
    // escaped apostrophe is a rare edge case this best-effort scan leaves unsynthesized rather than
    // mis-splitting (matches the class of heuristic RecalcEngine.ExternalWorkbookReferencePattern
    // already uses for the sibling "is this even shaped like an external reference" check).
    private static readonly Regex QuotedExternalReferencePattern = new(
        @"'\[([^\[\]']+)\]((?:[^']|'')*)'!",
        RegexOptions.Compiled);

    // R108-io-external-link-string-literal-false-positive-1: QuotedExternalReferencePattern is a raw-text
    // regex with no notion of Excel's actual token grammar -- it cannot tell a genuine bracketed external
    // reference (='[Budget.xlsx]Data'!A1) apart from the SAME bracket/quote/bang shape sitting inertly
    // inside a double-quoted STRING LITERAL (="'[Budget.xlsx]Data'!A1", which just evaluates to that
    // literal text with no external link involved at all -- e.g. a user documenting formula syntax, or a
    // CONCATENATE result). Both scan sites below must exclude any regex match that starts inside an
    // unescaped-double-quote-delimited run, or they will (1) synthesize a bogus externalLink part/
    // <externalReference> entry for a book the user never referenced, and (2) rewrite the matched text
    // INSIDE the string literal from the quoted-filename form to the numeric-ordinal form, mutating the
    // literal's actual value out from under the user on the very next save.
    private static List<(int Start, int End)> FindDoubleQuotedStringLiteralSpans(string text)
    {
        var spans = new List<(int Start, int End)>();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '"')
            {
                i++;
                continue;
            }

            var start = i;
            i++;
            while (i < text.Length)
            {
                if (text[i] == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        // "" escape -- a literal quote character embedded in the string; the
                        // literal is still open.
                        i += 2;
                        continue;
                    }

                    i++; // consume the closing quote.
                    break;
                }

                i++;
            }

            spans.Add((start, i));
        }

        return spans;
    }

    private static bool IsIndexInsideAnySpan(List<(int Start, int End)> spans, int index)
    {
        foreach (var (start, end) in spans)
        {
            if (index >= start && index < end)
                return true;
        }

        return false;
    }

    /// <summary>Fresh-workbook (no source package) entry point -- opens its own archive.</summary>
    public static void Save(Stream packageStream, Workbook workbook)
    {
        var references = CollectDistinctReferences(workbook);
        if (references.Count == 0)
            return;

        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, references);
    }

    /// <summary>
    /// Source-package entry point -- called from <c>PreserveSourcePackageParts</c> against the
    /// already-open generated archive, right after <see cref="XlsxExternalLinkReferencePreserver"/>
    /// carries forward any pre-existing external links (so this pass's "already backed" scan sees
    /// them).
    /// </summary>
    public static void Save(ZipArchive archive, Workbook workbook)
    {
        var references = CollectDistinctReferences(workbook);
        if (references.Count == 0)
            return;

        Save(archive, references);
    }

    private static void Save(ZipArchive archive, List<ExternalReferenceGroup> references)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || workbookRelsEntry is null)
            return;

        var workbookRelsXml = XlsxPackageXmlEditor.LoadXml(workbookRelsEntry);
        var alreadyBacked = CollectAlreadyBackedBookNames(archive, workbookRelsXml);
        var newReferences = references.Where(reference => !alreadyBacked.Contains(reference.BookKey)).ToList();

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var root = workbookXml.Root;
        if (root is null)
            return;

        var externalReferencesElement = root.Element(WorkbookNs + "externalReferences");

        if (newReferences.Count > 0)
        {
            if (externalReferencesElement is null)
            {
                externalReferencesElement = new XElement(WorkbookNs + "externalReferences");
                // Position doesn't need to be schema-exact here: both call sites (PreserveSourcePackageParts
                // and the fresh-workbook post-processing path) always run XlsxWorkbookSchemaNormalizer
                // afterward, which reorders every CT_Workbook child -- including externalReferences -- into
                // its required sequence position.
                root.Add(externalReferencesElement);
            }

            // R102-io-external-link-authoring-mint-collision-1: XlsxExternalLinkReferencePreserver can
            // have just written an <externalReference> whose r:id is a PLACEHOLDER -- deliberately left
            // unbacked by any Relationship element in workbookRelsXml, to mirror a dangling reference the
            // source package itself carried (see that class's own doc comment). EnsureRelationshipForPackagePart's
            // id-minting only scans workbookRelsXml's own Relationship elements, so without this it would
            // deterministically re-mint that SAME "next" id here -- minting a REAL Relationship for it and
            // leaving two sibling <externalReference> elements sharing one r:id, which the end-of-save
            // schema normalizer's dedup-by-r:id then silently collapses into a single ordinal slot.
            // Reserve every r:id already used by an existing <externalReference> element (backed or not)
            // so a freshly minted id here can never collide with one.
            var reservedExternalReferenceRelIds = externalReferencesElement
                .Elements(WorkbookNs + "externalReference")
                .Select(element => element.Attribute(RelNs + "id")?.Value?.Trim())
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToList();

            var nextPartNumber = GetNextExternalLinkPartNumber(archive);
            foreach (var reference in newReferences)
            {
                var partPath = $"xl/externalLinks/externalLink{nextPartNumber}.xml";
                nextPartNumber++;

                var relId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                    workbookRelsXml,
                    PackageRelNs,
                    "xl/workbook.xml",
                    partPath,
                    ExternalLinkRelationshipType,
                    reservedExternalReferenceRelIds);
                reservedExternalReferenceRelIds.Add(relId);
                externalReferencesElement.Add(new XElement(
                    WorkbookNs + "externalReference",
                    new XAttribute(RelNs + "id", relId)));

                WriteExternalLinkPart(archive, partPath, reference);
                XlsxPackageXmlEditor.EnsureSpecificContentType(archive, partPath, ExternalLinkContentType);
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/_rels/workbook.xml.rels", workbookRelsXml);
        }

        // R104-io-external-link-formula-ordinal-1: a freshly typed external-workbook formula is still
        // sitting in the just-saved worksheet XML with its ORIGINAL quoted-filename bracket text (e.g.
        // '[Budget.xlsx]Data'!A1) -- XlsxFileAdapter.Save.cs/ClosedXML wrote that literal text verbatim,
        // with no notion of the externalReference ordinal this method just established (or already
        // found established) for that book. Real Excel never persists the filename inside a formula's
        // <f> text; it always stores the 1-based ordinal position of the matching <externalReference> in
        // workbook.xml (e.g. '[1]Data'!A1), translating to/from the friendly bracketed-filename form only
        // for formula-bar display. Left unrewritten, the saved package is internally self-contradictory
        // (workbook.xml declares external reference #1 for Budget.xlsx, but the formula that supposedly
        // drove that synthesis still spells the filename out), a shape Excel's own save path never
        // produces and is not guaranteed to load cleanly. Rewrite every such formula now that the final
        // ordinal for each book is known -- covers both a book synthesized just above AND one already
        // backed from an earlier save/source package, since either way the ordinal is now resolvable.
        if (externalReferencesElement is not null)
        {
            var bookKeyToOrdinal = BuildBookKeyOrdinals(archive, workbookRelsXml, externalReferencesElement);
            RewriteFormulaBookReferences(archive, bookKeyToOrdinal);
        }
    }

    // Resolves the final 1-based ordinal (matching Excel's '[n]' formula addressing, and
    // ExternalSheetReferenceResolver/Workbook.ExternalLinks' own 1-based indexing on load) for every
    // book backed by an <externalReference> element in the given (already-finalized-for-this-save)
    // container -- both pre-existing entries carried in from a source package/earlier save, and any
    // just appended above, since both are resolvable identically once their backing part/rels exist in
    // the archive and their r:id is registered in workbookRelsXml.
    private static Dictionary<string, int> BuildBookKeyOrdinals(
        ZipArchive archive,
        XDocument workbookRelsXml,
        XElement externalReferencesElement)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 0;
        foreach (var externalReference in externalReferencesElement.Elements(WorkbookNs + "externalReference"))
        {
            ordinal++;
            var relId = externalReference.Attribute(RelNs + "id")?.Value?.Trim();
            if (string.IsNullOrEmpty(relId))
                continue;

            var bookKey = ResolveBookKeyForWorkbookRelationshipId(archive, workbookRelsXml, relId);
            if (bookKey is not null)
                result.TryAdd(bookKey, ordinal);
        }

        return result;
    }

    private static string? ResolveBookKeyForWorkbookRelationshipId(ZipArchive archive, XDocument workbookRelsXml, string relId)
    {
        var relationship = workbookRelsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(element.Attribute("Id")?.Value?.Trim(), relId, StringComparison.OrdinalIgnoreCase));
        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var partPath = XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target.Trim());
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(partPath);
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return null;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var pathRelationship = relsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(element.Attribute("Type")?.Value?.Trim(), ExternalLinkPathRelationshipType, StringComparison.OrdinalIgnoreCase));

        var bookTarget = pathRelationship?.Attribute("Target")?.Value;
        return string.IsNullOrWhiteSpace(bookTarget) ? null : NormalizeBookKey(bookTarget);
    }

    // Rewrites every persisted worksheet <f> whose text still carries the quoted-filename bracket form
    // for a book we can resolve an ordinal for, in place, to the numeric '[n]' form -- scans the actual
    // saved worksheet parts directly (rather than the in-memory model) since that is exactly the text
    // XlsxFileAdapter.Save.cs/ClosedXML already committed to the package and the only thing left to fix.
    private static void RewriteFormulaBookReferences(ZipArchive archive, IReadOnlyDictionary<string, int> bookKeyToOrdinal)
    {
        if (bookKeyToOrdinal.Count == 0)
            return;

        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var worksheetNs = root.Name.Namespace;
            var sheetData = root.Element(worksheetNs + "sheetData");
            if (sheetData is null)
                continue;

            var changed = false;
            foreach (var formula in sheetData.Elements(worksheetNs + "row").Elements(worksheetNs + "c").Elements(worksheetNs + "f"))
            {
                var text = formula.Value;
                if (string.IsNullOrEmpty(text) || !QuotedExternalReferencePattern.IsMatch(text))
                    continue;

                var stringLiteralSpans = FindDoubleQuotedStringLiteralSpans(text);
                var rewritten = QuotedExternalReferencePattern.Replace(text, match =>
                {
                    if (IsIndexInsideAnySpan(stringLiteralSpans, match.Index))
                        return match.Value; // sits inside a string literal -- not a real reference, leave verbatim.

                    var book = match.Groups[1].Value;
                    if (book.Length == 0 || book.All(char.IsAsciiDigit))
                        return match.Value; // already the numeric-ordinal form -- leave untouched.

                    if (!bookKeyToOrdinal.TryGetValue(NormalizeBookKey(book), out var ordinal))
                        return match.Value; // no resolvable backing (shouldn't happen) -- leave verbatim.

                    return $"'[{ordinal}]{match.Groups[2].Value}'!";
                });

                if (!string.Equals(rewritten, text, StringComparison.Ordinal))
                {
                    formula.Value = rewritten;
                    changed = true;
                }
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, worksheetXml);
        }
    }

    private static void WriteExternalLinkPart(ZipArchive archive, string partPath, ExternalReferenceGroup reference)
    {
        const string bookRelId = "rId1";
        var externalBook = new XElement(
            WorkbookNs + "externalBook",
            new XAttribute(RelNs + "id", bookRelId));

        if (reference.SheetNames.Count > 0)
        {
            externalBook.Add(new XElement(
                WorkbookNs + "sheetNames",
                reference.SheetNames.Select(name => new XElement(
                    WorkbookNs + "sheetName",
                    new XAttribute("val", name)))));
        }

        var externalLinkXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                WorkbookNs + "externalLink",
                new XAttribute(XNamespace.Xmlns + "r", RelNs),
                externalBook));
        XlsxPackageXmlEditor.ReplaceXml(archive, partPath, externalLinkXml);

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(partPath);
        var relsXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                PackageRelNs + "Relationships",
                new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", bookRelId),
                    new XAttribute("Type", ExternalLinkPathRelationshipType),
                    new XAttribute("Target", reference.Book),
                    new XAttribute("TargetMode", "External"))));
        XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, relsXml);
    }

    private static HashSet<string> CollectAlreadyBackedBookNames(ZipArchive archive, XDocument workbookRelsXml)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var externalLinkRelationships = workbookRelsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship =>
                string.Equals(relationship.Attribute("Type")?.Value?.Trim(), ExternalLinkRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(relationship.Attribute("TargetMode")?.Value?.Trim(), "External", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        foreach (var relationship in externalLinkRelationships)
        {
            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                continue;

            var partPath = XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target.Trim());
            var relsPath = XlsxPackagePath.GetRelationshipPartPath(partPath);
            var relsEntry = archive.GetEntry(relsPath);
            if (relsEntry is null)
                continue;

            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            foreach (var pathRelationship in relsXml.Root?.Elements(PackageRelNs + "Relationship") ?? [])
            {
                if (!string.Equals(pathRelationship.Attribute("Type")?.Value?.Trim(), ExternalLinkPathRelationshipType, StringComparison.OrdinalIgnoreCase))
                    continue;

                var bookTarget = pathRelationship.Attribute("Target")?.Value;
                if (!string.IsNullOrWhiteSpace(bookTarget))
                    result.Add(NormalizeBookKey(bookTarget));
            }
        }

        return result;
    }

    private static int GetNextExternalLinkPartNumber(ZipArchive archive)
    {
        const string prefix = "xl/externalLinks/externalLink";
        const string suffix = ".xml";
        var highestExisting = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name =>
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(name => int.TryParse(name[prefix.Length..^suffix.Length], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return highestExisting + 1;
    }

    private static List<ExternalReferenceGroup> CollectDistinctReferences(Workbook workbook)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, (string Book, List<string> SheetNames)>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var pair in sheet.GetOccupiedCellMap())
            {
                var formulaText = pair.Value.FormulaText;
                if (string.IsNullOrEmpty(formulaText))
                    continue;

                var stringLiteralSpans = FindDoubleQuotedStringLiteralSpans(formulaText);
                foreach (Match match in QuotedExternalReferencePattern.Matches(formulaText))
                {
                    // The match sits inside a double-quoted STRING LITERAL (e.g. ="'[Budget.xlsx]Data'!A1")
                    // rather than being an actual reference token -- Excel evaluates that formula to the
                    // literal text with no external link involved. Don't synthesize backing for it.
                    if (IsIndexInsideAnySpan(stringLiteralSpans, match.Index))
                        continue;

                    var book = match.Groups[1].Value;
                    var sheetName = match.Groups[2].Value.Replace("''", "'");
                    if (book.Length == 0 || sheetName.Length == 0)
                        continue;

                    // Purely-numeric bracket content ("[1]Sheet1'!A1") addresses an EXISTING external
                    // reference by 1-based position -- there is no filename to synthesize a backing
                    // part for when none exists, and when one does exist it's already backed. See the
                    // class doc comment.
                    if (book.All(char.IsAsciiDigit))
                        continue;

                    var key = NormalizeBookKey(book);
                    if (!groups.TryGetValue(key, out var group))
                    {
                        group = (book, new List<string>());
                        groups[key] = group;
                        order.Add(key);
                    }

                    if (!group.SheetNames.Contains(sheetName, StringComparer.OrdinalIgnoreCase))
                        group.SheetNames.Add(sheetName);
                }
            }
        }

        return order
            .Select(key => new ExternalReferenceGroup(key, groups[key].Book, groups[key].SheetNames))
            .ToList();
    }

    private static string NormalizeBookKey(string book)
    {
        var trimmed = book.Trim();
        var separatorIndex = trimmed.LastIndexOfAny(['/', '\\']);
        return separatorIndex >= 0 ? trimmed[(separatorIndex + 1)..] : trimmed;
    }

    private readonly record struct ExternalReferenceGroup(string BookKey, string Book, List<string> SheetNames);
}
