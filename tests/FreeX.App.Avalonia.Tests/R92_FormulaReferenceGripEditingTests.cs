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
                window.AllowCloseWithoutDirtyPromptForParityCapture();

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
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ThreeDSheetRange_OnMiddleSheet_ShowsGripResizesCalculatesAndRoundTrips()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sourceSheet = window.Session.Workbook.Sheets[0];
            sourceSheet.Name = "Source Sheet";
            var middleSheet = window.Session.Workbook.AddSheet("Middle Sheet");
            var endSheet = window.Session.Workbook.AddSheet("Final Sheet");
            window.Session.SelectSheet(sourceSheet.Id);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            try
            {
                for (uint row = 1; row <= 3; row++)
                {
                    for (uint col = 1; col <= 3; col++)
                    {
                        middleSheet.SetCell(new CellAddress(middleSheet.Id, row, col), new NumberValue(row * 3 + col));
                        endSheet.SetCell(new CellAddress(endSheet.Id, row, col), new NumberValue(9 + row * 3 + col));
                    }
                }

                var formulaAddress = new CellAddress(sourceSheet.Id, 8, 7);
                var formula = "=SUM('Middle Sheet:Final Sheet'!A1:B2)";
                sourceSheet.SetCell(formulaAddress, Cell.FromFormula(formula[1..]));
                window.Session.SelectCell(formulaAddress);
                window.BeginFormulaEditForTest(formulaAddress, formula);

                window.SelectFormulaReferenceSheetForTest(middleSheet.Id).Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(middleSheet.Id);
                window.FormulaReferenceGripCountForTest.Should().Be(1);
                window.RaiseFormulaReferenceGripDragForTest(
                    highlightIndex: 0,
                    new CellAddress(middleSheet.Id, 3, 3)).Should().BeTrue();
                window.FormulaBoxTextForTest.Should().Be("=SUM('Middle Sheet:Final Sheet'!A1:C3)");

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.Enter,
                    PhysicalKey = PhysicalKey.Enter,
                });

                sourceSheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM('Middle Sheet:Final Sheet'!A1:C3)");
                sourceSheet.GetValue(formulaAddress).Should().Be(new NumberValue(225));

                using var stream = new MemoryStream();
                new NativeJsonAdapter().Save(window.Session.Workbook, stream);
                stream.Position = 0;
                var reopened = new NativeJsonAdapter().Load(stream);
                var reopenedSource = reopened.Sheets.Single(sheet => sheet.Name == "Source Sheet");
                var reopenedFormulaAddress = new CellAddress(reopenedSource.Id, 8, 7);
                reopenedSource.GetCell(reopenedFormulaAddress)!.FormulaText
                    .Should().Be("SUM('Middle Sheet:Final Sheet'!A1:C3)");
                reopenedSource.GetValue(reopenedFormulaAddress).Should().Be(new NumberValue(225));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ThreeDSheetRange_NativeXlsxRoundTrip_PreservesEscapedReverseQualifierAndResult()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var summarySheet = window.Session.Workbook.Sheets[0];
            summarySheet.Name = "Summary";
            var forwardSheet = window.Session.Workbook.AddSheet("Revenue Data");
            var reverseSheet = window.Session.Workbook.AddSheet("O'Brien Data");
            window.Session.SelectSheet(summarySheet.Id);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            try
            {
                for (uint row = 2; row <= 4; row++)
                {
                    for (uint col = 2; col <= 4; col++)
                    {
                        forwardSheet.SetCell(new CellAddress(forwardSheet.Id, row, col),
                            new NumberValue((row - 1) * 3 + col - 1));
                        reverseSheet.SetCell(new CellAddress(reverseSheet.Id, row, col),
                            new NumberValue(10 + (row - 1) * 3 + col - 1));
                    }
                }

                var formulaAddress = new CellAddress(summarySheet.Id, 8, 7);
                var formula = "=SUM('O''Brien Data:Revenue Data'!B2:C3)";
                summarySheet.SetCell(formulaAddress, Cell.FromFormula(formula[1..]));
                window.Session.SelectCell(formulaAddress);
                window.BeginFormulaEditForTest(formulaAddress, formula);

                window.SelectFormulaReferenceSheetForTest(forwardSheet.Id).Should().BeTrue();
                window.FormulaReferenceGripCountForTest.Should().Be(1);
                window.RaiseFormulaReferenceGripDragForTest(
                    highlightIndex: 0,
                    new CellAddress(forwardSheet.Id, 4, 4)).Should().BeTrue();
                window.FormulaBoxTextForTest.Should().Be("=SUM('O''Brien Data:Revenue Data'!B2:D4)");

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.Enter,
                    PhysicalKey = PhysicalKey.Enter,
                });

                summarySheet.GetCell(formulaAddress)!.FormulaText
                    .Should().Be("SUM('O''Brien Data:Revenue Data'!B2:D4)");
                summarySheet.GetValue(formulaAddress).Should().Be(new NumberValue(234));

                using var stream = new MemoryStream();
                new XlsxFileAdapter().Save(window.Session.Workbook, stream);
                stream.Position = 0;
                var reopened = new XlsxFileAdapter().Load(stream);
                var reopenedSummary = reopened.Sheets.Single(sheet => sheet.Name == "Summary");
                var reopenedFormulaAddress = new CellAddress(reopenedSummary.Id, 8, 7);
                reopenedSummary.GetCell(reopenedFormulaAddress)!.FormulaText
                    .Should().Be("SUM('O''Brien Data:Revenue Data'!B2:D4)");
                reopenedSummary.GetValue(reopenedFormulaAddress).Should().Be(new NumberValue(234));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }
}
