using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using DOXV = DocumentFormat.OpenXml.FileFormatVersions;

/// <summary>
/// FreeX Sheet Fidelity — discovery-only tool that loads an .xlsx file through FreeX and
/// emits a thorough automated fidelity report covering load warnings, unsupported features,
/// structural inventory, formula parity, and round-trip schema validation.
/// </summary>
internal static class Program
{
    private const string DefaultWorkbookPath = @"E:\Users\quinn\Downloads\ExcelExamples1.xlsx";

    public static int Main(string[] args)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var xlsxPath = args.Length > 0 ? args[0] : DefaultWorkbookPath;

        if (args.Contains("--validate-only"))
        {
            using var d = SpreadsheetDocument.Open(xlsxPath, isEditable: false);
            var v = new OpenXmlValidator(DOXV.Microsoft365);
            int n = 0;
            foreach (var e in v.Validate(d).Where(ev => ev.ErrorType == ValidationErrorType.Schema))
            { n++; Console.WriteLine($"[{e.Id}] {e.Description} :: {e.Path?.XPath}"); }
            Console.WriteLine($"TOTAL SCHEMA ERRORS: {n}");
            return 0;
        }

        var outputDir = Path.Combine(Path.GetTempPath(), "sheetfidelity");
        Directory.CreateDirectory(outputDir);
        var reportPath = Path.Combine(outputDir, "REPORT.txt");

        var sb = new StringBuilder();
        void Emit(string line)
        {
            sb.AppendLine(line);
            Console.WriteLine(line);
        }

        Emit("================================================================================");
        Emit("  FreeX Sheet Fidelity Report");
        Emit($"  Generated : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Emit($"  Input file: {xlsxPath}");
        Emit($"  Report    : {reportPath}");
        Emit("================================================================================");
        Emit("");

        // =====================================================================
        // SECTION 1 — LOAD
        // =====================================================================
        Emit("--------------------------------------------------------------------------------");
        Emit("SECTION 1: LOAD");
        Emit("--------------------------------------------------------------------------------");

        Workbook workbook;
        XlsxLoadResult loadResult;
        try
        {
            using var stream = File.OpenRead(xlsxPath);
            loadResult = new XlsxFileAdapter().LoadWithWarnings(stream, inspectFeatures: true);
            workbook = loadResult.Workbook;
            Emit("  Status: SUCCESS");
        }
        catch (Exception ex)
        {
            Emit($"  Status: EXCEPTION — {ex.GetType().FullName}: {ex.Message}");
            Emit($"  Stack top: {FirstStackLine(ex)}");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            return 1;
        }

        if (loadResult.Warnings.Count == 0)
        {
            Emit("  Load warnings: none");
        }
        else
        {
            Emit($"  Load warnings ({loadResult.Warnings.Count}):");
            foreach (var w in loadResult.Warnings)
                Emit($"    - {w}");
        }

        Emit("");

        // =====================================================================
        // SECTION 2 — UNSUPPORTED FEATURES
        // =====================================================================
        Emit("--------------------------------------------------------------------------------");
        Emit("SECTION 2: UNSUPPORTED FEATURES");
        Emit("--------------------------------------------------------------------------------");

        var featureReport = loadResult.FeatureReport;
        if (featureReport is null)
        {
            Emit("  Feature report: not available (inspectFeatures returned null)");
        }
        else if (!featureReport.HasUnsupportedFeatures)
        {
            Emit("  No unsupported features detected.");
        }
        else
        {
            Emit($"  Unsupported features ({featureReport.Features.Count}):");
            foreach (var f in featureReport.Features)
                Emit($"    Kind={f.Kind,-30}  PackagePart={f.PackagePart}");
        }

        Emit("");

        // =====================================================================
        // SECTION 3 — STRUCTURAL INVENTORY
        // =====================================================================
        Emit("--------------------------------------------------------------------------------");
        Emit("SECTION 3: STRUCTURAL INVENTORY");
        Emit("--------------------------------------------------------------------------------");

        var totalCells = 0;
        var totalFormulas = 0;
        var totalMerges = 0;
        var totalCF = 0;
        var totalDV = 0;
        var totalTables = 0;
        var totalCharts = 0;
        var totalPivots = 0;
        var totalHyperlinks = 0;
        var totalComments = 0;

        Emit($"  {"Sheet",-30} {"Cells",7} {"Fmlas",6} {"Merges",7} {"CF",5} {"DV",5} {"Tbls",5} {"Chrts",5} {"Pivts",5} {"Links",6} {"Cmts",5}");
        Emit($"  {new string('-', 100)}");

        foreach (var sheet in workbook.Sheets)
        {
            var cellMap = sheet.GetOccupiedCellMap();
            var cells = cellMap.Count;
            var fmlas = sheet.FormulaCellCount;
            var merges = sheet.MergedRegions.Count;
            var cf = sheet.ConditionalFormats.Count;
            var dv = sheet.DataValidations.Count();
            var tables = sheet.StructuredTables.Count;
            var charts = sheet.Charts.Count;
            var pivots = sheet.PivotTables.Count;
            var hyperlinks = sheet.Hyperlinks.Count;
            var comments = sheet.Comments.Count;

            Emit($"  {Trunc(sheet.Name, 30),-30} {cells,7} {fmlas,6} {merges,7} {cf,5} {dv,5} {tables,5} {charts,5} {pivots,5} {hyperlinks,6} {comments,5}");

            totalCells += cells;
            totalFormulas += fmlas;
            totalMerges += merges;
            totalCF += cf;
            totalDV += dv;
            totalTables += tables;
            totalCharts += charts;
            totalPivots += pivots;
            totalHyperlinks += hyperlinks;
            totalComments += comments;
        }

        Emit($"  {new string('-', 100)}");
        Emit($"  {"WORKBOOK TOTALS",-30} {totalCells,7} {totalFormulas,6} {totalMerges,7} {totalCF,5} {totalDV,5} {totalTables,5} {totalCharts,5} {totalPivots,5} {totalHyperlinks,6} {totalComments,5}");
        Emit($"  Named ranges: {workbook.NamedRanges.Count}");
        Emit($"  Sheet count : {workbook.Sheets.Count}");

        Emit("");

        // =====================================================================
        // SECTION 4 — FORMULA PARITY
        // =====================================================================
        Emit("--------------------------------------------------------------------------------");
        Emit("SECTION 4: FORMULA PARITY");
        Emit("--------------------------------------------------------------------------------");

        // Snapshot cached values BEFORE recalc
        var snapshots = new Dictionary<(string SheetName, uint Row, uint Col), (string Formula, ScalarValue Cached)>();
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
            {
                if (cell.HasFormula && cell.FormulaText is not null)
                    snapshots[(sheet.Name, row, col)] = (cell.FormulaText, cell.Value);
            }
        }

