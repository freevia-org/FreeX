using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class SpreadsheetXmlFileAdapter
{
    /// <summary>
    /// Excel saves SpreadsheetML 2003 formulas in R1C1 notation (e.g. <c>=RC[-1]+R[-1]C</c>). The model
    /// stores formulas in A1, so on read we convert every R1C1 reference token to A1 relative to the
    /// owning cell, and on write we convert A1 back to R1C1 — round-tripping the formula faithfully
    /// without corrupting it into literal R1C1 text. The conversion logic lives in the shared
    /// <see cref="R1C1FormulaConverter"/> so the SYLK (.slk) adapter, which also stores R1C1 formulas,
    /// reuses exactly the same scanner.
    /// </summary>
    private static string ConvertR1C1FormulaToA1(string formula, uint row, uint col) =>
        R1C1FormulaConverter.ToA1(formula, row, col);

    private static string ConvertA1FormulaToR1C1(string formula, uint row, uint col) =>
        R1C1FormulaConverter.ToR1C1(formula, row, col);

    private static bool LooksLikeR1C1(string formula) =>
        R1C1FormulaConverter.LooksLikeR1C1(formula);
}
