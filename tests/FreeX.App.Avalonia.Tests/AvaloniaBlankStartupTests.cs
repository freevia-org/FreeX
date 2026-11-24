using System.Threading;
using Avalonia.Headless;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaBlankStartupTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task NoArgumentStartup_CreatesBlankBook1UsingCurrentNewWorkbookOptions() =>
        Session.Dispatch(() =>
        {
            var previousFactory = App.ExternalStartupSessionFactory;
            App.ExternalStartupSessionFactory = null;
            MainWindow? window = null;
            try
            {
                var options = new AppOptions { DefaultSheetCount = 3, DefaultFontName = "Arial", DefaultFontSize = 14 };
                window = new MainWindow([], null!, new FreeXOptionsRuntimeSession(
                    load: () => options.Clone(), save: _ => true));

                AssertBlankStartup(window);
                window.Session.Workbook.Sheets.Select(sheet => sheet.Name).Should().Equal("Sheet1", "Sheet2", "Sheet3");
                var style = window.Session.Workbook.GetStyle(StyleId.Default);
                style.FontName.Should().Be("Arial");
                style.FontSize.Should().Be(14);
            }
            finally
            {
                Close(window);
                App.ExternalStartupSessionFactory = previousFactory;
            }
        }, CancellationToken.None);

    [Fact]
    public Task DeferredFileStartup_LeavesBlankWorkbookForRecoveryAndLaterFileOpen() =>
        Session.Dispatch(() =>
        {
            var previousFactory = App.ExternalStartupSessionFactory;
            App.ExternalStartupSessionFactory = null;
            var path = Path.Combine(Path.GetTempPath(), $"freex-startup-{Guid.NewGuid():N}.csv");
            MainWindow? window = null;
            try
            {
                File.WriteAllText(path, "Should,OpenLater\r\n1,2\r\n");
                window = new MainWindow([path], deferStartupFileOpen: true);

                AssertBlankStartup(window);
                window.Session.StartupStatus.Should().Be("Created new workbook.");
            }
            finally
            {
                Close(window);
                App.ExternalStartupSessionFactory = previousFactory;
                File.Delete(path);
            }
        }, CancellationToken.None);

    private static void AssertBlankStartup(MainWindow window)
    {
        window.Session.DisplayName.Should().Be(WorkbookFactory.DefaultWorkbookName);
        window.Session.IsDirty.Should().BeFalse();
        window.Session.CurrentFilePath.Should().BeNull();
        foreach (var sheet in window.Session.Workbook.Sheets)
        {
            sheet.EnumerateCells().Should().BeEmpty();
            sheet.DrawingShapes.Should().BeEmpty();
            sheet.TextBoxes.Should().BeEmpty();
            sheet.Pictures.Should().BeEmpty();
            sheet.FrozenRows.Should().Be(0);
            sheet.FrozenCols.Should().Be(0);
        }
    }

    private static void Close(MainWindow? window)
    {
        if (window is null)
            return;
        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
    }
}