        Emit($"  Total formula cells (snapshot): {snapshots.Count}");

        // Run recalc. Keep the dependency graph so volatile-taint can be propagated transitively
        // (cells that don't call a volatile directly but depend on one are still legitimately divergent).
        var dependencyGraph = new DependencyGraph();
        Exception? recalcException = null;
        try
        {
            new RecalcEngine(dependencyGraph, new FormulaEvaluator()).RecalculateAllFormulas(workbook);
            Emit("  Recalc: completed without exception");
        }
        catch (Exception ex)
        {
            recalcException = ex;
            Emit($"  Recalc: EXCEPTION — {ex.GetType().FullName}: {ex.Message}");
            Emit($"  Stack top: {FirstStackLine(ex)}");
        }

        Emit("");

        // Compare recalc result vs cached. Cells whose cached value CANNOT match a recalc by design are
        // segregated into separate buckets so the headline mismatch count reflects only GENUINE divergences:
        //   - VOLATILE: formula calls a non-deterministic builtin (TODAY/NOW/RAND/RANDARRAY/RANDBETWEEN);
        //     the cached value reflects authoring time, so a differing recalc is expected and correct.
        //   - VBA-UDF: formula calls a name that is neither a builtin nor a workbook defined-name — a VBA
        //     user-defined function FreeX cannot evaluate (macros unsupported); recalc errors legitimately.
        var definedNames = new HashSet<string>(workbook.NamedRanges.Keys, StringComparer.OrdinalIgnoreCase);
        var mismatches = new List<(string Sheet, uint Row, uint Col, string Formula, ScalarValue Cached, ScalarValue Recalc)>();
        var volatileExcluded = new List<(string Sheet, uint Row, uint Col, string Formula)>();
        var vbaUdfExcluded = new List<(string Sheet, uint Row, uint Col, string Formula, string UdfName)>();

        // Volatile taint: every formula cell that directly calls a non-deterministic volatile, plus every
        // cell that transitively depends on one (e.g. COUNTIFS / IF over a "D-TODAY()" column). Such cells'
        // cached values reflect authoring time and cannot match a recalc — excluded as volatile, not genuine.
        var volatileTainted = recalcException is null
            ? ComputeVolatileTaint(workbook, dependencyGraph)
            : new HashSet<CellAddress>();

