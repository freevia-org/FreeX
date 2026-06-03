using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void FormulasFunctionLibraryDynamicMenu_IsKeyTipRoutable()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.M);
            harness.HandleKeyTip(Key.L);

            harness.SelectedRibbonTabHeader.Should().Be("Formulas");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("IF").Should().Be("I");
        });
    }

    [Fact]
    public void FormulasUseInFormulaDynamicMenu_IsKeyTipRoutable()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(workbook =>
            {
                var sheet = workbook.Sheets[0];
                workbook.DefineNamedRange(
                    "Sales",
                    new GridRange(
                        new CellAddress(sheet.Id, 1, 1),
                        new CellAddress(sheet.Id, 1, 1)));
            });

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.M);
            harness.HandleKeyTip(Key.I);

            harness.SelectedRibbonTabHeader.Should().Be("Formulas");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("Sales").Should().Be("S");
        });
    }

    [Fact]
    public void FormulasAutoSumAndCalculationOptionKeyTips_InvokeMenuItems()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetNumber(1, 2, 10);
            harness.SetNumber(2, 2, 20);
            harness.SelectRange(3, 2, 3, 2);

            harness.OpenRibbonMenu(Key.M, Key.U);
            harness.ActiveMenuItemGestureText("Average").Should().Be("A");
            harness.ActiveMenuItemGestureText("Count Numbers").Should().Be("C");
            harness.ActiveMenuItemGestureText("More Functions...").Should().Be("F");
            harness.HandleKeyTip(Key.A);

            harness.CellFormulaText(3, 2).Should().Be("AVERAGE(B1:B2)");
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.WorkbookCalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

            harness.OpenRibbonMenu(Key.M, Key.O);
            harness.ActiveMenuItemGestureText("Manual").Should().Be("M");
            harness.HandleKeyTip(Key.M);

            harness.WorkbookCalculationMode.Should().Be(WorkbookCalculationMode.Manual);
            harness.KeyTipScope.Should().Be("None");

            harness.OpenRibbonMenu(Key.M, Key.O);
            harness.ActiveMenuItemGestureText("Automatic").Should().Be("A");
            harness.HandleKeyTip(Key.A);

            harness.WorkbookCalculationMode.Should().Be(WorkbookCalculationMode.Automatic);
            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
        });
    }
}
