using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxNamedRangeMapper
{
    // R66-io-defined-names-scope-6-1: only the built-ins FreeX has DEDICATED handling for (via
    // ClosedXML's PageSetup.PrintAreas/PrintTitleRows/PrintTitleColumns for the first two, and the
    // AutoFilter-derived FilterDatabaseDefinedName const below for the third) belong in this bare
    // (unprefixed) reserved set. "Criteria"/"Database"/"Extract"/"Consolidate_Area" are NOT Excel
    // built-ins in their bare form — those are only reserved when Excel itself writes them with the
    // "_xlnm." prefix (handled by IsExcelReservedDefinedName's prefix check below). A bare user-
    // created name like "Database" (e.g. for a legacy Data > Consolidate/Advanced-Filter range) is a
    // perfectly legitimate ordinary defined name and must be loaded/saved like any other — treating
    // it as reserved silently dropped it on load and refused it on save.
    private static readonly HashSet<string> ExcelReservedDefinedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Print_Area",
        "Print_Titles",
        "_FilterDatabase",
    };

    /// <summary>
    /// The canonical OOXML built-in defined-name identifier for a sheet's AutoFilter database range
    /// (ECMA-376 built-in names use the "_xlnm." prefix; see <c>ST_DefinedNames</c>). Unlike the
    /// other Excel-reserved names above (Print_Area, etc., which FreeX never models and always
    /// treats as pure passthrough), this one IS actively managed: FreeX derives it from each sheet's
    /// live AutoFilter range on every save (see <see cref="CreateDefinedNameEntries"/>) so it never
    /// goes stale relative to the worksheet's own &lt;autoFilter ref=...&gt; element written by
    /// <see cref="XlsxWorksheetAutoFilterXmlMapper"/> (R49-io-defined-name-scope-3-2).
    /// </summary>
    private const string FilterDatabaseDefinedName = "_xlnm._FilterDatabase";

    private static bool IsFilterDatabaseDefinedName(string? name) =>
        string.Equals(name?.Trim(), FilterDatabaseDefinedName, StringComparison.OrdinalIgnoreCase);

    public static void Load(XLWorkbook xlWorkbook, Workbook workbook, List<string>? warnings = null)
    {
        // Load workbook-scoped defined names.
        LoadDefinedNames(xlWorkbook.DefinedNames, workbook, scopeSheetId: null, warnings);

        // Load sheet-scoped defined names. Excel allows the same name at both workbook scope and
        // a specific sheet scope simultaneously. Sheet-scoped names win during resolution when the
        // formula's context sheet matches the name's scope sheet.
        foreach (var xlSheet in xlWorkbook.Worksheets)
        {
            var sheet = workbook.GetSheet(xlSheet.Name);
            if (sheet is null)
                continue;

            LoadDefinedNames(xlSheet.DefinedNames, workbook, scopeSheetId: sheet.Id, warnings);
        }
    }

    public static void LoadWorkbookDefinedNameFormulasFromPackage(Stream packageStream, Workbook workbook, List<string>? warnings = null)
    {
        try
        {
            packageStream.Position = 0;
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var definedName in workbookXml.Root?
                         .Element(workbookNs + "definedNames")?
                         .Elements(workbookNs + "definedName")
                     ?? [])
            {
                var name = definedName.Attribute("name")?.Value.Trim();
                if (IsExcelReservedDefinedName(name) ||
                    workbook.ValidateNamedRangeName(name!) is not null)
                {
                    continue;
                }

                var refersToBody = definedName.Value.Trim();
                if (refersToBody.StartsWith('='))
                    refersToBody = refersToBody[1..].Trim();
                if (string.IsNullOrWhiteSpace(refersToBody) ||
                    !(IsFormulaExpression(refersToBody) || IsSheetSpanRefersTo(refersToBody) ||
                      IsBareDefinedNameAliasRefersTo(workbook, refersToBody) ||
                      IsConstantLiteralRefersTo(refersToBody)))
                    continue;

                var localSheetIdText = definedName.Attribute("localSheetId")?.Value;
                if (int.TryParse(localSheetIdText, out var localSheetId))
                {
                    if (localSheetId < 0 || localSheetId >= workbook.Sheets.Count)
                        continue;

                    var sheetId = workbook.Sheets[localSheetId].Id;
                    if (!workbook.ScopedNamedFormulas.ContainsKey((name!, sheetId)))
                        workbook.DefineNamedFormula(name!, refersToBody, sheetId);
                }
                else
                {
                    workbook.NamedFormulas.TryAdd(name!, refersToBody);
                }
            }
        }
        catch (Exception ex)
        {
            warnings?.Add($"[named-ranges] Workbook defined-name formulas could not be loaded from package XML: {ex.Message}");
        }
    }

    private static void LoadDefinedNames(
        IXLDefinedNames namedRanges,
        Workbook workbook,
        SheetId? scopeSheetId,
        List<string>? warnings)
    {
        foreach (var namedRange in namedRanges)
        {
            try
            {
                if (IsExcelReservedDefinedName(namedRange.Name))
                    continue;

                // Use the raw RefersTo text as the primary discriminant.
                // ClosedXML's Ranges property may return cell references found *inside* a formula
                // (e.g. for DATE(Sheet1!$C$13,...) it yields $C$13), which is NOT the named range
                // — it's just a constituent reference. We must classify the refers-to expression
                // first and only use Ranges when the refers-to is a plain range reference.
                var refersTo = namedRange.RefersTo?.Trim();
                if (string.IsNullOrWhiteSpace(refersTo))
                    continue;

                // Strip the leading '=' if present.
                var refersToBody = refersTo.StartsWith('=') ? refersTo[1..].Trim() : refersTo;

                if (IsFormulaExpression(refersToBody) || IsSheetSpanRefersTo(refersToBody) ||
                    IsBareDefinedNameAliasRefersTo(workbook, refersToBody) ||
                    IsConstantLiteralRefersTo(refersToBody))
                {
                    // Named formula, a 3-D sheet-span reference (e.g. Sheet1:Sheet3!$A$1), a bare
                    // alias to another defined name (e.g. RefersTo="Name1"), or a constant literal
                    // (e.g. RefersTo=0.21 or ="Hello", R66-io-defined-names-scope-6-2): store the
                    // bare expression/opaque refers-to text for on-demand evaluation/round-trip. None
                    // of these can be represented by the single-rectangle GridRange model below (and
                    // ClosedXML's own namedRange.Ranges enumerates to zero items for all of them), so
                    // they must be routed through this opaque-preserving branch instead of falling
                    // into the "plain range" branch, where they would otherwise be silently dropped.
                    if (workbook.ValidateNamedRangeName(namedRange.Name) is null)
                    {
                        if (scopeSheetId is { } fid)
                            workbook.DefineNamedFormula(namedRange.Name, refersToBody, fid);
                        else
                            workbook.NamedFormulas[namedRange.Name] = refersToBody;
                    }
                    continue;
                }

                // Plain range reference: resolve through ClosedXML. A defined name's RefersTo may be
                // a multi-area (union) reference — one comma-separated area per entry in
                // namedRange.Ranges (e.g. Sheet1!$A$1,Sheet1!$C$1 created via Ctrl-click in Excel's
                // Name Manager). The in-memory model (GridRange) can only represent a single
                // rectangle, so a union cannot be resolved into one. Rather than truncating to the
                // first area (which would then permanently overwrite the on-disk union text with a
                // single-area address on the next save — silent, irreversible data loss), we keep the
                // FULL refers-to text verbatim as an opaque named formula, the same mechanism already
                // used above for 3-D sheet spans and name aliases, so it round-trips unchanged.
                IXLRange? xlRange = null;
                var areaCount = 0;
                try
                {
                    foreach (var candidateRange in namedRange.Ranges)
                    {
                        areaCount++;
                        xlRange ??= candidateRange;
                    }
                }
                catch (Exception ex)
                {
                    warnings?.Add($"[named-ranges] Named range '{namedRange.Name}' could not expose its range and was skipped: {ex.Message}");
                    // ClosedXML failed — skip this name.
                }

                if (xlRange is null)
                    continue;

                if (areaCount > 1)
                {
                    // Multi-area (union) reference: not representable as a single GridRange. Preserve
                    // the raw refers-to text verbatim so a later save re-emits the SAME union text
                    // instead of collapsing it to a single (truncated) area.
                    if (workbook.ValidateNamedRangeName(namedRange.Name) is null)
                    {
                        if (scopeSheetId is { } unionScopeId)
                            workbook.DefineNamedFormula(namedRange.Name, refersToBody, unionScopeId);
                        else
                            workbook.NamedFormulas[namedRange.Name] = refersToBody;
                    }
                    continue;
                }

                if (HasRelativeReferenceComponent(refersToBody))
                {
                    // R66-io-defined-names-scope-6-3: a relative-reference defined name (e.g.
                    // Sheet1!A1, no $) has Excel's shift-by-using-cell semantics — GridRange is
                    // absolute-only (a fixed CellAddress pair), so resolving straight into one here
                    // would silently FREEZE the name to today's absolute address and lose that
                    // semantics forever on the very next save. Route it through the same opaque
                    // named-formula-preserving mechanism used for unions/aliases/sheet-spans instead:
                    // this both round-trips the exact original (relative) refers-to text unchanged
                    // AND gets genuine per-using-cell relative resolution for free, because
                    // FormulaEvaluator.ApplyRelativeNameAnchor already re-anchors a named FORMULA's
                    // non-$ references to whichever cell is using the name — the same mechanism a
                    // formula-refersTo name already relies on.
                    if (workbook.ValidateNamedRangeName(namedRange.Name) is null)
                    {
                        if (scopeSheetId is { } relativeScopeId)
                            workbook.DefineNamedFormula(namedRange.Name, refersToBody, relativeScopeId);
                        else
                            workbook.NamedFormulas[namedRange.Name] = refersToBody;
                    }
                    continue;
                }

                var firstCell = xlRange.FirstCell();
                var lastCell = xlRange.LastCell();
                var sheet = workbook.GetSheet(firstCell.Worksheet.Name);
                if (sheet is null)
                    continue;

                var start = new CellAddress(
                    sheet.Id,
                    (uint)firstCell.Address.RowNumber,
                    (uint)firstCell.Address.ColumnNumber);
                var end = new CellAddress(
                    sheet.Id,
                    (uint)lastCell.Address.RowNumber,
                    (uint)lastCell.Address.ColumnNumber);

                var metadata = new NamedRangeMetadata("Workbook", namedRange.Comment ?? "", !namedRange.Visible);

                if (scopeSheetId is { } sid)
                    workbook.DefineNamedRange(namedRange.Name, new GridRange(start, end), metadata, sid);
                else
                    workbook.DefineNamedRange(namedRange.Name, new GridRange(start, end), metadata);
            }
            catch (Exception ex)
            {
                warnings?.Add($"[named-ranges] Named range '{namedRange.Name}' could not be loaded and was skipped: {ex.Message}");
                // Skip any named range that cannot be mapped into the workbook model.
            }
        }
    }

    /// <summary>
    /// Returns true when the refers-to expression is a formula (function call, arithmetic, array
    /// constant, etc.) rather than a plain cell/range reference like Sheet1!$A$1:$B$2 or
    /// Table[Column].
    /// <para>
    /// Detection strategy: scan for operators, parentheses, and array-constant braces that appear
    /// OUTSIDE of single-quoted sheet-name sections. A plain range reference has sheet names quoted
    /// with apostrophes ('Sheet Name'!$A$1) and cell addresses that contain only alphanumerics, $,
    /// !, and :. A leading '{' uniquely identifies an array constant (e.g. <c>{1,2;3,4}</c> or
    /// <c>{"Mon","Tue","Wed"}</c>) — a valid Excel defined-name form that ClosedXML's
    /// <c>IXLDefinedName.Ranges</c> enumerates to zero items for (no exception), so without this
    /// check the plain-range branch in <see cref="LoadDefinedNames"/> would silently drop the name.
    /// </para>
    /// </summary>
    private static bool IsFormulaExpression(string refersToBody)
    {
        bool inQuote = false;
        for (int i = 0; i < refersToBody.Length; i++)
        {
            var ch = refersToBody[i];
            if (ch == '\'')
            {
                // Handle escaped apostrophes ('') inside quoted sheet names
                if (inQuote && i + 1 < refersToBody.Length && refersToBody[i + 1] == '\'')
                {
                    i++; // skip escaped apostrophe
                    continue;
                }
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            // Outside a quoted section: any of these characters indicates a formula expression.
            // Plain range refs only have: alphanumeric, $, !, :, comma (multi-area), space.
            if (ch is '(' or ')' or '+' or '-' or '*' or '/' or '^' or '&' or '%' or '{' or '}')
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true when the refers-to expression is a bare reference to ANOTHER defined name (an
    /// alias, e.g. name "Name2" with RefersTo <c>=Name1</c>) — a legal and commonly-used Excel
    /// pattern. <see cref="IsFormulaExpression"/> classifies this as a plain range (no
    /// operator/paren/brace characters) and it has no '!' so <see cref="IsSheetSpanRefersTo"/> also
    /// misses it; ClosedXML's <c>IXLDefinedName.Ranges</c> enumerates to zero items for a bare
    /// identifier (no exception), so the plain-range branch in <see cref="LoadDefinedNames"/> would
    /// silently drop the name.
    /// <para>
    /// Detection: a genuine plain cell/range reference is always either sheet-qualified (containing
    /// '!', which <see cref="Workbook.ValidateNamedRangeName"/> rejects as an invalid name
    /// character) or shaped like a bare cell address / structured table reference (e.g. "A1" or
    /// "Table1[Column1]", both of which <c>ValidateNamedRangeName</c> also rejects — the former for
    /// looking like a cell reference, the latter for containing '[' / ']'). So any refers-to body
    /// that IS itself a syntactically valid defined name can only be an alias to another name.
    /// </para>
    /// </summary>
    private static bool IsBareDefinedNameAliasRefersTo(Workbook workbook, string refersToBody) =>
        workbook.ValidateNamedRangeName(refersToBody) is null;

    /// <summary>
    /// Returns true when the refers-to expression is a constant literal — a bare number (e.g.
    /// <c>0.21</c>), a double-quoted text literal (e.g. <c>"Hello"</c>), or a boolean literal
    /// (<c>TRUE</c>/<c>FALSE</c>) — rather than a range reference or formula. Excel's Name Manager
    /// allows a defined name's RefersTo to be a plain constant (e.g. a "TaxRate" name whose RefersTo
    /// is literally <c>=0.21</c>), which formulas then use like <c>=B2*TaxRate</c>.
    /// <see cref="IsFormulaExpression"/> classifies these as "not a formula" (no operator/paren/
    /// brace characters), and ClosedXML's <c>IXLDefinedName.Ranges</c> enumerates to zero items for
    /// them (no exception, nothing to resolve), so without this check they fall all the way into the
    /// plain-range branch of <see cref="LoadDefinedNames"/> and are silently dropped instead of being
    /// captured as a named formula/constant (R66-io-defined-names-scope-6-2). A numeric literal
    /// additionally fails <see cref="Workbook.ValidateNamedRangeName"/> (names can't start with a
    /// digit), so <see cref="IsBareDefinedNameAliasRefersTo"/> never catches it either.
    /// </summary>
    private static bool IsConstantLiteralRefersTo(string refersToBody)
    {
        if (string.IsNullOrEmpty(refersToBody))
            return false;

        if (string.Equals(refersToBody, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(refersToBody, "FALSE", StringComparison.OrdinalIgnoreCase))
            return true;

        if (refersToBody.Length >= 2 && refersToBody[0] == '"' && refersToBody[^1] == '"')
            return true;

        return double.TryParse(
            refersToBody,
            System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture,
            out _);
    }

    /// <summary>
    /// Returns true when the refers-to expression is a 3-D "sheet span" reference, e.g.
    /// <c>Sheet1:Sheet3!$A$1</c> or the quoted form <c>'Sheet1:Sheet3'!$A$1</c> (both valid inside
    /// e.g. <c>=SUM(MySpan)</c> in Excel). <see cref="IsFormulaExpression"/> classifies these as
    /// plain range references (no operator/paren characters), but ClosedXML's <c>IXLDefinedName.Ranges</c>
    /// enumerates to zero items for them (no exception), so the plain-range branch in
    /// <see cref="LoadDefinedNames"/> would silently drop the name. Detection: scan up to the first
    /// '!' that is outside a quoted sheet-name section (the "sheet name" portion of the reference)
    /// and check whether that portion contains a ':' — a plain single-sheet range's colon always
    /// appears AFTER the '!' (inside the cell range, e.g. Sheet1!$A$1:$B$2), never before it.
    /// </summary>
    private static bool IsSheetSpanRefersTo(string refersToBody)
    {
        bool inQuote = false;
        for (int i = 0; i < refersToBody.Length; i++)
        {
            var ch = refersToBody[i];
            if (ch == '\'')
            {
                if (inQuote && i + 1 < refersToBody.Length && refersToBody[i + 1] == '\'')
                {
                    i++; // skip escaped apostrophe
                    continue;
                }
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && ch == '!')
                return refersToBody[..i].Contains(':');
        }
        return false;
    }

    /// <summary>
    /// Returns true when the (single-area) refers-to address has at least one relative (non-$)
    /// row or column component, e.g. <c>Sheet1!A1</c>, <c>Sheet1!$A1:$B2</c>, or <c>Sheet1!A:A</c>.
    /// Excel treats such a reference as relative to whatever cell is USING the name (its implicit
    /// anchor is A1 of the using cell's sheet), unlike a fully <c>$</c>-anchored reference, which
    /// always means the same cell no matter where it's used. Used by <see cref="LoadDefinedNames"/>
    /// to route a relative-reference name through the opaque named-formula-preserving branch instead
    /// of freezing it into an absolute <see cref="GridRange"/> (R66-io-defined-names-scope-6-3).
    /// </summary>
    private static bool HasRelativeReferenceComponent(string refersToBody)
    {
        // Only inspect the address portion after the (optional) sheet-name qualifier, so letters
        // that happen to appear inside the sheet name itself are never mistaken for a column
        // reference.
        var bangIndex = refersToBody.LastIndexOf('!');
        var addressPart = bangIndex >= 0 ? refersToBody[(bangIndex + 1)..].Trim() : refersToBody.Trim();
        if (addressPart.Length == 0)
            return false;

        foreach (var bound in addressPart.Split(':'))
        {
            if (!IsFullyAbsoluteBound(bound))
                return true;
        }

        return false;
    }

    // Matches a fully $-anchored single-cell address ($A$1), whole-column ($A), or whole-row ($1)
    // bound. Anything else (missing a $ before the column letters and/or the row digits) has a
    // relative component.
    private static readonly Regex FullyAbsoluteBoundPattern =
        new(@"^(\$[A-Za-z]{1,3}\$\d{1,7}|\$[A-Za-z]{1,3}|\$\d{1,7})$", RegexOptions.Compiled);

    private static bool IsFullyAbsoluteBound(string bound) =>
        FullyAbsoluteBoundPattern.IsMatch(bound.Trim());

    public static void Save(Workbook workbook, XLWorkbook xlWorkbook, List<string>? warnings = null)
    {
        foreach (var (name, range) in workbook.NamedRanges)
        {
            // R62-commands-name-box-6-2: skip a NamedRanges entry that collides with a
            // NamedFormulas entry of the same name (see CreateDefinedNameEntries' matching guard).
            // Without this, ClosedXML's xlWorkbook.DefinedNames.Add would be called twice for the
            // same name — the NamedRanges call below succeeds first, then the NamedFormulas call
            // further down throws (Excel/ClosedXML disallow a duplicate defined name), which
            // SaveWorkbookDefinedName's catch-all silently swallows as a spurious "could not be
            // saved and was skipped" warning for the user's real, authoritative name — even though
            // no data is actually lost (ApplyPackagePostProcessing's SaveToPackage pass corrects the
            // text afterward). Skipping the non-authoritative NamedRanges side here avoids the
            // ClosedXML exception (and the false-positive warning) entirely.
            if (workbook.NamedFormulas.ContainsKey(name))
                continue;

            SaveWorkbookDefinedName(workbook, xlWorkbook, name, range, warnings);
        }

        foreach (var (key, range) in workbook.ScopedNamedRanges)
        {
            if (workbook.ScopedNamedFormulas.ContainsKey(key))
                continue;

            SaveSheetScopedDefinedName(workbook, xlWorkbook, key.Name, key.Sheet, range, warnings);
        }

        foreach (var (name, formulaText) in workbook.NamedFormulas)
        {
            SaveWorkbookDefinedName(workbook, xlWorkbook, name, formulaText, warnings);
        }

        foreach (var (key, formulaText) in workbook.ScopedNamedFormulas)
        {
            SaveSheetScopedDefinedName(workbook, xlWorkbook, key.Name, key.Sheet, formulaText, warnings);
        }
    }

    public static void SaveToPackage(Workbook workbook, Stream packageStream, List<string>? warnings = null)
    {
        try
        {
            packageStream.Position = 0;
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var entries = CreateDefinedNameEntries(workbook).ToList();

            var definedNames = root.Element(workbookNs + "definedNames");
            if (definedNames is null)
            {
                if (entries.Count == 0)
                    return;

                definedNames = new XElement(workbookNs + "definedNames");
                InsertDefinedNamesElement(root, workbookNs, definedNames);
            }

            var existingByKey = definedNames
                .Elements(workbookNs + "definedName")
                .GroupBy(DefinedNameKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var changed = false;
            var liveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var key = DefinedNameKey(entry.Name, entry.LocalSheetId);
                liveKeys.Add(key);
                var escapedText = XlsxXmlTextEscaper.EscapeForXml(entry.Text);
                if (existingByKey.TryGetValue(key, out var existing))
                {
                    if (!string.Equals(existing.Value, escapedText, StringComparison.Ordinal))
                    {
                        existing.Value = escapedText;
                        changed = true;
                    }

                    var desiredHidden = entry.Hidden ? "1" : null;
                    if (!string.Equals(existing.Attribute("hidden")?.Value, desiredHidden, StringComparison.Ordinal))
                    {
                        existing.SetAttributeValue("hidden", desiredHidden);
                        changed = true;
                    }

                    // Escape like every other model-text site in the patch-save path: a defined
                    // name's free-text comment is user-typed, and an XML-illegal character in it
                    // would abort the whole save. Compare escaped-to-escaped so an already-escaped
                    // value does not look like a change on every save.
                    var desiredComment = string.IsNullOrEmpty(entry.Comment)
                        ? null
                        : XlsxXmlTextEscaper.EscapeForXml(entry.Comment);
                    if (!string.Equals(existing.Attribute("comment")?.Value, desiredComment, StringComparison.Ordinal))
                    {
                        existing.SetAttributeValue("comment", desiredComment);
                        changed = true;
                    }
                    continue;
                }

                var element = new XElement(workbookNs + "definedName", new XAttribute("name", entry.Name), escapedText);
                if (entry.LocalSheetId is { } localSheetId)
                    element.SetAttributeValue("localSheetId", localSheetId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (entry.Hidden)
                    element.SetAttributeValue("hidden", "1");
                if (!string.IsNullOrEmpty(entry.Comment))
                    element.SetAttributeValue("comment", XlsxXmlTextEscaper.EscapeForXml(entry.Comment));
                definedNames.Add(element);
                existingByKey[key] = element;
                changed = true;
            }

            // Remove any on-disk defined name that is no longer present in the live model (e.g. the
            // user deleted it via the Name Manager). Reserved/Excel-internal names (Print_Area, etc.)
            // and unrecognized entries with a malformed name are left untouched since CreateDefinedNameEntries
            // never yields them and they are not owned by the model round-trip. _xlnm._FilterDatabase is
            // the one exception (R49-io-defined-name-scope-3-2): CreateDefinedNameEntries DOES emit it
            // whenever the owning sheet still has an AutoFilter, so its absence from liveKeys genuinely
            // means the AutoFilter was cleared and the stale name must be removed like any other name.
            foreach (var (key, existing) in existingByKey)
            {
                if (liveKeys.Contains(key))
                    continue;

                var existingName = existing.Attribute("name")?.Value;
                if (IsExcelReservedDefinedName(existingName) && !IsFilterDatabaseDefinedName(existingName))
                    continue;

                existing.Remove();
                changed = true;
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[XlsxNamedRangeMapper] Defined names package post-processing failed: {ex.Message}");
            warnings?.Add("[defined-names] Defined names could not be post-processed.");
        }
    }

    /// <summary>
    /// Returns the set of defined-name keys (name + local-sheet-scope, in the same
    /// "<c>namelocalSheetId</c>" format used by <see cref="SaveToPackage"/>) that are currently
    /// live in the workbook model. Used by the patch-save defined-name restoration path
    /// (<c>XlsxFileAdapter.SourcePackageSnapshot.RestorePatchWorkbookDefinedNames</c>) so a defined
    /// name the user deleted from the model is not resurrected from the pristine source snapshot.
    /// </summary>
    public static HashSet<string> GetLiveDefinedNameKeys(Workbook workbook)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in CreateDefinedNameEntries(workbook))
            keys.Add(DefinedNameKey(entry.Name, entry.LocalSheetId));
        return keys;
    }

    private static IEnumerable<DefinedNameEntry> CreateDefinedNameEntries(Workbook workbook)
    {
        foreach (var (name, range) in workbook.NamedRanges)
        {
            // R62-commands-name-box-6-2: a name can end up present in BOTH NamedRanges and
            // NamedFormulas (e.g. a multi-area/union name loaded into NamedFormulas because a
            // single GridRange cannot represent it, followed by the Name Box's create-on-unknown
            // fallback defining a colliding single-area NamedRanges entry with the same text).
            // NamedFormulas is authoritative for a colliding name — skip the NamedRanges entry so
            // CreateDefinedNameEntries never yields two entries for the same (name, scope) key.
            if (workbook.NamedFormulas.ContainsKey(name))
                continue;

            if (IsExcelReservedDefinedName(name) ||
                !TryFormatRangeAddress(workbook, range, xlWorkbook: null, out var address))
                continue;

            var hasMetadata = workbook.TryGetNamedRangeMetadata(name, out var metadata);
            yield return new DefinedNameEntry(
                name,
                null,
                address,
                hasMetadata && metadata.Hidden,
                hasMetadata ? metadata.Comment : null);
        }

        foreach (var (key, range) in workbook.ScopedNamedRanges)
        {
            // Same collision guard as above, for the sheet-scoped pair.
            if (workbook.ScopedNamedFormulas.ContainsKey(key))
                continue;

            if (IsExcelReservedDefinedName(key.Name) ||
                !TryGetLocalSheetId(workbook, key.Sheet, out var localSheetId) ||
                !TryFormatRangeAddress(workbook, range, xlWorkbook: null, out var address))
                continue;

            var hasMetadata = workbook.TryGetScopedNamedRangeMetadata(key.Name, key.Sheet, out var metadata);
            yield return new DefinedNameEntry(
                key.Name,
                localSheetId,
                address,
                hasMetadata && metadata.Hidden,
                hasMetadata ? metadata.Comment : null);
        }

        foreach (var (name, formulaText) in workbook.NamedFormulas)
        {
            if (IsExcelReservedDefinedName(name) || workbook.ValidateNamedRangeName(name) is not null)
                continue;

            // A name can land here as a formula-backed "#REF!" left behind by deleting a sheet
            // its range referred to (Workbook.RemoveNamedRangesForSheet) rather than being
            // authored as a formula from the start. Excel keeps that name's Hidden flag and
            // Comment across the conversion, so fall back to the same metadata lookup used for
            // NamedRanges above instead of hard-coding hidden:false/comment:null.
            var hasMetadata = workbook.TryGetNamedRangeMetadata(name, out var metadata);
            yield return new DefinedNameEntry(
                name,
                null,
                FormatDefinedNameFormulaForXml(formulaText),
                hasMetadata && metadata.Hidden,
                hasMetadata ? metadata.Comment : null);
        }

        foreach (var (key, formulaText) in workbook.ScopedNamedFormulas)
        {
            if (IsExcelReservedDefinedName(key.Name) ||
                workbook.ValidateNamedRangeName(key.Name) is not null ||
                !TryGetLocalSheetId(workbook, key.Sheet, out var localSheetId))
                continue;

            // Same fallback as the workbook-global NamedFormulas branch above, for a
            // sheet-scoped name converted to "#REF!" by a cross-sheet delete.
            var hasMetadata = workbook.TryGetScopedNamedRangeMetadata(key.Name, key.Sheet, out var metadata);
            yield return new DefinedNameEntry(
                key.Name,
                localSheetId,
                FormatDefinedNameFormulaForXml(formulaText),
                hasMetadata && metadata.Hidden,
                hasMetadata ? metadata.Comment : null);
        }

        // R49-io-defined-name-scope-3-2: emit/keep-in-sync the built-in _xlnm._FilterDatabase
        // sheet-scoped name for every sheet that currently has an AutoFilter, so it always matches
        // the live <autoFilter ref=...> range that XlsxWorksheetAutoFilterXmlMapper writes into that
        // sheet's own worksheet XML (rather than being a stale/absent passthrough). A sheet with no
        // AutoFilter yields nothing here, so the add/update/remove reconciliation in SaveToPackage
        // drops any stale _xlnm._FilterDatabase left over from a cleared AutoFilter.
        for (var localSheetId = 0; localSheetId < workbook.Sheets.Count; localSheetId++)
        {
            var sheet = workbook.Sheets[localSheetId];
            var autoFilterReference = XlsxWorksheetAutoFilterXmlMapper.GetEffectiveReference(sheet.AutoFilter);
            if (string.IsNullOrWhiteSpace(autoFilterReference) ||
                !TryFormatAutoFilterRangeAddress(workbook, sheet, autoFilterReference, out var filterDatabaseAddress))
            {
                continue;
            }

            yield return new DefinedNameEntry(FilterDatabaseDefinedName, localSheetId, filterDatabaseAddress, Hidden: true, Comment: null);
        }
    }

    /// <summary>
    /// Formats a sheet's live AutoFilter range reference (e.g. "A1:C10", as read straight off the
    /// worksheet's own &lt;autoFilter ref=...&gt; attribute) as an absolute, sheet-qualified defined-name
    /// refersTo address (e.g. "Sheet1!$A$1:$C$10"), for the _xlnm._FilterDatabase entry in
    /// <see cref="CreateDefinedNameEntries"/>. Returns false (and leaves the sheet's _FilterDatabase
    /// name unmanaged for this save) for a reference this parser cannot understand, rather than
    /// throwing and aborting the whole defined-names save.
    /// </summary>
    private static bool TryFormatAutoFilterRangeAddress(
        Workbook workbook,
        Sheet sheet,
        string autoFilterReference,
        out string address)
    {
        address = "";
        GridRange range;
        try
        {
            range = GridRange.ParseCellOrRange(autoFilterReference, sheet.Id);
        }
        catch (FormatException)
        {
            return false;
        }

        return TryFormatRangeAddress(workbook, range, xlWorkbook: null, out address);
    }

    // Mirrors XlsxWorkbookSchemaNormalizer.WorkbookChildOrder's CT_Workbook child sequence so a
    // newly-created <definedNames> element (patch-save path, which does not run the full workbook
    // schema normalizer) is inserted after sheets/functionGroups/externalReferences and before
    // calcPr/oleSize/etc, instead of unconditionally right after <sheets/>. Placing it before
    // <externalReferences/> violates the CT_Workbook sequence and triggers Excel's repair prompt.
    private static readonly string[] WorkbookElementsBeforeDefinedNames =
    {
        "sheets",
        "functionGroups",
        "externalReferences",
    };

    private static readonly string[] WorkbookElementsAfterDefinedNames =
    {
        "calcPr",
        "oleSize",
        "customWorkbookViews",
        "pivotCaches",
        "smartTagPr",
        "smartTagTypes",
        "webPublishing",
        "fileRecoveryPr",
        "webPublishObjects",
        "extLst",
    };

    private static void InsertDefinedNamesElement(XElement root, XNamespace workbookNs, XElement definedNames)
    {
        // Insert immediately after the last of sheets/functionGroups/externalReferences that is
        // present, in document order, so definedNames lands after all three per the schema.
        XElement? lastPrecedingSibling = null;
        foreach (var localName in WorkbookElementsBeforeDefinedNames)
        {
            var element = root.Element(workbookNs + localName);
            if (element is not null)
                lastPrecedingSibling = element;
        }

        if (lastPrecedingSibling is not null)
        {
            lastPrecedingSibling.AddAfterSelf(definedNames);
            return;
        }

        // No sheets/functionGroups/externalReferences element found (unexpected but be defensive):
        // insert before the first element that must follow definedNames, if any.
        foreach (var localName in WorkbookElementsAfterDefinedNames)
        {
            var element = root.Element(workbookNs + localName);
            if (element is not null)
            {
                element.AddBeforeSelf(definedNames);
                return;
            }
        }

        root.Add(definedNames);
    }

    private static string DefinedNameKey(XElement element) =>
        DefinedNameKey(
            element.Attribute("name")?.Value ?? string.Empty,
            int.TryParse(element.Attribute("localSheetId")?.Value, out var localSheetId) ? (int?)localSheetId : null);

    private static string DefinedNameKey(string name, int? localSheetId) =>
        $"{name}\u001f{localSheetId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty}";

    private static bool TryGetLocalSheetId(Workbook workbook, SheetId scopeSheetId, out int localSheetId)
    {
        for (var i = 0; i < workbook.Sheets.Count; i++)
        {
            if (!workbook.Sheets[i].Id.Equals(scopeSheetId))
                continue;

            localSheetId = i;
            return true;
        }

        localSheetId = -1;
        return false;
    }

    private static void SaveWorkbookDefinedName(
        Workbook workbook,
        XLWorkbook xlWorkbook,
        string name,
        GridRange range,
        List<string>? warnings)
    {
        try
        {
            if (IsExcelReservedDefinedName(name))
                return;

            if (!TryFormatRangeAddress(workbook, range, xlWorkbook, out var address))
                return;

            var hasMetadata = workbook.TryGetNamedRangeMetadata(name, out var metadata);
            var definedName = hasMetadata && !string.IsNullOrEmpty(metadata.Comment)
                ? xlWorkbook.DefinedNames.Add(name, address, metadata.Comment)
                : xlWorkbook.DefinedNames.Add(name, address);
            if (hasMetadata && metadata.Hidden)
                definedName.Visible = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[XlsxNamedRangeMapper] Skipping named range '{name}': {ex.Message}");
            warnings?.Add($"[named-range] Named range '{name}' could not be saved and was skipped.");
        }
    }

    private static void SaveSheetScopedDefinedName(
        Workbook workbook,
        XLWorkbook xlWorkbook,
        string name,
        SheetId scopeSheetId,
        GridRange range,
        List<string>? warnings)
    {
        try
        {
            if (IsExcelReservedDefinedName(name))
                return;

            var scopeSheet = workbook.GetSheet(scopeSheetId);
            if (scopeSheet is null || !xlWorkbook.TryGetWorksheet(scopeSheet.Name, out var xlScopeSheet))
                return;

            if (!TryFormatRangeAddress(workbook, range, xlWorkbook, out var address))
                return;

            var hasMetadata = workbook.TryGetScopedNamedRangeMetadata(name, scopeSheetId, out var metadata);
            var definedName = hasMetadata && !string.IsNullOrEmpty(metadata.Comment)
                ? xlScopeSheet.DefinedNames.Add(name, address, metadata.Comment)
                : xlScopeSheet.DefinedNames.Add(name, address);
            if (hasMetadata && metadata.Hidden)
                definedName.Visible = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[XlsxNamedRangeMapper] Skipping sheet-scoped named range '{name}': {ex.Message}");
            warnings?.Add($"[named-range] Sheet-scoped named range '{name}' could not be saved and was skipped.");
        }
    }

    private static void SaveWorkbookDefinedName(
        Workbook workbook,
        XLWorkbook xlWorkbook,
        string name,
        string formulaText,
        List<string>? warnings)
    {
        try
        {
            if (IsExcelReservedDefinedName(name))
                return;

            if (workbook.ValidateNamedRangeName(name) is not null)
                return;

            xlWorkbook.DefinedNames.Add(name, FormatDefinedNameFormula(formulaText));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[XlsxNamedRangeMapper] Skipping named formula '{name}': {ex.Message}");
            warnings?.Add($"[named-formula] Named formula '{name}' could not be saved and was skipped.");
        }
    }

    private static void SaveSheetScopedDefinedName(
        Workbook workbook,
        XLWorkbook xlWorkbook,
        string name,
        SheetId scopeSheetId,
        string formulaText,
        List<string>? warnings)
    {
        try
        {
            if (IsExcelReservedDefinedName(name))
                return;

            if (workbook.ValidateNamedRangeName(name) is not null)
                return;

            var scopeSheet = workbook.GetSheet(scopeSheetId);
            if (scopeSheet is null || !xlWorkbook.TryGetWorksheet(scopeSheet.Name, out var xlScopeSheet))
                return;

            xlScopeSheet.DefinedNames.Add(name, FormatDefinedNameFormula(formulaText));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[XlsxNamedRangeMapper] Skipping sheet-scoped named formula '{name}': {ex.Message}");
            warnings?.Add($"[named-formula] Sheet-scoped named formula '{name}' could not be saved and was skipped.");
        }
    }

    private static bool TryFormatRangeAddress(
        Workbook workbook,
        GridRange range,
        XLWorkbook? xlWorkbook,
        out string address)
    {
        address = "";

        var sheet = workbook.GetSheet(range.Start.Sheet);
        if (sheet is null)
            return false;

        if (xlWorkbook is not null && !xlWorkbook.TryGetWorksheet(sheet.Name, out _))
            return false;

        var startA1 = ToAbsoluteA1(range.Start);
        var endA1 = ToAbsoluteA1(range.End);
        address = $"{SheetNameFormatter.QuoteIfNeeded(sheet.Name)}!{startA1}:{endA1}";
        return true;
    }

    /// <summary>
    /// Formats a cell address as an absolute ($-anchored) A1 reference (e.g. "$B$7"). A defined
    /// name's refers-to formula MUST be absolute: Excel interprets a relative reference in a
    /// defined name relative to the active/using cell, so writing a bare "B7" (as
    /// <see cref="CellAddress.ToA1"/> does) silently shifts the name's meaning depending on where
    /// it is used and can trigger Excel's repair prompt for whole-column/row names.
    /// </summary>
    private static string ToAbsoluteA1(CellAddress address) =>
        $"${CellAddress.NumberToColumnName(address.Col)}${address.Row.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static string FormatDefinedNameFormula(string formulaText)
    {
        var trimmed = formulaText.Trim();
        return trimmed.StartsWith('=') ? trimmed : "=" + trimmed;
    }

    private static string FormatDefinedNameFormulaForXml(string formulaText)
    {
        var trimmed = formulaText.Trim();
        return trimmed.StartsWith('=') ? trimmed[1..].Trim() : trimmed;
    }

    private sealed record DefinedNameEntry(string Name, int? LocalSheetId, string Text, bool Hidden = false, string? Comment = null);

    internal static bool IsExcelReservedDefinedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        var trimmedName = name.Trim();
        return trimmedName.StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase) ||
               trimmedName.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase) ||
               ExcelReservedDefinedNames.Contains(trimmedName);
    }

    // A defined name's refersTo body never makes it into the model (NamedRanges/
    // NamedFormulas/ScopedNamedFormulas) when it is an external-workbook reference (e.g.
    // [1]Sheet1!$A$1) or a broken reference (#REF!). (R66-io-defined-names-scope-6-2: a constant
    // literal such as 0.21 or "Hello" USED to be unmodelable for the same reason but is now — see
    // IsConstantLiteralRefersTo — routed into NamedFormulas/ScopedNamedFormulas, so it is excluded
    // below rather than being lumped in with the two genuinely-unmodelable cases.) IsFormulaExpression
    // treats an external reference/broken reference as "not a formula" (no operator/parenthesis
    // characters), so LoadWorkbookDefinedNameFormulasFromPackage / LoadDefinedNames silently skip
    // them, and they are equally not a resolvable plain range reference (ClosedXML has nothing to
    // resolve for an external workbook index or a #REF! error). ValidateNamedRangeName only inspects
    // the NAME text, so it happily passes both, which would otherwise make a caller's
    // isModelRepresentable check true for content FreeX can never model - the defined-name
    // resurrection gates (RestorePatchWorkbookDefinedNames and XlsxWorkbookMetadataPreserver.
    // MergeDefinedNames) must detect that case directly (matching the same never-loaded-in-the-
    // first-place reasoning already applied to validator-rejected names) so they never mistake such
    // a name's absence from the live model for a user deletion.
    internal static bool IsUnmodelableDefinedNameRefersTo(string refersTo)
    {
        var body = refersTo.Trim();
        if (body.StartsWith('='))
            body = body[1..].Trim();

        if (body.Length == 0)
            return true;

        // R66-io-defined-names-scope-6-2: a constant literal (bare number, quoted text, or
        // TRUE/FALSE) IS modeled — LoadDefinedNames / LoadWorkbookDefinedNameFormulasFromPackage
        // route it into NamedFormulas/ScopedNamedFormulas via IsConstantLiteralRefersTo — so it must
        // not be treated as unmodelable, even though (being digit-leading or quote-leading) it would
        // otherwise fail the bare-cell-address heuristic below.
        if (IsConstantLiteralRefersTo(body))
            return false;

        // R87-io-external-links-5-1: a formula expression IS modeled — LoadDefinedNames /
        // LoadWorkbookDefinedNameFormulasFromPackage route anything IsFormulaExpression flags
        // (operators/parens/braces outside quoted sheet names) into NamedFormulas/
        // ScopedNamedFormulas as a live, opaque formula — even when that formula also happens to
        // embed an external-workbook reference (e.g. "=[1]Sheet1!$B$2*2" or
        // "=SUM([1]Sheet1!A1:A10)+Local!B1"). Such a name IS live/modeled, so it must not be
        // reported as unmodelable here regardless of what the unanchored external-ref/'#REF!'
        // checks below would otherwise match inside it — this call must mirror the loader's own
        // classification order (IsConstantLiteralRefersTo above, now IsFormulaExpression here) or
        // the liveness gate below misclassifies a genuinely-deleted live formula name as "never
        // modeled" and silently resurrects it from the pristine source on every save.
        if (IsFormulaExpression(body))
            return false;

        // Broken reference, anywhere in the body (Excel keeps these; FreeX has no #REF! model).
        if (body.Contains("#REF!", StringComparison.OrdinalIgnoreCase))
            return true;

        // External-workbook reference: '[<index>]SheetName!...' (optionally with the sheet name
        // single-quoted, e.g. '[1]Sheet1'!$A$1). FreeX only models references into the current
        // workbook, so any external-workbook marker is unmodelable regardless of what follows.
        var externalRefOpen = body.IndexOf('[');
        var externalRefClose = externalRefOpen >= 0 ? body.IndexOf(']', externalRefOpen + 1) : -1;
        if (externalRefOpen >= 0 &&
            externalRefClose > externalRefOpen &&
            int.TryParse(
                body.Substring(externalRefOpen + 1, externalRefClose - externalRefOpen - 1),
                out _))
        {
            return true;
        }

        // A plain range/cell reference always contains a '!' scope separator (SheetName!$A$1) or
        // is a bare cell/range address without one; formula expressions were already excluded by
        // the ValidateNamedRangeName/IsFormulaExpression checks made by the caller before this
        // helper runs on the remaining "not a formula" bodies. Anything left with no '!' and no
        // digit-bearing cell-address shape (e.g. a text/number/boolean constant such as 0.21 or
        // "Hello") is a constant literal, which is never loaded into the model.
        if (!body.Contains('!'))
        {
            var looksLikeBareCellAddress = body.Length > 0 &&
                (body[0] == '$' || char.IsLetter(body[0])) &&
                body.Any(char.IsDigit);
            if (!looksLikeBareCellAddress)
                return true;
        }

        return false;
    }
}
