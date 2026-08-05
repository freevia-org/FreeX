using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Free.Shared.Ribbon.Icons;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonIconFactorySvgTests
{
    [Fact]
    public void RibbonIcon_InheritsSharedWpfRenderer()
    {
        typeof(RibbonIcon).BaseType.Should().Be(typeof(Free.Shared.Ribbon.Wpf.RibbonIcon));
    }

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
    public void RibbonIcon_UsesCommandArtworkForWhiteSidebarGlyphs()
    {
        StaTestRunner.Run(() =>
        {
            var icon = new RibbonIcon
            {
                CommandName = "Open",
                Kind = RibbonCommandIconKind.GetData,
                IconSize = 24,
                Foreground = Brushes.White
            };

            icon.Child.Should().BeOfType<Image>();
        });
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("Account")]
    [InlineData("Options")]
    [InlineData("New")]
    [InlineData("Open")]
    [InlineData("Share")]
    [InlineData("Info")]
    [InlineData("Save")]
    [InlineData("Save As")]
    [InlineData("Print")]
    [InlineData("Export")]
    [InlineData("Close")]
    public void BackstageSidebarCommands_LoadDedicatedSvgArtworkAsWhiteVectorImages(string commandName)
    {
        StaTestRunner.Run(() =>
        {
            var icon = RibbonIconFactory.CreateCommandIcon(
                commandName,
                new RibbonCommandIcon(RibbonCommandIconKind.Generic),
                size: 24,
                Brushes.White);

            var image = icon.Should().BeOfType<Image>().Subject;
            image.Source.Should().BeOfType<DrawingImage>();
            image.Width.Should().Be(24);
            image.Height.Should().Be(24);
        });
    }

    [Theory]
    [InlineData("Pin to list")]
    [InlineData("Unpin from list")]
    public void BackstageRecentPinCommands_LoadDedicatedSvgArtworkAsVectorImages(string commandName)
    {
        StaTestRunner.Run(() =>
        {
            var icon = RibbonIconFactory.CreateCommandIcon(
                commandName,
                new RibbonCommandIcon(RibbonCommandIconKind.Pin),
                size: 24,
                Brushes.Black);

            var image = icon.Should().BeOfType<Image>().Subject;
            image.Source.Should().BeOfType<DrawingImage>();
            image.Width.Should().Be(24);
            image.Height.Should().Be(24);
        });
    }

    [Fact]
    public void BackstageRecentPinButtons_RequestDistinctPinAndUnpinSvgArtwork()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        xaml.Should().Contain("CommandName=\"Pin to list\" Kind=\"Pin\"");
        xaml.Should().Contain("CommandName=\"Unpin from list\" Kind=\"Pin\"");
    }

    [Theory]
    [InlineData("Recommended PivotTables")]
    [InlineData("Recommended Charts")]
    [InlineData("What-If Analysis")]
    [InlineData("Reapply")]
    [InlineData("Add Watch")]
    [InlineData("Delete Watch")]
    [InlineData("Copy Diagnostics")]
    [InlineData("Quick Analysis")]
    [InlineData("Insert Copied Cells")]
    [InlineData("Pick From Drop-down List...")]
    [InlineData("Resolve Comment")]
    [InlineData("Unresolve Comment")]
    [InlineData("Shape Gradient")]
    public void AcceptedIconReviewCommands_LoadDedicatedSvgArtworkAsVectorImages(string commandName)
    {
        StaTestRunner.Run(() =>
        {
            var icon = RibbonIconFactory.CreateCommandIcon(
                commandName,
                new RibbonCommandIcon(RibbonCommandIconKind.Generic),
                size: 32,
                Brushes.Black);

            var image = icon.Should().BeOfType<Image>().Subject;
            image.Source.Should().BeOfType<DrawingImage>();
        });
    }

    [Fact]
    public void CreateCommandIcon_PrefersNativeSvgVariantForRequestedRibbonSlot()
    {
        // The size-specific SVG slug logic was extracted from RibbonIconFactory into the shared
        // Free.Shared.Ribbon.Wpf.SvgCommandIconLoader (signature now (slug, size), no trailing flag).
        var method = typeof(Free.Shared.Ribbon.Wpf.SvgCommandIconLoader).GetMethod(
            "GetSizeSpecificSlugCandidates",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var smallCandidates = ((IEnumerable<string>)method!.Invoke(null, ["paste", 20d])!).ToList();
        var largeCandidates = ((IEnumerable<string>)method.Invoke(null, ["paste", 32d])!).ToList();

        smallCandidates.Should().StartWith("paste-small");
        smallCandidates.Should().ContainInOrder("paste-small", "paste");
        largeCandidates.Should().StartWith("paste-large");
        largeCandidates.Should().ContainInOrder("paste-large", "paste");
    }

    [Theory]
    [InlineData("Sort & Filter", "sort-and-filter", "sort")]
    [InlineData("Find & Select", "find-and-select", "find")]
    [InlineData("Insert Link", "insert-link", "hyperlink")]
    [InlineData("Header & Footer", "header-and-footer", "header-footer")]
    [InlineData("Export PDF/XPS", "export-pdf-xps", "export")]
    [InlineData("Collapse Group", "collapse-group", "hide-detail")]
    [InlineData("Expand Group", "expand-group", "show-detail")]
    [InlineData("Add Watch", "add-watch", "watch-add")]
    [InlineData("Delete Watch", "delete-watch", "watch-delete")]
    [InlineData("Reapply", "reapply", "reapply-filter")]
    [InlineData("Sort A to Z", "sort-a-to-z", "sort-ascending")]
    [InlineData("Sort Z to A", "sort-z-to-a", "sort-descending")]
    [InlineData("100%", "100", "zoom-to-100")]
    [InlineData("Pick From Drop-down List...", "pick-from-drop-down-list", "pick-from-dropdown")]
    public void CommandIconSlugAliases_NormalizePlainAmpersands(
        string commandName,
        string expectedSlug,
        string expectedAlias)
    {
        var slug = RibbonCommandIconPolicy.ToCommandIconSlug(commandName);
        var candidates = RibbonCommandIconPolicy.GetCommandIconSlugCandidates(slug).ToList();

        slug.Should().Be(expectedSlug);
        candidates.Should().ContainInOrder(expectedSlug, expectedAlias);
    }

    [Fact]
    public void CommandIconSlugAliases_TrySharedCanonicalAliasBeforeHistoricalPictureSlug()
    {
        RibbonCommandIconPolicy.GetCommandIconSlugCandidates("pictures")
            .Should().Equal("picture", "pictures");
    }

    [Theory]
    [InlineData("Selection Pane#SelectionPaneBtn_Click", "selection-pane")]
    [InlineData("Remove Duplicates#RemoveDuplicatesBtn_Click", "remove-duplicates")]
    [InlineData("AutoSum#FormulasAutoSumPickerBtn_Click", "autosum")]
    [InlineData("Protect Sheet#ProtectSheetBtn_Click", "protect-sheet")]
    [InlineData("Clear#ClearFilterButton_Click", "clear-filter")]
    public void CommandIconNames_StripHandlerSuffixesBeforeSlugLookup(
        string commandName,
        string expectedSlug)
    {
        var normalized = RibbonCommandIconPolicy.NormalizeCommandIconName(commandName);
        var slug = RibbonCommandIconPolicy.ToCommandIconSlug(normalized);

        normalized.Should().NotContain("#");
        slug.Should().Be(expectedSlug);
    }

    [Theory]
    [InlineData("Selection Pane#SelectionPaneBtn_Click")]
    [InlineData("Remove Duplicates#RemoveDuplicatesBtn_Click")]
    [InlineData("AutoSum#FormulasAutoSumPickerBtn_Click")]
    [InlineData("Protect Sheet#ProtectSheetBtn_Click")]
    [InlineData("Clear#ClearFilterButton_Click")]
    public void CreateCommandIcon_LoadsDedicatedSvgArtworkForHandlerSuffixedCommandIds(string commandName)
    {
        StaTestRunner.Run(() =>
        {
            var icon = RibbonIconFactory.CreateCommandIcon(
                commandName,
                new RibbonCommandIcon(RibbonCommandIconKind.Generic),
                size: 32,
                Brushes.Black);

            var image = icon.Should().BeOfType<Image>().Subject;
            image.Source.Should().BeOfType<DrawingImage>();
        });
    }

    [Theory]
    [InlineData("Advanced", "advanced-filter")]
    [InlineData("Page Setup dialog", "page-setup")]
    [InlineData("View Gridlines", "gridlines")]
    [InlineData("View Headings", "headings")]
    public void CommandIconSlugAliases_MapDeclarativeRibbonLabelsToExistingSvgArtwork(
        string commandName,
        string expectedAlias)
    {
        var slug = RibbonCommandIconPolicy.ToCommandIconSlug(commandName);
        var candidates = RibbonCommandIconPolicy.GetCommandIconSlugCandidates(slug).ToList();

        candidates.Should().Contain(expectedAlias);
    }

    [Theory]
    [InlineData("Allow Edit Ranges")]
    [InlineData("Date and Time")]
    [InlineData("Lookup and Reference")]
    [InlineData("Math and Trig")]
    public void CreateCommandIcon_ResolvesRemovedShortNamesThroughCanonicalSvgAliases(string commandName)
    {
        StaTestRunner.Run(() =>
        {
            var icon = RibbonIconFactory.CreateCommandIcon(
                commandName,
                new RibbonCommandIcon(RibbonCommandIconKind.Generic),
                size: 32,
                Brushes.Black);

            icon.Should().BeOfType<Image>().Which.Source.Should().BeOfType<DrawingImage>();
        });
    }

    [Fact]
    public void AppHostProject_CopiesSvgCommandIconsInsteadOfPreRenderedPngs()
    {
        // After the declarative-ribbon cutover the per-command SVG glyphs live in
        // FreeX.Ribbon.Definitions (shared with the Avalonia app) and are copied to the WPF host's
        // output transitively at Resources\CommandIconsSvg\. Assert the shared project still ships the
        // SVG glob, and that neither it nor the host project resurrect the old pre-rendered PNG assets.
        var ribbonDefinitions = DialogSourceTestSupport.ReadRibbonDefinitionFile("FreeX.Ribbon.Definitions.csproj");
        var host = DialogSourceTestSupport.ReadHostSourceFile("FreeX.App.Host.csproj");

        ribbonDefinitions.Should().Contain(@"Resources\CommandIconsSvg\**\*.svg");
        ribbonDefinitions.Should().NotContain(@"Resources\CommandIcons\**\*.png");
        host.Should().NotContain(@"Resources\CommandIcons\**\*.png");
    }

    [Fact]
    public void HomeRibbonLargeCommandArtwork_UsesDistinctSvgFiles()
    {
        var iconDirectory = Path.Combine(
            DialogSourceTestSupport.FindRibbonDefinitionDirectory("FreeX.Ribbon.Definitions.csproj"),
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
                var text = DialogSourceTestSupport.ReadRibbonDefinitionFile("Resources", "CommandIconsSvg", fileName)
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
    public void HelpIcon_UsesCenteredPathQuestionMark()
    {
        var svg = DialogSourceTestSupport.ReadRibbonDefinitionFile("Resources", "CommandIconsSvg", "help.svg");

        svg.Should().Contain("width=\"32\"");
        svg.Should().Contain("viewBox=\"0 0 32 32\"");
        svg.Should().Contain("cx=\"16\" cy=\"16\"");
        svg.Should().NotContain("<text");
        svg.Should().Contain("aria-label=\"Help\"");
    }

    [Fact]
    public void SizeSpecificSvgCommandIcons_DoNotUseDocumentPlaceholderArtwork()
    {
        var iconDirectory = Path.Combine(
            DialogSourceTestSupport.FindRibbonDefinitionDirectory("FreeX.Ribbon.Definitions.csproj"),
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
            DialogSourceTestSupport.FindRibbonDefinitionDirectory("FreeX.Ribbon.Definitions.csproj"),
            "Resources",
            "CommandIconsSvg");

        var allowedSizeSpecificFiles = new[]
        {
            "align-left-small.svg",
            "align-right-small.svg",
            "bottom-align-small.svg",
            "center-small.svg",
            "decrease-indent-small.svg",
            "distributed-justify-small.svg",
            "increase-indent-small.svg",
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
            "only alignment and indentation icons should need size-specific SVGs, so their 1 px rule lines do not get fractionally scaled");

        foreach (var fileName in allowedSizeSpecificFiles)
        {
            var smallText = DialogSourceTestSupport.ReadRibbonDefinitionFile("Resources", "CommandIconsSvg", fileName);
            var baseFileName = fileName.Replace("-small.svg", ".svg", StringComparison.OrdinalIgnoreCase);
            var baseText = DialogSourceTestSupport.ReadRibbonDefinitionFile("Resources", "CommandIconsSvg", baseFileName);

            smallText.Should().Contain("height=\"1\"");
            smallText.Should().NotContain("height=\"2\"");
            baseText.Should().Contain("height=\"1\"");
            baseText.Should().NotContain("height=\"2\"");
        }
    }

    [Fact]
    public void CommandIconAssets_DoNotContainEmptyShellSvgs()
    {
        var iconDirectory = Path.Combine(
            DialogSourceTestSupport.FindRibbonDefinitionDirectory("FreeX.Ribbon.Definitions.csproj"),
            "Resources",
            "CommandIconsSvg");

        var emptyShells = Directory
            .EnumerateFiles(iconDirectory, "*.svg")
            .Where(path => File.ReadAllText(path).TrimEnd().EndsWith("/>", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        emptyShells.Should().BeEmpty("visible command SVG assets should render actual geometry, not blank placeholders");
    }

    [Fact]
    public void RibbonIconVariantGenerator_ProcessesBaseIconsDeterministicallyAndReportsActualCount()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "generate-ribbon-icon-variants.ps1");

        script.Should().Contain("Sort-Object Name");
        script.Should().Contain("$generatedCount = 0");
        script.Should().Contain("$generatedCount++");
        script.Should().Contain("Generated $generatedCount native ribbon SVG variants.");
        script.Should().NotContain("Generated $($baseFiles.Count * 2) native ribbon SVG variants.");
        script.Any(ch => ch > 0x7f).Should().BeFalse(
            "the generator must stay ASCII-safe for Windows PowerShell source decoding");
    }

    [Theory]
    [InlineData("get-add-ins.svg")]
    [InlineData("my-add-ins.svg")]
    [InlineData("draw-with-touch.svg")]
    [InlineData("lasso-select.svg")]
    [InlineData("page-orientation.svg")]
    [InlineData("stocks.svg")]
    [InlineData("geography.svg")]
    [InlineData("queries-connections.svg")]
    [InlineData("macros.svg")]
    [InlineData("contact-support.svg")]
    [InlineData("what-s-new.svg")]
    [InlineData("check-for-updates.svg")]
    [InlineData("selection-pane.svg")]
    [InlineData("clear-filter.svg")]
    [InlineData("recommended-pivottables.svg")]
    [InlineData("recommended-charts.svg")]
    [InlineData("what-if-analysis.svg")]
    [InlineData("reapply-filter.svg")]
    [InlineData("watch-add.svg")]
    [InlineData("watch-delete.svg")]
    [InlineData("copy-diagnostics.svg")]
    [InlineData("quick-analysis.svg")]
    [InlineData("insert-copied-cells.svg")]
    [InlineData("pick-from-dropdown.svg")]
    [InlineData("resolve-comment.svg")]
    [InlineData("unresolve-comment.svg")]
    [InlineData("shape-gradient.svg")]
    public void AcceptedIconReviewCommands_HaveDedicatedNonblankSvgArtwork(string fileName)
    {
        var svg = DialogSourceTestSupport.ReadRibbonDefinitionFile("Resources", "CommandIconsSvg", fileName);

        svg.Should().Contain("<svg");
        svg.Should().MatchRegex("<(path|rect|circle|ellipse|line)\\b");
        svg.Should().NotEndWith("/>");
    }
}
