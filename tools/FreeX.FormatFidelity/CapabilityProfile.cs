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

        // ---- xml (SpreadsheetML 2003): values/numfmt/structure Full; styling None; formulas Lossy
        //      (R1C1 not converted, footnote 2); comments Lossy (author hard-coded, footnote 5);
        //      defined names Lossy (single-area only, footnote 6); hyperlinks Lossy.
        profiles.Add(new CapabilityProfile { Key = "xml", Extension = ".xml" }
            .Set(Cap.Full, Dim.CellValues, Dim.NumberFormats, Dim.MultiSheet, Dim.SheetNames,
                Dim.MergedCells, Dim.ColumnWidths, Dim.RowHeights, Dim.FreezePanes)
            .Set(Cap.Lossy, Dim.Formulas, Dim.Hyperlinks, Dim.Comments, Dim.DefinedNames)
            .Set(Cap.None, Dim.Fonts, Dim.Fills, Dim.Borders, Dim.Alignment, Dim.DataValidation,
                Dim.ConditionalFormat, Dim.Charts, Dim.Images, Dim.Vba));

        // ---- csv / txt(tab): single-sheet, values-only. CellValues Lossy (text↔typed coercion,
        //      footnote 1); formula written as TEXT not result, recovered on reload → Lossy (footnote 3).
        //      Everything else None (MultiSheet/SheetNames None — one sheet, name not preserved).
        foreach (var (key, ext) in new[] { ("csv", ".csv"), ("txt", ".txt") })
        {
            profiles.Add(new CapabilityProfile { Key = key, Extension = ext }
                .Set(Cap.Lossy, Dim.CellValues, Dim.Formulas)
                .Set(Cap.None, Dim.NumberFormats, Dim.Fonts, Dim.Fills, Dim.Borders, Dim.Alignment,
                    Dim.MultiSheet, Dim.SheetNames, Dim.MergedCells, Dim.ColumnWidths, Dim.RowHeights,
                    Dim.FreezePanes, Dim.Hyperlinks, Dim.Comments, Dim.DefinedNames, Dim.DataValidation,
                    Dim.ConditionalFormat, Dim.Charts, Dim.Images, Dim.Vba));
        }

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
