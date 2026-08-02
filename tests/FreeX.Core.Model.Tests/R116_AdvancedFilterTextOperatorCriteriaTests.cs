using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R116-commands-advancedfilter-criteria-engine-1: Advanced Filter's plain (non-computed) column
/// criteria must be compiled by the same shared criteria engine COUNTIF/SUMIF/*IFS/DSUM use
/// (BuiltInFunctions.CompileCriteria), not the AutoFilter custom-filter dialog's own
/// FilterInputParser/FilterCriterionInputParser mini-language. That parser's operator-prefix
/// branches ("<>", ">=", "<=", ">", "<", "=") only ever accept a numeric right-hand side and
/// silently reject any text operand, so a text-valued operator criterion like "&lt;&gt;East",
/// a bare "&lt;&gt;", or "&gt;Denver" used to fall through to a generic tail that had no operator
/// awareness left and matched the ENTIRE operator-prefixed string (including the operator
/// characters) as a literal "begins with" prefix -- which real cell text essentially never
/// satisfies, so the column silently excluded every row.
/// </summary>
public sealed class R116_AdvancedFilterTextOperatorCriteriaTests
{
    [Fact]
    public void AdvancedFilter_TextNotEqualsCriterion_ExcludesOnlyTheNamedValue()
    {
        var (_, sheet, ctx) = Setup();
        Set(sheet, 1, 1, "Region");
        Set(sheet, 2, 1, "East");
        Set(sheet, 3, 1, "West");
        Set(sheet, 4, 1, "East");
        Set(sheet, 5, 1, "North");

        Set(sheet, 1, 3, "Region");
        Set(sheet, 2, 3, "<>East");

        var command = new AdvancedFilterCommand(
            ListRange: Range(sheet, 1, 1, 5, 1),
            CriteriaRange: Range(sheet, 1, 3, 2, 3),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        // Excel ground truth: "<>France" (here "<>East") under a text header returns every row
        // whose value is NOT "East" -- rows 3 (West) and 5 (North) stay visible; rows 2 and 4
        // (both "East") are hidden.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain([3u, 5u]);

        command.Revert(ctx);
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void AdvancedFilter_BareNotEqualsCriterion_MeansFieldIsNotBlank()
    {
        var (_, sheet, ctx) = Setup();
        Set(sheet, 1, 1, "Rep");
        Set(sheet, 2, 1, "Ana");
        // Row 3 intentionally left blank (no cell at all).
        Set(sheet, 4, 1, "Ben");

        Set(sheet, 1, 3, "Rep");
        Set(sheet, 2, 3, "<>");

        var command = new AdvancedFilterCommand(
            ListRange: Range(sheet, 1, 1, 4, 1),
            CriteriaRange: Range(sheet, 1, 3, 2, 3),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        // Excel ground truth: a bare "<>" criterion cell means "field is not blank" -- rows with a
        // value stay visible, the blank row is hidden.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
        sheet.FilterHiddenRows.Should().NotContain([2u, 4u]);
    }

    [Fact]
    public void AdvancedFilter_TextOrderingCriterion_ComparesLexicographically()
    {
        var (_, sheet, ctx) = Setup();
        Set(sheet, 1, 1, "City");
        Set(sheet, 2, 1, "Denver");
        Set(sheet, 3, 1, "Reno");
        Set(sheet, 4, 1, "Austin");

        Set(sheet, 1, 3, "City");
        Set(sheet, 2, 3, ">Denver");

        var command = new AdvancedFilterCommand(
            ListRange: Range(sheet, 1, 1, 4, 1),
            CriteriaRange: Range(sheet, 1, 3, 2, 3),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        // Excel ground truth: ">Denver" compares text lexicographically (case-insensitive) -- only
        // "Reno" sorts after "Denver"; "Denver" itself and "Austin" (which sorts before) are hidden.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain([3u]);
    }

    // --- No-regression siblings: numeric operator criteria and the plain-text begins-with
    // fallback (both already exercised by AdvancedFilterCommandTests/FreeXR12Q8Tests) must keep
    // working unchanged now that CreateCriterion routes through the shared engine.

    [Fact]
    public void AdvancedFilter_NumericNotEqualsCriterion_StillExcludesOnlyTheMatchingNumber()
    {
        var (_, sheet, ctx) = Setup();
        Set(sheet, 1, 1, "Sales");
        Set(sheet, 2, 1, 100.0);
        Set(sheet, 3, 1, 150.0);
        Set(sheet, 4, 1, 100.0);

        Set(sheet, 1, 3, "Sales");
        Set(sheet, 2, 3, "<>100");

        var command = new AdvancedFilterCommand(
            ListRange: Range(sheet, 1, 1, 4, 1),
            CriteriaRange: Range(sheet, 1, 3, 2, 3),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain([3u]);
    }

    [Fact]
    public void AdvancedFilter_PlainTextCriterion_StillMeansBeginsWith()
    {
        var (_, sheet, ctx) = Setup();
        Set(sheet, 1, 1, "Name");
        Set(sheet, 2, 1, "Smith");
        Set(sheet, 3, 1, "Smart");
        Set(sheet, 4, 1, "Jones");

        Set(sheet, 1, 3, "Name");
        Set(sheet, 2, 3, "Sm");

        var command = new AdvancedFilterCommand(
            ListRange: Range(sheet, 1, 1, 4, 1),
            CriteriaRange: Range(sheet, 1, 3, 2, 3),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);
        sheet.FilterHiddenRows.Should().NotContain([2u, 3u]);
    }

    private static (Workbook Wb, Sheet Sheet, ICommandContext Ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static void Set(Sheet sheet, uint row, uint col, string text) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new TextValue(text)));

    private static void Set(Sheet sheet, uint row, uint col, double number) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(number)));

    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));
}
