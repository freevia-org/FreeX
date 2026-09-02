using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r193 (backlog item 40): <c>DuplicateSheetDrawingCloner.CloneChart</c> copies every one of the
/// ~23 SeriesIndex-keyed fields <c>ChartCommands.Mutate.cs</c> names as having to move together --
/// except <see cref="ChartModel.SeriesNameOverrides"/>, which its object initializer never assigned.
/// A chart series whose name comes from an explicit formula (Select Data &gt; Series name = a cell
/// reference, which the loader captures into that list) therefore lost the override when the chart
/// was duplicated or pasted, and the writer fell back to the recomputed header-cell title: the copy
/// silently showed a different legend name from the original on the next save.
///
/// All three gestures -- Duplicate Sheet, Ctrl+D on the chart, and copy/paste of a chart -- route
/// through this one cloner.
/// </summary>
public sealed class R193_ChartCloneSeriesNameOverrideTests
{
    private static readonly SheetId SheetIdA = new(Guid.NewGuid());
    private static readonly SheetId SheetIdB = new(Guid.NewGuid());

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;
                i++;
                continue;
            }

            if (char.IsLowSurrogate(text[i]))
                return true;
        }

        return false;
    }

    private static ChartModel ChartWithOverride() => new()
    {
        Type = ChartType.Column,
        SeriesNameOverrides =
        {
            new ChartSeriesNameOverride(0, "Sheet1!$D$1"),
            new ChartSeriesNameOverride(1, "Sheet1!$E$1"),
        },
    };

    [Fact]
    public void CloneChart_CarriesTheSeriesNameOverrides()
    {
        var source = ChartWithOverride();

        var clone = DuplicateSheetDrawingCloner.CloneChart(source, SheetIdA, SheetIdB);

        clone.SeriesNameOverrides.Should().Equal(source.SeriesNameOverrides);
    }

    [Fact]
    public void CloneChart_CopiesTheListRatherThanSharingIt()
    {
        // The sibling fields all use .ToList() for this reason: an aliased list would let an edit to
        // the copy change the original's legend names too.
        var source = ChartWithOverride();

        var clone = DuplicateSheetDrawingCloner.CloneChart(source, SheetIdA, SheetIdB);
        clone.SeriesNameOverrides.Add(new ChartSeriesNameOverride(2, "Sheet1!$F$1"));

        source.SeriesNameOverrides.Should().HaveCount(2, "the source must not grow with the copy");
    }

    [Fact]
    public void CloneChart_WithNoOverrides_ProducesAnEmptyListNotNull()
    {
        var clone = DuplicateSheetDrawingCloner.CloneChart(new ChartModel { Type = ChartType.Column }, SheetIdA, SheetIdB);

        clone.SeriesNameOverrides.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void GenerateCopyName_WhenTheSuffixCutFallsInsideAPair_StillProducesAWellFormedName()
    {
        // r195: r194 guarded the INITIAL sheet-name truncation but not the dedup loops, which
        // re-slice at a different cut point -- 31 minus the " (N)" suffix. So the same lone-surrogate
        // name stayed reachable by renaming a sheet and duplicating it, after which every save to
        // .xlsx throws. Reported by the meta lens auditing r194's own commit.
        var workbook = new Workbook("test");

        // 26 ASCII + one astral emoji (2 code units) + 3 ASCII = 31 code units. The " (2)" suffix
        // forces a cut at 27, which lands inside the pair.
        var name = new string('A', 26) + "\U0001F600" + "bcd";
        name.Length.Should().Be(31);

        var sheet = workbook.AddSheet("Sheet1");
        sheet.Name = name;

        var copy = DuplicateSheetNameGenerator.GenerateCopyName(workbook, name);

        HasLoneSurrogate(copy).Should().BeFalse(
            "a lone surrogate in a sheet name makes every later .xlsx save throw; got '{0}'",
            copy);
        copy.Length.Should().BeLessThanOrEqualTo(31);
    }
}
