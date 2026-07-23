using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R79-render-namebar-statusbar-5-2: the status bar never showed Excel's "Calculate" cell-mode
/// indicator for a dirty Manual-calculation workbook. These tests cover the new
/// <see cref="StatusBarTextResourceKeys.CalculateText"/> resource key and the
/// <see cref="StatusBarTextResourceKeys.CellModeResourceKey"/> visibility-decision logic.
/// </summary>
public sealed class R79_StatusBarCalculateIndicatorTests
{
    [Fact]
    public void CellModeResourceKey_ManualModeWithPendingRecalculation_ReturnsCalculateText()
    {
        // Failing before the fix: CalculateText/CellModeResourceKey did not exist at all, so
        // there was no way for Manual mode + a pending recalculation to resolve to anything but
        // the "Ready" text -- Excel instead shows "Calculate" here.
        var resourceKey = StatusBarTextResourceKeys.CellModeResourceKey(
            isManualCalculationMode: true,
            hasPendingRecalculation: true);

        resourceKey.Should().Be(StatusBarTextResourceKeys.CalculateText);
    }

    [Theory]
    [InlineData(false, false)] // Automatic mode, nothing pending
    [InlineData(false, true)]  // Automatic mode still recalculates synchronously -- never "Calculate"
    [InlineData(true, false)]  // Manual mode but no pending recalculation (nothing edited yet)
    public void CellModeResourceKey_NotBothManualAndPending_ReturnsReadyText(
        bool isManualCalculationMode,
        bool hasPendingRecalculation)
    {
        var resourceKey = StatusBarTextResourceKeys.CellModeResourceKey(
            isManualCalculationMode,
            hasPendingRecalculation);

        resourceKey.Should().Be(StatusBarTextResourceKeys.ReadyText);
    }

    [Fact]
    public void RequiredKeys_ContainsCalculateText()
    {
        StatusBarTextResourceKeys.RequiredKeys.Should().Contain(StatusBarTextResourceKeys.CalculateText);
    }

    [Fact]
    public void RequiredKeys_HasNoDuplicates()
    {
        // No-regression sibling for the pre-existing duplicate-key contract: adding CalculateText
        // must not introduce a duplicate entry in the required-keys list.
        StatusBarTextResourceKeys.RequiredKeys
            .Should()
            .OnlyHaveUniqueItems();
    }
}
