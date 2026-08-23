using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Excel's real "Convert to Range" lowers every structured reference into the converted table
/// (TableName[Column], [@Column]/[#This Row],[Column], TableName[#Data], etc.) into the
/// equivalent absolute A1 cell/range reference before the table metadata disappears. Structured
/// references resolve purely by looking the table up in <see cref="Sheet.StructuredTables"/> via
/// <see cref="StructuredReferenceResolver"/> — once <see cref="ConvertStructuredTableToRangeCommand"/>
/// removes the table model, every formula still using a structured reference into it would
/// otherwise evaluate to #NAME?/#REF! forever. This must run BEFORE the table is removed from the
/// sheet, since resolving each reference needs the table's still-live column layout/extent.
/// <para>
/// Scope: this walks every ordinary cell formula in the workbook (<see cref="Sheet.EnumerateFormulaCells"/>
/// on every sheet), which covers the documented scenario (a formula on another sheet referencing
/// the converted table) as well as formulas inside the table's own data body (e.g. a calculated
/// column's bare <c>[Column]</c> reference). Defined-name (NamedFormula) bodies that themselves
/// contain a structured reference into this table are intentionally out of scope here — lowering
/// those would need the same AST walk applied to <c>Workbook.NamedFormulas</c>/<c>ScopedNamedFormulas</c>,
/// which is a natural follow-up but not required by the reported scenario.
/// </para>
/// </summary>
internal static class ConvertToRangeStructuredReferenceLowering
{
    // Mirrors the slot numbering RowColumnShiftHelpers.Rules.cs's Slot* constants use for
    // (Guid, Slot) threshold-snapshot keys, so LowerRuleFormulas below can write into --
    // and RowColumnShiftHelpers.RestoreRuleFormulas can read back out of -- the very same
    // dictionaries that RenameStructuredTableCommand/MoveRangeCommand already use.
    private const int SlotColorScaleMin = 1;
    private const int SlotColorScaleMid = 2;
    private const int SlotColorScaleMax = 3;
    private const int SlotDataBarMin    = 4;
    private const int SlotDataBarMax    = 5;
    private const int SlotIconSetBase   = 10;

