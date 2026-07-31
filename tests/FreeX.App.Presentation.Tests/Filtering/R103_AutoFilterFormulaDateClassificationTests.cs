using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

/// <summary>
/// R103-app-presentation-autofilter-1-1: FreeX's date-producing formula machinery (DATE()/EDATE()/
/// EOMONTH(), date arithmetic like `=PrevDate+7`) round-trips through the formula engine as a plain
/// <see cref="NumberValue"/>, never <see cref="DateTimeValue"/>. Excel itself has no separate "date
/// value" runtime type distinct from a formatted double -- it decides the Date Filters family (and
/// chronological checklist ordering) purely from whether the cell's NUMBER FORMAT is date-like.
/// <see cref="AutoFilterDropdownMenuPlanner.CreateMenuPlan(Workbook?, Sheet, AutoFilterDropdownPlan,
/// IAutoFilterMenuTextProvider, string)"/> and <see cref="AutoFilterChecklistPlanner"/> must do the
/// same, in addition to (not only) checking the ScalarValue's CLR type.
/// </summary>
public sealed class R103_AutoFilterFormulaDateClassificationTests
{
    [Fact]
    public void CreateMenuPlan_FormulaComputedDateColumn_ClassifiesAsDateNotNumber()
    {
        // Simulates a "Due Date" column built from `=OrderDate+30` (or DATE()/EDATE()/EOMONTH()) --
        // every cell holds a NumberValue (an OADate serial), never a DateTimeValue, but the column
        // carries a date number format exactly as FreeX's arithmetic/date-function paths leave it.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Due Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(new DateTime(2026, 5, 1).ToOADate()));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(new DateTime(2026, 6, 1).ToOADate()));

        var dateStyle = CellStyle.Default.Clone();
        dateStyle.NumberFormat = "m/d/yyyy";
        var styleId = workbook.RegisterStyle(dateStyle);
        sheet.GetCell(2, 1)!.StyleId = styleId;
        sheet.GetCell(3, 1)!.StyleId = styleId;

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            workbook, sheet, plan,
            InvariantAutoFilterMenuTextProvider.Instance, InvariantAutoFilterMenuTextProvider.BlankDisplayText);

        menu.FilterKind.Should().Be(AutoFilterMenuFilterKind.Date);
    }

    [Fact]
    public void CreateMenuPlan_PlainNumberColumn_WithoutDateFormat_StillClassifiesAsNumber()
    {
        // No-regression guard: a column of NumberValue cells that is NOT date-formatted (the
        // ordinary case DetectFilterKind already handled) must keep classifying as Number.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Quantity"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            workbook, sheet, plan,
            InvariantAutoFilterMenuTextProvider.Instance, InvariantAutoFilterMenuTextProvider.BlankDisplayText);

        menu.FilterKind.Should().Be(AutoFilterMenuFilterKind.Number);
    }

    [Fact]
    public void CreateMenuPlan_WithoutWorkbook_FormulaComputedDateColumn_FallsBackToNumber()
    {
        // No-regression guard for the legacy no-workbook overload: without a Workbook there is no
        // style to resolve a number format from, so the best this call can do is the prior
        // type-only check -- it must not throw and must keep its previous (documented) behavior.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Due Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(new DateTime(2026, 5, 1).ToOADate()));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(new DateTime(2026, 6, 1).ToOADate()));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            sheet, plan,
            InvariantAutoFilterMenuTextProvider.Instance, InvariantAutoFilterMenuTextProvider.BlankDisplayText);

        menu.FilterKind.Should().Be(AutoFilterMenuFilterKind.Number);
    }

    [Fact]
    public void CreateItems_MixedLiteralAndFormulaComputedDates_SortChronologically()
    {
        // The secondary, directly-visible symptom: a column mixing a literally-typed date
        // (DateTimeValue, raw filter text "yyyy-MM-dd" -> old Rank 1) with formula-computed dates
        // (NumberValue, raw filter text is an invariant OADate-serial number string -> old Rank 0)
        // must not put every computed-date entry ahead of the literal one regardless of actual
        // chronological order.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Due Date"));

        // Row 2: literal date, earliest chronologically.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 1, 1)));
        var dateStyle = CellStyle.Default.Clone();
        dateStyle.NumberFormat = "m/d/yyyy";
        var dateStyleId = workbook.RegisterStyle(dateStyle);
        sheet.GetCell(2, 1)!.StyleId = dateStyleId;

        // Row 3: formula-computed date (`=PrevDate+120ish`), latest chronologically, stored as a
        // plain NumberValue with the same date number format.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(new DateTime(2026, 6, 1).ToOADate()));
        sheet.GetCell(3, 1)!.StyleId = dateStyleId;

        // Row 4: another formula-computed date, in between the two above chronologically -- this
        // is what actually exposes an ordering bug (rather than merely a Rank split that could
        // coincidentally still look "sorted" with only two distinct rank-groups).
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(new DateTime(2026, 3, 1).ToOADate()));
        sheet.GetCell(4, 1)!.StyleId = dateStyleId;

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            FilterColumnOffset: 0);

        var items = AutoFilterChecklistPlanner.CreateItems(workbook, sheet, plan, "(Blanks)");

        items.Select(item => item.DisplayText).Should().ContainInOrder("1/1/2026", "3/1/2026", "6/1/2026");
    }

    [Fact]
    public void CreateItems_MixedNumberAndLiteralDateColumn_KeepsPriorOrdering_WhenNotDateFormatted()
    {
        // No-regression guard: when the raw-number entries are genuinely NOT date-formatted (an
        // actual mixed text/date/number column, not this defect's scenario), the numeric bucket
        // must still sort before the date bucket exactly as before.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Mixed"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(99)); // General format, plain number
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 1, 1)));
        var dateStyle = CellStyle.Default.Clone();
        dateStyle.NumberFormat = "m/d/yyyy";
        sheet.GetCell(3, 1)!.StyleId = workbook.RegisterStyle(dateStyle);

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);

        var items = AutoFilterChecklistPlanner.CreateItems(workbook, sheet, plan, "(Blanks)");

        items.Select(item => item.DisplayText).Should().ContainInOrder("99", "1/1/2026");
    }
}
