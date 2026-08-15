using System.Reflection;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class R92_PivotClickGetPivotDataToggleAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ClickPivotValueCell_OptionOn_InsertsGetPivotDataFormula()
    {
        await WithOptions(new AppOptions { GenerateGetPivotData = true }, async () =>
        {
            await Session.Dispatch(() =>
            {
                var window = CreateFormulaPointWindow(out var sheet, out var formulaAddress);
                try
                {
                    ApplyFormulaRangeSelection(window, new CellAddress(sheet.Id, 4, 6)).Should().BeTrue();

                    window.FormulaBoxTextForTest.Should().Be(
                        "=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")");
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();
                    window.Close();
                }
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ClickPivotValueCell_OptionOff_InsertsPlainA1Reference()
    {
        await WithOptions(new AppOptions { GenerateGetPivotData = false }, async () =>
        {
            await Session.Dispatch(() =>
            {
                var window = CreateFormulaPointWindow(out var sheet, out _);
                try
                {
                    ApplyFormulaRangeSelection(window, new CellAddress(sheet.Id, 4, 6)).Should().BeTrue();

                    window.FormulaBoxTextForTest.Should().Be("=F4");
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();
                    window.Close();
                }
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task ClickOrdinaryCell_OptionOn_StillInsertsPlainA1Reference()
    {
        await WithOptions(new AppOptions { GenerateGetPivotData = true }, async () =>
        {
            await Session.Dispatch(() =>
            {
                var window = CreateFormulaPointWindow(out var sheet, out _);
                try
                {
                    ApplyFormulaRangeSelection(window, new CellAddress(sheet.Id, 1, 1)).Should().BeTrue();

                    window.FormulaBoxTextForTest.Should().Be("=A1");
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();
                    window.Close();
                }
            }, CancellationToken.None);
        });
    }

    private static MainWindow CreateFormulaPointWindow(out Sheet sheet, out CellAddress formulaAddress)
    {
        var window = new MainWindow([]);
        sheet = window.Session.ActiveSheet;
        SetUpRowPivot(sheet);
        formulaAddress = new CellAddress(sheet.Id, 1, 8);
        window.BeginFormulaEditForTest(formulaAddress, "=");
        window.SetFormulaBoxSelectionForTest(1, 0);
        return window;
    }

    private static bool ApplyFormulaRangeSelection(MainWindow window, CellAddress target)
    {
        var method = typeof(MainWindow).GetMethod(
            "TryApplyFormulaRangeSelection",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(CellAddress), typeof(bool)],
            modifiers: null);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(window, [target, false])!;
    }

    private static void SetUpRowPivot(Sheet sheet)
    {
        SetCells(
            sheet,
            ("A1", new TextValue("Region")),
            ("B1", new TextValue("Amount")),
            ("E2", new TextValue("Region")),
            ("F2", new TextValue("Sum of Amount")),
            ("E3", new TextValue("East")),
            ("F3", new NumberValue(25)),
            ("E4", new TextValue("West")),
            ("F4", new NumberValue(45)),
            ("E5", new TextValue("Grand Total")),
            ("F5", new NumberValue(70)));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1:B5"),
            TargetRange = Range(sheet, "E2:F5")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
    }

    private static GridRange Range(Sheet sheet, string reference)
    {
        var parts = reference.Split(':');
        return new GridRange(CellAddress.Parse(parts[0], sheet.Id), CellAddress.Parse(parts[^1], sheet.Id));
    }

    private static void SetCells(Sheet sheet, params (string Address, ScalarValue Value)[] cells)
    {
        foreach (var (address, value) in cells)
            sheet.SetCell(CellAddress.Parse(address, sheet.Id), value);
    }

    private static async Task WithOptions(AppOptions options, Func<Task> action)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"freex-avalonia-pivot-options-{Guid.NewGuid():N}.json");
        var previousPath = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        try
        {
            AppOptionsStore.SaveToPath(options, tempPath).Should().BeTrue();
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, tempPath);
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, previousPath);
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
