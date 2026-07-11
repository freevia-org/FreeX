using System.Globalization;

using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

/// <summary>
/// Round-24 regression: R24-localization-parsing-2 — the PivotTable Value Filter dialog's numeric
/// comparison bounds (the 5-arg <see cref="PivotFieldFilterPlanner.TryCreateValueFilter(int,int,PivotValueFilterKind,string?,string?,out PivotValueFilterModel?,out string?)"/>
/// overload used by the Avalonia shell) must not silently misread a '.'-decimal value as a
/// grouped integer under a comma-decimal CurrentCulture. Previously this parsed with
/// <c>NumberStyles.Any</c> (which implies AllowThousands) against CurrentCulture only, so under
/// de-DE (decimal ',', group '.') typing "1000.5" silently produced 10005 - a 10x-too-large
/// threshold with no error shown. The fix mirrors the established two-culture fallback convention
/// used by every other numeric-entry parser in the app (ChartDialogValueParser, DrawingInputParser,
/// PageSetupDialogModel, etc): try CurrentCulture first, then fall back to InvariantCulture.
/// </summary>
public sealed class PivotFieldFilterPlannerLocaleTests
{
    [Fact]
    public void TryCreateValueFilter_DoesNotMisreadDotDecimalAsGroupedInteger_UnderCommaDecimalCurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // de-DE: decimal separator ',', group separator '.'. Under the old
            // NumberStyles.Any + CurrentCulture-only parse, "1000.5" silently misparsed to
            // 10005 (the '.' treated as an ignorable thousands separator).
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var ok = PivotFieldFilterPlanner.TryCreateValueFilter(
                sourceFieldIndex: 0,
                dataFieldIndex: 0,
                kind: PivotValueFilterKind.GreaterThan,
                primaryText: "1000.5",
                secondaryText: null,
                out var filter,
                out var error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            filter.Should().NotBeNull();
            filter!.ComparisonValue.Should().Be(1000.5);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void TryCreateValueFilter_StillAcceptsCommaDecimal_UnderCommaDecimalCurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // A locale-typed value ("1000,5" meaning 1000.5 under de-DE) must keep working -
            // the invariant fallback must not break the CurrentCulture parse path.
            var ok = PivotFieldFilterPlanner.TryCreateValueFilter(
                sourceFieldIndex: 0,
                dataFieldIndex: 0,
                kind: PivotValueFilterKind.GreaterThan,
                primaryText: "1000,5",
                secondaryText: null,
                out var filter,
                out var error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            filter.Should().NotBeNull();
            filter!.ComparisonValue.Should().Be(1000.5);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void TryCreateValueFilter_SecondaryValue_DoesNotMisreadDotDecimal_UnderCommaDecimalCurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var ok = PivotFieldFilterPlanner.TryCreateValueFilter(
                sourceFieldIndex: 0,
                dataFieldIndex: 0,
                kind: PivotValueFilterKind.Between,
                primaryText: "1000.5",
                secondaryText: "2000.5",
                out var filter,
                out var error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            filter.Should().NotBeNull();
            filter!.ComparisonValue.Should().Be(1000.5);
            filter.ComparisonValue2.Should().Be(2000.5);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
