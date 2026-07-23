using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaSharedWorkbookWindowTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task NewWindow_SharesDocumentRefreshesMutationAndKeepsViewStateLocal()
    {
        await Session.Dispatch(() =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);

            try
            {
                first.Session.Workbook.Should().BeSameAs(second.Session.Workbook);
                first.Session.Should().NotBeSameAs(second.Session);
                first.Title.Should().Be($"{first.Session.DisplayName} - 1 - FreeX");
                second.Title.Should().Be($"{second.Session.DisplayName} - 2 - FreeX");
                first.Session.DataValidationPromptResolver
                    .Should().NotBeSameAs(second.Session.DataValidationPromptResolver);

                var sheet = first.Session.ActiveSheet;
                var firstCell = new CellAddress(sheet.Id, 2, 2);
                var secondCell = new CellAddress(sheet.Id, 4, 4);
                first.Session.SelectCell(firstCell);
                second.Session.SelectCell(secondCell);
                first.Session.SetViewportOrigin(20, 5).Should().BeTrue();
                second.Session.SetViewportOrigin(30, 7).Should().BeTrue();

                first.Session.ActiveCell.Should().Be(firstCell);
                second.Session.ActiveCell.Should().Be(secondCell);
                first.Session.ViewportOrigin.Should().Be((20u, 5u));
                second.Session.ViewportOrigin.Should().Be((30u, 7u));

                first.Session.CommitCellText("shared edit").Success.Should().BeTrue();

                second.Session.Workbook.Should().BeSameAs(first.Session.Workbook);
                second.Session.ActiveSheet.GetValue(firstCell)
                    .Should().Be(new TextValue("shared edit"));
                second.Session.ActiveCell.Should().Be(secondCell);
                second.Session.ViewportOrigin.Should().Be((30u, 7u));
                second.Title.Should().Be($"{second.Session.DisplayName} - 2 * - FreeX");
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                second.Close();
                first.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReplacingOneViewDetachesItAndClosingItLeavesSiblingFunctional()
    {
        await Session.Dispatch(() =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);

            try
            {
                var originalWorkbook = second.Session.Workbook;
                var replacement = new WorkbookSessionFactory().CreateNew(
                    viewportHeight: 880,
                    viewportWidth: 1440,
                    includeObjects: true);

                first.ReplaceSession(replacement);

                first.Session.Workbook.Should().NotBeSameAs(originalWorkbook);
                second.Session.Workbook.Should().BeSameAs(originalWorkbook);
                first.Title.Should().Be($"{first.Session.DisplayName} - FreeX");
                second.Title.Should().Be($"{second.Session.DisplayName} - FreeX");

                first.AllowCloseWithoutDirtyPromptForParityCapture();
                first.Close();
                second.IsVisible.Should().BeTrue();

                var address = second.Session.ActiveCell;
                second.Session.CommitCellText("surviving view").Success.Should().BeTrue();
                second.Session.ActiveSheet.GetValue(address)
                    .Should().Be(new TextValue("surviving view"));
            }
            finally
            {
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                second.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UndoToSavePointRefreshesSiblingTitleToClean()
    {
        await Session.Dispatch(() =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);

            try
            {
                var savePath = Path.Combine(Path.GetTempPath(), "AvaloniaSharedSavePoint.xlsx");
                first.Session.MarkSaved(savePath);

                first.Title.Should().Be($"{first.Session.DisplayName} - 1 - FreeX");
                second.Title.Should().Be($"{second.Session.DisplayName} - 2 - FreeX");

                first.Session.CommitCellText("dirty after save").Success.Should().BeTrue();
                second.Title.Should().Be($"{second.Session.DisplayName} - 2 * - FreeX");

                first.Session.UndoLastEdit().Success.Should().BeTrue();

                first.Session.IsDirty.Should().BeFalse();
                second.Session.IsDirty.Should().BeFalse();
                second.Title.Should().Be($"{second.Session.DisplayName} - 2 - FreeX");
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                second.Close();
                first.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NewWindow_UsesOriginatingSheetAndStartsThatViewAtA1()
    {
        await Session.Dispatch(() =>
        {
            var first = new MainWindow([]);
            MainWindow? second = null;
            try
            {
                var activeSheet = first.Session.Workbook.AddSheet("OriginatingSheet");
                first.Session.SelectSheet(activeSheet.Id);
                var originCell = new CellAddress(activeSheet.Id, 7, 3);
                first.Session.SelectCell(originCell);

                second = first.CreateSharedViewForTest();

                second.Session.ActiveSheet.Should().BeSameAs(activeSheet);
                second.Session.ActiveCell.Should().Be(new CellAddress(activeSheet.Id, 1, 1));
                first.Session.ActiveSheet.Should().BeSameAs(activeSheet);
                first.Session.ActiveCell.Should().Be(originCell);
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second?.AllowCloseWithoutDirtyPromptForParityCapture();
                second?.Close();
                first.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlF6_CyclesRealWorkbookWindowsThroughRegistryForwardAndBackward()
    {
        await Session.Dispatch(async () =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            var third = first.CreateSharedViewForTest();
            Show(first);
            Show(second);
            Show(third);

            try
            {
                MainWindow? activeBefore = null;
                first.Activated += (_, _) => activeBefore = first;
                second.Activated += (_, _) => activeBefore = second;
                third.Activated += (_, _) => activeBefore = third;

                first.ActivateWorkbookWindow();
                MainWindow.WindowRegistryForTest.NextWindowTarget(first, forward: true)
                    .Should().BeSameAs(second);
                MainWindow.WindowRegistryForTest.NextWindowTarget(second, forward: true)
                    .Should().BeSameAs(third);
                MainWindow.WindowRegistryForTest.NextWindowTarget(first, forward: false)
                    .Should().BeSameAs(third);

                await first.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.F6,
                    KeyModifiers = KeyModifiers.Control,
                });
                second.IsActive.Should().BeTrue();

                await second.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.F6,
                    KeyModifiers = KeyModifiers.Control,
                });
                third.IsActive.Should().BeTrue();

                await third.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.F6,
                    KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                });
                second.IsActive.Should().BeTrue();
                activeBefore.Should().BeSameAs(second);
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                third.AllowCloseWithoutDirtyPromptForParityCapture();
                third.Close();
                second.Close();
                first.Close();
            }
        }, CancellationToken.None);
    }

    private static void Show(MainWindow window)
    {
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        window.UpdateLayout();
    }
}
