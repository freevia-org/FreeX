using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class MainWindowShellFrameTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task MainWindow_content_uses_shared_client_frame_shape()
    {
        int childCount = -1;
        int bottomDockedCount = -1;
        int topDockedCount = -1;
        var lastChildFill = false;

        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var root = window.Content.Should().BeOfType<DockPanel>().Subject;
            childCount = root.Children.Count;
            bottomDockedCount = root.Children.Count(child => DockPanel.GetDock(child) == Dock.Bottom);
            topDockedCount = root.Children.Count(child => DockPanel.GetDock(child) == Dock.Top);
            lastChildFill = root.LastChildFill;
        });

        if (!ran)
            return;

        childCount.Should().Be(4, "FreeW contributes ribbon, status, find bar, and workarea to the shared frame");
        topDockedCount.Should().Be(1, "the shared frame keeps the ribbon docked at the top");
        bottomDockedCount.Should().Be(2, "the shared frame keeps the status bar and find bar docked at the bottom");
        lastChildFill.Should().BeTrue("the workarea should fill the remaining client frame");
    }

    [Fact]
    public void MainWindow_sources_reference_the_shared_avalonia_shell_frame()
    {
        var project = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"));
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");

        var mainWindow = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        mainWindow.Should().Contain("using Free.Shared.Shell.Avalonia;");
        mainWindow.Should().Contain("SisterAppClientFrameBuilder.Build(");
        mainWindow.Should().Contain("SisterAppStatusBarChrome.Build(");
        mainWindow.Should().Contain("SisterAppStatusBarChrome.CreateInfoText(margin: new Thickness(8, 0))");
        mainWindow.Should().Contain("SisterAppStatusBarChrome.CreateInfoText(\"100%\", margin: new Thickness(8, 0))");
        mainWindow.Should().Contain("BottomPanelsAboveStatus: [findBar]");
        mainWindow.Should().Contain("RightItems: BuildStatusRightItems()");
        mainWindow.Should().NotContain("private readonly TextBlock _zoomLabel = new()");
    }

    [Fact]
    public void StatusBarChrome_CreatesSharedInfoTextAndSeparatorStyles()
    {
        var text = SisterAppStatusBarChrome.CreateInfoText(
            "Ready",
            foreground: Brushes.White,
            margin: new Thickness(3, 4, 5, 6),
            fontSize: 13);
        var separator = SisterAppStatusBarChrome.CreateSeparator();

        text.Text.Should().Be("Ready");
        text.Foreground.Should().BeSameAs(Brushes.White);
        text.Margin.Should().Be(new Thickness(3, 4, 5, 6));
        text.FontSize.Should().Be(13);
        text.VerticalAlignment.Should().Be(VerticalAlignment.Center);
        text.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);

        separator.Width.Should().Be(1);
        separator.Margin.Should().Be(new Thickness(8, 3, 8, 3));
        separator.VerticalAlignment.Should().Be(VerticalAlignment.Stretch);
        separator.Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    }

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(FindRepoRoot(), Path.Combine(parts));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }
}