        if (recalcException is null)
        {
            foreach (var sheet in workbook.Sheets)
            {
                foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
                {
                    if (!cell.HasFormula || cell.FormulaText is null) continue;

                    var key = (sheet.Name, row, col);
                    if (!snapshots.TryGetValue(key, out var snap)) continue;

                    var cached = snap.Cached;
                    var recalc = cell.Value;

                    if (ValuesMatch(cached, recalc))
                        continue;

                    // Only segregate when CONFIDENT: a volatile-tainted cell (direct or transitive), or an
                    // unknown call token that is provably neither a builtin nor a defined name.
                    if (volatileTainted.Contains(new CellAddress(sheet.Id, row, col)))
                    {
                        volatileExcluded.Add((sheet.Name, row, col, snap.Formula));
                        continue;
                    }

                    if (TryFindUnknownFunctionCall(snap.Formula, definedNames, out var udfName))
                    {
                        vbaUdfExcluded.Add((sheet.Name, row, col, snap.Formula, udfName));
                        continue;
                    }

                    mismatches.Add((sheet.Name, row, col, snap.Formula, cached, recalc));
                }
            }
        }

        // Per-sheet summary
        Emit($"  {"Sheet",-30} {"Formula Cells",14} {"Mismatches",12} {"Volatile",10} {"VBA-UDF",9}");
        Emit($"  {new string('-', 79)}");
        int workbookFormulas = 0, workbookMismatches = 0;
        foreach (var sheet in workbook.Sheets)
        {
            var sheetFormulas = snapshots.Keys.Count(k => k.SheetName == sheet.Name);
            var sheetMismatches = mismatches.Count(m => m.Sheet == sheet.Name);
            var sheetVolatile = volatileExcluded.Count(m => m.Sheet == sheet.Name);
            var sheetUdf = vbaUdfExcluded.Count(m => m.Sheet == sheet.Name);
            if (sheetFormulas > 0)
                Emit($"  {Trunc(sheet.Name, 30),-30} {sheetFormulas,14} {sheetMismatches,12} {sheetVolatile,10} {sheetUdf,9}");
            workbookFormulas += sheetFormulas;
            workbookMismatches += sheetMismatches;
        }
        Emit($"  {new string('-', 79)}");
        Emit($"  {"WORKBOOK TOTAL",-30} {workbookFormulas,14} {workbookMismatches,12} {volatileExcluded.Count,10} {vbaUdfExcluded.Count,9}");
        Emit("");
        Emit($"  GENUINE mismatches      : {mismatches.Count}");
        Emit($"  volatile (excluded)     : {volatileExcluded.Count}");
        Emit($"  VBA-UDF (excluded)      : {vbaUdfExcluded.Count}");
        Emit("");

        if (volatileExcluded.Count > 0)
        {
            Emit($"  Volatile-excluded examples (cached reflects authoring time; recalc legitimately differs):");
            foreach (var (sht, row, col, formula) in volatileExcluded.Take(10))
                Emit($"    {Trunc($"{sht}!{ColToLetter(col)}{row}", 22),-22} {Trunc(formula, 50),-50}");
            Emit("");
        }

        if (vbaUdfExcluded.Count > 0)
        {
            Emit($"  VBA-UDF-excluded examples (unknown function FreeX cannot evaluate; macros unsupported):");
            foreach (var (sht, row, col, formula, udf) in vbaUdfExcluded.Take(10))
                Emit($"    {Trunc($"{sht}!{ColToLetter(col)}{row}", 22),-22} {Trunc($"[{udf}] {formula}", 50),-50}");
            Emit("");
        }

