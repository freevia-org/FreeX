using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

/// <summary>
/// R45-commands-autofilter-topbottom-3-1: reopening a value-list filter dropdown while ANOTHER
/// column's filter is active must not silently uncheck values the reopened column's OWN filter
/// still allows. AutoFilterDropdownMenuPlanner previously derived a checklist item's IsChecked
/// state from sheet.FilterHiddenRows -- the AND-across-columns recomputed row-visibility set --
/// which conflates "hidden by THIS column's filter" with "hidden purely by a sibling column's
/// unrelated filter". Real Excel keeps a column's own checkbox state scoped to that column's own
/// persisted selection (sheet.ActiveValueFilterColumns) regardless of what any other column hides.
/// </summary>
public sealed class R45_AutoFilterDropdownOwnColumnChecklistTests
{
    [Fact]
    public void CreateMenuPlan_KeepsOwnColumnValueChecked_WhenOnlyHiddenByAnotherColumnsFilter()
    {
        // Range A1:B4 -- header row 1: A2=East/B2=10, A3=West/B3=20, A4=East/B4=10.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Amount"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(10));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("West"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(20));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(10));

        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 2));
        var ctx = new FakeCommandContext(workbook);

        // Apply a value filter on column A (Region) allowing only ["East"] -> row 3 hidden.
        new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["East"]).Apply(ctx);
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        // Apply a value filter on column B (Amount) allowing BOTH "10" and "20".
        new FilterCommand(sheet.Id, range, filterColOffset: 1, allowedValues: ["10", "20"]).Apply(ctx);
        sheet.ActiveValueFilterColumns[2].Should().BeEquivalentTo(["10", "20"]);
        // Row 3 stays hidden -- but only because of column A's Region filter.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        // Reopen the AutoFilter dropdown for column B (Amount, offset 1).
        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 1);
        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, TextProvider, "(Blanks)");

        var checklist = menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .ToDictionary(entry => entry.Value, entry => entry.IsChecked);

        // "20" is still explicitly allowed by column B's own persisted filter -- it must stay
        // checked even though its only row (3) is hidden solely by column A's unrelated filter.
        checklist.Should().BeEquivalentTo(new Dictionary<string, bool?>
        {
            ["10"] = true,
            ["20"] = true
        });
    }

    [Fact]
    public void CreateMenuPlan_UnchecksOwnColumnValue_WhenOwnFilterExcludesIt()
    {
        // Sibling/no-regression case: column B's OWN filter genuinely excludes "20" -- that must
        // still render unchecked (the fix must not make every value checked unconditionally).
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Amount"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(10));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("West"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(20));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(10));

        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 2));
        var ctx = new FakeCommandContext(workbook);

        // Column B (Amount) filtered to allow only "10" -- "20" is genuinely excluded.
        new FilterCommand(sheet.Id, range, filterColOffset: 1, allowedValues: ["10"]).Apply(ctx);

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 1);
        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, TextProvider, "(Blanks)");

        var checklist = menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .ToDictionary(entry => entry.Value, entry => entry.IsChecked);

        checklist.Should().BeEquivalentTo(new Dictionary<string, bool?>
        {
            ["10"] = true,
            ["20"] = false
        });
    }

    [Fact]
    public void CreateMenuPlan_AllValuesChecked_ForUnfilteredColumnWhileAnotherColumnFilters()
    {
        // No-regression: a column with no filter of its own must show every value checked, even
        // while another column's filter is hiding rows in the range.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Amount"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(10));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("West"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(20));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(10));

        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 2));
        var ctx = new FakeCommandContext(workbook);

        // Only column A (Region) is filtered; column B (Amount) has no filter mechanism of its own.
        new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["East"]).Apply(ctx);
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 1);
        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, TextProvider, "(Blanks)");

        var checklist = menu.Entries
            .Where(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem)
            .ToDictionary(entry => entry.Value, entry => entry.IsChecked);

        checklist.Should().BeEquivalentTo(new Dictionary<string, bool?>
        {
            ["10"] = true,
            ["20"] = true
        });
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static readonly TestTextProvider TextProvider = new();

    private sealed class FakeCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private sealed class TestTextProvider : IAutoFilterMenuTextProvider
    {
        public string Get(string resourceKey) => resourceKey;

        public string Format(string resourceKey, string value) => $"{resourceKey}: {value}";
    }
}
