using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R72-commands-sort-filter-4-2: the Avalonia AutoFilter header button's chevron already switches
/// between a plain-arrow (inactive) and funnel (active) glyph based on per-column filter state -- but
/// only ever consulted <c>sheet.AutoFilter?.FilterColumns</c> for that state. When the effective
/// AutoFilter range comes from a structured (Excel) TABLE instead of a worksheet-level
/// <c>&lt;autoFilter&gt;</c> (<see cref="AutoFilterRangeResolver.TryGetEffectiveAutoFilterRange"/> falls
/// back to the first filtered table when <c>sheet.AutoFilter</c> is null), a filtered column's state
/// lives in <c>table.FilterColumns</c> (<c>FilterCommand.ApplyToStructuredTableIfMatched</c>) instead,
/// which the header button never checked -- so a filtered TABLE column's dropdown arrow never showed
/// the filtered state at all. The fix also falls back to the matching table's own
/// <c>FilterColumns</c> when <c>sheet.AutoFilter</c> doesn't cover the column.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R72_AutoFilterFilteredIconTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // Mirrors MainWindow.AutoFilter.cs's private glyph brushes -- verified via the rendered Fill color
    // rather than reflection, since these constants are stable, documented render contracts.
    private static readonly Color InactiveGlyphColor = Color.FromRgb(45, 55, 65);
    private static readonly Color ActiveGlyphColor = Color.FromRgb(15, 109, 140);

    [Fact]
    public async Task StructuredTableFilteredColumn_ShowsFilteredIcon_UnfilteredColumnShowsPlainArrow()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixture(out var sheet, out var range);

            // A structured Excel Table carries its own AutoFilter (HasAutoFilter) and its filtered-
            // column state in table.FilterColumns -- distinct from sheet.AutoFilter, which stays null
            // here (no worksheet-level Data > Filter toggle).
            var table = new StructuredTableModel { Id = 1, Name = "Table1", Range = range, HasAutoFilter = true };
            sheet.StructuredTables.Add(table);
            sheet.AutoFilter.Should().BeNull("this fixture exercises the TABLE-only AutoFilter path, not a worksheet-level <autoFilter>");

            // Filter column B (offset 1) via the real Core command, exactly like the ribbon/dropdown
            // would -- FilterCommand.ApplyToStructuredTableIfMatched writes into table.FilterColumns
            // because `range` here equals table.Range exactly.
            var result = window.Session.ExecuteReviewCommand(new FilterCommand(sheet.Id, range, filterColOffset: 1, ["x"]));
            result.Success.Should().BeTrue(result.ErrorMessage);
            sheet.StructuredTables[0].FilterColumns.Should().Contain(fc => fc.ColumnId == 1,
                "the command must have recorded the filter on the table's own FilterColumns model");

            var grid = window.RebuildSheetGridForTest();
            var columnBGlyph = GetHeaderChevronFill(grid, row: 1, col: 2);
            var columnCGlyph = GetHeaderChevronFill(grid, row: 1, col: 3);

            columnBGlyph.Should().Be(ActiveGlyphColor,
                "column B is filtered via the structured table's own FilterColumns, so its dropdown must show the filtered-state icon");
            columnCGlyph.Should().Be(InactiveGlyphColor,
                "column C has no filter applied on either the table or the worksheet AutoFilter, so it must keep the plain arrow");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StructuredTableFilteredColumn_ClearingFilter_RevertsToPlainArrow()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixture(out var sheet, out var range);
            var table = new StructuredTableModel { Id = 1, Name = "Table1", Range = range, HasAutoFilter = true };
            sheet.StructuredTables.Add(table);

            window.Session.ExecuteReviewCommand(new FilterCommand(sheet.Id, range, filterColOffset: 1, ["x"]))
                .Success.Should().BeTrue();
            GetHeaderChevronFill(window.RebuildSheetGridForTest(), row: 1, col: 2).Should().Be(ActiveGlyphColor);

            // Clear Filter re-runs FilterCommand with an empty allowed-value set (RunAutoFilterResult's
            // ClearFilter branch / RunAutoFilter(range, columnOffset, [])).
            window.Session.ExecuteReviewCommand(new FilterCommand(sheet.Id, range, filterColOffset: 1, []))
                .Success.Should().BeTrue();

            GetHeaderChevronFill(window.RebuildSheetGridForTest(), row: 1, col: 2).Should().Be(InactiveGlyphColor,
                "clearing the column's filter must revert its dropdown back to the plain arrow");
        }, CancellationToken.None);
    }

    // ── No-regression sibling: the pre-existing worksheet-level AutoFilter path is unaffected ───────

    [Fact]
    public async Task WorksheetAutoFilterFilteredColumn_StillShowsFilteredIcon()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixture(out var sheet, out var range);

            window.Session.ExecuteReviewCommand(new ToggleWorksheetAutoFilterCommand(sheet.Id, range))
                .Success.Should().BeTrue();
            sheet.AutoFilter.Should().NotBeNull();

            window.Session.ExecuteReviewCommand(new FilterCommand(sheet.Id, range, filterColOffset: 1, ["x"]))
                .Success.Should().BeTrue();
            sheet.AutoFilter!.FilterColumns.Should().Contain(fc => fc.ColumnId == 1);

            var grid = window.RebuildSheetGridForTest();
            GetHeaderChevronFill(grid, row: 1, col: 2).Should().Be(ActiveGlyphColor,
                "a plain worksheet-level AutoFilter's filtered column must keep showing the filtered-state icon (unchanged by the table fallback)");
            GetHeaderChevronFill(grid, row: 1, col: 3).Should().Be(InactiveGlyphColor);
        }, CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static MainWindow CreateFixture(out Sheet sheet, out GridRange range)
    {
        var window = new MainWindow([]);
        var createdSheet = window.Session.Workbook.AddSheet("AutoFilterIconFixture");
        window.Session.SelectSheet(createdSheet.Id);

        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 1), new TextValue("ColA"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 2), new TextValue("ColB"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 3), new TextValue("ColC"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 1), new TextValue("a1"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 2), new TextValue("x"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 3), new TextValue("c1"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 3, 1), new TextValue("a2"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 3, 2), new TextValue("y"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 3, 3), new TextValue("c2"));

        window.Session.UpdateViewportSize(881, 1440);

        sheet = createdSheet;
        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
        return window;
    }

    private static Color GetHeaderChevronFill(Control grid, uint row, uint col)
    {
        var button = FindDescendantsAndSelf(grid).OfType<Button>()
            .Single(control => AutomationProperties.GetAutomationId(control) == $"AutoFilterButton_{row}_{col}");
        var chevron = FindDescendantsAndSelf(button).OfType<AvaloniaPath>().Single();
        var brush = chevron.Fill.Should().BeOfType<ImmutableSolidColorBrush>().Subject;
        return brush.Color;
    }

    private static IEnumerable<Control> FindDescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (var descendant in FindDescendants(root))
            yield return descendant;
    }

    private static IEnumerable<Control> FindDescendants(Control root)
    {
        if (root is Decorator { Child: { } child })
        {
            yield return child;
            foreach (var descendant in FindDescendants(child))
                yield return descendant;
        }
        else if (root is Panel panel)
        {
            foreach (var childControl in panel.Children)
            {
                yield return childControl;
                foreach (var descendant in FindDescendants(childControl))
                    yield return descendant;
            }
        }
        else if (root is ContentControl { Content: Control content })
        {
            yield return content;
            foreach (var descendant in FindDescendants(content))
                yield return descendant;
        }
    }
}