        // Up to 40 example mismatches, grouped by leading function name
        if (mismatches.Count > 0 && recalcException is null)
        {
            // Sort by inferred function name so patterns are visible, then by sheet/row/col
            var sorted = mismatches
                .OrderBy(m => ExtractLeadingFunction(m.Formula))
                .ThenBy(m => m.Sheet)
                .ThenBy(m => m.Row)
                .ThenBy(m => m.Col)
                .Take(40)
                .ToList();

            Emit($"  Mismatch examples (up to 40, sorted by function pattern):");
            Emit($"  {"Address",-22} {"Formula",-45} {"Cached",-22} {"Recalc",-22}");
            Emit($"  {new string('-', 115)}");
            foreach (var (sht, row, col, formula, cached, recalc) in sorted)
            {
                var addr = $"{sht}!{ColToLetter(col)}{row}";
                Emit($"  {Trunc(addr, 22),-22} {Trunc(formula, 45),-45} {Trunc(ScalarStr(cached), 22),-22} {Trunc(ScalarStr(recalc), 22),-22}");
            }

            Emit("");
            // Cluster summary
            var clusters = mismatches
                .GroupBy(m => ExtractLeadingFunction(m.Formula))
                .OrderByDescending(g => g.Count())
                .ToList();
            Emit($"  Mismatch clusters by leading function ({clusters.Count} distinct patterns):");
            foreach (var g in clusters.Take(20))
                Emit($"    {g.Key,-30} {g.Count(),6} mismatches");
        }
        else if (recalcException is null)
        {
            Emit("  No mismatches — all formula cells match cached values.");
        }

        Emit("");

        // =====================================================================
        // SECTION 5 — ROUND-TRIP
        // =====================================================================
        Emit("--------------------------------------------------------------------------------");
        Emit("SECTION 5: ROUND-TRIP");
        Emit("--------------------------------------------------------------------------------");

        var roundTripPath = Path.Combine(outputDir, "roundtrip.xlsx");

        // Save
        Exception? saveException = null;
        IReadOnlyList<string> saveWarnings = [];
        try
        {
            using var outStream = File.Create(roundTripPath);
            saveWarnings = new XlsxFileAdapter().SaveWithWarnings(workbook, outStream).Warnings;
            Emit($"  Save: SUCCESS -> {roundTripPath}");
            if (saveWarnings.Count == 0)
            {
                Emit("  Save warnings: none");
            }
            else
            {
                Emit($"  Save warnings ({saveWarnings.Count}):");
                foreach (var warning in saveWarnings)
                    Emit($"    - {warning}");
            }
        }
        catch (Exception ex)
        {
            saveException = ex;
            Emit($"  Save: EXCEPTION — {ex.GetType().FullName}: {ex.Message}");
            Emit($"  Stack top: {FirstStackLine(ex)}");
        }

        if (saveException is null)
        {
            // OPC open + schema validation — capture all strings while doc is still open
            List<(string Id, string Desc, string Path, string Node)> schemaErrorCaptures = [];
            int schemaErrorCount = 0;
            Exception? valException = null;
            try
            {
                using (var doc = SpreadsheetDocument.Open(roundTripPath, isEditable: false))
                {
                    var validator = new OpenXmlValidator(DOXV.Microsoft365);
                    foreach (var e in validator.Validate(doc).Where(ev => ev.ErrorType == ValidationErrorType.Schema))
                    {
                        schemaErrorCount++;
                        if (schemaErrorCaptures.Count < 15)
                        {
                            var xpath = "(none)";
                            var nodeName = "(none)";
                            try { xpath = e.Path?.XPath ?? "(none)"; } catch { xpath = "(unavailable)"; }
                            try { nodeName = e.Node?.LocalName ?? "(none)"; } catch { nodeName = "(unavailable)"; }
                            schemaErrorCaptures.Add((e.Id ?? "", e.Description ?? "", xpath, nodeName));
                        }
                    }
                }
                Emit($"  OpenXML schema validation: {schemaErrorCount} errors");
            }
            catch (Exception ex)
            {
                valException = ex;
                Emit($"  OpenXML validation: EXCEPTION — {ex.GetType().FullName}: {ex.Message}");
                Emit($"  Stack top: {FirstStackLine(ex)}");
            }

            if (valException is null && schemaErrorCount > 0)
            {
                Emit($"  First {schemaErrorCaptures.Count} schema errors:");
                foreach (var (id, desc, path, node) in schemaErrorCaptures)
                {
                    Emit($"    [{id}] {desc}");
                    Emit($"      Path : {path}");
                    Emit($"      Node : {node}");
                }
            }

            Emit("");

            // Reload round-trip in FreeX and compare cell counts
            Exception? reloadException = null;
            Workbook? reloaded = null;
            try
            {
                using var reloadStream = File.OpenRead(roundTripPath);
                reloaded = new XlsxFileAdapter().Load(reloadStream);
                Emit("  FreeX reload of round-trip: SUCCESS");
            }
            catch (Exception ex)
            {
                reloadException = ex;
                Emit($"  FreeX reload: EXCEPTION — {ex.GetType().FullName}: {ex.Message}");
                Emit($"  Stack top: {FirstStackLine(ex)}");
            }

            if (reloadException is null && reloaded is not null)
            {
                var origTotal = workbook.Sheets.Sum(s => s.GetOccupiedCellMap().Count);
                var rtTotal = reloaded.Sheets.Sum(s => s.GetOccupiedCellMap().Count);
                var delta = rtTotal - origTotal;
                Emit($"  Occupied cells — original: {origTotal}, round-trip: {rtTotal}, delta: {delta:+0;-0;0}");

                Emit($"  Per-sheet cell-count delta:");
                Emit($"  {"Sheet",-30} {"Orig",7} {"RT",7} {"Delta",7}");
                Emit($"  {new string('-', 55)}");
                foreach (var origSheet in workbook.Sheets)
                {
                    var origCount = origSheet.GetOccupiedCellMap().Count;
                    var rtSheet = reloaded.Sheets.FirstOrDefault(s => s.Name == origSheet.Name);
                    var rtCount = rtSheet?.GetOccupiedCellMap().Count ?? 0;
                    var d = rtCount - origCount;
                    if (d != 0 || origCount > 0)
                    {
                        var dStr = d == 0 ? "0" : d > 0 ? $"+{d}" : $"{d}";
                        Emit($"  {Trunc(origSheet.Name, 30),-30} {origCount,7} {rtCount,7} {dStr,7}");
                    }
                }
            }
        }

