using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void FocusedRibbonTabAndEscape_StayInRibbonThenReturnToWorksheet()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusSelectedRibbonTab().Should().BeTrue();
            harness.FocusedElementIsInsideRibbon.Should().BeTrue();

            harness.HandleFocusedRibbonKey(Key.Tab).Should().BeTrue();

            harness.FocusedElementIsInsideRibbon.Should().BeTrue("focused-ribbon Tab should request WPF ribbon traversal instead of worksheet movement");

            harness.HandleFocusedRibbonKey(Key.Escape).Should().BeTrue();

            harness.FocusedElementIsWorksheet.Should().BeTrue("Escape should leave focused ribbon navigation and return to the worksheet");
        });
    }
}
