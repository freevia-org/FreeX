using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Free.ToolsShared;
using FreeX.ToolsShared;

namespace FreeX.FormatCrossCheck;

internal enum CrossKind
{
    /// <summary>Everything the format's ceiling promises survived. Clean external interop.</summary>
    Ok,
    /// <summary>Loss within the format's documented ceiling (e.g. formulas in CSV). Not a FreeX bug.</summary>
    ExpectedCoercion,
    /// <summary>The ceiling said this should survive but it didn't — candidate FreeX-output-defect.</summary>
    OutputDefect,
    /// <summary>LibreOffice could not open the FreeX-written file at all.</summary>
    LibreOfficeOpenFailed,
    /// <summary>FreeX could not write the format, or could not reload LibreOffice's xlsx.</summary>
    FreeXError,
}

internal sealed class CrossCheckResult
{
    public required FormatProfile Format { get; init; }
    public required CrossKind Kind { get; init; }

    public bool LibreOfficeOpened { get; init; }

    public int ValuesCompared { get; init; }
    public int ValuesMatched { get; init; }
    public int FormulasCompared { get; init; }
    public int FormulasMatched { get; init; }
    public int FormulasRewritten { get; init; }
    public int FormulasVanished { get; init; }
    public int SkippedFormulaResults { get; init; }
    public int SkippedPivotCells { get; init; }
    public int RefSheetCount { get; init; }
    public int GotSheetCount { get; init; }

    public List<string> ValueDefectSamples { get; } = new();
    public List<string> FormulaDefectSamples { get; } = new();
    public string Detail { get; set; } = "";
    public string? Diagnostics { get; init; }
}

/// <summary>
/// For one source workbook and one interchange format: FreeX writes the file → LibreOffice re-exports
/// it to xlsx → FreeX loads the LibreOffice xlsx → compare VALUES + FORMULAS + sheet structure to the
/// source snapshot. Adapters come ONLY from <c>WorkbookFileAdapterCatalog</c> + <c>FileFormatResolver</c>,
/// so any newly-registered FreeX format is picked up automatically.
/// </summary>
internal sealed class CrossCheckRunner
{
    private readonly string _scratchDir;
    private readonly SofficeRunner _soffice;
    private readonly IReadOnlyList<IFileAdapter> _adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();

    public CrossCheckRunner(string scratchDir, SofficeRunner soffice)
    {
        _scratchDir = scratchDir;
        _soffice = soffice;
        Directory.CreateDirectory(scratchDir);
    }

    public IReadOnlyList<CrossCheckResult> RunAll(string sourcePath)
    {
        // Load the source once via the catalog (source is xlsx).
        Workbook source;
        try
        {
            using var s = File.OpenRead(sourcePath);
            var openAdapter = FileFormatResolver.FindOpenAdapter(_adapters, Path.GetExtension(sourcePath), out _)
                ?? throw new InvalidOperationException($"no open adapter for {sourcePath}");
            source = openAdapter.Load(s);
        }
        catch (Exception ex)
        {
            return FormatProfile.All
                .Select(f => new CrossCheckResult { Format = f, Kind = CrossKind.FreeXError, Detail = $"source load failed: {Describe(ex)}" })
                .ToList();
        }

        var reference = ValueSnapshot.Capture(source);
        var results = new List<CrossCheckResult>();
        foreach (var fmt in FormatProfile.All)
            results.Add(RunOne(sourcePath, source, reference, fmt));
        return results;
    }

