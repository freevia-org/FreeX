using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeX.FormatFidelity;

/// <summary>
/// How faithfully a format can carry a fidelity dimension on a write→read hop.
/// Ordering matters: None &lt; Lossy &lt; Full (used by <see cref="ChainCapability.Min"/>).
/// </summary>
internal enum Cap
{
    /// <summary>Cannot represent it at all — loss here is an expected format ceiling, never a BUG.</summary>
    None = 0,
    /// <summary>Can carry an approximation — compared with tolerance / display-normalized.</summary>
    Lossy = 1,
    /// <summary>Can carry it faithfully — any change is a BUG.</summary>
    Full = 2,
}

/// <summary>
/// The fidelity dimensions compared per round-trip. Each maps to a column in the §3a grid and an
/// extraction in <see cref="WorkbookSnapshot"/>.
/// </summary>
internal enum Dim
{
    CellValues,
    Formulas,
    NumberFormats,
    Fonts,
    Fills,
    Borders,
    Alignment,
    MultiSheet,
    SheetNames,
    MergedCells,
    ColumnWidths,
    RowHeights,
    FreezePanes,
    Hyperlinks,
    Comments,
    DefinedNames,
    DataValidation,
    ConditionalFormat,
    Charts,
    Images,
    Vba,
}

/// <summary>
/// A declarative per-format capability row (§3a). A "format key" is not always a bare extension:
/// xlsx is split into <c>xlsx-preserved</c> (patch / source-copy path) and <c>xlsx-rebuilt</c>
/// (full ClosedXML re-save), because the rebuild downgrades verbatim-preserved parts (§3d).
/// </summary>
internal sealed class CapabilityProfile
{
    public required string Key { get; init; }
    /// <summary>The extension used to resolve adapters (e.g. ".xlsx" for both xlsx-* keys).</summary>
    public required string Extension { get; init; }
    private readonly Dictionary<Dim, Cap> _caps = new();

    public Cap this[Dim d] => _caps.TryGetValue(d, out var c) ? c : Cap.None;

    public CapabilityProfile Set(Cap value, params Dim[] dims)
    {
        foreach (var d in dims) _caps[d] = value;
        return this;
    }

    /// <summary>The §3a table, grounded in §1 behavior and footnotes.</summary>
    public static IReadOnlyDictionary<string, CapabilityProfile> All { get; } = Build();