    /// <summary>
    /// Rewrites every formula in the workbook that references <paramref name="table"/> (which must
    /// still be present in <paramref name="tableSheet"/>.StructuredTables at call time) via a
    /// structured reference into the equivalent absolute A1 cell/range reference. Every cell whose
    /// formula text changes has its pre-rewrite text recorded in <paramref name="snapshot"/> so the
    /// caller can restore it verbatim on undo.
    /// </summary>
    internal static void LowerAllFormulas(
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        Dictionary<CellAddress, string> snapshot)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var address in sheet.EnumerateFormulaCells())
            {
                var cell = sheet.GetCell(address);
                if (cell?.FormulaText is null)
                    continue;

                var lowered = LowerFormula(cell.FormulaText, workbook, tableSheet, table, address);
                if (lowered is null)
                    continue;

                snapshot[address] = cell.FormulaText;
                // Lowering a structured reference is not a fresh authoring/edit of this cell: the
                // Cell.FormulaText setter resets ArrayMode/LegacyArrayRows/LegacyArrayCols to the
                // "freshly authored modern formula" defaults on every assignment, which would
                // silently strip a legacy CSE array cell's fixed-extent identity (and the "you
                // cannot change part of an array" protection that depends on LegacyArrayRows/Cols
                // being non-zero) merely because the table it referenced was converted to a range.
                // Same reason RowColumnShiftHelpers.RewriteAllFormulas routes through this helper --
                // and the RestoreFormulas that undoes this snapshot already does too.
                RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(cell, lowered);
            }
        }
    }

    /// <summary>
    /// Workbook-wide counterpart of <see cref="LowerAllFormulas"/> for every ConditionalFormat
    /// rule's FormulaText/threshold values and every DataValidation rule's Formula1/Formula2 in
    /// the workbook -- mirrors the scope of <see cref="RowColumnShiftHelpers.RewriteRuleFormulas(Workbook, RewriteOperation, Dictionary{Guid, string}, Dictionary{ValueTuple{Guid, int}, string}, Dictionary{ValueTuple{Guid, int}, string})"/>
    /// (the primitive <c>RenameStructuredTableCommand</c> uses for a plain rename), except this
    /// LOWERS to an absolute A1 reference instead of substituting a new table name, since the table
    /// is about to disappear entirely and there will be nothing left for a renamed structured
    /// reference to point at. Each rule's own <c>AppliesTo.Start</c> is used as the host cell for
    /// resolving <c>[@Column]</c>/<c>[#This Row]</c> relative references, matching the anchor
    /// <see cref="FreeX.Core.Calc.ViewportConditionalFormatEvaluator"/> and
    /// <c>DataValidationService</c> already use at evaluation time. Changed values are recorded in
    /// the same three snapshot dictionaries <c>RewriteRuleFormulas</c> populates, so the existing
    /// <see cref="RowColumnShiftHelpers.RestoreRuleFormulas(Workbook, Dictionary{Guid, string}, Dictionary{ValueTuple{Guid, int}, string}, Dictionary{ValueTuple{Guid, int}, string})"/>
    /// undoes this rewrite too.
    /// </summary>
    internal static void LowerRuleFormulas(
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        Dictionary<Guid, string?> cfSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot,
        Dictionary<(Guid Id, int Slot), string?> dvSnapshot)
    {
        foreach (var sheet in workbook.Sheets)
        {
            var cfCountBefore = cfSnapshot.Count;
            var cfThresholdCountBefore = cfThresholdSnapshot.Count;

            foreach (var rule in sheet.ConditionalFormats)
            {
                var anchor = rule.AppliesTo.Start;

                if (rule.FormulaText is { } ft)
                {
                    var lowered = LowerFormula(ft, workbook, tableSheet, table, anchor);
                    if (lowered is not null)
                    {
                        cfSnapshot[rule.Id] = ft;
                        rule.FormulaText = lowered;
                    }
                }

                LowerThreshold(rule, SlotColorScaleMin, rule.MinThresholdType, rule.MinThresholdValue,
                    workbook, tableSheet, table, anchor, cfThresholdSnapshot,
                    lowered => rule.MinThresholdValue = lowered);
                LowerThreshold(rule, SlotColorScaleMid, rule.MidThresholdType, rule.MidThresholdValue,
                    workbook, tableSheet, table, anchor, cfThresholdSnapshot,
                    lowered => rule.MidThresholdValue = lowered);
                LowerThreshold(rule, SlotColorScaleMax, rule.MaxThresholdType, rule.MaxThresholdValue,
                    workbook, tableSheet, table, anchor, cfThresholdSnapshot,
                    lowered => rule.MaxThresholdValue = lowered);
                LowerThreshold(rule, SlotDataBarMin, rule.DataBarMinThresholdType, rule.DataBarMinThresholdValue,
                    workbook, tableSheet, table, anchor, cfThresholdSnapshot,
                    lowered => rule.DataBarMinThresholdValue = lowered);
                LowerThreshold(rule, SlotDataBarMax, rule.DataBarMaxThresholdType, rule.DataBarMaxThresholdValue,
                    workbook, tableSheet, table, anchor, cfThresholdSnapshot,
                    lowered => rule.DataBarMaxThresholdValue = lowered);

                for (var i = 0; i < rule.IconSetThresholds.Count; i++)
                {
                    var threshold = rule.IconSetThresholds[i];
                    if (threshold.Type == CfThresholdType.Formula && threshold.Value is { } tv)
                    {
                        var lowered = LowerFormula(tv, workbook, tableSheet, table, anchor);
                        if (lowered is not null)
                        {
                            cfThresholdSnapshot[(rule.Id, SlotIconSetBase + i)] = tv;
                            rule.IconSetThresholds[i] = threshold with { Value = lowered };
                        }
                    }
                }
            }

            foreach (var rule in sheet.DataValidations)
            {
                var anchor = rule.AppliesTo.Start;

                if (rule.Formula1 is { } f1)
                {
                    var lowered = LowerFormula(f1, workbook, tableSheet, table, anchor);
                    if (lowered is not null)
                    {
                        dvSnapshot[(rule.Id, 1)] = f1;
                        rule.Formula1 = lowered;
                    }
                }
                if (rule.Formula2 is { } f2)
                {
                    var lowered = LowerFormula(f2, workbook, tableSheet, table, anchor);
                    if (lowered is not null)
                    {
                        dvSnapshot[(rule.Id, 2)] = f2;
                        rule.Formula2 = lowered;
                    }
                }
            }

            // Mirrors RewriteRuleFormulas' own cache-invalidation: the CF viewport context cache is
            // keyed on (sheet.Id, sheet.ContentVersion, sheet.ConditionalFormats.Version) and caches a
            // precompiled AST per rule, so mutating FormulaText/threshold values in place above never
            // invalidates it on its own.
            if (cfSnapshot.Count > cfCountBefore || cfThresholdSnapshot.Count > cfThresholdCountBefore)
                sheet.ConditionalFormats.NotifyRulesChanged();
        }
    }

    private static void LowerThreshold(
        ConditionalFormat rule,
        int slot,
        CfThresholdType type,
        string? value,
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        CellAddress hostAddress,
        Dictionary<(Guid Id, int Slot), string?> snapshot,
        Action<string> apply)
    {
        if (type != CfThresholdType.Formula || value is null)
            return;
        var lowered = LowerFormula(value, workbook, tableSheet, table, hostAddress);
        if (lowered is not null)
        {
            snapshot[(rule.Id, slot)] = value;
            apply(lowered);
        }
    }

    /// <summary>
    /// Lowers every chart-verbatim formula in the workbook (series Val/Cat/Tx/BubbleSize, series
    /// range data-label source formulas, and custom error-bar +/- range formulas) that references
    /// <paramref name="table"/> via a structured reference into the equivalent absolute A1
    /// reference -- the chart-formula counterpart of <see cref="LowerRuleFormulas"/>, mirroring the
    /// scope of <see cref="RowColumnShiftHelpers.RewriteAllChartFormulasForTableRename"/> (the
    /// primitive a plain table rename uses for charts). Each chart's own hosting sheet is used as
    /// the host context so the lowered reference is correctly sheet-qualified when the chart lives
    /// on a different sheet than the table. Callers must snapshot beforehand via
    /// <see cref="RowColumnShiftHelpers.CaptureChartVerbatimFormulas(Workbook)"/> and restore via
    /// <see cref="RowColumnShiftHelpers.RestoreChartVerbatimFormulas(Workbook, List{RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot})"/>
    /// on undo -- those helpers are unconditional before/after snapshots, independent of what
    /// rewrote the formulas in between.
    /// </summary>
    internal static void LowerChartFormulas(Workbook workbook, Sheet tableSheet, StructuredTableModel table)
    {
        foreach (var sheet in workbook.Sheets)
        {
            // A chart formula's structured reference is always table-qualified (there is no "current
            // row" concept for a chart series), so only the anchor's .Sheet matters here -- it decides
            // whether the lowered reference gets an explicit sheet qualifier.
            var hostAddress = new CellAddress(sheet.Id, 0, 0);

            foreach (var chart in sheet.Charts)
                ChartFormulaFieldTransformer.Transform(
                    chart,
                    formula => LowerChartFormula(formula, workbook, tableSheet, table, hostAddress));
        }
    }

    private static string? LowerChartFormula(
        string? formulaText, Workbook workbook, Sheet tableSheet, StructuredTableModel table, CellAddress hostAddress)
    {
        if (string.IsNullOrWhiteSpace(formulaText))
            return formulaText;

        return LowerFormula(formulaText, workbook, tableSheet, table, hostAddress) ?? formulaText;
    }

    private static string? LowerFormula(
        string formulaText,
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        CellAddress hostAddress)
    {
        try
        {
            var tokens = new Lexer(formulaText).Tokenize();
            var ast = new Parser(tokens).Parse();
            var changed = false;
            var rewritten = LowerNode(ast, workbook, tableSheet, table, hostAddress, ref changed);
            return changed ? FormulaSerializer.Serialize(rewritten) : null;
        }
        catch
        {
            return null; // malformed formula — leave untouched, mirrors FormulaRewriter.Rewrite.
        }
    }

    private static FormulaNode LowerNode(
        FormulaNode node,
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        CellAddress hostAddress,
        ref bool changed)
    {
        switch (node)
        {
            case StructuredReferenceNode sr:
                return LowerStructuredReference(sr, workbook, tableSheet, table, hostAddress, ref changed);

            case StructuredCurrentRowReferenceNode scr:
                return LowerCurrentRowReference(scr, workbook, tableSheet, table, hostAddress, ref changed);

            case BinaryOpNode b:
                return b with
                {
                    Left = LowerNode(b.Left, workbook, tableSheet, table, hostAddress, ref changed),
                    Right = LowerNode(b.Right, workbook, tableSheet, table, hostAddress, ref changed)
                };

            case UnaryOpNode u:
                return u with { Operand = LowerNode(u.Operand, workbook, tableSheet, table, hostAddress, ref changed) };

            case FunctionCallNode f:
                var newArgs = new List<FormulaNode>(f.Arguments.Count);
                foreach (var arg in f.Arguments)
                    newArgs.Add(LowerNode(arg, workbook, tableSheet, table, hostAddress, ref changed));
                return f with { Arguments = newArgs };

            default:
                // CellRefNode, RangeRefNode, FullColumnRangeRefNode, FullRowRangeRefNode, NumberNode,
                // StringNode, BooleanNode, NamedRangeNode, ErrorNode, ArrayConstantNode, and
                // OmittedArgumentNode never nest a structured reference to this table.
                return node;
        }
    }

    private static FormulaNode LowerStructuredReference(
        StructuredReferenceNode sr,
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        CellAddress hostAddress,
        ref bool changed)
    {
        if (!ReferencesTable(sr.TableName, table, tableSheet, hostAddress))
            return sr;

        var range = StructuredReferenceResolver.Resolve(workbook, tableSheet, sr.TableName, sr.ColumnName, hostAddress);
        if (range is null)
            return sr; // couldn't resolve (e.g. already-invalid selector) — leave the text as-is.

        changed = true;
        return BuildReferenceNode(range.Value, tableSheet, hostAddress);
    }

    private static FormulaNode LowerCurrentRowReference(
        StructuredCurrentRowReferenceNode scr,
        Workbook workbook,
        Sheet tableSheet,
        StructuredTableModel table,
        CellAddress hostAddress,
        ref bool changed)
    {
        if (!ReferencesTable(scr.TableName, table, tableSheet, hostAddress))
            return scr;

        var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
            workbook, tableSheet, hostAddress, scr.TableName, scr.ColumnName);
        if (address is null)
            return scr;

        changed = true;
        return BuildReferenceNode(new GridRange(address.Value, address.Value), tableSheet, hostAddress);
    }

    /// <summary>
    /// True when a structured reference's (possibly empty/whitespace) table-name literal targets
    /// <paramref name="table"/>: either explicitly by name/display-name, or — when empty — because
    /// the host formula cell itself lives inside the table's range, the only case in which an
    /// unqualified structured reference is legal (mirroring <see cref="StructuredReferenceResolver.Resolve"/>'s
    /// own unqualified-selector handling).
    /// </summary>
    private static bool ReferencesTable(string? tableName, StructuredTableModel table, Sheet tableSheet, CellAddress hostAddress)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            return string.Equals(table.Name, tableName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(table.DisplayName, tableName, StringComparison.OrdinalIgnoreCase);
        }

        return tableSheet.Id == hostAddress.Sheet &&
               hostAddress.Row >= table.Range.Start.Row && hostAddress.Row <= table.Range.End.Row &&
               hostAddress.Col >= table.Range.Start.Col && hostAddress.Col <= table.Range.End.Col;
    }

    /// <summary>
    /// Builds the absolute A1 replacement for a resolved structured-reference range. Excel always
    /// emits absolute ($) coordinates when lowering a structured reference, and adds an explicit
    /// sheet qualifier only when the table lives on a different sheet than the formula referencing
    /// it (same convention <see cref="FormulaRewriter"/> uses for sheet-qualified ranges: the range
    /// carries <c>SheetName</c>, and so does its Start endpoint).
    /// </summary>
    private static FormulaNode BuildReferenceNode(GridRange range, Sheet tableSheet, CellAddress hostAddress)
    {
        var sheetName = range.Start.Sheet == hostAddress.Sheet ? null : tableSheet.Name;
        var start = new CellRefNode(
            CellAddress.NumberToColumnName(range.Start.Col),
            range.Start.Row,
            IsColAbsolute: true,
            IsRowAbsolute: true,
            SheetName: sheetName);

        if (range.Start == range.End)
            return start;

        var end = new CellRefNode(
            CellAddress.NumberToColumnName(range.End.Col),
            range.End.Row,
            IsColAbsolute: true,
            IsRowAbsolute: true);

        return new RangeRefNode(start, end, SheetName: sheetName);
    }
}
