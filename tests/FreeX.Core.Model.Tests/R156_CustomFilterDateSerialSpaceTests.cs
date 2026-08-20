using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r156 remediation. A Custom AutoFilter date threshold is persisted into the worksheet's
/// <c>&lt;customFilters&gt;</c> XML, which real Excel reads. It was written in OADate space while
/// the <see cref="NumberValue"/> branch of the paired matcher -- a few lines below it in the same
/// switch -- compares a genuine Excel serial.
///
/// The two spaces differ by exactly one day for any date before 1 March 1900, because Excel keeps a
/// day that never existed (29 February 1900) and .NET does not. So a filter threshold in that
/// window was saved one day off from what Excel means by the same date, and a date-like column
/// stored as plain numbers was filtered against the wrong boundary.
///
/// These assert AGREEMENT between the write side, the model's canonical conversion, and the
/// numeric comparison -- not a literal serial -- because the defect was three code paths using two
/// different definitions of the same date.
/// </summary>
public sealed class R156_CustomFilterDateSerialSpaceTests
{
    [Theory]
    [InlineData(1900, 1, 14)]  // inside the affected window: OADate and Excel serial differ
    [InlineData(1900, 2, 28)]  // the last day before Excel's phantom 29 February
    [InlineData(1900, 3, 1)]   // the first day the two spaces agree
    [InlineData(2026, 8, 20)]  // an ordinary modern date, unaffected
    public void PersistedThreshold_MatchesTheModelsOwnSerialForTheSameDate(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);

        var built = FilterCriterionAutoFilterModelBuilder.Build(new DateAfterFilterCriterion(date));
        built.Should().NotBeNull("an After criterion has a customFilter equivalent");

        var persisted = double.Parse(
            built!.Value.Filters.Single().Value!,
            CultureInfo.InvariantCulture);

        var canonical = DateTimeValue.FromDateTime(date.ToDateTime(TimeOnly.MinValue)).Value;

        persisted.Should().Be(
            canonical,
            "the threshold written into <customFilters> must be the same serial the model gives "
            + "that date, or the saved filter disagrees with Excel and with a numeric column");
    }

    [Fact]
    public void ADateCellAndANumberCellHoldingItsSerial_FilterIdentically()
    {
        // The read side had the same split: a DateTimeValue cell was converted through OADate while
        // a NumberValue cell carrying the identical serial was compared directly. Inside the
        // affected window that made two cells the user considers equal filter differently.
        var threshold = new DateOnly(1900, 1, 10);
        var cellDate = new DateOnly(1900, 1, 14);

        var built = FilterCriterionAutoFilterModelBuilder.Build(new DateAfterFilterCriterion(threshold));
        var criterion = CustomFilterModelReconstructor.Reconstruct(built!.Value.Filters, built.Value.And);
        criterion.Should().NotBeNull();

        var asDate = DateTimeValue.FromDateTime(cellDate.ToDateTime(TimeOnly.MinValue));
        var asNumber = new NumberValue(asDate.Value);

        criterion!.Matches(asDate).Should().Be(
            criterion.Matches(asNumber),
            "a date cell and a number cell holding that date's serial are the same date to a user, "
            + "so a filter must not include one and exclude the other");
        criterion.Matches(asDate).Should().BeTrue("14 January 1900 is after 10 January 1900");
    }
}
