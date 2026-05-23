using System.IO;
using System.Reflection;
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
    public void CreateCommandIcon_PrefersNativeSvgVariantForRequestedRibbonSlot()
    {
        var method = typeof(RibbonIconFactory).GetMethod(
            "GetSizeSpecificSlugCandidates",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var smallCandidates = ((IEnumerable<string>)method!.Invoke(null, ["paste", 20d, false])!).ToList();
        var largeCandidates = ((IEnumerable<string>)method.Invoke(null, ["paste", 32d, false])!).ToList();

        smallCandidates.Should().StartWith("paste-small");
        smallCandidates.Should().ContainInOrder("paste-small", "paste");
        largeCandidates.Should().StartWith("paste-large");
        largeCandidates.Should().ContainInOrder("paste-large", "paste");
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

    [Fact]
    public void HomeRibbonLargeCommandArtwork_UsesDistinctSvgFiles()
    {
        var iconDirectory = Path.Combine(
            Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "Freexcel.App.Host.csproj"))!,
            "Resources",
            "CommandIconsSvg");

        var homeCommands = new[]
        {
            "paste-large.svg",
            "conditional-formatting-large.svg",
            "format-as-table-large.svg",
            "cell-styles-large.svg",
            "insert-large.svg",
            "delete-large.svg",
            "format-large.svg",
            "autosum-large.svg",
            "fill-large.svg",
            "clear-large.svg",
            "sort-large.svg",
            "find-large.svg"
        };

        var normalizedArtwork = homeCommands
            .Select(fileName =>
            {
                var path = Path.Combine(iconDirectory, fileName);
                File.Exists(path).Should().BeTrue(path);
                var text = File.ReadAllText(path)
                    .ReplaceLineEndings(string.Empty);
                return (fileName, text);
            })
            .ToList();

        normalizedArtwork
            .Select(item => item.text)
            .Distinct(StringComparer.Ordinal)
            .Should()
            .HaveCount(homeCommands.Length);
    }
}
