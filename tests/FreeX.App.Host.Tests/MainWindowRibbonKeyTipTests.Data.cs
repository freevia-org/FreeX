using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void DataWhatIfKeyTip_OpensAnalysisMenuWithExcelChoices()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.A, Key.W);

            harness.SelectedRibbonTabHeader.Should().Be("Data");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Goal Seek...").Should().Be("G");
            harness.ActiveMenuItemGestureText("Scenario Manager...").Should().Be("S");
            harness.ActiveMenuItemGestureText("Data Table...").Should().Be("D");
        });
    }

    [Fact]
    public void DataOutlineKeyTips_GroupAndUngroupSelectedRows()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(2, 1, 4, 1);

            harness.HandleDirectTopLevelKeyTip(Key.A).Should().BeTrue();
            harness.HandleKeyTip(Key.G);

            harness.SelectedRibbonTabHeader.Should().Be("Data");
            harness.KeyTipScope.Should().Be("None");
            harness.RowOutlineLevel(2).Should().Be(1);
            harness.RowOutlineLevel(3).Should().Be(1);
            harness.RowOutlineLevel(4).Should().Be(1);

            harness.HandleDirectTopLevelKeyTip(Key.A).Should().BeTrue();
            harness.HandleKeyTip(Key.U);

            harness.KeyTipScope.Should().Be("None");
            harness.RowOutlineLevel(2).Should().Be(0);
            harness.RowOutlineLevel(3).Should().Be(0);
            harness.RowOutlineLevel(4).Should().Be(0);
        });
    }
}
