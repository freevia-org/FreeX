using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

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

            var baseName = Sanitize(Path.GetFileNameWithoutExtension(sourcePath)) + "_" + fmt.Key;
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

        // 2) LibreOffice opens it and re-exports to xlsx.
        var loDir = Path.Combine(_scratchDir, "lo_" + fmt.Key);
        var convert = _soffice.ConvertToXlsx(freexFile, loDir);
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
        var valueDefects = new List<string>();
        var formulaDefects = new List<string>();

        foreach (var (refSheet, gotSheet) in sheetPairs)
        {
            if (gotSheet is null) continue; // a dropped sheet is handled via the structure result below.

            foreach (var ((row, col), refCell) in refSheet.Cells)
            {
                // VALUES — compare every non-blank source cell that should carry a value.
                if (refCell.Value is not BlankValue)
                {
                    gotSheet.Cells.TryGetValue((row, col), out var gotCell);
                    var gotVal = gotCell?.Value ?? BlankValue.Instance;
                    valuesCompared++;
                    var match = fmt.ValueComparisonIsDisplayOnly
                        ? FidelityCompare.DisplayMatch(refCell.Value, gotVal)
                        : FidelityCompare.ValuesMatch(refCell.Value, gotVal);
                    if (match) valuesMatched++;
                    else if (valueDefects.Count < 12)
                        valueDefects.Add($"{refSheet.Name}!{FidelityCompare.ColToLetter(col)}{row}: " +
                                         $"{FidelityCompare.ScalarStr(refCell.Value)} -> {FidelityCompare.ScalarStr(gotVal)}");
                }

                // FORMULAS — only meaningful for formats whose ceiling preserves them.
                if (fmt.PreservesFormulas && refCell.HasFormula)
                {
                    gotSheet.Cells.TryGetValue((row, col), out var gotCell);
                    formulasCompared++;
                    if (gotCell is { HasFormula: true } && FormulasEquivalent(refCell.FormulaText!, gotCell.FormulaText!))
                        formulasMatched++;
                    else if (formulaDefects.Count < 12)
                        formulaDefects.Add($"{refSheet.Name}!{FidelityCompare.ColToLetter(col)}{row}: " +
                                           $"={refCell.FormulaText} -> " +
                                           (gotCell?.HasFormula == true ? "=" + gotCell.FormulaText : "(no formula: " + FidelityCompare.ScalarStr(gotCell?.Value ?? BlankValue.Instance) + ")"));
                }
            }
        }

        // Sheet-structure check (only when the format is supposed to preserve all sheets).
        bool sheetStructureOk = !fmt.PreservesMultiSheet || got.Sheets.Count >= reference.Sheets.Count;

        bool valueDefect = valuesMatched < valuesCompared;
        bool formulaDefect = fmt.PreservesFormulas && formulasMatched < formulasCompared;

        // Classification:
        //   * every format here is supposed to carry VALUES, so a value mismatch is a candidate
        //     FreeX-output-defect (LibreOffice mis-read a value FreeX wrote).
        //   * formula loss counts as a defect ONLY for formats whose ceiling preserves formulas; for the
        //     rest (csv/html) flattening is expected coercion.
        //   * a dropped sheet (when multi-sheet is promised) is a structural defect.
        var kind = (valueDefect || formulaDefect || !sheetStructureOk)
            ? CrossKind.OutputDefect
            : CrossKind.Ok;

        var notes = new List<string>();
        if (!fmt.PreservesFormulas)
            notes.Add("formulas flattened to values (expected for this format)");
        if (!fmt.PreservesMultiSheet && reference.Sheets.Count > 1)
            notes.Add("multi-sheet collapsed to 1 (expected); compared sheet 1 only");
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
    /// Normalized formula equivalence: LibreOffice rewrites some syntax on round-trip (it may add/strip
    /// '$', re-case function names, or re-emit the leading '='). We compare on an upper-cased,
    /// whitespace-collapsed, '$'-stripped form so cosmetic rewrites don't read as a loss, while a genuinely
    /// different formula still diverges.
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
            if (s.StartsWith("=", StringComparison.Ordinal)) s = s[1..];
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

    private static string Sanitize(string name) =>
        new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static string Describe(Exception ex)
    {
        var top = ex.StackTrace?.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("at ", StringComparison.Ordinal))?.Trim();
        return $"{ex.GetType().Name}: {ex.Message}" + (top is null ? "" : $" @ {top}");
    }
}
