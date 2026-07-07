using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxNamedRangeMapper
{
    private static readonly HashSet<string> ExcelReservedDefinedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Print_Area",
        "Print_Titles",
        "_FilterDatabase",
        "Criteria",
        "Database",
        "Extract",
        "Consolidate_Area"
    };

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
                if (string.IsNullOrWhiteSpace(refersToBody) || !IsFormulaExpression(refersToBody))
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

                if (IsFormulaExpression(refersToBody))
                {
                    // Named formula: store the bare expression for on-demand evaluation.
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
                // rectangle, so we keep the first area (the one Excel itself treats as primary for
                // Name Box navigation) and surface a warning naming every area that had to be
                // dropped, instead of discarding them silently.
                IXLRange? xlRange = null;
                var extraAreaCount = 0;
                try
                {
                    foreach (var candidateRange in namedRange.Ranges)
                    {
                        if (xlRange is null)
                        {
                            xlRange = candidateRange;
                            continue;
                        }

                        extraAreaCount++;
                    }
                }
                catch (Exception ex)
                {
                    warnings?.Add($"[named-ranges] Named range '{namedRange.Name}' could not expose its range and was skipped: {ex.Message}");
                    // ClosedXML failed — skip this name.
                }

                if (xlRange is null)
                    continue;

                if (extraAreaCount > 0)
                {
                    var extraAreaNoun = extraAreaCount == 1 ? "area was" : "areas were";
                    warnings?.Add(
                        $"[named-ranges] Named range '{namedRange.Name}' refers to a multi-area (union) reference " +
                        $"('{refersToBody}'); only the first area ('{xlRange.RangeAddress}') was kept and " +
                        $"{extraAreaCount} additional {extraAreaNoun} dropped " +
                        "because multi-area named ranges are not yet supported.");
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

                if (scopeSheetId is { } sid)
                    workbook.DefineNamedRange(namedRange.Name, new GridRange(start, end), metadata: null, sid);
                else
                    workbook.DefineNamedRange(namedRange.Name, new GridRange(start, end));
            }
            catch (Exception ex)
            {
                warnings?.Add($"[named-ranges] Named range '{namedRange.Name}' could not be loaded and was skipped: {ex.Message}");
                // Skip any named range that cannot be mapped into the workbook model.
            }
        }
    }

    /// <summary>
    /// Returns true when the refers-to expression is a formula (function call, arithmetic, etc.)
    /// rather than a plain cell/range reference like Sheet1!$A$1:$B$2 or Table[Column].
    /// <para>
    /// Detection strategy: scan for operators and parentheses that appear OUTSIDE of single-quoted
    /// sheet-name sections. A plain range reference has sheet names quoted with apostrophes
    /// ('Sheet Name'!$A$1) and cell addresses that contain only alphanumerics, $, !, and :.
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
            if (ch is '(' or ')' or '+' or '-' or '*' or '/' or '^' or '&' or '%')
                return true;
        }
        return false;
    }

    public static void Save(Workbook workbook, XLWorkbook xlWorkbook, List<string>? warnings = null)
    {
        foreach (var (name, range) in workbook.NamedRanges)
        {
            SaveWorkbookDefinedName(workbook, xlWorkbook, name, range, warnings);
        }

        foreach (var (key, range) in workbook.ScopedNamedRanges)
        {
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
                if (existingByKey.TryGetValue(key, out var existing))
                {
                    if (!string.Equals(existing.Value, entry.Text, StringComparison.Ordinal))
                    {
                        existing.Value = entry.Text;
                        changed = true;
                    }
                    continue;
                }

                var element = new XElement(workbookNs + "definedName", new XAttribute("name", entry.Name), entry.Text);
                if (entry.LocalSheetId is { } localSheetId)
                    element.SetAttributeValue("localSheetId", localSheetId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                definedNames.Add(element);
                existingByKey[key] = element;
                changed = true;
            }

            // Remove any on-disk defined name that is no longer present in the live model (e.g. the
            // user deleted it via the Name Manager). Reserved/Excel-internal names (Print_Area, etc.)
            // and unrecognized entries with a malformed name are left untouched since CreateDefinedNameEntries
            // never yields them and they are not owned by the model round-trip.
            foreach (var (key, existing) in existingByKey)
            {
                if (liveKeys.Contains(key))
                    continue;

                var existingName = existing.Attribute("name")?.Value;
                if (IsExcelReservedDefinedName(existingName))
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
            if (IsExcelReservedDefinedName(name) ||
                !TryFormatRangeAddress(workbook, range, xlWorkbook: null, out var address))
                continue;

            yield return new DefinedNameEntry(name, null, address);
        }

        foreach (var (key, range) in workbook.ScopedNamedRanges)
        {
            if (IsExcelReservedDefinedName(key.Name) ||
                !TryGetLocalSheetId(workbook, key.Sheet, out var localSheetId) ||
                !TryFormatRangeAddress(workbook, range, xlWorkbook: null, out var address))
                continue;

            yield return new DefinedNameEntry(key.Name, localSheetId, address);
        }

        foreach (var (name, formulaText) in workbook.NamedFormulas)
        {
            if (IsExcelReservedDefinedName(name) || workbook.ValidateNamedRangeName(name) is not null)
                continue;

            yield return new DefinedNameEntry(name, null, FormatDefinedNameFormulaForXml(formulaText));
        }

        foreach (var (key, formulaText) in workbook.ScopedNamedFormulas)
        {
            if (IsExcelReservedDefinedName(key.Name) ||
                workbook.ValidateNamedRangeName(key.Name) is not null ||
                !TryGetLocalSheetId(workbook, key.Sheet, out var localSheetId))
                continue;

            yield return new DefinedNameEntry(key.Name, localSheetId, FormatDefinedNameFormulaForXml(formulaText));
        }
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

            xlWorkbook.DefinedNames.Add(name, address);
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

            xlScopeSheet.DefinedNames.Add(name, address);
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

    private sealed record DefinedNameEntry(string Name, int? LocalSheetId, string Text);

    internal static bool IsExcelReservedDefinedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        var trimmedName = name.Trim();
        return trimmedName.StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase) ||
               trimmedName.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase) ||
               ExcelReservedDefinedNames.Contains(trimmedName);
    }
}
