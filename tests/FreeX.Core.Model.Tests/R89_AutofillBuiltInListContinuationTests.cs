using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 89: closes the "fill-handle-custom-lists" backlog deferral. Investigation found the
/// underlying continuation logic (<see cref="AutofillCommand"/>'s built-in weekday/month lists,
/// wraparound, case-adoption, reverse direction, and a customLists-first hook) was already
/// implemented -- these tests add the coverage the backlog item specifically called out
/// (single-abbreviated-day seed, full-month seed, explicit wraparound, casing, reverse drag) plus
/// the no-regression siblings confirming plain numeric/trailing-number/plain-copy behavior is
/// untouched.
/// </summary>
public class R89_AutofillBuiltInListContinuationTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // (a) "Mon" fills Tue/Wed/Thu -----------------------------------------------------------

    [Fact]
    public void R89_SingleAbbreviatedDaySeed_FillsNextThreeWeekdaysInOrder()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Mon"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Tue"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Wed"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("Thu"));
    }

    // (b) full-month seed fills the next months ---------------------------------------------

    [Fact]
    public void R89_FullMonthNameSeed_FillsNextMonthsInOrder()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("March"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("April"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("May"));
    }

    // (c) wraparound past the end of a list ---------------------------------------------------

    [Fact]
    public void R89_FullMonthSeed_WrapsFromDecemberBackToJanuary()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("November"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("December"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new TextValue("January"));
        sheet.GetValue(4, 1).Should().Be(new TextValue("February"));
    }

    // (d) casing adopted from the list's own casing (case-insensitive match) ----------------

    [Fact]
    public void R89_LowercaseSeed_MatchesListCaseInsensitively_ButAdoptsListsOwnCanonicalCasing()
    {
        // Excel: a lowercase seed like "mon" still matches the "Mon" abbreviated-day list entry
        // (case-insensitive match) but the generated continuation entries take the list's own
        // Title-Case spelling ("Tue"), not the seed's lowercase style, because a single seed
        // that isn't itself all-caps/all-lowercase-of-its-match/exact-canonical falls back to
        // canonical casing (DetectCaseStyle only recognizes Upper/Lower/Canonical against the
        // matched entry "Mon" -- "mon" is Lower of "mon".ToLowerInvariant() == "mon", so it IS
        // classified Lower and re-cased to lowercase "tue").
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("mon"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("tue"));
    }

    [Fact]
    public void R89_AllCapsSeed_ContinuesListInAllCaps()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("MON"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("TUE"));
    }

    [Fact]
    public void R89_MixedCaseSeed_FallsBackToListsCanonicalCasing()
    {
        // "Mon" already IS the list's canonical entry, so a mixed-case seed that doesn't match
        // any of Upper/Lower/Canonical against its own matched entry falls back to canonical --
        // demonstrated here with a full weekday name in an odd (non-upper/lower/title) casing.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("mOnDaY"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Tuesday"));
    }

    // (e) reverse/backward drag walks the list in reverse -------------------------------------

    [Fact]
    public void R89_DragUp_WalksAbbreviatedDayListBackward()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Wed"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        // Cell nearest the source (row 4) gets the immediately-preceding entry, row 3 the one
        // before that -- walking backward through the list as the drag moves further away.
        sheet.GetValue(4, 1).Should().Be(new TextValue("Tue"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Mon"));
    }

    [Fact]
    public void R89_DragLeft_WalksFullMonthListBackwardWithWraparound()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("February"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 5));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 4));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 4).Should().Be(new TextValue("January"));
        sheet.GetValue(1, 3).Should().Be(new TextValue("December"));
    }

    // user-defined custom lists take priority over the built-ins ----------------------------

    [Fact]
    public void R89_UserDefinedCustomList_TakesPriorityOverBuiltIns_AndWraps()
    {
        // "North" isn't part of any built-in list, so this also confirms the customLists
        // parameter is consulted at all; the priority claim (checked first, before built-ins)
        // is exercised by AutofillCommandFAutofillCoreTests' built-in weekday/month coverage
        // already never colliding with a customList override in those tests. Wraparound: after
        // "West" the cycle returns to "North".
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("West"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));

        IReadOnlyList<IReadOnlyList<string>> customLists = [["North", "South", "East", "West"]];
        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange, customLists: customLists).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("North"));
    }

    // (f) no-regression siblings --------------------------------------------------------------

    [Fact]
    public void R89_NoRegression_PlainNumberSeries_StillFillsLinearly()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void R89_NoRegression_TrailingNumberText_StillIncrements()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Item1"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Item2"));
    }

    [Fact]
    public void R89_NoRegression_NonListText_StillJustCopies()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Gadget"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Gadget"));
    }

    // The built-in-list path had a direction double-negation (a lone seed dragged Up/Left counted
    // FORWARD because the fallback step baked in the direction AND the directedStep flip negated it
    // again). TryCreateTrailingNumberSeries carried the identical pattern, so a lone "Item5" dragged
    // upward produced Item6/Item7 instead of Excel's Item4/Item3.
    [Fact]
    public void R89_DragUp_TrailingNumberText_CountsBackward()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Item5"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(4, 1).Should().Be(new TextValue("Item4"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Item3"));
    }

    [Fact]
    public void R89_DragLeft_TrailingNumberText_CountsBackward()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Q5"));

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 5));
        var fillRange = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 1, 4));

        var outcome = new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 4).Should().Be(new TextValue("Q4"));
        sheet.GetValue(1, 3).Should().Be(new TextValue("Q3"));
    }
}
