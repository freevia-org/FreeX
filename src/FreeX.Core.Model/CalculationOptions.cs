namespace FreeX.Core.Model;

public enum WorkbookCalculationMode
{
    Automatic,
    Manual,

    /// <summary>
    /// Excel's "Automatic Except for Data Tables" (File &gt; Options &gt; Formulas). Everything
    /// recalculates automatically except What-If Analysis Data Tables (<c>TABLE()</c> arrays),
    /// which only recalculate on demand (F9 / manual recalc). Corresponds to XLSX
    /// <c>calcPr/@calcMode="autoNoTable"</c> and the legacy .xls <c>CalcModeRecord</c>
    /// <c>AUTOMATIC_EXCEPT_TABLES</c> value.
    /// </summary>
    AutomaticExceptDataTables
}
