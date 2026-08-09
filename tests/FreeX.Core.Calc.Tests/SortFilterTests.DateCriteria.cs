using FreeX.Core.Model;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class SortFilterTests
{
    [Fact]
    public void FilterCondition_DateEquals_HidesNonMatchingRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15, 12, 30, 0)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateEqualsFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateNotEquals_HidesMatchingDateRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15, 12, 30, 0)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateNotEqualsFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_DateAfter_HidesEarlierRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateAfterFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_DateBefore_HidesLaterRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateBeforeFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateOnOrAfter_HidesEarlierRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateOnOrAfterFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
    }

    [Fact]
    public void FilterCondition_DateOnOrBefore_HidesLaterRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateOnOrBeforeFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateBetween_HidesOutsideRows()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14)));
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)));
        sheet.SetCell(new CellAddress(sid, 4, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 20)));
        sheet.SetCell(new CellAddress(sid, 5, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 21)));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateBetweenFilterCriterion(new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 20)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u);
        sheet.FilterHiddenRows.Should().Contain(5u);
    }

    // R132-commands-autofilter-date-serial-guard-1 [HIGH]: a Custom Date AutoFilter criterion called
    // DateTimeValue.ToDateTime() unguarded on every candidate cell, so a single out-of-range date
    // serial (negative, or beyond DateTime.MaxValue -- reachable from a loaded file, date-autofill
    // extrapolation, or Paste Special arithmetic on a date) threw and aborted the WHOLE filter
    // apply instead of just excluding that row. Fixed via DateTimeValue.TryToDateTime: Excel treats
    // an unconvertible value as simply not matching, exactly like a non-date cell. Each test below
    // mixes a normal matching row, a normal non-matching row, and an out-of-range-serial row in the
    // SAME Apply() call, so it proves both the crash fix AND that the ordinary in-range comparison
    // (the sibling behavior) is unaffected.

    [Fact]
    public void FilterCondition_DateEquals_OutOfRangeSerial_DoesNotCrash_AndNormalMatchingStillWorks()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15))); // matches
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16))); // doesn't match
        sheet.SetCell(new CellAddress(sid, 4, 1), new DateTimeValue(1e18)); // beyond DateTime.MaxValue
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateEqualsFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u, "an unconvertible serial can never equal the target date, so it must be excluded (not crash the whole filter)");
    }

    [Fact]
    public void FilterCondition_DateNotEquals_OutOfRangeSerial_DoesNotCrash_AndStaysVisible()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15))); // matches -> hidden
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16))); // doesn't match -> visible
        sheet.SetCell(new CellAddress(sid, 4, 1), new DateTimeValue(-1e18)); // negative, unconvertible
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateNotEqualsFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().Contain(2u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
        sheet.FilterHiddenRows.Should().NotContain(4u, "an unconvertible serial is never the matching date, so 'does not equal' must keep it visible, mirroring a non-date value");
    }

    [Fact]
    public void FilterCondition_DateAfter_OutOfRangeSerial_DoesNotCrash_AndNormalComparisonStillWorks()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16))); // after -> visible
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14))); // not after -> hidden
        sheet.SetCell(new CellAddress(sid, 4, 1), new DateTimeValue(1e18));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateAfterFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateOnOrAfter_OutOfRangeSerial_DoesNotCrash_AndNormalComparisonStillWorks()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15))); // on threshold -> visible
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14))); // before -> hidden
        sheet.SetCell(new CellAddress(sid, 4, 1), new DateTimeValue(-1e18));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateOnOrAfterFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateBefore_OutOfRangeSerial_DoesNotCrash_AndNormalComparisonStillWorks()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 14))); // before -> visible
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16))); // not before -> hidden
        sheet.SetCell(new CellAddress(sid, 4, 1), new DateTimeValue(1e18));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateBeforeFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateOnOrBefore_OutOfRangeSerial_DoesNotCrash_AndNormalComparisonStillWorks()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 15))); // on threshold -> visible
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 16))); // after -> hidden
        sheet.SetCell(new CellAddress(sid, 4, 1), new DateTimeValue(-1e18));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateOnOrBeforeFilterCriterion(new DateOnly(2026, 5, 15)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    [Fact]
    public void FilterCondition_DateBetween_OutOfRangeSerial_DoesNotCrash_AndNormalComparisonStillWorks()
    {
        var (wb, sheet, ctx) = MakeContext();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sid, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17))); // inside -> visible
        sheet.SetCell(new CellAddress(sid, 3, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 25))); // outside -> hidden
        sheet.SetCell(new CellAddress(sid, 4, 1), new DateTimeValue(1e18));
        var range = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1));

        var command = new FilterConditionCommand(sid, range, filterColOffset: 0, new DateBetweenFilterCriterion(new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 20)));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().NotContain(2u);
        sheet.FilterHiddenRows.Should().Contain(3u);
        sheet.FilterHiddenRows.Should().Contain(4u);
    }

    // Sibling: the same guard was needed in PersistedCustomFilterCriterion (the "reapply a saved
    // Custom AutoFilter from <customFilters> XML" path -- CustomFilterModelReconstructor.Reconstruct
    // is the only public entry point, since the criterion type itself is internal), which converted
    // a DateTimeValue cell's serial to compare against a persisted numeric threshold the same
    // unguarded way.
    [Fact]
    public void CustomFilterModelReconstructor_GreaterThanDate_OutOfRangeSerial_DoesNotCrash_AndNormalComparisonStillWorks()
    {
        var thresholdSerial = new DateTime(2026, 5, 15).ToOADate();
        var filters = new[] { new WorksheetAutoFilterCustomFilterModel("greaterThan", thresholdSerial.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        var criterion = CustomFilterModelReconstructor.Reconstruct(filters, useAnd: false);
        criterion.Should().NotBeNull();

        criterion!.Matches(DateTimeValue.FromDateTime(new DateTime(2026, 5, 16))).Should().BeTrue("a later in-range date must still match 'greater than' (sibling no-regression)");
        criterion.Matches(DateTimeValue.FromDateTime(new DateTime(2026, 5, 14))).Should().BeFalse();
        criterion.Matches(new DateTimeValue(1e18)).Should().BeFalse("an unconvertible serial must not crash the reapply and must not match a comparison operator");
    }
}