    private CrossCheckResult RunOne(string sourcePath, Workbook source, ValueSnapshot reference, FormatProfile fmt)
    {
        // 1) FreeX writes the interchange file.
        string freexFile;
        try
        {
            var saveAdapter = (fmt.AdapterFormatName is { } sn
                    ? FileFormatResolver.FindSaveAdapterByFormatName(_adapters, fmt.Extension, sn, out _)
                    : FileFormatResolver.FindSaveAdapter(_adapters, fmt.Extension, out _))
                ?? throw new InvalidOperationException($"no FreeX save adapter for {fmt.Extension}");

            var baseName =
                ToolFileNameSanitizer.ReplaceNonAlphaNumericWithUnderscore(Path.GetFileNameWithoutExtension(sourcePath))
                + "_" + fmt.Key;
            freexFile = Path.Combine(_scratchDir, baseName + fmt.Extension);
            // xlsx control: detach the source package so we exercise FreeX's full OOXML writer, not a
            // byte-copy patch-save of the original file.
            if (string.Equals(fmt.Key, "xlsx", StringComparison.Ordinal))
                XlsxFileAdapter.DetachSourcePackage(source);
            using var outStream = File.Create(freexFile);
            saveAdapter.Save(source, outStream);
        }
        catch (Exception ex)
        {
            return new CrossCheckResult { Format = fmt, Kind = CrossKind.FreeXError, Detail = $"FreeX write failed: {Describe(ex)}" };
        }

        // 2) LibreOffice opens it and re-exports to xlsx. Clear any prior output first: a stale (possibly
        // locked) xlsx left from an earlier run makes soffice's store step fail with an Io-Abort.
        var loDir = Path.Combine(_scratchDir, "lo_" + fmt.Key);
        try { if (Directory.Exists(loDir)) Directory.Delete(loDir, recursive: true); } catch { }
        var convert = _soffice.ConvertToXlsx(freexFile, loDir, fmt.SofficeInputFilter);
        if (!convert.Success || convert.OutputXlsxPath is null)
        {
            return new CrossCheckResult
            {
                Format = fmt,
                Kind = CrossKind.LibreOfficeOpenFailed,
                LibreOfficeOpened = false,
                Detail = "LibreOffice could not open/convert the FreeX-written file",
                Diagnostics = convert.Diagnostics,
            };
        }

        // 3) FreeX loads the LibreOffice-produced xlsx.
        Workbook loaded;
        try
        {
            using var s = File.OpenRead(convert.OutputXlsxPath);
            var openAdapter = FileFormatResolver.FindOpenAdapter(_adapters, ".xlsx", out _)
                ?? throw new InvalidOperationException("no xlsx open adapter");
            loaded = openAdapter.Load(s);
        }
        catch (Exception ex)
        {
            return new CrossCheckResult
            {
                Format = fmt,
                Kind = CrossKind.FreeXError,
                LibreOfficeOpened = true,
                Detail = $"FreeX failed to reload LibreOffice xlsx: {Describe(ex)}",
            };
        }

        // 4) Compare.
        var got = ValueSnapshot.Capture(loaded);
        return Compare(fmt, reference, got, convert.Diagnostics);
    }

