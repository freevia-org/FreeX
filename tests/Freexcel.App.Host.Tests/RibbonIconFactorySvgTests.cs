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
            "paste.svg",
            "conditional-formatting.svg",
            "format-as-table.svg",
            "cell-styles.svg",
            "insert.svg",
            "delete.svg",
            "format.svg",
            "autosum.svg",
            "fill.svg",
            "clear.svg",
            "sort.svg",
            "find.svg"
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

    [Fact]
    public void SizeSpecificSvgCommandIcons_DoNotUseDocumentPlaceholderArtwork()
    {
        var iconDirectory = Path.Combine(
            Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "Freexcel.App.Host.csproj"))!,
            "Resources",
            "CommandIconsSvg");

        var placeholderFragments = new[]
        {
            "H13 L16 5.5 V17 H5 Z M13 2.5 V5.5 H16",
            "H20.8 L25.6 8.8 V27.2 H8 Z M20.8 4 V8.8 H25.6"
        };

        var placeholderFiles = Directory
            .EnumerateFiles(iconDirectory, "*.svg")
            .Where(path => path.EndsWith("-small.svg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("-large.svg", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return placeholderFragments.Any(fragment => text.Contains(fragment, StringComparison.Ordinal));
            })
            .Select(Path.GetFileName)
            .ToList();

        placeholderFiles.Should().BeEmpty(
            "size-specific ribbon icons should be deliberately drawn or absent so the base SVG can be used as the fallback");
    }

    [Fact]
    public void CommandIconAssets_OnlyUseSizeVariantsForPixelCrispAlignmentLines()
    {
        var iconDirectory = Path.Combine(
            Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "Freexcel.App.Host.csproj"))!,
            "Resources",
            "CommandIconsSvg");

        var allowedSizeSpecificFiles = new[]
        {
            "align-left-small.svg",
            "align-right-small.svg",
            "bottom-align-small.svg",
            "center-small.svg",
            "distributed-justify-small.svg",
            "middle-align-small.svg",
            "top-align-small.svg"
        };

        var sizeSpecificFiles = Directory
            .EnumerateFiles(iconDirectory, "*.svg")
            .Where(path => path.EndsWith("-small.svg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("-large.svg", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        sizeSpecificFiles.Should().Equal(allowedSizeSpecificFiles,
            "only alignment icons should need size-specific SVGs, so their 1 px rule lines do not get fractionally scaled");

        foreach (var fileName in allowedSizeSpecificFiles)
        {
            var smallText = File.ReadAllText(Path.Combine(iconDirectory, fileName));
            var baseFileName = fileName.Replace("-small.svg", ".svg", StringComparison.OrdinalIgnoreCase);
            var baseText = File.ReadAllText(Path.Combine(iconDirectory, baseFileName));

            smallText.Should().Contain("height=\"1\"");
            smallText.Should().NotContain("height=\"2\"");
            baseText.Should().Contain("height=\"1\"");
            baseText.Should().NotContain("height=\"2\"");
        }
    }
}
