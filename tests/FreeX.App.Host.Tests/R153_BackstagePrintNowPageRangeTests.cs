using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// shared-print-settings F2: the Backstage Print pane's "Print Now" button (MainWindow.Backstage.cs,
/// <c>BackstagePrintNowButton_Click</c>) fed the typed Pages From/To straight through
/// <see cref="PrintSettingsPlanner.TryValidatePageRange"/> and, when that rejected an out-of-bounds or
/// reversed range, silently fell through to printing the whole, unranged document with zero feedback.
/// The fix extracts the range-resolution step into <c>TryResolveBackstagePrintPaginator</c> so it can
/// be exercised here directly -- <see cref="NativePrintDialogService"/> opens a real, blocking Win32
/// print dialog and must never run under test, so these tests never reach it.
/// </summary>
public sealed class R153_BackstagePrintNowPageRangeTests
{
    [Fact]
    public void ReversedPageRange_WarnsAndDoesNotPrint()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = Harness.Create();
            var document = ThreePageDocument();

            string? capturedMessage = null;
            HeadlessMessageBox.Handler = (message, _) =>
            {
                capturedMessage = message;
                return UserMessageResult.Ok;
            };

            try
            {
                // From (2) > To (1) -- both individually within the 3-page document, but reversed.
                var settings = new PrintPreviewSettings(PageFrom: 2, PageTo: 1);

                var result = harness.TryResolveBackstagePrintPaginator(document, settings, out _);

                result.Should().BeFalse("a reversed page range is not satisfiable and must abort printing");
                capturedMessage.Should().Be(
                    UiText.Get("PrintPreview_InvalidPageRangeMessage"),
                    "the user must be warned instead of the bad range being silently discarded");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
            }
        });
    }

    [Fact]
    public void OutOfBoundsPageRange_WarnsAndDoesNotPrint()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = Harness.Create();
            var document = ThreePageDocument(); // only 3 pages

            string? capturedMessage = null;
            HeadlessMessageBox.Handler = (message, _) =>
            {
                capturedMessage = message;
                return UserMessageResult.Ok;
            };

            try
            {
                var settings = new PrintPreviewSettings(PageFrom: 50, PageTo: null);

                var result = harness.TryResolveBackstagePrintPaginator(document, settings, out _);

                result.Should().BeFalse("From=50 exceeds the document's 3 pages");
                capturedMessage.Should().Be(UiText.Get("PrintPreview_InvalidPageRangeMessage"));
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
            }
        });
    }

    // ── Sibling / no-regression coverage: valid ranges and "no range requested" must keep working ──

    [Fact]
    public void ValidPageRange_ResolvesARangedPaginatorWithoutWarning()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = Harness.Create();
            var document = ThreePageDocument();

            string? capturedMessage = null;
            HeadlessMessageBox.Handler = (message, _) =>
            {
                capturedMessage = message;
                return UserMessageResult.Ok;
            };

            try
            {
                var settings = new PrintPreviewSettings(PageFrom: 1, PageTo: 2);

                var result = harness.TryResolveBackstagePrintPaginator(document, settings, out var paginator);

                result.Should().BeTrue("1..2 is a satisfiable range on a 3-page document");
                capturedMessage.Should().BeNull("a valid range must not trigger the invalid-range warning");
                paginator.Should().NotBeNull();
                paginator!.PageCount.Should().Be(2, "the resolved paginator must be narrowed to the requested range");
            }
            finally
            {
                HeadlessMessageBox.Handler = null;
            }
        });
    }

    [Fact]
    public void NoPageRangeRequested_ResolvesTheFullUnrangedPaginator()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = Harness.Create();
            var document = ThreePageDocument();
            var settings = new PrintPreviewSettings();

            var result = harness.TryResolveBackstagePrintPaginator(document, settings, out var paginator);

            result.Should().BeTrue();
            paginator.Should().BeSameAs(
                document.DocumentPaginator,
                "with no range requested the full document paginator must be used, unchanged");
        });
    }

    private static FixedDocument ThreePageDocument()
    {
        var document = new FixedDocument();
        document.Pages.Add(new PageContent());
        document.Pages.Add(new PageContent());
        document.Pages.Add(new PageContent());
        return document;
    }

    private sealed class Harness : IDisposable
    {
        private readonly MainWindow _window;

        private Harness(MainWindow window) => _window = window;

        public static Harness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();

            return new Harness(window);
        }

        /// <summary>
        /// Invokes MainWindow's private page-range guard by reflection so the test never has to drive
        /// the real click handler through to <see cref="NativePrintDialogService"/> (which would open a
        /// real, blocking Win32 print dialog). Throws with a clear message if the guard method is
        /// missing -- the pre-fix shape of BackstagePrintNowButton_Click had no such extracted method.
        /// </summary>
        public bool TryResolveBackstagePrintPaginator(
            FixedDocument document,
            PrintPreviewSettings settings,
            out DocumentPaginator? paginator)
        {
            var method = typeof(MainWindow).GetMethod(
                    "TryResolveBackstagePrintPaginator",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "MainWindow.TryResolveBackstagePrintPaginator was not found -- the Backstage " +
                    "Print Now invalid-page-range guard is missing.");

            var parameters = new object?[] { document, settings, null };
            var result = (bool)method.Invoke(_window, parameters)!;
            paginator = (DocumentPaginator?)parameters[2];
            return result;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }
}
