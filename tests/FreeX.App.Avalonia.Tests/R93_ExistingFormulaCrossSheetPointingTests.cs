using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class R93_ExistingFormulaCrossSheetPointingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ExistingFormulaEdit_ShiftedSheetTabs_PreservesThreeDSheetQualifier()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sourceSheet = window.Session.ActiveSheet;
                sourceSheet.Name = "Summary";
                var middleSheet = window.Session.Workbook.AddSheet("Middle Sheet");
                var endSheet = window.Session.Workbook.AddSheet("Final Sheet");
                var formulaAddress = new CellAddress(sourceSheet.Id, 8, 7);

                sourceSheet.SetCell(formulaAddress, Cell.FromFormula("SUM("));
                window.Session.SelectCell(formulaAddress);
                window.BeginFormulaEditForTest(formulaAddress, "=SUM(");
                window.FormulaPointModeForTest.Should().BeFalse();

                window.RaiseSheetTabModifierClickForTest(middleSheet.Id, KeyModifiers.None);
                window.RaiseSheetTabModifierClickForTest(endSheet.Id, KeyModifiers.Shift);
                window.Session.FormulaEditAddress.Should().Be(formulaAddress);

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.F2 });
                window.FormulaPointModeForTest.Should().BeTrue();
                window.SetFormulaBoxSelectionForTest("=SUM(".Length, 0);

                Invoke<bool>(window, "TryInsertFormulaPointReference", new CellAddress(endSheet.Id, 2, 2))
                    .Should().BeTrue();
                window.FormulaBoxTextForTest.Should().Be("=SUM('Middle Sheet:Final Sheet'!B2");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static T Invoke<T>(MainWindow window, string methodName, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (T)method!.Invoke(window, args)!;
    }
}