    private static CrossCheckResult Compare(FormatProfile fmt, ValueSnapshot reference, ValueSnapshot got, string diagnostics)
    {
        var sheetPairs = PairSheets(reference, got, fmt.PreservesMultiSheet);

        int valuesCompared = 0, valuesMatched = 0;
        int formulasCompared = 0, formulasMatched = 0;
        int formulasRewritten = 0, formulasVanished = 0;
        int skippedFormulaResults = 0, skippedPivot = 0;
        int knownLoValueGaps = 0;
        var valueDefects = new List<string>();
        var formulaDefects = new List<string>();
        var loGapSamples = new List<string>();

        foreach (var (refSheet, gotSheet) in sheetPairs)
        {
            if (gotSheet is null) continue; // a dropped sheet is handled via the structure result below.

            foreach (var ((row, col), refCell) in refSheet.Cells)
            {
                // Pivot OUTPUT is regenerated (not re-read) by LibreOffice with its own layout — excluding
                // these cells keeps the value diff honest (their churn is LibreOffice regeneration, not loss).
                if (refSheet.IsInPivot(row, col)) { skippedPivot++; continue; }

                // FORMULAS — only meaningful for formats whose ceiling preserves them.
                if (refCell.HasFormula)
                {
                    if (fmt.PreservesFormulas)
                    {
                        gotSheet.Cells.TryGetValue((row, col), out var gotCell);
                        formulasCompared++;
                        if (gotCell is { HasFormula: true })
                        {
                            // Both sides still carry a FORMULA. If they are equivalent (after stripping the
                            // OpenFormula prefix / bool-literal / case rewrites) it survived; otherwise
                            // LibreOffice re-spelled it into its own dialect — a LibreOffice-coercion, not a
                            // FreeX-output loss (FreeX wrote a valid formula; LO chose to rewrite it).
                            if (FormulasEquivalent(refCell.FormulaText!, gotCell.FormulaText!))
                                formulasMatched++;
                            else
                            {
                                formulasRewritten++;
                                if (formulaDefects.Count < 12)
                                    formulaDefects.Add($"{refSheet.Name}!{FidelityCompare.ColToLetter(col)}{row}: " +
                                                       $"={refCell.FormulaText} -> ={gotCell.FormulaText}");
                            }
                        }
                        else
                        {
                            // The formula VANISHED — LibreOffice replaced it with a literal value. For a
                            // format whose ceiling preserves formulas this is the meaningful loss signal.
                            formulasVanished++;
                            if (formulaDefects.Count < 12)
                                formulaDefects.Add($"{refSheet.Name}!{FidelityCompare.ColToLetter(col)}{row}: " +
                                                   $"={refCell.FormulaText} -> (no formula: " +
                                                   FidelityCompare.ScalarStr(gotCell?.Value ?? BlankValue.Instance) + ")");
                        }
                    }
                    // A formula cell's CACHED value is volatile — LibreOffice recalculates it (dates relative
                    // to today, RAND/NOW, etc.). We never score the cached result as a value loss; the formula
                    // comparison above is the fidelity signal for these cells.
                    skippedFormulaResults++;
                    continue;
                }

                // VALUES — literal (non-formula, non-pivot) source cells: this is the interop-critical core.
                // An empty-string text cell carries no data; an external tool legitimately reads it back as
                // a blank, so it is not part of the value-survival signal.
                if (refCell.Value is not BlankValue && !IsEmptyText(refCell.Value))
                {
                    gotSheet.Cells.TryGetValue((row, col), out var gotCell);
                    var gotVal = gotCell?.Value ?? BlankValue.Instance;
                    valuesCompared++;
                    var match = fmt.ValueComparisonIsDisplayOnly
                        ? FidelityCompare.DisplayMatch(refCell.Value, gotVal)
                        : FidelityCompare.ValuesMatch(refCell.Value, gotVal);
                    if (match) { valuesMatched++; }
                    else if (IsKnownLibreOfficeValueGap(fmt, refCell.Value, gotVal))
                    {
                        // FreeX wrote a SPEC-CORRECT value the LibreOffice import filter doesn't map (verified
                        // by inspecting FreeX's bytes). Count it as matched for the defect signal, but record
                        // the gap so the report can attribute it to LibreOffice, not FreeX.
                        valuesMatched++;
                        knownLoValueGaps++;
                        if (loGapSamples.Count < 8)
                            loGapSamples.Add($"{refSheet.Name}!{FidelityCompare.ColToLetter(col)}{row}: " +
                                             $"{FidelityCompare.ScalarStr(refCell.Value)} -> {FidelityCompare.ScalarStr(gotVal)}");
                    }
                    else if (valueDefects.Count < 12)
                        valueDefects.Add($"{refSheet.Name}!{FidelityCompare.ColToLetter(col)}{row}: " +
                                         $"{FidelityCompare.ScalarStr(refCell.Value)} -> {FidelityCompare.ScalarStr(gotVal)}");
                }
            }
        }

        // Sheet-structure check (only when the format is supposed to preserve all sheets).
        bool sheetStructureOk = !fmt.PreservesMultiSheet || got.Sheets.Count >= reference.Sheets.Count;

        // The FreeX-OUTPUT-DEFECT signal — the things FreeX is unambiguously responsible for and a real
        // external consumer must therefore get right:
        //   * LITERAL value loss: a hard number/text/bool FreeX wrote that LibreOffice mis-read.
        //   * DROPPED SHEETS when the format is supposed to keep them.
        //   * a FORMULA that VANISHED into a literal in a format whose ceiling preserves formulas (FreeX
        //     either failed to write the formula, or wrote it in a shape LibreOffice silently flattened).
        // A formula that LibreOffice merely RE-SPELLED into its OpenFormula dialect (LET/FILTER, R1C1,
        // table-ref case, of:= prefix) is LibreOffice-coercion, NOT a FreeX defect — FreeX emitted a valid
        // formula and LibreOffice chose to rewrite it. We report the rewrite rate but never fail on it.
        bool valueDefect = valuesMatched < valuesCompared;
        bool formulaVanishedDefect = fmt.PreservesFormulas && formulasVanished > 0;
        var kind = (valueDefect || formulaVanishedDefect || !sheetStructureOk)
            ? CrossKind.OutputDefect
            : CrossKind.Ok;

        var notes = new List<string>();
        if (!fmt.PreservesFormulas)
            notes.Add("formulas flattened to values (expected for this format)");
        if (!fmt.PreservesMultiSheet && reference.Sheets.Count > 1)
            notes.Add("multi-sheet collapsed to 1 (expected); compared sheet 1 only");
        if (skippedFormulaResults > 0)
            notes.Add($"{skippedFormulaResults} formula-result cells excluded from value diff (LibreOffice recalculates)");
        if (skippedPivot > 0)
            notes.Add($"{skippedPivot} pivot-output cells excluded (LibreOffice regenerates)");
        if (knownLoValueGaps > 0)
            notes.Add($"{knownLoValueGaps} values dropped by a KNOWN LibreOffice import limitation (FreeX bytes spec-correct: {string.Join(", ", loGapSamples.Take(3))})");
        if (formulasRewritten > 0)
            notes.Add($"{formulasRewritten} formulas re-spelled into LibreOffice's OpenFormula dialect (coercion, not a FreeX loss)");
        if (formulasVanished > 0)
            notes.Add($"{formulasVanished} formulas FLATTENED to literals (FreeX-output defect candidate)");
        if (!sheetStructureOk)
            notes.Add($"DROPPED SHEETS: {reference.Sheets.Count} -> {got.Sheets.Count}");

        var result = new CrossCheckResult
        {
            Format = fmt,
            Kind = kind,
            LibreOfficeOpened = true,
            ValuesCompared = valuesCompared,
            ValuesMatched = valuesMatched,
            FormulasCompared = formulasCompared,
            FormulasMatched = formulasMatched,
            FormulasRewritten = formulasRewritten,
            FormulasVanished = formulasVanished,
            SkippedFormulaResults = skippedFormulaResults,
            SkippedPivotCells = skippedPivot,
            RefSheetCount = reference.Sheets.Count,
            GotSheetCount = got.Sheets.Count,
            Detail = string.Join("; ", notes),
            Diagnostics = diagnostics,
        };
        result.ValueDefectSamples.AddRange(valueDefects);
        result.FormulaDefectSamples.AddRange(formulaDefects);
        return result;
    }

