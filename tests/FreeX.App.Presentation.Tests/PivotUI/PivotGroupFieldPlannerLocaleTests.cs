using System.Globalization;

using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

/// <summary>
/// Round-24 regression: R24-localization-parsing-3 — the "Group" dialog's numeric Start/End/By
/// bounds must accept a '.'-decimal value even under a comma-decimal CurrentCulture, exactly like
/// every other numeric-entry site in the app (CellEntryParser, DataValidationDialogModel,
/// FormatCellsInputParser, FormatPicturePlanner/PictureCropPlanner, GoalSeekDialog, etc.), instead
/// of being rejected outright because <c>double.TryParse</c> only tried CurrentCulture.
/// </summary>
public sealed class PivotGroupFieldPlannerLocaleTests
{
    [Fact]
    public void TryValidate_AcceptsInvariantDotDecimal_UnderCommaDecimalCurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            // de-DE formats/parses decimals with ',' (and NumberStyles.Float has no
            // AllowThousands), so a plain '.'-decimal like "1.5" must fall back to
            // InvariantCulture rather than being rejected as invalid.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var ok = PivotGroupFieldPlanner.TryValidate(
                PivotFieldGrouping.NumberRange,
                ungroup: false,
                startText: "1.5",
                endText: "10.5",
                intervalText: "1.5",
                out var start,
                out var end,
                out var interval,
                out var error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            start.Should().Be(1.5);
            end.Should().Be(10.5);
            interval.Should().Be(1.5);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void TryValidate_StillAcceptsCommaDecimal_UnderCommaDecimalCurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            // A locale-typed value ("1,5" meaning 1.5 under de-DE) must keep working too — the
            // invariant fallback must not break the CurrentCulture parse path.
            var ok = PivotGroupFieldPlanner.TryValidate(
                PivotFieldGrouping.NumberRange,
                ungroup: false,
                startText: "1,5",
                endText: "10,5",
                intervalText: "1,5",
                out var start,
                out var end,
                out var interval,
                out var error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            start.Should().Be(1.5);
            end.Should().Be(10.5);
            interval.Should().Be(1.5);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
