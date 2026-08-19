using System.Globalization;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Resolves the display text shown inside list-style legacy form controls (drop-downs and list
/// boxes) for rendering. The selected item is the <see cref="FormControlModel.SelectedIndex"/>-th
/// (1-based, Excel <c>sel</c>) cell value of the control's <see cref="FormControlModel.ListFillRange"/>
/// source range — which may be an A1 range, a cross-sheet reference, or a defined name.
///
/// This runs in the host (where the workbook is available) and projects the resolved string onto
/// <see cref="FormControlModel.SelectedText"/> so the UI layer renders text without needing raw
/// workbook/range access. Anything that cannot be resolved falls back to <see langword="null"/>
/// (blank in the renderer), preserving the prior behavior.
/// </summary>
public static class FormControlListResolver
{
    /// <summary>
    /// Populates <see cref="FormControlModel.SelectedText"/> for every list-style control on the
    /// sheet, resolving each control's selected item against the workbook. Non-list controls are
    /// left untouched. Safe to call repeatedly (it overwrites the projection each time).
    /// </summary>
    public static void PopulateSelectedText(Sheet sheet, Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        foreach (var control in sheet.FormControls)
        {
            if (!IsListControl(control.Kind))
                continue;

            control.SelectedText = ResolveSelectedText(control, sheet, workbook);
        }
    }

    /// <summary>
    /// Resolves the selected item's display text for a single list-style control, or
    /// <see langword="null"/> when the control is not a list control, has no selection, or the
    /// source range / defined name cannot be resolved.
    /// </summary>
    /// <param name="control">The control whose <see cref="FormControlModel.ListFillRange"/> and
    /// <see cref="FormControlModel.SelectedIndex"/> drive the lookup.</param>
    /// <param name="sheet">The sheet the control lives on (used to resolve same-sheet, unqualified refs).</param>
    /// <param name="workbook">The workbook (used for cross-sheet refs and defined names). May be null.</param>
    public static string? ResolveSelectedText(FormControlModel control, Sheet sheet, Workbook? workbook)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(sheet);

        if (!IsListControl(control.Kind))
            return null;

        // Excel encodes "nothing selected" as sel="0" (or an absent sel).
        if (control.SelectedIndex is not { } selectedIndex || selectedIndex < 1)
            return null;

        if (string.IsNullOrWhiteSpace(control.ListFillRange))
            return null;

        if (!TryResolveRange(control.ListFillRange.Trim(), sheet, workbook, out var resolved))
            return null;

        // SelectedIndex is 1-based; Excel populates list-style controls from the FIRST COLUMN of
        // ListFillRange only (a multi-column range never contributes its 2nd+ columns as items).
        var zeroBased = selectedIndex - 1;
        var rowCount = (long)(resolved.EndRow - resolved.StartRow) + 1;
        if (zeroBased >= rowCount)
            return null;

        var row = resolved.StartRow + (uint)zeroBased;
        var col = resolved.StartCol;

        // Use GetValue (not GetCell) so a selected item that falls on a spill member of another
        // formula's dynamic array resolves correctly: spill members live only in the sheet's spill
        // overlay, not in the cell dictionary GetCell reads, so GetCell would see a live spill member
        // as "no cell" and blank it out.
        var value = resolved.Sheet.GetValue(row, col);
        var text = ToDisplayText(value);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Returns the number of list items exposed by a form-control fill range. The same resolver used
    /// for the rendered selected text is used here so A1 ranges, workbook-global names, and
    /// sheet-scoped names all have identical behavior in both shells.
    /// </summary>
    public static int EstimateItemCount(FormControlModel control, Sheet sheet, Workbook? workbook)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(sheet);

        if (!IsListControl(control.Kind) || string.IsNullOrWhiteSpace(control.ListFillRange))
            return 0;

        return TryResolveRange(control.ListFillRange.Trim(), sheet, workbook, out var resolved)
            ? checked((int)((long)resolved.EndRow - resolved.StartRow + 1))
            : 0;
    }

    private static bool IsListControl(FormControlKind kind) =>
        kind is FormControlKind.DropDown or FormControlKind.ListBox;

    private readonly record struct ResolvedRange(Sheet Sheet, uint StartRow, uint StartCol, uint EndRow, uint EndCol);

    private static bool TryResolveRange(string source, Sheet sheet, Workbook? workbook, out ResolvedRange resolved)
    {
        resolved = default;

        // Drop a leading '=' if the fmlaRange was authored as a formula.
        if (source.StartsWith('='))
            source = source[1..].Trim();

        if (source.Length == 0)
            return false;

        // Fast path: a plain same-sheet A1 cell/range with no sheet qualifier.
        if (source.IndexOf('!') < 0 && TryResolveSimpleSameSheetRange(source, sheet, out resolved))
            return true;

        try
        {
            var tokens = new Lexer(source).Tokenize();
            var ast = new Parser(tokens).Parse();

            switch (ast)
            {
                case RangeRefNode range:
                    return TryResolveRangeRefNode(range, sheet, workbook, out resolved);

                case CellRefNode cell:
                    return TryResolveCellRefNode(cell, sheet, workbook, out resolved);

                case NamedRangeNode named when workbook is not null &&
                                               !HasSheetScopedNamedFormula(workbook, named.Name, sheet.Id) &&
                                               workbook.TryGetNamedRange(named.Name, sheet.Id, out var namedRange):
                    return TryResolveNamedRange(namedRange, sheet, workbook, out resolved);

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveSimpleSameSheetRange(string source, Sheet sheet, out ResolvedRange resolved)
    {
        resolved = default;

        var span = source.AsSpan();
        var colon = span.IndexOf(':');
        if (colon < 0)
        {
            if (!TryParseA1Cell(span, sheet.Id, out var single))
                return false;

            resolved = new ResolvedRange(sheet, single.Row, single.Col, single.Row, single.Col);
            return true;
        }

        if (!TryParseA1Cell(span[..colon], sheet.Id, out var start) ||
            !TryParseA1Cell(span[(colon + 1)..], sheet.Id, out var end))
        {
            return false;
        }

        resolved = Normalize(sheet, start.Row, start.Col, end.Row, end.Col);
        return true;
    }

    private static bool TryResolveRangeRefNode(RangeRefNode range, Sheet sheet, Workbook? workbook, out ResolvedRange resolved)
    {
        resolved = default;

        var sheetName = range.SheetName ?? range.Start.SheetName ?? range.End.SheetName;
        if (!TryResolveSourceSheet(sheetName, sheet, workbook, out var sourceSheet))
            return false;

        resolved = Normalize(
            sourceSheet,
            range.Start.Row,
            range.Start.ColumnNumber,
            range.End.Row,
            range.End.ColumnNumber);
        return true;
    }

    private static bool TryResolveCellRefNode(CellRefNode cell, Sheet sheet, Workbook? workbook, out ResolvedRange resolved)
    {
        resolved = default;

        if (!TryResolveSourceSheet(cell.SheetName, sheet, workbook, out var sourceSheet))
            return false;

        resolved = new ResolvedRange(sourceSheet, cell.Row, cell.ColumnNumber, cell.Row, cell.ColumnNumber);
        return true;
    }

    private static bool TryResolveNamedRange(GridRange namedRange, Sheet sheet, Workbook workbook, out ResolvedRange resolved)
    {
        var sourceSheet = workbook.GetSheet(namedRange.Start.Sheet) ?? sheet;
        resolved = Normalize(
            sourceSheet,
            namedRange.Start.Row,
            namedRange.Start.Col,
            namedRange.End.Row,
            namedRange.End.Col);
        return true;
    }

    /// <summary>
    /// Excel scope precedence: a name scoped to the current sheet always wins over a
    /// same-named workbook-global name, regardless of whether either name is a plain range
    /// or a formula expression. Workbook.TryGetNamedRange(name, sheetId) only consults
    /// ScopedNamedRanges (range-kind) before falling back to the workbook-global NamedRanges
    /// dictionary — it never looks at ScopedNamedFormulas, so a sheet-scoped named FORMULA is
    /// invisible to it and the shadowed workbook-global range would be returned as if it were
    /// the correct match. Guard the named-range case here so that scenario is left unresolved
    /// (falls back to null/blank) instead of silently showing the wrong workbook-global range.
    /// Mirrors DataValidationService.ListSources.cs's HasSheetScopedNamedFormula helper.
    /// </summary>
    private static bool HasSheetScopedNamedFormula(Workbook workbook, string name, SheetId sheetId) =>
        workbook.ScopedNamedFormulas.ContainsKey((name, sheetId));

    private static bool TryResolveSourceSheet(string? sheetName, Sheet sheet, Workbook? workbook, out Sheet sourceSheet)
    {
        sourceSheet = sheet;
        if (string.IsNullOrWhiteSpace(sheetName))
            return true;

        var found = workbook?.GetSheet(sheetName);
        if (found is null)
            return false;

        sourceSheet = found;
        return true;
    }

    private static ResolvedRange Normalize(Sheet sheet, uint firstRow, uint firstCol, uint lastRow, uint lastCol) =>
        new(
            sheet,
            Math.Min(firstRow, lastRow),
            Math.Min(firstCol, lastCol),
            Math.Max(firstRow, lastRow),
            Math.Max(firstCol, lastCol));

    private static bool TryParseA1Cell(ReadOnlySpan<char> text, SheetId sheetId, out CellAddress address)
    {
        var normalized = text.Trim().ToString().Replace("$", "", StringComparison.Ordinal);
        return CellAddress.TryParse(normalized, sheetId, out address);
    }

    private static string ToDisplayText(ScalarValue value) =>
        value switch
        {
            TextValue t => t.Value,
            NumberValue n => n.Value.ToString(CultureInfo.CurrentCulture),
            BoolValue b => b.Value ? "TRUE" : "FALSE",
            BlankValue => string.Empty,
            ErrorValue => string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
}
