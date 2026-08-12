using System.Collections.Generic;
using System.Linq;

namespace FreeW.Core.Model;

/// <summary>
/// Specifies the visual style for one conditional-format region of a table style (e.g. the header row,
/// a banded row, or the whole-table default).
/// </summary>
public sealed record TableStyleBand(
    /// <summary>Background fill in RRGGBB hex (no '#'), or null for transparent.</summary>
    string? FillHex,
    /// <summary>When true, text in this band is rendered bold.</summary>
    bool Bold = false);

/// <summary>
/// A built-in Word-compatible table style. Each style defines the visual appearance of a table's
/// key regions — overall borders, header-row emphasis, banded-row alternation, first/last column
/// emphasis — and provides a <see cref="WordStyleId"/> used for OOXML round-trip via
/// <c>w:tblStyle w:val</c>.
/// </summary>
public sealed record DocumentTableStyle(
    /// <summary>Display name shown in the Table Styles gallery.</summary>
    string Name,
    /// <summary>OOXML style id emitted as <c>w:tblStyle w:val</c> (also used in styles.xml as <c>w:styleId</c>).</summary>
    string WordStyleId,
    /// <summary>Whether the style draws outer + inner cell borders.</summary>
    bool Borders,
    /// <summary>Header-row band (first row): fill + bold, or null for no special treatment.</summary>
    TableStyleBand? HeaderBand,
    /// <summary>Odd body-row band: fill, or null for no banding.</summary>
    TableStyleBand? BandedRowOdd,
    /// <summary>Even body-row band: fill, or null for unshaded (transparent) even rows.</summary>
    TableStyleBand? BandedRowEven,
    /// <summary>First-column emphasis: bold, or null for no emphasis.</summary>
    TableStyleBand? FirstColumnBand,
    /// <summary>Last-column emphasis: bold, or null for no emphasis.</summary>
    TableStyleBand? LastColumnBand,
    /// <summary>Last-row band: fill + bold, or null for no special treatment.</summary>
    TableStyleBand? LastRowBand,
    /// <summary>Outer border color in RRGGBB hex (no '#'), or null to inherit/default.</summary>
    string? BorderColorHex = null)
{
    // ── Built-in catalog ────────────────────────────────────────────────────────────────────────────
    // Mirrors the subset of Word's built-in table styles that FreeW exposes in the gallery. Accent colors
    // follow the Office theme palette (accent1 = 4472C4, accent2 = ED7D31, accent3 = A9D18E, etc.).

    private static readonly TableStyleBand NoBand = new(FillHex: null);

    /// <summary>The full built-in table-style catalog, in gallery display order.</summary>
    public static readonly IReadOnlyList<DocumentTableStyle> Catalog =
    [
        // ── Plain / Grid ────────────────────────────────────────────────────────────────────────────
        new(
            Name: "Table Grid",
            WordStyleId: "TableGrid",
            Borders: true,
            HeaderBand: null,
            BandedRowOdd: null,
            BandedRowEven: null,
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null),

        new(
            Name: "Plain Table 1",
            WordStyleId: "PlainTable1",
            Borders: false,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: null,
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null),

        new(
            Name: "Plain Table 2",
            WordStyleId: "PlainTable2",
            Borders: false,
            HeaderBand: new(FillHex: null, Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: null,
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null),

        new(
            Name: "Plain Table 3",
            WordStyleId: "PlainTable3",
            Borders: false,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: null,
            BandedRowEven: null,
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: new(FillHex: null, Bold: true)),

        new(
            Name: "Plain Table 4",
            WordStyleId: "PlainTable4",
            Borders: false,
            HeaderBand: new(FillHex: null, Bold: true),
            BandedRowOdd: null,
            BandedRowEven: null,
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null),

        new(
            Name: "Plain Table 5",
            WordStyleId: "PlainTable5",
            Borders: false,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: new("BDD7EE"),
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null),

        // ── Grid Table Light ────────────────────────────────────────────────────────────────────────
        new(
            Name: "Grid Table Light",
            WordStyleId: "GridTableLight",
            Borders: true,
            HeaderBand: null,
            BandedRowOdd: null,
            BandedRowEven: null,
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null),

        // ── Grid Table 1 – 6 (accent1 / blue) ──────────────────────────────────────────────────────
        new(
            Name: "Grid Table 1 Light",
            WordStyleId: "GridTable1Light",
            Borders: true,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: null,
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null,
            BorderColorHex: "4472C4"),

        new(
            Name: "Grid Table 2",
            WordStyleId: "GridTable2",
            Borders: true,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: new("BDD7EE"),
            BandedRowEven: new("DEEAF1"),
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null,
            BorderColorHex: "4472C4"),

        new(
            Name: "Grid Table 3",
            WordStyleId: "GridTable3",
            Borders: true,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: null,
            FirstColumnBand: new(FillHex: null, Bold: true),
            LastColumnBand: null,
            LastRowBand: new("4472C4", Bold: true),
            BorderColorHex: "4472C4"),

        new(
            Name: "Grid Table 4",
            WordStyleId: "GridTable4",
            Borders: true,
            HeaderBand: new("2F5496", Bold: true),
            BandedRowOdd: new("BDD7EE"),
            BandedRowEven: new("DEEAF1"),
            FirstColumnBand: new(FillHex: null, Bold: true),
            LastColumnBand: null,
            LastRowBand: new("2F5496", Bold: true),
            BorderColorHex: "2F5496"),

        new(
            Name: "Grid Table 5 Dark",
            WordStyleId: "GridTable5Dark",
            Borders: true,
            HeaderBand: new("2F5496", Bold: true),
            BandedRowOdd: new("2F5496"),
            BandedRowEven: new("4472C4"),
            FirstColumnBand: new("1F3864", Bold: true),
            LastColumnBand: null,
            LastRowBand: new("2F5496", Bold: true),
            BorderColorHex: "1F3864"),

        new(
            Name: "Grid Table 6 Colorful",
            WordStyleId: "GridTable6Colorful",
            Borders: true,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: new("BDD7EE"),
            FirstColumnBand: new("4472C4", Bold: true),
            LastColumnBand: new("4472C4", Bold: true),
            LastRowBand: new("4472C4", Bold: true),
            BorderColorHex: "4472C4"),

        new(
            Name: "Grid Table 7 Colorful",
            WordStyleId: "GridTable7Colorful",
            Borders: true,
            HeaderBand: new("2F5496", Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: new("BDD7EE"),
            FirstColumnBand: new("2F5496", Bold: true),
            LastColumnBand: new("2F5496", Bold: true),
            LastRowBand: new("2F5496", Bold: true),
            BorderColorHex: "2F5496"),

        // ── List Table 1 – 6 (accent1 / blue) ──────────────────────────────────────────────────────
        new(
            Name: "List Table 1 Light",
            WordStyleId: "ListTable1Light",
            Borders: false,
            HeaderBand: new(FillHex: null, Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: null,
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null),

        new(
            Name: "List Table 2",
            WordStyleId: "ListTable2",
            Borders: false,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: new("BDD7EE"),
            BandedRowEven: new("DEEAF1"),
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: null),

        new(
            Name: "List Table 3",
            WordStyleId: "ListTable3",
            Borders: false,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: null,
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: new("4472C4", Bold: true)),

        new(
            Name: "List Table 4",
            WordStyleId: "ListTable4",
            Borders: false,
            HeaderBand: new("2F5496", Bold: true),
            BandedRowOdd: new("BDD7EE"),
            BandedRowEven: new("DEEAF1"),
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: new("2F5496", Bold: true)),

        new(
            Name: "List Table 5 Dark",
            WordStyleId: "ListTable5Dark",
            Borders: false,
            HeaderBand: new("2F5496", Bold: true),
            BandedRowOdd: new("2F5496"),
            BandedRowEven: new("4472C4"),
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: new("2F5496", Bold: true)),

        new(
            Name: "List Table 6 Colorful",
            WordStyleId: "ListTable6Colorful",
            Borders: false,
            HeaderBand: new("4472C4", Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: new("BDD7EE"),
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: new("4472C4", Bold: true)),

        new(
            Name: "List Table 7 Colorful",
            WordStyleId: "ListTable7Colorful",
            Borders: false,
            HeaderBand: new("2F5496", Bold: true),
            BandedRowOdd: new("DEEAF1"),
            BandedRowEven: new("BDD7EE"),
            FirstColumnBand: null,
            LastColumnBand: null,
            LastRowBand: new("2F5496", Bold: true)),
    ];

    // ── Lookup helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Find a catalog entry by its OOXML style id (case-insensitive), or null when not found.</summary>
    public static DocumentTableStyle? FindById(string wordStyleId) =>
        Catalog.FirstOrDefault(s =>
            string.Equals(s.WordStyleId, wordStyleId, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>Find a catalog entry by display name (case-insensitive), or null when not found.</summary>
    public static DocumentTableStyle? FindByName(string name) =>
        Catalog.FirstOrDefault(s =>
            string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase));

    // ── Rendering helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the effective fill hex and bold flag for a specific row position, given the active
    /// formatting toggles and the total number of rows. Returns (null, false) for a plain unstyled cell.
    /// </summary>
    public (string? FillHex, bool Bold) ResolveCellStyle(int rowIndex, int totalRows, bool isFirstCol, bool isLastCol, TableFormatting fmt)
    {
        // Header row wins over everything else.
        if (fmt.HeaderRow && rowIndex == 0)
            return (HeaderBand?.FillHex, HeaderBand?.Bold ?? false);

        // Last row wins over banding.
        if (fmt.LastRow && rowIndex == totalRows - 1)
            return (LastRowBand?.FillHex, LastRowBand?.Bold ?? false);

        // Column emphasis: first and last columns win over banding.
        if (fmt.FirstColumn && isFirstCol && FirstColumnBand is not null)
            return (FirstColumnBand.FillHex, FirstColumnBand.Bold);
        if (fmt.LastColumn && isLastCol && LastColumnBand is not null)
            return (LastColumnBand.FillHex, LastColumnBand.Bold);

        // Banded rows.
        if (fmt.BandedRows)
        {
            var bodyIndex = TableBanding.BodyRowIndex(rowIndex, fmt.HeaderRow);
            if (bodyIndex >= 0)
            {
                var band = bodyIndex % 2 == 0 ? BandedRowOdd : BandedRowEven;
                return (band?.FillHex, band?.Bold ?? false);
            }
        }

        return (null, false);
    }
}
