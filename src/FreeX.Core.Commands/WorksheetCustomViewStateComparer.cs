using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// r248: content comparison for <see cref="WorksheetCustomViewState"/>.
/// <para>
/// The fifth instance in this program of one trap: a record whose <c>==</c> LOOKS like value
/// equality but carries collection members, which records compare by REFERENCE. Every capture builds
/// fresh lists, so two captures of an unchanged sheet are never equal, and a guard written with
/// <c>==</c> would compile, read correctly, and never fire. The earlier four were r231 (scenario and
/// custom-view records), r236 (parallel snapshots), r242 (DataValidation clones) and r244
/// (header/footer picture bytes).
/// </para>
/// <para>
/// <c>R248_ViewStateComparisonCoverageContractTests</c> asserts that every member of the record is
/// compared here or exempted with a reason, so the comparison cannot fall behind the type -- the
/// same protection r234 gave the Cell comparison, and for the same reason: thirty members is past
/// the point where re-reading is a check.
/// </para>
/// </summary>
internal static class WorksheetCustomViewStateComparer
{
    internal static bool Same(WorksheetCustomViewState left, WorksheetCustomViewState right) =>
        string.Equals(left.SheetName, right.SheetName, StringComparison.Ordinal)
        && left.ViewMode == right.ViewMode
        && left.FrozenRows == right.FrozenRows
        && left.FrozenCols == right.FrozenCols
        && left.SplitRow == right.SplitRow
        && left.SplitColumn == right.SplitColumn
        && left.ShowGridlines == right.ShowGridlines
        && left.ShowHeadings == right.ShowHeadings
        && left.ShowRulers == right.ShowRulers
        && left.ZoomPercent == right.ZoomPercent
        && left.ShowFormulas == right.ShowFormulas
        && left.ActiveRow == right.ActiveRow
        && left.ActiveCol == right.ActiveCol
        && left.ViewTopRow == right.ViewTopRow
        && left.ViewLeftCol == right.ViewLeftCol
        && SameList(left.HiddenRows, right.HiddenRows)
        && SameList(left.HiddenCols, right.HiddenCols)
        && SameList(left.FilterHiddenRows, right.FilterHiddenRows)
        && Equals(left.AutoFilter, right.AutoFilter)
        && SameList(left.PrintAreas, right.PrintAreas)
        && SameList(left.PrintAreaReferenceForms, right.PrintAreaReferenceForms)
        && string.Equals(left.OpaquePrintAreaExpression, right.OpaquePrintAreaExpression, StringComparison.Ordinal)
        && string.Equals(left.OpaquePrintTitlesExpression, right.OpaquePrintTitlesExpression, StringComparison.Ordinal)
        && left.PageOrientation == right.PageOrientation
        && left.PaperSize == right.PaperSize
        && left.PaperSizeCode == right.PaperSizeCode
        && Equals(left.PageMargins, right.PageMargins)
        && Equals(left.HeaderMargin, right.HeaderMargin)
        && Equals(left.FooterMargin, right.FooterMargin)
        && left.PrintGridlines == right.PrintGridlines
        && left.PrintHeadings == right.PrintHeadings
        && Equals(left.FitToPage, right.FitToPage)
        && Equals(left.ScaleToFit, right.ScaleToFit)
        && SamePrintSettings(left.PrintSettings, right.PrintSettings);

    private static bool SamePrintSettings(WorksheetCustomViewPrintSettings? left, WorksheetCustomViewPrintSettings? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return left with
        {
            RowPageBreaks = Array.Empty<uint>(), ColumnPageBreaks = Array.Empty<uint>(),
            RowPageBreaksMetadata = null, ColumnPageBreaksMetadata = null,
            PageSetupMetadata = null, HeaderFooterMetadata = null, PageMarginsMetadata = null, PrintOptionsMetadata = null,
        } == right with
        {
            RowPageBreaks = Array.Empty<uint>(), ColumnPageBreaks = Array.Empty<uint>(),
            RowPageBreaksMetadata = null, ColumnPageBreaksMetadata = null,
            PageSetupMetadata = null, HeaderFooterMetadata = null, PageMarginsMetadata = null, PrintOptionsMetadata = null,
        }
        && SameBag(left.PageSetupMetadata, right.PageSetupMetadata)
        && SameBag(left.HeaderFooterMetadata, right.HeaderFooterMetadata)
        && SameBag(left.PageMarginsMetadata, right.PageMarginsMetadata)
        && SameBag(left.PrintOptionsMetadata, right.PrintOptionsMetadata)
        && SameBreakMetadata(left.RowPageBreaksMetadata, right.RowPageBreaksMetadata)
        && SameBreakMetadata(left.ColumnPageBreaksMetadata, right.ColumnPageBreaksMetadata)
        && SameList(left.RowPageBreaks, right.RowPageBreaks)
        && SameList(left.ColumnPageBreaks, right.ColumnPageBreaks);
    }

    private static bool SameBag(NativeXmlPreserveBag? left, NativeXmlPreserveBag? right) =>
        left is null || right is null ? left is null && right is null : SameAttributes(left.All, right.All);

    private static bool SameBreakMetadata(WorksheetPageBreaksMetadataModel? left, WorksheetPageBreaksMetadataModel? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return SameAttributes(left.NativeAttributes, right.NativeAttributes)
            && left.BreakNativeAttributes.Count == right.BreakNativeAttributes.Count
            && left.BreakNativeAttributes.All(pair => right.BreakNativeAttributes.TryGetValue(pair.Key, out var attrs)
                && SameAttributes(pair.Value, attrs));
    }

    private static bool SameAttributes(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static bool SameList<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.Count == right.Count && left.SequenceEqual(right);
    }
}
