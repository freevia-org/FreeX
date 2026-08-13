extern alias ProductionWpf;

using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ParityCaptureAssemblyOwnershipTests
{
    [Fact]
    public void ShippingAssembly_DoesNotOwnParityCaptureOrScreenshotTours()
    {
        var assembly = typeof(ProductionWpf::FreeX.App.Host.MainWindow).Assembly;

        assembly.GetType("FreeX.App.Host.ParityCapture").Should().BeNull();
        assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic))
            .Select(method => method.Name)
            .Should().NotContain(name => name.StartsWith("TryStartScreenshotTour", StringComparison.Ordinal));
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain("FreeX.ParityCapture.Support");

        var mainWindow = assembly.GetType("FreeX.App.Host.MainWindow");
        mainWindow.Should().NotBeNull();
        var members = mainWindow!.GetMembers(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        members.Select(member => member.Name).Should().NotContain(new[]
        {
            "_parityCaptureWorkbookPrepared",
            "_reviewNotesWindow",
            "AdoptWorkbookForParityCapture",
            "TryStartSheetTabVisualTour",
            "TryStartSheetTabWorkflowsTour",
            "TryStartAccentBarVisualTour"
        });
    }

    [Fact]
    public void CaptureAssembly_OwnsStartupToursAndCaptureOnlyState()
    {
        var assembly = typeof(FreeX.App.Host.MainWindow).Assembly;
        var mainWindow = assembly.GetType("FreeX.App.Host.MainWindow");
        mainWindow.Should().NotBeNull();
        var members = mainWindow!.GetMembers(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);

        assembly.GetType("FreeX.App.Host.ParityCapture").Should().NotBeNull();
        members.Select(member => member.Name).Should().Contain(new[]
        {
            "_parityCaptureWorkbookPrepared",
            "_reviewNotesWindow",
            "AdoptWorkbookForParityCapture",
            "TryStartScreenshotTour",
            "TryStartSheetTabVisualTour",
            "TryStartSheetTabWorkflowsTour",
            "TryStartAccentBarVisualTour"
        });
    }

    [Fact]
    public void ShippingSources_DoNotContainCapturePreprocessorOrTourOwnership()
    {
        var app = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "App.xaml.cs");
        var startup = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.Startup.cs");
        var review = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs");
        var project = WorkspaceFileLocator.ReadAllText("tools", "FreeX.ParityCapture.Wpf", "FreeX.ParityCapture.Wpf.csproj");
        var toolStartup = WorkspaceFileLocator.ReadAllText(
            "tools", "FreeX.ParityCapture.Wpf", "Capture", "App.ParityCaptureStartup.cs");

        string.Concat(app, startup, review).Should().NotContain("FREEX_PARITY_CAPTURE");
        app.Should().NotContain("ParityCapture.");
        startup.Should().NotContain("TryStartScreenshotTour");
        startup.Should().NotContain("TryStartSheetTabVisualTour");
        startup.Should().NotContain("TryStartSheetTabWorkflowsTour");
        startup.Should().NotContain("TryStartAccentBarVisualTour");
        review.Should().NotContain("_reviewNotesWindow");
        project.Should().NotContain("<DefineConstants>");
        toolStartup.Should().Contain("ParityCapture.Run(");
    }

    [Fact]
    public void WpfCapture_NormalizesBackstageBeforeCapturingContentTabs()
    {
        var capture = WorkspaceFileLocator.ReadAllText(
            "tools", "FreeX.ParityCapture.Wpf", "Capture", "ParityCapture.cs");

        capture.Should().Contain(".Where(tab => tab.Id != FreeXRibbonTabIds.File)");
        capture.Should().Contain("InvokePrivate(window, \"HideStartScreen\")");
        capture.Should().Contain("TrySelectRibbonTab(window, FreeXRibbonTabIds.Home)");
    }
}