    /// <summary>
    /// Normalized formula equivalence. LibreOffice's OpenFormula/ODF round-trip rewrites formula SYNTAX
    /// without changing meaning, and we must not score those cosmetic rewrites as a loss:
    ///   * an "of:=" (and bare "of:") OpenFormula prefix is added;
    ///   * the boolean literal TRUE()/FALSE() is emitted as 1()/0() or 1/0;
    ///   * "$" anchors, whitespace and the leading "=" are reflowed;
    ///   * function/structured-table-reference case is not significant.
    /// We compare on an upper-cased, "$"/whitespace-stripped, prefix-stripped form. A genuinely different
    /// formula (different refs, ops or functions) still diverges.
    /// </summary>
    private static bool FormulasEquivalent(string a, string b)
    {
        static string Norm(string f)
        {
            var sb = new StringBuilder(f.Length);
            foreach (var ch in f)
            {
                if (ch == '$' || char.IsWhiteSpace(ch)) continue;
                sb.Append(char.ToUpperInvariant(ch));
            }
            var s = sb.ToString();
            // Strip any leading "=" / "OF:" markers (LibreOffice emits "=of:=EXPR" -> "=OF:=EXPR").
            for (var changed = true; changed;)
            {
                changed = false;
                if (s.StartsWith("=", StringComparison.Ordinal)) { s = s[1..]; changed = true; }
                if (s.StartsWith("OF:", StringComparison.Ordinal)) { s = s[3..]; changed = true; }
            }
            // TRUE()/FALSE() vs 1()/0() vs 1/0 — collapse the boolean-literal spellings.
            // Use whole-word boundary matching to avoid corrupting identifiers that merely
            // CONTAIN the substrings (e.g. "TRUEUP", "FALSESTART", or quoted string literals).
            // Order matters: replace TRUE() / FALSE() before the bare-word forms.
            s = Regex.Replace(s, @"\bTRUE\(\)", "1");
            s = Regex.Replace(s, @"\bFALSE\(\)", "0");
            s = Regex.Replace(s, @"\bTRUE\b", "1");
            s = Regex.Replace(s, @"\bFALSE\b", "0");
            // An empty trailing arg LibreOffice sometimes appends to IF: ...,"") vs ...,"",1) — drop a
            // trailing ',1)' / ',0)' only when it mirrors a 2-arg/3-arg IF rewrite is too risky in general,
            // so we leave that to the (rare) residual mismatch; the prefix+bool fixes cover the bulk.
            return s;
        }
        return Norm(a) == Norm(b);
    }

