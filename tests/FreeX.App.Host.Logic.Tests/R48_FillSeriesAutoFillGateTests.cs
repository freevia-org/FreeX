using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R48-commands-fill-series-3-1
/// (src/FreeX.App.Host/MainWindow.HomeEditing.cs, FillSeriesMenuItem_Click's entry gate).
///
/// Before the fix: the Fill ▸ Series dialog only opened when the selection's leading cell was
/// a <see cref="NumberValue"/> or <see cref="DateTimeValue"/>. But the dialog's AutoFill series
/// type (FillSeriesPlanner.BuildAutoFillSeriesEdits) only ever produces edits for a
/// <see cref="TextValue"/> seed (via AutofillCommand.TryCreateAutoFillTextSeries, e.g.
/// "Item 1" -&gt; "Item 2"). So a text seed -- the only seed AutoFill actually supports -- was
/// rejected before the dialog even opened, making the AutoFill radio button completely
/// unreachable on the WPF host.
///
/// After the fix, the entry gate (extracted as the testable <c>CanStartFillSeries</c> helper)
/// also admits a <see cref="TextValue"/> seed, so AutoFill becomes reachable, while Blank/Bool/
/// Error seeds -- which no series type can ever act on -- remain rejected exactly as before.
/// </summary>
public sealed class R48_FillSeriesAutoFillGateTests
{
    private static bool CanStartFillSeries(ScalarValue? startValue) =>
        MainWindow.CanStartFillSeries(startValue);

    [Fact]
    public void CanStartFillSeries_TextSeed_IsAllowed_SoAutoFillBecomesReachable()
    {
        // The concrete failure scenario: a text seed like "Item 1" is exactly what
        // BuildAutoFillSeriesEdits' TryCreateAutoFillTextSeries path supports, but pre-fix the
        // entry gate rejected it outright, so the Fill Series dialog (and its AutoFill radio
        // button) never even opened for this cell.
        CanStartFillSeries(new TextValue("Item 1")).Should().BeTrue();
        CanStartFillSeries(new TextValue("North")).Should().BeTrue();
    }

    [Fact]
    public void CanStartFillSeries_NumberAndDateSeeds_StillAllowed_BlankBoolErrorStillRejected()
    {
        // No-regression: Linear/Growth/Date series only ever worked from Number/Date seeds and
        // must remain reachable exactly as before, while seeds no series type can ever act on
        // (blank, boolean, error) must remain rejected exactly as before.
        CanStartFillSeries(new NumberValue(42)).Should().BeTrue();
        CanStartFillSeries(DateTimeValue.FromDateTime(new DateTime(2026, 1, 1))).Should().BeTrue();

        CanStartFillSeries(BlankValue.Instance).Should().BeFalse();
        CanStartFillSeries(new BoolValue(true)).Should().BeFalse();
        CanStartFillSeries(ErrorValue.Value).Should().BeFalse();
    }
}
