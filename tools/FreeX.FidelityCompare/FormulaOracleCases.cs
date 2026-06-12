// Formula oracle cases for uncertain/disputed semantic behaviors.
//
// These are the cases marked as "needs oracle" or "refuted finder claims" in the 2026-06-12 code review —
// cases where Excel's actual behavior on edge inputs was not definitively established.  When the
// FidelityCompare tool runs against desktop Excel, these synthetic workbooks are included alongside
// the XLSX corpus so the next on-demand COM run arbitrates the correct result.
//
// Scope:
//   - YEARFRAC basis-0 NASD Feb ordering (do both dates need to be end-of-Feb for the cap to apply?)
//   - DATEDIF "MD" boundary: does it borrow from the month before the end-date month?
//   - TEXT sign-prefix format: does Excel prepend minus before the prefix literal ("+$" → "-$1.00")?
//   - VDB fractional periods: does switch-point arithmetic use floor(currentPeriod)?
//
// Each case is a (label, excelFormula) pair.  The generator writes the formula as a formula cell in
// a single-sheet workbook; Excel opens the workbook and recalculates, giving us the ground-truth cached
// value which the comparison engine then checks against FreeX's recalculated result.
//
// NOTE: These cases exercise LOAD-fidelity only (FreeX reads the cached value from the file FreeX
// itself generated).  To check COMPUTE-fidelity (FreeX engine vs Excel), run with --recalc.

using FreeX.Core.IO;
using FreeX.Core.Model;

internal static class FormulaOracleCases
{
    // Each oracle case: a label used as the workbook file name and the Excel formula string.
    // Formulas use A1 notation; any required constants are baked into the formula text directly.
    private static readonly IReadOnlyList<(string Label, string Formula)> Cases =
    [
        // YEARFRAC basis-0 (US 30/360 NASD) — Feb end-of-month rule.
        // Excel NASD: if both dates fall on the last day of February, the end-date is set to 30;
        // if only the start date is end-of-Feb, the end-date is also capped.  Verify this ordering.
        ("yearfrac_basis0_both_feb_end",          "=YEARFRAC(DATE(2000,2,29),DATE(2001,2,28),0)"),
        ("yearfrac_basis0_start_feb_end_only",    "=YEARFRAC(DATE(2000,2,29),DATE(2001,3,31),0)"),
        ("yearfrac_basis0_end_feb_start_not",     "=YEARFRAC(DATE(2000,1,31),DATE(2001,2,28),0)"),
        ("yearfrac_basis0_non_feb",               "=YEARFRAC(DATE(2000,1,31),DATE(2000,3,31),0)"),
        // YEARFRAC basis-1 (actual/actual) — mid-year spanning.
        ("yearfrac_basis1_spanning_year",         "=YEARFRAC(DATE(2000,7,1),DATE(2001,7,1),1)"),

        // DATEDIF "MD" — days remaining after month subtraction.
        // The documented-as-unreliable MD calculation: does Excel borrow from the month before
        // the end-date month when start-day > end-day?  E.g. DATEDIF(Jan 31, Mar 1) MD.
        ("datedif_md_start_after_end_day",        "=DATEDIF(DATE(2000,1,31),DATE(2000,3,1),\"MD\")"),
        ("datedif_md_boundary_feb",               "=DATEDIF(DATE(2000,1,31),DATE(2000,3,31),\"MD\")"),
        ("datedif_md_same_month_boundary",        "=DATEDIF(DATE(2000,1,31),DATE(2000,2,28),\"MD\")"),

        // TEXT sign-prefix format: does ""+$#,##0.00"" with a negative number produce ""-$1.00"" or ""($1.00)""?
        // And for a format with an explicit sign prefix "+", does a positive get "+$1.00"?
        ("text_positive_sign_prefix",             "=TEXT(1.5,\"+$#,##0.00\")"),
        ("text_negative_sign_prefix",             "=TEXT(-1.5,\"+$#,##0.00\")"),
        ("text_prefix_literal_sign",              "=TEXT(-1234.5,\"$#,##0.00\")"),

        // VDB fractional periods — switch from DB to SLN.
        // The switch denominator uses floor(currentPeriod): verify whether fractional start/end
        // periods round down or whether Excel uses a different rounding scheme.
        ("vdb_fractional_period_switch",          "=VDB(10000,1000,5,0,0.5)"),
        ("vdb_fractional_period_full",            "=VDB(10000,1000,5,0,3.7)"),
        ("vdb_no_switch",                         "=VDB(10000,1000,5,0,5,2,TRUE)"),
        ("vdb_switch_at_boundary",                "=VDB(10000,1000,5,2,3)"),
    ];

    /// <summary>
    /// Generates synthetic XLSX files for the oracle cases in the given directory.
    /// Each file contains a single formula cell (A1) on Sheet1; Excel will recalculate on open.
    /// Returns the list of generated file paths.
    /// </summary>
    public static IReadOnlyList<string> GenerateOracleWorkbooks(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var paths = new List<string>();

        foreach (var (label, formula) in Cases)
        {
            var path = Path.Combine(outputDirectory, $"oracle_{label}.xlsx");
            try
            {
                var workbook = new Workbook();
                var sheet = workbook.AddSheet("Sheet1");
                // Write the formula as a formula cell.  FreeX will compute the cached value when
                // loading; Excel will recalculate and display its own result for comparison.
                // SetFormula takes the text without the leading "=".
                var cellAddress = new CellAddress(sheet.Id, 1, 1);
                string formulaText = formula.StartsWith("=", StringComparison.Ordinal)
                    ? formula[1..] : formula;
                sheet.SetFormula(cellAddress, formulaText);
                // Write a label in B1 for human inspection of the generated workbooks.
                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue(label));

                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
                new XlsxFileAdapter().Save(workbook, stream);
                paths.Add(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[oracle] Failed to generate {label}: {ex.Message}");
            }
        }

        return paths;
    }
}
