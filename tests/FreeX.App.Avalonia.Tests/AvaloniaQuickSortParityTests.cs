using System.Threading;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.Ribbon;
using FreeX.Ribbon.Definitions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaQuickSortParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData("Sort A to Z#SortAscButton_Click", true)]
    [InlineData("Sort Z to A#SortDescButton_Click", false)]
    public async Task QuickSort_LeavesDetectedHeaderInPlace_AndUndoRestoresData(
        string avaloniaCommandId,
        bool ascending)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                SeedHeaderAndData(sheet);
                var selected = new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 2));
                window.Session.SelectRange(selected);

                var commandId = FreeXRibbonCommandCatalog.GetRequired(avaloniaCommandId);
                window.RibbonCommandRegistryForTest!.TryGet(commandId, out var command).Should().BeTrue();
                command!.Execute(RibbonCommandContext.Empty);

                sheet.GetValue(1, 1).Should().Be(new TextValue("Name"));
                sheet.GetValue(1, 2).Should().Be(new TextValue("Score"));
                if (ascending)
                {
                    sheet.GetValue(2, 1).Should().Be(new TextValue("Alice"));
                    sheet.GetValue(3, 1).Should().Be(new TextValue("Bob"));
                }
                else
                {
                    sheet.GetValue(2, 1).Should().Be(new TextValue("Bob"));
                    sheet.GetValue(3, 1).Should().Be(new TextValue("Alice"));
                }

                window.Session.CanUndo.Should().BeTrue();
                window.Session.UndoLastEdit().Success.Should().BeTrue();
                sheet.GetValue(1, 1).Should().Be(new TextValue("Name"));
                sheet.GetValue(2, 1).Should().Be(new TextValue("Bob"));
                sheet.GetValue(3, 1).Should().Be(new TextValue("Alice"));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task QuickSort_HeaderlessSelectionSortsFirstRowAsData()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                sheet.SetCell(Address(sheet, 1, 1), new NumberValue(30));
                sheet.SetCell(Address(sheet, 1, 2), new TextValue("Xray"));
                sheet.SetCell(Address(sheet, 2, 1), new NumberValue(10));
                sheet.SetCell(Address(sheet, 2, 2), new TextValue("Apple"));
                sheet.SetCell(Address(sheet, 3, 1), new NumberValue(20));
                sheet.SetCell(Address(sheet, 3, 2), new TextValue("Mango"));
                window.Session.SelectRange(new GridRange(Address(sheet, 1, 1), Address(sheet, 3, 2)));

                var commandId = FreeXRibbonCommandCatalog.GetRequired("Sort A to Z#SortAscButton_Click");
                window.RibbonCommandRegistryForTest!.TryGet(commandId, out var command).Should().BeTrue();
                command!.Execute(RibbonCommandContext.Empty);

                sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
                sheet.GetValue(2, 1).Should().Be(new NumberValue(20));
                sheet.GetValue(3, 1).Should().Be(new NumberValue(30));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task QuickSort_AppliesToGroupedSheets_AndUndoRestoresEverySheet()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var first = window.Session.ActiveSheet;
                var second = window.Session.Workbook.AddSheet("GroupedQuickSort");
                SeedHeaderAndData(first);
                SeedHeaderAndData(second);
                window.Session.SelectSheet(first.Id);
                window.Session.SelectAllVisibleSheets().Should().BeTrue();
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                window.Session.SelectRange(new GridRange(Address(first, 1, 1), Address(first, 3, 2)));

                var commandId = FreeXRibbonCommandCatalog.GetRequired("Sort A to Z#SortAscButton_Click");
                window.RibbonCommandRegistryForTest!.TryGet(commandId, out var command).Should().BeTrue();
                command!.Execute(RibbonCommandContext.Empty);

                AssertSortedData(first);
                AssertSortedData(second);
                window.Session.CanUndo.Should().BeTrue();
                window.Session.UndoLastEdit().Success.Should().BeTrue();
                AssertOriginalData(first);
                AssertOriginalData(second);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void SeedHeaderAndData(Sheet sheet)
    {
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Score"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Bob"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(80));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Alice"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(90));
    }

    private static void AssertSortedData(Sheet sheet)
    {
        sheet.GetValue(1, 1).Should().Be(new TextValue("Name"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Alice"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Bob"));
    }

    private static void AssertOriginalData(Sheet sheet)
    {
        sheet.GetValue(1, 1).Should().Be(new TextValue("Name"));
        sheet.GetValue(2, 1).Should().Be(new TextValue("Bob"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("Alice"));
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);
}
