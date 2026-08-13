using Free.Shared.AppServices;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R128-status-bar-calculate-indicator: the Avalonia shell renders its default "ready" cell-mode
/// text through the literal <c>"Ready"</c> placeholder passed by ~40 <c>MainWindow.cs</c>
/// <c>RefreshShell("Ready")</c> call sites (see <c>StatusBarReadyTextPlanner.NormalizeTransientReadyText</c>'s
/// doc comment) rather than a single production choke point like the WPF host's
/// <c>StatusBarRefreshPlanner</c>. <see cref="FreeXStatusBarRendererPlanner.NormalizeReadyText"/>
/// is the calc-mode-aware overload <c>MainWindow.StatusBar.cs</c>'s <c>BuildStatusBarViewModel</c> now
/// calls so those call sites surface Excel's "Calculate" indicator instead of "Ready" while a
/// Manual-mode edit is pending recalculation, without touching any of them individually.
/// </summary>
public sealed class R128_AvaloniaStatusBarCalculateIndicatorTests
{
    private static readonly IStatusBarTextProvider TextProvider =
        new ResourceKeyStatusBarTextProvider(UiText.Get);

    [Fact]
    public void NormalizeReadyText_LiteralReady_ManualModePending_ResolvesToCalculateText()
    {
        // Failing before the fix: the two-bool overload did not exist (only the plain
        // NormalizeReadyText(string?) did), so there was no way for the Avalonia shell to ever
        // surface "Calculate" -- every RefreshShell("Ready") call rendered literal "Ready" text.
        var text = FreeXStatusBarRendererPlanner.NormalizeReadyText(
            "Ready",
            TextProvider,
            isManualCalculationMode: true,
            hasPendingRecalculation: true);

        Assert.Equal(UiText.Get(StatusBarTextResourceKeys.CalculateText), text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NormalizeReadyText_LiteralReady_NotBothManualAndPending_StaysReady(
        bool isManualCalculationMode,
        bool hasPendingRecalculation)
    {
        // Family completeness for the (isManualCalculationMode, hasPendingRecalculation) pair,
        // mirrored from R79's CellModeResourceKey_NotBothManualAndPending_ReturnsReadyText.
        var text = FreeXStatusBarRendererPlanner.NormalizeReadyText(
            "Ready",
            TextProvider,
            isManualCalculationMode,
            hasPendingRecalculation);

        Assert.Equal(UiText.Get(StatusBarTextResourceKeys.ReadyText), text);
    }

    [Fact]
    public void NormalizeReadyText_GenuineTransientStatus_PassesThroughEvenWhilePending()
    {
        // No-regression sibling: a real transient message (what most RefreshShell(...) call sites
        // actually pass, e.g. "Selected all visible sheets") must not be clobbered by the calc-mode
        // substitution -- only the literal "Ready" placeholder is special-cased.
        var text = FreeXStatusBarRendererPlanner.NormalizeReadyText(
            "Selected all visible sheets",
            TextProvider,
            isManualCalculationMode: true,
            hasPendingRecalculation: true);

        Assert.Equal("Selected all visible sheets", text);
    }

    [Fact]
    public void NormalizeReadyText_OneArgOverload_StillIgnoresCalcMode()
    {
        // No-regression sibling: the pre-existing single-argument overload (still used by
        // OnStatusBarCustomizeToggled's re-render-from-current-text path) must keep its old
        // behavior -- it never had calc-mode information and must not silently start guessing.
        var text = FreeXStatusBarRendererPlanner.NormalizeReadyText("Ready", TextProvider);

        Assert.Equal(UiText.Get(StatusBarTextResourceKeys.ReadyText), text);
    }
}
