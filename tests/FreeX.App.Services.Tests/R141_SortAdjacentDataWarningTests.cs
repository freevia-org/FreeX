using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R141-services-sort-adjacent-data-1: real Excel's ribbon Sort Ascending/Descending refuses to
/// silently sort a selection that is a proper subset of a larger contiguous data block -- it shows
/// the "Sort Warning" dialog ("Microsoft Excel found data next to your selection...") offering to
/// expand the selection. Before this fix, <see cref="WorkbookSession.SortSelectedRange(bool)"/>
/// (the shared entry point both the WPF and Avalonia shells' Sort Ascending/Descending commands
/// call, via <see cref="QuickSortRangePlanner"/>) had no such check: selecting only C2:C6 out of an
/// A2:C6 table and sorting just reordered column C in place while columns A/B stayed put,
/// permanently scrambling which value belonged to which record. This adds
/// <see cref="WorkbookSession.SortAdjacentDataPromptResolver"/>, mirroring the existing
/// <see cref="WorkbookSession.DataValidationPromptResolver"/> host-hook seam: resolving
/// <see cref="UserMessageResult.Yes"/> expands the sort to the whole block; anything else (or no
/// resolver wired) sorts exactly the current selection, matching this session's prior behavior.
/// </summary>
public sealed class R141_SortAdjacentDataWarningTests
{
    [Fact]
    public void SortSelectedRange_SubsetOfWiderTable_NoResolverWired_SortsOnlySelectedColumnUnwarned()
    {
        // No-regression sibling: a host that has NOT wired the new resolver keeps this session's
        // prior pass-through behavior exactly -- selecting only column C of an A2:C6 table and
        // sorting it must still just sort column C in place, unprompted.
        var (session, sheet) = CreateSessionWithSalesTable();
        var columnC = new GridRange(Address(sheet, 2, 3), Address(sheet, 6, 3));
        session.SelectRange(columnC);

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Column A (Name) must be untouched -- still in original row order.
        sheet.GetValue(2, 1).Should().Be(new TextValue("Beth"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Ada"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Cy"));
        sheet.GetValue(5, 1).Should().Be(new TextValue("Deb"));
        sheet.GetValue(6, 1).Should().Be(new TextValue("Eve"));
    }

    [Fact]
    public void SortSelectedRange_SubsetOfWiderTable_ResolverDeclines_SortsOnlySelectedColumn()
    {
        var (session, sheet) = CreateSessionWithSalesTable();
        var columnC = new GridRange(Address(sheet, 2, 3), Address(sheet, 6, 3));
        session.SelectRange(columnC);
        SortAdjacentDataPromptRequest? capturedRequest = null;
        session.SortAdjacentDataPromptResolver = request =>
        {
            capturedRequest = request;
            return UserMessageResult.No; // "Continue with the current selection"
        };

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        capturedRequest.Should().NotBeNull("declining the expansion still requires the host to have been asked");
        capturedRequest!.Value.SelectedRange.Should().Be(columnC);
        capturedRequest.Value.ExpandedRange.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3)));
        // Column A (Name) must stay untouched, same as the no-resolver case.
        sheet.GetValue(2, 1).Should().Be(new TextValue("Beth"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Ada"));
    }

    [Fact]
    public void SortSelectedRange_SubsetOfWiderTable_ResolverAccepts_ExpandsAndKeepsRecordsAligned()
    {
        var (session, sheet) = CreateSessionWithSalesTable();
        var columnC = new GridRange(Address(sheet, 2, 3), Address(sheet, 6, 3));
        session.SelectRange(columnC);
        session.SortAdjacentDataPromptResolver = _ => UserMessageResult.Yes; // "Expand the selection"

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Ascending sort on column C (Team: West/East/North/South/Central) expanded to include
        // columns A/B -- each Name/Score must have traveled WITH its own Team, not been scrambled.
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
            expectedByTeam[name].Should().Be(team, $"{name}'s Team must still match their original record after the expanded sort");

        // and the columns are now actually sorted ascending by Team text.
        rows.Select(r => r.Team).Should().BeInAscendingOrder();
    }

    [Fact]
    public void SortSelectedRange_SelectionAlreadyCoversWholeCurrentRegion_NeverPrompts()
    {
        // No-regression sibling: selecting the FULL A1:C6 block (header included) is not a proper
        // subset of anything -- the resolver must never even be invoked.
        var (session, sheet) = CreateSessionWithSalesTable();
        var wholeTable = new GridRange(Address(sheet, 1, 1), Address(sheet, 6, 3));
        session.SelectRange(wholeTable);
        var promptInvoked = false;
        session.SortAdjacentDataPromptResolver = _ => { promptInvoked = true; return UserMessageResult.Yes; };

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        promptInvoked.Should().BeFalse();
    }

    [Fact]
    public void SortSelectedRange_SingleCellSelection_PreExistingRejectionIsUnaffectedByTheNewPrompt()
    {
        // No-regression sibling: WorkbookSession.CanSortSelectedRange (RowCount > 1) already
        // rejects a literal single-cell selection before SortSelectedRange builds a sort plan at
        // all ("Select at least two rows to sort.") -- that pre-existing policy is untouched by
        // this fix, and the new prompt must not fire on a path that never reaches it.
        var (session, sheet) = CreateSessionWithSalesTable();
        session.SelectCell(Address(sheet, 3, 2)); // a single cell inside the table body
        var promptInvoked = false;
        session.SortAdjacentDataPromptResolver = _ => { promptInvoked = true; return UserMessageResult.No; };

        var result = session.SortSelectedRange(ascending: true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Select at least two rows to sort.");
        promptInvoked.Should().BeFalse("this selection is rejected before the adjacent-data prompt is ever consulted");
    }

    [Fact]
    public void ResolveAdjacentDataExpansion_SingleCellSelection_ReturnsNull()
    {
        // Planner-level sibling: QuickSortRangePlanner.ResolveAdjacentDataExpansion (the new
        // detection this fix adds) must not fire for a single-cell selection -- that case is
        // handled entirely by the pre-existing ResolveCandidateRange current-region auto-expand,
        // which this fix must leave intact.
        var (_, sheet) = CreateSessionWithSalesTable();
        var singleCell = new GridRange(Address(sheet, 3, 2), Address(sheet, 3, 2));

        QuickSortRangePlanner.ResolveAdjacentDataExpansion(sheet, singleCell).Should().BeNull();
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