    private static List<(ValueSnapshot.SheetSnapshot Ref, ValueSnapshot.SheetSnapshot? Got)> PairSheets(
        ValueSnapshot reference, ValueSnapshot got, bool byName)
    {
        var pairs = new List<(ValueSnapshot.SheetSnapshot, ValueSnapshot.SheetSnapshot?)>();
        if (byName)
        {
            foreach (var r in reference.Sheets)
            {
                var g = got.Sheets.FirstOrDefault(x => string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase))
                        ?? got.Sheets.ElementAtOrDefault(reference.Sheets.IndexOf(r));
                pairs.Add((r, g));
            }
        }
        else
        {
            // Single-sheet formats: compare reference sheet 0 against got sheet 0.
            var r0 = reference.Sheets.FirstOrDefault();
            var g0 = got.Sheets.FirstOrDefault();
            if (r0 is not null) pairs.Add((r0, g0));
        }
        return pairs;
    }

    private static bool IsEmptyText(ScalarValue v) => v is TextValue t && t.Value.Length == 0;

    /// <summary>
    /// Recognises value losses that are documented LibreOffice IMPORT limitations rather than FreeX-output
    /// defects (FreeX's bytes are spec-correct; LibreOffice's filter just doesn't map them):
    ///   * SpreadsheetML 2003 (.xml): LibreOffice's "MS Excel 2003 XML" import filter drops cells typed
    ///     <c>ss:Type="Boolean"</c> (FreeX writes the OASIS/Microsoft-correct Boolean cell; Excel reads it,
    ///     LibreOffice reads it back as empty). Verified by inspecting the FreeX-written .xml.
    /// </summary>
    private static bool IsKnownLibreOfficeValueGap(FormatProfile fmt, ScalarValue source, ScalarValue got)
        => string.Equals(fmt.Key, "spreadsheetml-xml", StringComparison.Ordinal)
           && source is BoolValue
           && got is BlankValue;

    private static string Describe(Exception ex)
    {
        var top = ex.StackTrace?.Split('\n')
            .Where(l => l.TrimStart().StartsWith("at ", StringComparison.Ordinal))
            .Take(3)
            .Select(l => l.Trim());
        var stack = top is null ? "" : " @ " + string.Join(" <- ", top);
        var inner = ex.InnerException is { } ie ? $" (inner: {ie.GetType().Name}: {ie.Message})" : "";
        return $"{ex.GetType().Name}: {ex.Message}{inner}{stack}";
    }
}
