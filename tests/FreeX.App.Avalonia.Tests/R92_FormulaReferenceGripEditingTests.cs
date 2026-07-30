using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class R92_FormulaReferenceGripEditingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ExistingMultiAreaFormula_ResizeGrip_RewritesOnlyDraggedSameSheetArea()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("FormulaGripFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            try
            {
                sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1)); // B2
                sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2)); // C3
                sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(3)); // D4
                sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(4)); // E5
                sheet.SetCell(new CellAddress(sheet.Id, 6, 6), new NumberValue(5)); // F6

                var formulaAddress = new CellAddress(sheet.Id, 8, 7); // G8
                sheet.SetCell(formulaAddress, new Cell
                {
                    FormulaText = "SUM(B2:C3,D4:E5)",
                    Value = new NumberValue(10),
                });

                window.Session.SelectCell(formulaAddress);
                window.BeginFormulaEditForTest(formulaAddress, "=SUM(B2:C3,D4:E5)");

                // Existing formulas open in Edit mode; WPF still exposes reference highlights and
                // resize grips in that mode, so this must not depend on Point mode being active.
                window.FormulaPointModeForTest.Should().BeFalse();
                window.FormulaReferenceGripCountForTest.Should().Be(2);

                var resized = window.RaiseFormulaReferenceGripDragForTest(
                    highlightIndex: 1,
                    new CellAddress(sheet.Id, 6, 6)); // Drag D4:E5's bottom-right grip to F6.

                resized.Should().BeTrue();
                window.FormulaBoxTextForTest.Should().Be("=SUM(B2:C3,D4:F6)");

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.Enter,
                    PhysicalKey = PhysicalKey.Enter,
                });

                sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM(B2:C3,D4:F6)");
                sheet.GetValue(formulaAddress).Should().Be(new NumberValue(15));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