    private static IReadOnlyDictionary<string, CapabilityProfile> Build()
    {
        var all = new Dim[]
        {
            Dim.CellValues, Dim.Formulas, Dim.NumberFormats, Dim.Fonts, Dim.Fills, Dim.Borders,
            Dim.Alignment, Dim.MultiSheet, Dim.SheetNames, Dim.MergedCells, Dim.ColumnWidths,
            Dim.RowHeights, Dim.FreezePanes, Dim.Hyperlinks, Dim.Comments, Dim.DefinedNames,
            Dim.DataValidation, Dim.ConditionalFormat, Dim.Charts, Dim.Images, Dim.Vba,
        };

        var profiles = new List<CapabilityProfile>();

        // ---- fxl: native, Full everywhere except VBA (FreeX does not model macros). --------------
        profiles.Add(new CapabilityProfile { Key = "fxl", Extension = ".fxl" }
            .Set(Cap.Full, all)
            .Set(Cap.None, Dim.Vba));

        // ---- xlsx-preserved: source-package / patch path. Full for everything the model carries;
        //      VBA/CF/charts/images survive verbatim → Full while the original package is preserved.
        profiles.Add(new CapabilityProfile { Key = "xlsx-preserved", Extension = ".xlsx" }
            .Set(Cap.Full, all));

        // ---- xlsx-rebuilt: a full ClosedXML re-save. Modeled content stays Full, but verbatim-only
        //      parts (VBA, chartEx-class charts/images, unmodelled CF) downgrade to None (footnote 7/8,
        //      §3d). CF/charts/images are therefore Lossy here (modeled subset survives, native parts drop).
        profiles.Add(new CapabilityProfile { Key = "xlsx-rebuilt", Extension = ".xlsx" }
            .Set(Cap.Full, all)
            .Set(Cap.Lossy, Dim.ConditionalFormat, Dim.Charts, Dim.Images)
            .Set(Cap.None, Dim.Vba));

        // ---- xml (SpreadsheetML 2003): values/numfmt/structure Full; styling None. Formulas are now
        //      Full (footnote 2 retired): the adapter converts Excel's R1C1 to A1 on read and A1 back to
        //      R1C1 on write, so formulas round-trip faithfully. Comments Lossy (author hard-coded,
        //      footnote 5); defined names Lossy (single-area only, footnote 6); hyperlinks Lossy.
        profiles.Add(new CapabilityProfile { Key = "xml", Extension = ".xml" }
            .Set(Cap.Full, Dim.CellValues, Dim.Formulas, Dim.NumberFormats, Dim.MultiSheet, Dim.SheetNames,
                Dim.MergedCells, Dim.ColumnWidths, Dim.RowHeights, Dim.FreezePanes)
            .Set(Cap.Lossy, Dim.Hyperlinks, Dim.Comments, Dim.DefinedNames)
            .Set(Cap.None, Dim.Fonts, Dim.Fills, Dim.Borders, Dim.Alignment, Dim.DataValidation,
                Dim.ConditionalFormat, Dim.Charts, Dim.Images, Dim.Vba));

        // ---- csv / txt(tab): single-sheet, values-only. CellValues Lossy (text↔typed coercion,
        //      footnote 1); formula written as TEXT not result, recovered on reload → Lossy (footnote 3).
        //      Everything else None (MultiSheet/SheetNames None — one sheet, name not preserved).
        //      The encoding variants (csv-utf8 BOM, txt-unicode UTF-16LE BOM) share the exact same engine
        //      → identical capability ceiling; only the on-disk encoding differs.
        foreach (var (key, ext) in new[] { ("csv", ".csv"), ("txt", ".txt"), ("csv-utf8", ".csv"), ("txt-unicode", ".txt") })
        {
            profiles.Add(new CapabilityProfile { Key = key, Extension = ext }
                .Set(Cap.Lossy, Dim.CellValues, Dim.Formulas)
                .Set(Cap.None, Dim.NumberFormats, Dim.Fonts, Dim.Fills, Dim.Borders, Dim.Alignment,
                    Dim.MultiSheet, Dim.SheetNames, Dim.MergedCells, Dim.ColumnWidths, Dim.RowHeights,
                    Dim.FreezePanes, Dim.Hyperlinks, Dim.Comments, Dim.DefinedNames, Dim.DataValidation,
                    Dim.ConditionalFormat, Dim.Charts, Dim.Images, Dim.Vba));
        }

        // ---- slk (SYLK): single-sheet line format. Values + R1C1 formulas round-trip (both Lossy: value
        //      coercion + R1C1 notation). Number formats are a best-effort coarse subset preserved only on
        //      value-bearing cells (formatted-but-empty cells cannot carry a format in SYLK), so the
        //      dimension is None (the round-trip that DOES survive shows as a preserved-anyway bonus rather
        //      than being a guaranteed-faithful assertion). Everything structural/visual is None.
        profiles.Add(new CapabilityProfile { Key = "slk", Extension = ".slk" }
            .Set(Cap.Lossy, Dim.CellValues, Dim.Formulas)
            .Set(Cap.None, Dim.NumberFormats, Dim.Fonts, Dim.Fills, Dim.Borders, Dim.Alignment, Dim.MultiSheet,
                Dim.SheetNames, Dim.MergedCells, Dim.ColumnWidths, Dim.RowHeights, Dim.FreezePanes, Dim.Hyperlinks,
                Dim.Comments, Dim.DefinedNames, Dim.DataValidation, Dim.ConditionalFormat, Dim.Charts,
                Dim.Images, Dim.Vba));

        // ---- dif (Data Interchange Format): single-sheet, values only. Nothing else representable.
        profiles.Add(new CapabilityProfile { Key = "dif", Extension = ".dif" }
            .Set(Cap.Lossy, Dim.CellValues)
            .Set(Cap.None, Dim.Formulas, Dim.NumberFormats, Dim.Fonts, Dim.Fills, Dim.Borders, Dim.Alignment,
                Dim.MultiSheet, Dim.SheetNames, Dim.MergedCells, Dim.ColumnWidths, Dim.RowHeights,
                Dim.FreezePanes, Dim.Hyperlinks, Dim.Comments, Dim.DefinedNames, Dim.DataValidation,
                Dim.ConditionalFormat, Dim.Charts, Dim.Images, Dim.Vba));

        // ---- ods (OpenDocument Spreadsheet): the highest-ROI net-new format. The adapter maps cells,
        //      A1<->OpenFormula formulas, number formats, fonts/fills/borders/alignment, merges, multiple
        //      sheets + names, and column/row sizes faithfully — all Full, so the xlsx->ods->xlsx gate
        //      catches any regression in those dimensions exactly. Deferred (None, expected ceiling for
        //      now): freeze panes, hyperlinks, comments, defined names, data validation, conditional
        //      formatting, charts, images, VBA. ODF *can* hold several of these, so they are honestly
        //      marked None (not Lossy) until mapped — their loss is expected, never a BUG.
        profiles.Add(new CapabilityProfile { Key = "ods", Extension = ".ods" }
            .Set(Cap.Full, Dim.CellValues, Dim.Formulas, Dim.NumberFormats, Dim.Fonts, Dim.Fills, Dim.Borders,
                Dim.Alignment, Dim.MultiSheet, Dim.SheetNames, Dim.MergedCells, Dim.ColumnWidths, Dim.RowHeights)
            .Set(Cap.None, Dim.FreezePanes, Dim.Hyperlinks, Dim.Comments, Dim.DefinedNames, Dim.DataValidation,
                Dim.ConditionalFormat, Dim.Charts, Dim.Images, Dim.Vba));

        // ---- html: Excel reads + writes HTML. The adapter imports <table> rows/cells and exports a styled
        //      single <table>. CellValues are Lossy (export writes the DISPLAY value, re-import coerces text
        //      back to typed heuristically — compared by display, not raw type). MergedCells are Lossy: the
        //      <td colspan/rowspan> geometry round-trips exactly back into the same merged region.
        //      Fonts/Fills/Borders/Alignment are mapped to inline CSS on export and PARSED BACK on import, so
        //      they ARE asserted — but as Lossy, because the CSS mapping is an approximation: colors are the
        //      RESOLVED RGB (theme refs flatten to concrete values), a double underline collapses to single,
        //      strikethrough is not carried, a pattern fill flattens to a solid swatch, border styles map to
        //      the nearest CSS width/line bucket, and only the {left,center,right,justify} horizontal-align
        //      subset survives (vertical/wrap/rotation/indent drop). The comparer's Lossy branch scores those
        //      tolerances, so a faithful round-trip is BUGS:0. NumberFormats stays None — HTML carries the
        //      DISPLAY TEXT, not the format string, so there is nothing to assert (honest, not a fake pass).
        //      Everything else — formulas, multi-sheet, sheet names, widths/heights, charts, etc. — is None
        //      (one table = one sheet; display text carries no formula).
        profiles.Add(new CapabilityProfile { Key = "html", Extension = ".html" }
            .Set(Cap.Lossy, Dim.CellValues, Dim.MergedCells, Dim.Fonts, Dim.Fills, Dim.Borders, Dim.Alignment)
            .Set(Cap.None, Dim.Formulas, Dim.NumberFormats,
                Dim.MultiSheet, Dim.SheetNames, Dim.ColumnWidths, Dim.RowHeights, Dim.FreezePanes,
                Dim.Hyperlinks, Dim.Comments, Dim.DefinedNames, Dim.DataValidation, Dim.ConditionalFormat,
                Dim.Charts, Dim.Images, Dim.Vba));

        // ---- xltx: the xlsx writer with the package content-type flipped to template. The harness runs
        //      it through the verbatim source-copy/patch path (same as xlsx-preserved), so every modeled
        //      dimension round-trips faithfully — the chain's job is to prove the content-type flip does
        //      not corrupt the package. Identical ceiling to xlsx-preserved (all Full; Vba survives only
        //      on the verbatim path).
        profiles.Add(new CapabilityProfile { Key = "xltx", Extension = ".xltx" }
            .Set(Cap.Full, all));

        return profiles.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Chain-cap computation (§3b): the minimum capability over every write→read hop.</summary>
internal static class ChainCapability
{
    /// <summary>
    /// For a chain F0 → F1 → … → Fn the surviving cap per dimension is the min over every hop's
    /// profile (F0 is the in-memory source; every subsequent format is a write→read ceiling).
    /// </summary>
    public static Cap Min(IReadOnlyList<CapabilityProfile> hopProfiles, Dim d)
    {
        var acc = Cap.Full;
        // hopProfiles already excludes F0 (the source is captured in-memory, not a hop).
        foreach (var p in hopProfiles)
            acc = (Cap)Math.Min((int)acc, (int)p[d]);
        return acc;
    }
}
