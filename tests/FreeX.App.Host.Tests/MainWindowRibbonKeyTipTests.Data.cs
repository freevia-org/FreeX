using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void LegacyAltDataFilterKeyTip_DFF_TogglesAutoFilterOnSelection()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(workbook =>
            {
                var sheet = workbook.Sheets[0];
                var sheetId = sheet.Id;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Country"));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("US"));
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("UK"));
            });
            harness.SelectRange(1, 1, 3, 1);
            harness.ActiveSheetHasAutoFilter.Should().BeFalse();

            // Legacy Excel access-key sequence Alt+D, F, F should toggle AutoFilter on (Issue 119).
            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.D);
            harness.SelectedRibbonTabHeader.Should().Be("Data");
            harness.HandleKeyTip(Key.F);
            // After the first F the keytip mode must stay active to accept the second F.
            harness.KeyTipScope.Should().NotBe("None");
            harness.SelectedRibbonTabHeader.Should().Be("Data");
            harness.ActiveSheetHasAutoFilter.Should().BeFalse("the first F is only the legacy Data > Filter prefix");
            harness.HandleKeyTip(Key.F);

            harness.KeyTipScope.Should().Be("None");
            harness.ActiveSheetHasAutoFilter.Should().BeTrue();
        });
    }

    [Fact]
    public void LegacyAltEditPasteSpecialKeyTip_ES_RoutesToPasteSpecialAndClosesKeyTips()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            System.Windows.Clipboard.Clear();
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.E);
            harness.KeyTipScope.Should().Be("Commands");
            harness.HandleKeyTip(Key.S);

            harness.KeyTipScope.Should().Be("None");

            System.Windows.Clipboard.Clear();
            harness.HandleDirectTopLevelKeyTip(Key.E).Should().BeTrue();
            harness.KeyTipScope.Should().Be("Commands");
            harness.HandleKeyTip(Key.S);

            harness.KeyTipScope.Should().Be("None");
        });
    }

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
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Group").Should().Be("G");
            harness.HandleKeyTip(Key.G);

            harness.KeyTipScope.Should().Be("None");
            harness.RowOutlineLevel(2).Should().Be(1);
            harness.RowOutlineLevel(3).Should().Be(1);
            harness.RowOutlineLevel(4).Should().Be(1);

            harness.HandleDirectTopLevelKeyTip(Key.A).Should().BeTrue();
            harness.HandleKeyTip(Key.U);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Ungroup").Should().Be("U");
            harness.HandleKeyTip(Key.U);

            harness.KeyTipScope.Should().Be("None");
            harness.RowOutlineLevel(2).Should().Be(0);
            harness.RowOutlineLevel(3).Should().Be(0);
            harness.RowOutlineLevel(4).Should().Be(0);
        });
    }
}