        Emit("");
        Emit("================================================================================");
        Emit("  END OF REPORT");
        Emit("================================================================================");

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"\n[Report written to {reportPath}]");
        return 0;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool ValuesMatch(ScalarValue a, ScalarValue b)
    {
        // A date serial and a plain number with the same value display identically (the cell's number
        // format decides date-vs-number rendering, not the scalar type). FreeX loads date-formatted cells
        // as DateTimeValue but recalc produces NumberValue; treat equal serials as a MATCH, not a mismatch,
        // so the report counts real divergences only. Same for bool<->1/0.
        if (TryNumeric(a, out var an) && TryNumeric(b, out var bn))
            return NumbersMatch(an, bn);

        return (a, b) switch
        {
            (BlankValue, BlankValue) => true,
            (TextValue ta, TextValue tb) => string.Equals(ta.Value, tb.Value, StringComparison.Ordinal),
            // Any error vs any error is a MATCH — the formula is genuinely erroneous on the available data;
            // only error-vs-non-error (caught by the wildcard below) remains a mismatch.
            (ErrorValue, ErrorValue) => true,
            _ => false
        };
    }

    // Numeric view of a scalar: numbers, dates (serial), and bools (1/0) are all comparable numerically.
    private static bool TryNumeric(ScalarValue v, out double value)
    {
        switch (v)
        {
            case NumberValue n: value = n.Value; return true;
            case DateTimeValue d: value = d.Value; return true;
            case BoolValue b: value = b.Value ? 1 : 0; return true;
            default: value = 0; return false;
        }
    }

    private static bool NumbersMatch(double a, double b)
    {
        if (a == b) return true;
        if (double.IsNaN(a) && double.IsNaN(b)) return true;
        var absDiff = Math.Abs(a - b);
        if (absDiff < 1e-9) return true;
        var magnitude = Math.Max(Math.Abs(a), Math.Abs(b));
        if (magnitude > 0 && absDiff / magnitude < 1e-6) return true;
        return false;
    }

    private static string ScalarStr(ScalarValue v) => v switch
    {
        BlankValue => "(blank)",
        NumberValue nv => nv.Value.ToString("G", CultureInfo.InvariantCulture),
        DateTimeValue dv => $"Date({dv.Value:G})",
        TextValue tv => $"\"{tv.Value}\"",
        BoolValue bv => bv.Value ? "TRUE" : "FALSE",
        ErrorValue ev => ev.ToString(),
        _ => v.ToString() ?? "?"
    };

    // Non-deterministic volatile builtins whose cached value (authoring time) cannot match a recalc.
    // Deliberately narrower than FreeX's full volatile set (which also includes INDIRECT/OFFSET/CELL/INFO):
    // those are reference-volatile but still deterministic given the same data, so they are NOT excluded.
    private static readonly HashSet<string> NonDeterministicVolatileFunctions =
        new(StringComparer.OrdinalIgnoreCase) { "TODAY", "NOW", "RAND", "RANDARRAY", "RANDBETWEEN" };

    private static bool FormulaCallsNonDeterministicVolatile(string formula)
    {
        foreach (var name in EnumerateFunctionCallNames(formula))
        {
            if (NonDeterministicVolatileFunctions.Contains(name))
                return true;
        }

        return false;
    }

    // Seeds the taint with every formula cell that directly calls a non-deterministic volatile, then
    // propagates DOWN to dependents via the populated dependency graph (GetDirectDependents covers both
    // exact-cell and range references). The result is the full set of cells whose cached value cannot
    // legitimately match a recalc because a volatile source feeds them.
    private static HashSet<CellAddress> ComputeVolatileTaint(Workbook workbook, DependencyGraph graph)
    {
        var tainted = new HashSet<CellAddress>();
        var worklist = new Queue<CellAddress>();

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
            {
                if (cell.HasFormula && cell.FormulaText is not null &&
                    FormulaCallsNonDeterministicVolatile(cell.FormulaText))
                {
                    var addr = new CellAddress(sheet.Id, row, col);
                    if (tainted.Add(addr))
                        worklist.Enqueue(addr);
                }
            }
        }

        while (worklist.Count > 0)
        {
            var current = worklist.Dequeue();
            foreach (var dependent in graph.GetDirectDependents(current))
            {
                if (tainted.Add(dependent))
                    worklist.Enqueue(dependent);
            }
        }

        return tainted;
    }

    // Returns the first call token that is neither a built-in function nor a workbook defined-name.
    // Such a token is a VBA user-defined function FreeX cannot evaluate (macros unsupported).
    private static bool TryFindUnknownFunctionCall(string formula, HashSet<string> definedNames, out string udfName)
    {
        foreach (var name in EnumerateFunctionCallNames(formula))
        {
            if (BuiltInFunctions.Exists(name.ToUpperInvariant()))
                continue;
            if (definedNames.Contains(name))
                continue;
            udfName = name;
            return true;
        }

        udfName = string.Empty;
        return false;
    }

    // Extracts identifiers that are immediately followed by '(' (i.e. function calls), skipping string
    // literals and qualified members ('foo.bar(' — the '.'-prefixed segment is a method, not a workbook
    // function). Sheet-qualified references like "Sheet1!A1" are not call tokens (no trailing '('), so they
    // are naturally ignored. Conservative by construction: only clear call sites are reported.
    private static IEnumerable<string> EnumerateFunctionCallNames(string formula)
    {
        if (string.IsNullOrEmpty(formula))
            yield break;

        var i = 0;
        var n = formula.Length;
        while (i < n)
        {
            var c = formula[i];

            // Skip string literals ("...", with "" as an escaped quote).
            if (c == '"')
            {
                i++;
                while (i < n)
                {
                    if (formula[i] == '"')
                    {
                        if (i + 1 < n && formula[i + 1] == '"') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Identifier start: letter or underscore (Excel function/name rules).
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < n && (char.IsLetterOrDigit(formula[i]) || formula[i] == '_' || formula[i] == '.'))
                    i++;

                // Look past whitespace for an opening paren — that makes this identifier a call.
                var j = i;
                while (j < n && char.IsWhiteSpace(formula[j])) j++;
                if (j < n && formula[j] == '(')
                {
                    var token = formula[start..i];
                    // A qualified member call (e.g. "Application.Run") is not a workbook-level function name;
                    // take the leaf after the last '.'. A leaf that's empty is ignored.
                    var dot = token.LastIndexOf('.');
                    var leaf = dot >= 0 ? token[(dot + 1)..] : token;
                    if (leaf.Length > 0)
                        yield return leaf;
                }
                continue;
            }

            i++;
        }
    }

    private static string ExtractLeadingFunction(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return "(empty)";
        var trimmed = formula.TrimStart();
        var idx = trimmed.IndexOf('(');
        if (idx <= 0) return trimmed.Length > 20 ? trimmed[..20] : trimmed;
        return trimmed[..idx].ToUpperInvariant();
    }

    private static string ColToLetter(uint col)
    {
        var sb = new StringBuilder(3);
        while (col > 0)
        {
            col--;
            sb.Insert(0, (char)('A' + col % 26));
            col /= 26;
        }
        return sb.ToString();
    }

    private static string Trunc(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..(max - 1)] + "~";
    }

    private static string FirstStackLine(Exception ex)
    {
        var lines = ex.StackTrace?.Split('\n');
        return lines?.FirstOrDefault(l => l.TrimStart().StartsWith("at ", StringComparison.Ordinal))?.Trim()
               ?? "(no stack)";
    }
}
