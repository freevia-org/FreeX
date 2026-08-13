using FluentAssertions;

namespace FreeP.App.Avalonia.Tests;

public sealed class PhysicalHyperlinkFixtureSourceTests
{
    [Fact]
    public void PhysicalHyperlinkFixtureAndPostconditions_AreOwnedByExternalValidationHost()
    {
        var mainWindow = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var slideShow = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "SlideShowWindow.cs"));
        var tool = File.ReadAllText(RepoFile(
            "freep", "TestSupport", "Validation.Avalonia", "PhysicalFixtureValidation.cs"));
        var adapter = File.ReadAllText(RepoFile(
            "freep", "TestSupport", "Validation.Avalonia", "MainWindow.ValidationAccessAdapter.cs"));

        mainWindow.Should().NotContain("FREEP_PHYSICAL_HYPERLINK");
        mainWindow.Should().NotContain("Id = 9001");
        mainWindow.Should().Contain("NotifyHyperlinkAppliedObserver();");
        adapter.Should().Contain("partial void NotifyHyperlinkAppliedObserver()");
        slideShow.Should().NotContain("FREEP_PHYSICAL_HYPERLINK");
        slideShow.Should().NotContain("File.WriteAllText");
        slideShow.Should().Contain("_internalHyperlinkNavigationObserver?.Invoke");
        tool.Should().Contain("--physical-internal-slide-hyperlink-fixture");
        tool.Should().Contain("Id = 9001");
        tool.Should().Contain("ExtentCxEmu = shapeWidth");
        tool.Should().Contain("new ShapeFill.Solid(new SrgbColor(0x44, 0x72, 0xC3))");
        tool.Should().Contain("fixture-postcondition.txt");
        tool.Should().Contain("authoring-postcondition.txt");
        tool.Should().Contain("activation-postcondition.txt");

        var runner = File.ReadAllText(RepoFile("tools", "Run-FreePInternalSlideHyperlinkValidation.ps1"));
        runner.Should().Contain("Host = \"Validation\"");
        runner.Should().Contain("--physical-internal-slide-hyperlink-fixture=/work/freep-internal-slide-hyperlink");
        runner.Should().NotContain("FREEP_PHYSICAL_HYPERLINK");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.ResolveFromDirectoryContainingFile(
            "FreeP.slnx", parts);
}
