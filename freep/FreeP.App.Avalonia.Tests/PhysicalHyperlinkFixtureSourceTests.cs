using FluentAssertions;

namespace FreeP.App.Avalonia.Tests;

public sealed class PhysicalHyperlinkFixtureSourceTests
{
    [Fact]
    public void PhysicalHyperlinkFixture_IsExplicitlyOptInAndCreatesTwoSlides()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("FREEP_PHYSICAL_HYPERLINK_SEED");
        source.Should().Contain("SeedPhysicalHyperlinkFixtureIfRequested();");
        source.Should().Contain("Id = 9001");
        source.Should().Contain("ExtentCxEmu = shapeWidth");
        source.Should().Contain("new ShapeFill.Solid(new SrgbColor(0x44, 0x72, 0xC3))");
        source.Should().Contain("Physical hyperlink fixture did not create a visible slide-1 rectangle");
        source.Should().Contain("Editor.InsertSlide();");
        source.Should().Contain("Editor.SelectSlide(0);");
        source.Should().Contain("Editor.Select(linkShape.Id);");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull();
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
