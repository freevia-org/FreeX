using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

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
            var sheet = window.Session.Workbook.AddSheet("Revenue Data");
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
                    FormulaText = "SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:E5)",
                    Value = new NumberValue(10),
                });

                window.Session.SelectCell(formulaAddress);
                window.BeginFormulaEditForTest(formulaAddress, "=SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:E5)");

                // Existing formulas open in Edit mode; WPF still exposes reference highlights and
                // resize grips in that mode, so this must not depend on Point mode being active.
                window.FormulaPointModeForTest.Should().BeFalse();
                window.FormulaReferenceGripCountForTest.Should().Be(2);

                var resized = window.RaiseFormulaReferenceGripDragForTest(
                    highlightIndex: 1,
                    new CellAddress(sheet.Id, 6, 6)); // Drag D4:E5's bottom-right grip to F6.

                resized.Should().BeTrue();
                window.FormulaBoxTextForTest.Should().Be("=SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:F6)");

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.Enter,
                    PhysicalKey = PhysicalKey.Enter,
                });

                sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM('Revenue Data'!B2:C3,'Revenue Data'!D4:F6)");
                sheet.GetValue(formulaAddress).Should().Be(new NumberValue(15));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExistingQualifiedFormula_SwitchesToReferencedSheet_ResizesAndRoundTrips()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sourceSheet = window.Session.Workbook.AddSheet("Source Sheet");
            var targetSheet = window.Session.Workbook.AddSheet("Sheet2");
            window.Session.SelectSheet(sourceSheet.Id);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            try
            {
                window.Session.SelectSheet(targetSheet.Id);
                foreach (var (address, value) in new[]
                {
                    (new CellAddress(targetSheet.Id, 2, 2), "1"), // B2
                    (new CellAddress(targetSheet.Id, 3, 3), "2"), // C3
                    (new CellAddress(targetSheet.Id, 4, 4), "3"), // D4
                    (new CellAddress(targetSheet.Id, 5, 5), "4"), // E5
                    (new CellAddress(targetSheet.Id, 6, 6), "5"), // F6
                })
                {
                    window.Session.SelectCell(address);
                    window.Session.CommitCellText(value).Success.Should().BeTrue();
                }

                var formulaAddress = new CellAddress(sourceSheet.Id, 8, 7); // G8
                var formula = "=SUM('Sheet2'!B2:C3,'Sheet2'!D4:E5)";
                sourceSheet.SetCell(formulaAddress, new Cell
                {
                    FormulaText = formula[1..],
                    Value = new NumberValue(10),
                });

                window.Session.SelectCell(formulaAddress);
                window.BeginFormulaEditForTest(formulaAddress, formula);
                window.FormulaPointModeForTest.Should().BeFalse();

                window.SelectFormulaReferenceSheetForTest(targetSheet.Id).Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(targetSheet.Id);
                window.Session.FormulaEditAddress.Should().Be(formulaAddress);
                window.FormulaReferenceGripCountForTest.Should().Be(2);

                window.RaiseFormulaReferenceGripDragForTest(
                    highlightIndex: 1,
                    new CellAddress(targetSheet.Id, 6, 6)).Should().BeTrue();
                window.FormulaBoxTextForTest.Should().Be(
                    "=SUM('Sheet2'!B2:C3,'Sheet2'!D4:F6)");

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.Enter,
                    PhysicalKey = PhysicalKey.Enter,
                });

                sourceSheet.GetCell(formulaAddress)!.FormulaText.Should()
                    .Be("SUM('Sheet2'!B2:C3,'Sheet2'!D4:F6)");
                sourceSheet.GetValue(formulaAddress).Should().Be(new NumberValue(15));

                using var stream = new MemoryStream();
                new NativeJsonAdapter().Save(window.Session.Workbook, stream);
                stream.Position = 0;
                var reopened = new NativeJsonAdapter().Load(stream);
                var reopenedSource = reopened.Sheets.Single(sheet => sheet.Name == "Source Sheet");
                reopenedSource.GetCell(new CellAddress(reopenedSource.Id, 8, 7))!.FormulaText
                    .Should().Be("SUM('Sheet2'!B2:C3,'Sheet2'!D4:F6)");
                reopenedSource.GetValue(new CellAddress(reopenedSource.Id, 8, 7))
                    .Should().Be(new NumberValue(15));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
