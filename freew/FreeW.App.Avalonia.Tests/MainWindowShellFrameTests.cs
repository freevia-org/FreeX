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
    public async Task MainWindow_content_uses_shared_outer_and_client_frame_shape()
    {
        int outerChildCount = -1;
        int clientChildCount = -1;
        int bottomDockedCount = -1;
        int topDockedCount = -1;
        var lastChildFill = false;
        var titleBarHeight = 0d;

        await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            var outer = window.Content.Should().BeOfType<Grid>().Subject;
            var root = outer.Children.OfType<DockPanel>().Single();
            outerChildCount = outer.Children.Count;
            clientChildCount = root.Children.Count;
            bottomDockedCount = root.Children.Count(child => DockPanel.GetDock(child) == Dock.Bottom);
            topDockedCount = root.Children.Count(child => DockPanel.GetDock(child) == Dock.Top);
            lastChildFill = root.LastChildFill;
            titleBarHeight = window.TitleBarForTests.Height;
        });

        outerChildCount.Should().Be(2, "the shared outer frame contains the titlebar and client body");
        clientChildCount.Should().Be(4, "FreeW contributes ribbon, status, find bar, and workarea to the shared client frame");
        topDockedCount.Should().Be(1, "the shared frame keeps the ribbon docked at the top");
        bottomDockedCount.Should().Be(2, "the shared frame keeps the status bar and find bar docked at the bottom");
        lastChildFill.Should().BeTrue("the workarea should fill the remaining client frame");
        titleBarHeight.Should().Be(34);
    }

    [Fact]
    public async Task MainWindow_uses_canonical_icon_document_first_title_and_shared_qat()
    {
        bool hasIcon = false;
        string title = string.Empty;
        string[] qatIds = [];

        await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            hasIcon = window.HasWindowIconForTests;
            title = window.Title ?? string.Empty;
            qatIds = window.QuickAccessButtonsForTests
                .Select(button => global::Avalonia.Automation.AutomationProperties.GetAutomationId(button) ?? string.Empty)
                .ToArray();
        });

        hasIcon.Should().BeTrue();
        title.Should().Be("Untitled \u2014 FreeW");
        qatIds.Should().Equal("Save", "Undo", "Redo");
    }

    [Fact]
    public async Task MainWindow_status_matches_Wpf_content_controls_and_zoom_state()
    {
        string page = string.Empty;
        string section = string.Empty;
        string counts = string.Empty;
        string dataFolder = string.Empty;
        string[] controlNames = [];
        bool printLayoutChecked = false;
        double minimum = 0;
        double maximum = 0;
        double value = 0;
        string zoomLabel = string.Empty;

        await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            page = window.PageStatusForTests;
            section = window.SectionStatusForTests;
            counts = window.CountsStatusForTests;
            dataFolder = window.DataFolderStatusForTests;
            controlNames = window.StatusViewControlsForTests
                .Select(control => global::Avalonia.Automation.AutomationProperties.GetName(control) ?? string.Empty)
                .ToArray();
            printLayoutChecked = ((global::Avalonia.Controls.Primitives.ToggleButton)window.StatusViewControlsForTests[1]).IsChecked == true;
            minimum = window.ZoomSliderForTests.Minimum;
            maximum = window.ZoomSliderForTests.Maximum;
            value = window.ZoomSliderForTests.Value;
            window.ApplyZoomForTests(1.4);
            zoomLabel = window.ZoomLabelForTests;
        });

        page.Should().StartWith("Page ");
        section.Should().StartWith("Section ");
        counts.Should().Contain("Words");
        dataFolder.Should().NotBeNullOrWhiteSpace();
        controlNames.Should().Equal("Read Mode", "Print Layout", "Web Layout", "Draft", "Page Edit");
        printLayoutChecked.Should().BeTrue();
        minimum.Should().Be(FreeW.Core.Model.ZoomLevels.Min);
        maximum.Should().Be(FreeW.Core.Model.ZoomLevels.Max);
        value.Should().Be(FreeW.Core.Model.ZoomLevels.Default);
        zoomLabel.Should().Be("140%");
    }

    [Fact]
    public void MainWindow_sources_reference_the_shared_avalonia_shell_frame()
    {
        var project = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"));
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");

        var mainWindow = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        mainWindow.Should().Contain("using Free.Shared.Shell.Avalonia;");
        mainWindow.Should().Contain("SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(");
        mainWindow.Should().Contain("SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(");
        mainWindow.Should().Contain("SisterQuickAccessToolbarBuilder.Render(");
        mainWindow.Should().Contain("ApplicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication");
        mainWindow.Should().Contain("ApplyWindowIcon();");
        mainWindow.Should().Contain("SisterAppStatusBarChrome.Build(");
        mainWindow.Should().Contain("private Border BuildStatusBar()");
        mainWindow.Should().Contain("FreeWApplicationFrameDescriptor.ResolveDataFolderLabel(_optionsStore.StorePath)");
        mainWindow.Should().Contain("BuildViewSwitchControl(white)");
        mainWindow.Should().Contain("BuildZoomControl(white)");
        mainWindow.Should().Contain("RibbonCommandIconKind.ReadMode");
        mainWindow.Should().Contain("Minimum = ZoomLevels.Min");
        mainWindow.Should().Contain("Maximum = ZoomLevels.Max");
        mainWindow.Should().Contain("chrome: ribbon,");
        mainWindow.Should().Contain("workArea: workArea,");
        mainWindow.Should().Contain("statusBar: statusBar,");
        mainWindow.Should().Contain("bottomPanelsAboveStatus: [findBar]");
        mainWindow.Should().Contain("RightItems: [viewSwitch, zoom]");
        mainWindow.Should().Contain("Content = windowFrame.Root;");
        AssertBefore(mainWindow, "SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(", "SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(");
        AssertBefore(mainWindow, "SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(", "Content = windowFrame.Root;");
        mainWindow.Should().NotContain("private readonly TextBlock _zoomLabel = new()");
    }

    [Fact]
    public void ClientFrameSpec_ForWorkArea_ExposesChromeWorkAreaContractAliases()
    {
        var chrome = new Border();
        var workArea = new Grid();
        var statusBar = new Border();
        var topPanel = new Border();
        var bottomPanel = new Border();

        var spec = SisterAppClientFrameSpec.ForWorkArea(
            chrome: chrome,
            workArea: workArea,
            statusBar: statusBar,
            bottomPanelsAboveStatus: [bottomPanel],
            topPanelsBelowChrome: [topPanel]);

        spec.Chrome.Should().BeSameAs(chrome);
        spec.Ribbon.Should().BeSameAs(chrome);
        spec.WorkArea.Should().BeSameAs(workArea);
        spec.StatusBar.Should().BeSameAs(statusBar);
        spec.TopPanelsBelowChrome.Should().Equal(topPanel);
        spec.TopPanelsBelowRibbon.Should().Equal(topPanel);
        spec.BottomPanelsAboveStatus.Should().Equal(bottomPanel);
    }

    [Fact]
    public void ClientFrameBuilder_ComposesTopWorkAreaBottomAndStatusFromSharedContract()
    {
        var chrome = new Border();
        var topPanel1 = new Border();
        var topPanel2 = new Border();
        var workArea = new Grid();
        var bottomPanel1 = new Border();
        var bottomPanel2 = new Border();
        var statusBar = new Border();

        var result = SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(
            chrome: chrome,
            workArea: workArea,
            statusBar: statusBar,
            bottomPanelsAboveStatus: [bottomPanel1, bottomPanel2],
            topPanelsBelowChrome: [topPanel1, topPanel2]));

        result.Root.LastChildFill.Should().BeTrue();
        result.Root.Children.Should().Equal(
            chrome,
            topPanel1,
            topPanel2,
            statusBar,
            bottomPanel2,
            bottomPanel1,
            workArea);
        result.Root.Children.Last().Should().BeSameAs(workArea);
        DockPanel.GetDock(chrome).Should().Be(Dock.Top);
        DockPanel.GetDock(topPanel1).Should().Be(Dock.Top);
        DockPanel.GetDock(topPanel2).Should().Be(Dock.Top);
        DockPanel.GetDock(statusBar).Should().Be(Dock.Bottom);
        DockPanel.GetDock(bottomPanel2).Should().Be(Dock.Bottom);
        DockPanel.GetDock(bottomPanel1).Should().Be(Dock.Bottom);
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

    private static Task OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine(parts));


    private static void AssertBefore(string source, string first, string second)
    {
        source.IndexOf(first, StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf(second, StringComparison.Ordinal), $"{first} should appear before {second}");
    }
}
