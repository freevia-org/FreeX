using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonIconFactorySvgTests
{
    [Fact]
    public void CreateCommandIcon_LoadsCommandArtworkAsVectorImage()
    {
        StaTestRunner.Run(() =>
        {
            var icon = RibbonIconFactory.CreateCommandIcon(
                "Open",
                new RibbonCommandIcon(RibbonCommandIconKind.Generic),
                size: 40,
                Brushes.Black);

            var image = icon.Should().BeOfType<Image>().Subject;
            image.Source.Should().BeOfType<DrawingImage>();
            image.Source.Should().NotBeOfType<BitmapImage>();
            image.Width.Should().Be(40);
            image.Height.Should().Be(40);
            image.Stretch.Should().Be(Stretch.Uniform);
        });
    }

    [Fact]
    public void CreateCommandIcon_LoadsWhiteTitleBarArtworkAsVectorImage()
    {
        StaTestRunner.Run(() =>
        {
            var icon = RibbonIconFactory.CreateCommandIcon(
                "Save",
                new RibbonCommandIcon(RibbonCommandIconKind.Save),
                size: 13,
                Brushes.White);

            var image = icon.Should().BeOfType<Image>().Subject;
            image.Source.Should().BeOfType<DrawingImage>();
            image.Source.Should().NotBeOfType<BitmapImage>();
            image.Width.Should().Be(13);
            image.Height.Should().Be(13);
        });
    }

    [Fact]
    public void AppHostProject_CopiesSvgCommandIconsInsteadOfPreRenderedPngs()
    {
        var projectFile = WorkspaceFileLocator.Find(
            "src",
            "Freexcel.App.Host",
            "Freexcel.App.Host.csproj");
        var project = File.ReadAllText(projectFile);

        project.Should().Contain(@"Resources\CommandIconsSvg\**\*.svg");
        project.Should().NotContain(@"Resources\CommandIcons\**\*.png");
    }
}
