using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal sealed record CellEditCompanionSnapshot(
    CellAddress Address,
    Cell? Cell,
    StyleId? StyleOnly,
    bool HadRichTextRuns,
    IReadOnlyList<CellTextRun>? RichTextRuns,
    bool HadHyperlink,
    string? Hyperlink,
    bool HadHyperlinkMetadata,
    HyperlinkMetadata? HyperlinkMetadata,
    bool HadPhoneticGuide,
    CellPhoneticGuide? PhoneticGuide)
{
    internal static CellEditCompanionSnapshot Capture(Sheet sheet, CellAddress address)
    {
        var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(address, out var richTextRuns);
        var hadHyperlink = sheet.Hyperlinks.TryGetValue(address, out var hyperlink);
        var hadHyperlinkMetadata = sheet.HyperlinkMetadata.TryGetValue(address, out var hyperlinkMetadata);
        var hadPhoneticGuide = sheet.CellPhoneticGuides.TryGetValue(address, out var phoneticGuide);

        return new(
            address,
            sheet.GetCell(address)?.Clone(),
            sheet.GetStyleOnly(address.Row, address.Col),
            hadRichTextRuns,
            richTextRuns,
            hadHyperlink,
            hyperlink,
            hadHyperlinkMetadata,
            hyperlinkMetadata,
            hadPhoneticGuide,
            phoneticGuide);
    }

    internal void Restore(Sheet sheet)
    {
        if (Cell is null)
        {
            sheet.ClearCell(Address);
            if (StyleOnly.HasValue)
                sheet.SetStyleOnly(Address.Row, Address.Col, StyleOnly.Value);
            else
                sheet.ClearStyleOnly(Address.Row, Address.Col);
        }
        else
        {
            sheet.SetCell(Address, Cell.Clone());
        }

        RestoreEntry(sheet.RichTextRuns, Address, HadRichTextRuns, RichTextRuns);
        RestoreEntry(sheet.Hyperlinks, Address, HadHyperlink, Hyperlink);
        RestoreEntry(sheet.HyperlinkMetadata, Address, HadHyperlinkMetadata, HyperlinkMetadata);
        RestoreEntry(sheet.CellPhoneticGuides, Address, HadPhoneticGuide, PhoneticGuide);
    }

    private static void RestoreEntry<T>(IDictionary<CellAddress, T> entries, CellAddress address, bool existed, T? value)
    {
        if (existed && value is not null)
            entries[address] = value;
        else
            entries.Remove(address);
    }

    /// <summary>
    /// r234: whether the sheet's state at <see cref="Address"/> still matches what this snapshot
    /// captured -- the question thirteen commands in
    /// <c>R208_WorkbookCommandDeclaresNoOpContractTests.KnownNoOpCapableNotYetFixed</c> need and
    /// could not ask. Those commands write into a target set their guards have already established
    /// is non-empty, so the post-hoc "did we write anything" test that fixed their neighbours is
    /// always true for them; what they need is "did the written values DIFFER".
    /// <para>
    /// This is deliberately NOT an equality override on <see cref="Cell"/>. Cell is a mutable class
    /// used as an identity throughout the model, and giving it value semantics would change meaning
    /// far beyond this question. It also carries <c>CachedAst</c>, a derived parse cache that must
    /// not participate: two cells with the same formula are the same cell whether or not either has
    /// been parsed yet.
    /// </para>
    /// <para>
    /// <c>R234_CellChangeComparisonCoverageContractTests</c> asserts by reflection that every
    /// settable member of Cell is either compared here or exempted with a reason. Without it this
    /// helper would silently fall out of step the first time a field is added to Cell -- and because
    /// thirteen commands would depend on it at once, that drift would be a partial mirror thirteen
    /// times over.
    /// </para>
    /// </summary>
    internal bool MatchesCurrent(Sheet sheet)
    {
        var currentCell = sheet.GetCell(Address);
        if (Cell is null != currentCell is null)
            return false;

        if (Cell is not null && currentCell is not null && !SameCell(Cell, currentCell))
            return false;

        if (Cell is null && !Nullable.Equals(StyleOnly, sheet.GetStyleOnly(Address.Row, Address.Col)))
            return false;

        return SameEntry(sheet.RichTextRuns, HadRichTextRuns, RichTextRuns, SameRuns)
            && SameEntry(sheet.Hyperlinks, HadHyperlink, Hyperlink, static (a, b) => string.Equals(a, b, StringComparison.Ordinal))
            && SameEntry(sheet.HyperlinkMetadata, HadHyperlinkMetadata, HyperlinkMetadata, static (a, b) => Equals(a, b))
            && SameEntry(sheet.CellPhoneticGuides, HadPhoneticGuide, PhoneticGuide, static (a, b) => Equals(a, b));
    }

    /// <summary>
    /// Every settable member of <see cref="Cell"/> except <c>CachedAst</c>, which is a derived parse
    /// cache. Kept as a named method so the r234 coverage contract can point at it.
    /// </summary>
    internal static bool SameCell(Cell left, Cell right) =>
        Equals(left.Value, right.Value)
        && string.Equals(left.FormulaText, right.FormulaText, StringComparison.Ordinal)
        && left.ArrayMode == right.ArrayMode
        && left.LegacyArrayRows == right.LegacyArrayRows
        && left.LegacyArrayCols == right.LegacyArrayCols
        && left.IgnoreFormulaError == right.IgnoreFormulaError
        && left.StyleId == right.StyleId
        && left.QuotePrefix == right.QuotePrefix;

    private static bool SameRuns(IReadOnlyList<CellTextRun>? left, IReadOnlyList<CellTextRun>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!Equals(left[index], right[index]))
                return false;
        }

        return true;
    }

    private bool SameEntry<T>(
        IDictionary<CellAddress, T> entries,
        bool captured,
        T? capturedValue,
        Func<T?, T?, bool> same)
    {
        var present = entries.TryGetValue(Address, out var live);
        if (present != captured)
            return false;

        return !present || same(capturedValue, live);
    }
}
