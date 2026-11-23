using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform.Storage;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class MacOsNativeActivationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task FileActivation_AfterOriginalCloses_OpensInSurvivingSibling() => Run(async root =>
    {
        var original = new MainWindow([]);
        original.Show();
        var sibling = original.CreateSharedViewForTest();
        sibling.Show();
        original.AllowCloseWithoutDirtyPromptForParityCapture();
        original.Close();
        var path = WriteWorkbook(root, "surviving.csv", "surviving workbook");
        var item = await StorageItem(sibling, path);

        await App.OpenNativeActivatedFilesAsync([item]);

        MainWindow.WindowRegistryForTest.Windows.Should().ContainSingle().Which.Should().BeSameAs(sibling);
        sibling.Session.CurrentFilePath.Should().Be(path);
        Value(sibling).Should().Be(new TextValue("surviving workbook"));
        sibling.IsVisible.Should().BeTrue();
    });

    [Fact]
    public Task FileActivation_OpensEveryValidItemInAnIndependentWindow() => Run(async root =>
    {
        var window = new MainWindow([]);
        window.Show();
        var first = WriteWorkbook(root, "first.csv", "first workbook");
        var second = WriteWorkbook(root, "second.csv", "second workbook");
        var unsupported = Path.Combine(root, "unsupported.bin");
        File.WriteAllText(unsupported, "not a workbook");

        await App.OpenNativeActivatedFilesAsync([
            await StorageItem(window, unsupported),
            await StorageItem(window, first),
            await StorageItem(window, second)]);

        var windows = MainWindow.WindowRegistryForTest.Windows;
        windows.Should().HaveCount(2);
        windows.Select(view => view.Session.CurrentFilePath).Should().BeEquivalentTo([first, second]);
        Value(windows.Single(view => view.Session.CurrentFilePath == first)).Should().Be(new TextValue("first workbook"));
        Value(windows.Single(view => view.Session.CurrentFilePath == second)).Should().Be(new TextValue("second workbook"));
        windows[0].Session.Workbook.Should().NotBeSameAs(windows[1].Session.Workbook);
    });

    [Fact]
    public Task Reopen_RestoresMinimizedLiveWindow() => Run(_ =>
    {
        var window = new MainWindow([]);
        window.Show();
        window.WindowState = WindowState.Minimized;

        App.ActivateNativeWorkbookWindow().Should().BeSameAs(window);

        window.WindowState.Should().Be(WindowState.Normal);
        window.IsVisible.Should().BeTrue();
        MainWindow.WindowRegistryForTest.Windows.Should().ContainSingle();
        return Task.CompletedTask;
    });

    [Fact]
    public Task Reopen_UnhidesWindowThroughRegistry() => Run(_ =>
    {
        var first = new MainWindow([]);
        var second = new MainWindow([]);
        first.Show();
        second.Show();
        MainWindow.WindowRegistryForTest.Hide(first).Should().BeTrue();
        second.AllowCloseWithoutDirtyPromptForParityCapture();
        second.Close();

        App.ActivateNativeWorkbookWindow().Should().BeSameAs(first);

        first.IsVisible.Should().BeTrue();
        MainWindow.WindowRegistryForTest.HiddenWindows.Should().BeEmpty();
        MainWindow.WindowRegistryForTest.VisibleWindows.Should().ContainSingle().Which.Should().BeSameAs(first);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Reopen_WithNoLiveWindow_CreatesOneIndependentWorkbook() => Run(_ =>
    {
        var window = App.ActivateNativeWorkbookWindow();

        window.IsVisible.Should().BeTrue();
        MainWindow.WindowRegistryForTest.Windows.Should().ContainSingle().Which.Should().BeSameAs(window);
        return Task.CompletedTask;
    });

    private static ScalarValue Value(MainWindow window) => window.Session.ActiveSheet.GetValue(
        new CellAddress(window.Session.ActiveSheet.Id, 1, 1));

    private static string WriteWorkbook(string root, string name, string value)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, value + Environment.NewLine);
        return path;
    }

    private static async Task<IStorageItem> StorageItem(MainWindow window, string path) =>
        await window.StorageProvider.TryGetFileFromPathAsync(new Uri(path))
        ?? throw new InvalidOperationException("Headless storage provider could not resolve the fixture.");

    private static Task Run(Func<string, Task> action) => Session.Dispatch(async () =>
    {
        CloseWindows();
        var root = Path.Combine(Path.GetTempPath(), $"freex-native-activation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await action(root);
        }
        finally
        {
            CloseWindows();
            Directory.Delete(root, recursive: true);
        }
        return true;
    }, CancellationToken.None);

    private static void CloseWindows()
    {
        foreach (var window in MainWindow.WindowRegistryForTest.Windows.ToArray())
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
    }
}
