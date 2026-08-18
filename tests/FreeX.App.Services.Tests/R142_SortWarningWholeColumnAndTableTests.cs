using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using CoreSortKey = FreeX.Core.Commands.SortKey;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R142-meta-sort-warning-whole-column-bypass / R142-sweep-sort-warning-custom-sort-dialog-gap /
/// R142-meta-sort-warning-table-false-positive: three sibling gaps in the R141 Sort Warning fix.
/// <list type="bullet">
/// <item>
/// (a) A whole-column/whole-row selection (the single most common way to "sort this column" --
/// click the column header, then Sort A-Z) has <see cref="GridRange.End"/> at
/// <see cref="CellAddress.MaxRow"/>/<see cref="CellAddress.MaxCol"/>, which is never &lt;= any real
/// data region's end -- so <see cref="QuickSortRangePlanner.ResolveAdjacentDataExpansion"/> always
/// returned null for it and the warning never fired, reproducing the exact scramble R141 was
/// written to prevent. Fixed by clamping the selection to the sheet's used range before comparing.
/// </item>
/// <item>
/// (b) The Custom Sort dialog path (<see cref="WorkbookSession.SortSelectedRange(SortDialogCommandPlan)"/>)
/// never called the R141 resolver at all -- only Quick Sort (ribbon A-Z/Z-A) did. Fixed by routing
/// both overloads through <see cref="WorkbookSession.ResolveSortRangeAfterAdjacentDataPrompt"/>.
/// </item>
/// <item>
/// (c) A selection entirely inside a genuine structured Table (ListObject) fired the warning even
/// though real Excel never does -- the table itself already defines the record boundary. Fixed by
/// suppressing the expansion whenever the (clamped) selection sits entirely inside one of the
/// sheet's <see cref="Sheet.StructuredTables"/>.
/// </item>
/// </list>
/// </summary>
public sealed class R142_SortWarningWholeColumnAndTableTests
{
    [Fact]
    public void SortSelectedRange_WholeColumnSelection_PromptsBeforeScramblingRecords()
    {
        // Fails before the fix: selecting the entire column C (as clicking the column header does)
        // and clicking Sort A-Z silently sorted column C alone, in place, against columns A/B --
        // the resolver was never even invoked because the whole-column selection's End.Row
        // (CellAddress.MaxRow) always failed the subset comparison.
        var (session, sheet) = CreateSessionWithSalesTable();
        var wholeColumnC = new GridRange(
            Address(sheet, 1, 3),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 3));
        session.SelectRange(wholeColumnC);
        var promptedRequest = default(SortAdjacentDataPromptRequest?);
        session.SortAdjacentDataPromptResolver = request =>
        {
            promptedRequest = request;
            return UserMessageResult.Yes; // "Expand the selection"
        };

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        promptedRequest.Should().NotBeNull("a whole-column selection over a table must trigger the Sort Warning, exactly like a narrower partial-column selection does");
        promptedRequest!.Value.ExpandedRange.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)));
        // Records must have traveled together -- Name/Team pairs preserved after the expanded sort.
        var rows = Enumerable.Range(2, 5)
            .Select(row => (
                Name: ((TextValue)sheet.GetValue((uint)row, 1)).Value,
                Team: ((TextValue)sheet.GetValue((uint)row, 3)).Value))
            .ToList();
        var expectedByTeam = new Dictionary<string, string>
        {
            ["Beth"] = "West",
            ["Ada"] = "East",
            ["Cy"] = "North",
            ["Deb"] = "South",
            ["Eve"] = "Central",
        };
        foreach (var (name, team) in rows)
            expectedByTeam[name].Should().Be(team, $"{name}'s Team must still match their original record");
    }

    [Fact]
    public void ResolveAdjacentDataExpansion_WholeRowMultiRowSelection_AlsoDetectsExpansionAfterClamping()
    {
        // Planner-level sibling of the whole-column case for the transposed gesture: a whole-ROW
        // *band* (e.g. shift-clicking two row headers -- CanSortSelectedRange requires RowCount > 1,
        // so a literal single whole-row selection can never reach the sort at all) reaches
        // CellAddress.MaxCol the same way a whole-column selection reaches CellAddress.MaxRow, and
        // must be clamped to the used range the same way before comparing.
        var (_, sheet) = CreateSessionWithSalesTable();
        var wholeRows2To3 = new GridRange(
            Address(sheet, 2, 1),
            new CellAddress(sheet.Id, 3, CellAddress.MaxCol));

        QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, wholeRows2To3).Should().Be(
            new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)));
    }

    [Fact]
    public void ResolveAdjacentDataExpansion_WholeColumnSelection_NoRegressionForColumnAlreadyCoveringData()
    {
        // No-regression sibling: a whole-column selection whose actual data span already equals the
        // current region (i.e. selecting the whole of column A alone, no other columns to expand
        // into) must still return null -- clamping to the used range must not manufacture a false
        // expansion where none exists.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Solo"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("A"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("B"));
        var wholeColumnA = new GridRange(
            Address(sheet, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, wholeColumnA).Should().BeNull();
    }

    [Fact]
    public void SortSelectedRange_CustomSortDialogPath_SingleArgOverload_NoRegressionStillSortsCurrentSelectionUnwarned()
    {
        // No-regression sibling: the single-arg SortSelectedRange(SortDialogCommandPlan) overload
        // (used by the 3-arg sortKeys/options/hasHeaders convenience, e.g. macros/automation whose
        // column offsets are always chosen against the CURRENT selection, never a wider dialog-
        // resolved range) must keep its exact pre-R142 behavior: no prompt, sorts SelectedRange
        // exactly as given. Routing the Sort Warning through THIS overload would be unsound -- it
        // has no way to re-derive column offsets against a since-expanded range the way the Custom
        // Sort dialog's column choices do -- so R142 fixes the Custom Sort dialog path via the new
        // two-arg overload below (used exclusively by both hosts) instead.
        var (session, sheet) = CreateSessionWithSalesTable();
        var columnC = new GridRange(Address(sheet, 2, 3), Address(sheet, 6, 3));
        session.SelectRange(columnC);
        var promptInvoked = false;
        session.SortAdjacentDataPromptResolver = _ => { promptInvoked = true; return UserMessageResult.Yes; };
        var sortPlan = SortDialogPlanner.CreateCommandPlan(
            [new CoreSortKey(0, true)],
            new SortOptions(),
            hasHeaders: false);

        var result = session.SortSelectedRange(sortPlan);

        result.Success.Should().BeTrue(result.ErrorMessage);
        promptInvoked.Should().BeFalse("the single-arg overload's behavior is unchanged by R142 -- only the new two-arg overload participates in the Sort Warning");
        sheet.GetValue(2, 1).Should().Be(new TextValue("Beth"), "column A must stay untouched -- exactly column C was sorted in place");
    }

    [Fact]
    public void SortSelectedRange_CustomSortDialogPath_TwoArgOverload_SortsExactlyTheHostResolvedRangeWithoutRePrompting()
    {
        // Host-wiring sibling: WPF's SortCustomButton_Click and Avalonia's ShowSortDialogAsync
        // resolve the warning ONCE (to build the dialog's column choices from the winning range),
        // then must pass that same range to execution without asking the resolver again.
        var (session, sheet) = CreateSessionWithSalesTable();
        var columnC = new GridRange(Address(sheet, 2, 3), Address(sheet, 6, 3));
        session.SelectRange(columnC);
        var promptCount = 0;
        session.SortAdjacentDataPromptResolver = _ => { promptCount++; return UserMessageResult.Yes; };

        var resolvedRange = session.ResolveSortRangeAfterAdjacentDataPrompt(session.SelectedRange);
        promptCount.Should().Be(1);
        resolvedRange.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)));

        // Column offset 2 chosen against the ALREADY-EXPANDED range (as the dialog would compute it).
        var sortPlan = SortDialogPlanner.CreateCommandPlan(
            [new CoreSortKey(2, true)],
            new SortOptions(),
            hasHeaders: true);

        var result = session.SortSelectedRange(sortPlan, resolvedRange);

        result.Success.Should().BeTrue(result.ErrorMessage);
        promptCount.Should().Be(1, "the two-arg overload must not re-invoke the resolver -- the host already resolved it once");
        var rows = Enumerable.Range(2, 5)
            .Select(row => ((TextValue)sheet.GetValue((uint)row, 3)).Value)
            .ToList();
        rows.Should().BeInAscendingOrder();
    }

    [Fact]
    public void ResolveAdjacentDataExpansion_SelectionInsideStructuredTable_NeverPrompts()
    {
        // Fails before the fix: a selection entirely inside a genuine Table (ListObject) still
        // triggered the "Excel found data next to your selection" warning, even though real Excel
        // never shows it for a Table sort -- the table itself defines the record boundary.
        var (session, sheet) = CreateSessionWithSalesTable();
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)),
            HeaderRowCount = 1,
        });
        var columnC = new GridRange(Address(sheet, 2, 3), Address(sheet, 6, 3));

        QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, columnC).Should().BeNull();

        // End-to-end sibling: SortSelectedRange must not even invoke the resolver for this table
        // selection, and must sort exactly the selected column in place (Excel's own Table sort
        // behavior for a single selected column -- no dialog interruption).
        session.SelectRange(columnC);
        var promptInvoked = false;
        session.SortAdjacentDataPromptResolver = _ => { promptInvoked = true; return UserMessageResult.Yes; };

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        promptInvoked.Should().BeFalse("a Table selection must never trigger the Sort Warning");
    }

    [Fact]
    public void ResolveAdjacentDataExpansion_SelectionOutsideAnyTable_NoRegressionStillPromptsForOrdinaryData()
    {
        // No-regression sibling: adding an UNRELATED table elsewhere on the sheet must not
        // suppress the warning for a plain-range selection that isn't inside any table.
        var (_, sheet) = CreateSessionWithSalesTable();
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "OtherTable",
            DisplayName = "OtherTable",
            Range = new GridRange(
                new CellAddress(sheet.Id, 20, 20),
                new CellAddress(sheet.Id, 25, 22)),
            HeaderRowCount = 1,
        });
        var columnC = new GridRange(Address(sheet, 2, 3), Address(sheet, 6, 3));

        QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, columnC).Should().NotBeNull();
    }

    private static (WorkbookSession Session, Sheet Sheet) CreateSessionWithSalesTable()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Score"));
        sheet.SetCell(Address(sheet, 1, 3), new TextValue("Team"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Beth"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(4));
        sheet.SetCell(Address(sheet, 2, 3), new TextValue("West"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(2));
        sheet.SetCell(Address(sheet, 3, 3), new TextValue("East"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Cy"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(3));
        sheet.SetCell(Address(sheet, 4, 3), new TextValue("North"));
        sheet.SetCell(Address(sheet, 5, 1), new TextValue("Deb"));
        sheet.SetCell(Address(sheet, 5, 2), new NumberValue(1));
        sheet.SetCell(Address(sheet, 5, 3), new TextValue("South"));
        sheet.SetCell(Address(sheet, 6, 1), new TextValue("Eve"));
        sheet.SetCell(Address(sheet, 6, 2), new NumberValue(5));
        sheet.SetCell(Address(sheet, 6, 3), new TextValue("Central"));

        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        return (session, sheet);
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);
}
