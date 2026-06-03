using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void ConditionalFormattingTopBottomRules_ExposeExcelParityMenuChoices()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var menuItems = document
            .Descendants(presentation + "MenuItem")
            .Select(element => new
            {
                Header = LocalizedAttribute(element, "Header"),
                Click = element.Attribute("Click")?.Value
            })
            .ToList();

        menuItems.Should().Contain(item => item.Header == "Top 10%..." && item.Click == "CfTop10PercentMenuItem_Click");
        menuItems.Should().Contain(item => item.Header == "Bottom 10%..." && item.Click == "CfBottom10PercentMenuItem_Click");
        menuItems.Should().Contain(item => item.Header == "Below Average..." && item.Click == "CfBelowAvgMenuItem_Click");
    }

    [Fact]
    public void DataTab_ExposesFlashFillCommand()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var dataTab = document
            .Descendants(presentation + "TabItem")
            .Single(element => LocalizedAttribute(element, "Header") == "Data");

        var flashFillButton = dataTab
            .Descendants(presentation + "Button")
            .Single(element => LocalizedAttribute(element, local + "RibbonTooltip.Title") == "Flash Fill");

        flashFillButton.Attribute("Click")?.Value.Should().Be("FlashFillMenuItem_Click");
        flashFillButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("FF");
        LocalizedAttribute(flashFillButton, local + "RibbonTooltip.Description").Should().Contain("examples");
    }

    [Fact]
    public void CellStylesGallery_ExposesExpandedPresetLabelsAndRoutesThroughPlanner()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var cellStylesMenu = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Cell Styles")
            .Descendants(presentation + "ContextMenu")
            .Single();

        var labels = cellStylesMenu
            .Elements(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .ToList();

        labels.Should().Contain([
            "Normal",
            "Good",
            "Bad",
            "Neutral",
            "Input",
            "Output",
            "Calculation",
            "Check Cell",
            "Linked Cell",
            "Explanatory Text",
            "Heading 1",
            "Heading 2",
            "Note",
            "Warning Text",
            "Total",
            "20% - Accent 1",
            "20% - Accent 2",
            "20% - Accent 3",
            "20% - Accent 4",
            "20% - Accent 5",
            "20% - Accent 6",
            "40% - Accent 1",
            "40% - Accent 2",
            "40% - Accent 3",
            "40% - Accent 4",
            "40% - Accent 5",
            "40% - Accent 6",
            "60% - Accent 1",
            "60% - Accent 2",
            "60% - Accent 3",
            "60% - Accent 4",
            "60% - Accent 5",
            "60% - Accent 6"
        ]);

        source.Should().Contain("ApplyCellStylePreset(CellStylePreset preset)");
        source.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme)");
        var menuItemsByHeader = cellStylesMenu
            .Elements(presentation + "MenuItem")
            .ToDictionary(
                item => LocalizedAttribute(item, "Header") ?? string.Empty,
                item => item.Attribute("Click")?.Value ?? string.Empty);

        foreach (var preset in Enum.GetValues<CellStylePreset>())
        {
            var header = CellStylePresetHeader(preset);
            var clickHandler = menuItemsByHeader[header];

            clickHandler.Should().NotBeNullOrWhiteSpace($"{preset} must have a Cell Styles menu route");
            source.Should().Contain($"private void {clickHandler}(object sender, RoutedEventArgs e)");
            source.Should().Contain($"=> ApplyCellStylePreset(CellStylePreset.{preset});");
        }

        source.Should().NotContain("CellStyleGoodMenuItem_Click(object sender, RoutedEventArgs e)\r\n        => ApplyStyleDiff(new StyleDiff");
    }

    [Fact]
    public void ConditionalFormattingIconSets_ExposeGroupedPresetGalleryAndMoreRules()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var iconSetsMenu = document
            .Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Icon Sets");

        iconSetsMenu.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("I");
        iconSetsMenu.Elements(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should()
            .Contain(["More Rules..."]);

        iconSetsMenu.Elements(presentation + "MenuItem")
            .Where(item => item.Attribute("Tag") is null)
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should()
            .NotContain(["Directional", "Shapes", "Indicators", "Ratings"]);

        iconSetsMenu.Descendants(presentation + "MenuItem")
            .Where(item => item.Attribute("Tag") is not null)
            .Select(item => item.Attribute("Tag")!.Value)
            .Should()
            .Contain(["3Arrows", "3TrafficLights1", "3Flags", "4Rating", "5Boxes"]);

        source.Should().Contain("CfIconSetPresetMenuItem_Click");
        source.Should().Contain("ApplyIconSetPreset");
        source.Should().Contain("ConditionalFormatIconSetPlanner.CreateRule");
    }

    [Fact]
    public void RibbonCheckBoxCommands_HaveTooltipTitlesDescriptionsAndKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "CheckBox")
            .Where(checkBox =>
                checkBox.Attribute("Click") is not null ||
                checkBox.Attribute("Checked") is not null ||
                checkBox.Attribute("Unchecked") is not null)
            .Where(checkBox =>
                checkBox.Attribute(local + "RibbonTooltip.Title") is null ||
                checkBox.Attribute(local + "RibbonTooltip.Description") is null ||
                checkBox.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(checkBox => LocalizedAttribute(checkBox, "Content") ?? checkBox.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("visible ribbon checkbox commands should expose the same Excel-style tooltip and keytip metadata as button commands");
    }

    [Fact]
    public void RibbonComboBoxCommands_HaveAccessibleNamesMatchingTooltipTitles()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "ComboBox")
            .Where(comboBox => comboBox.Attribute(local + "RibbonTooltip.Title") is not null)
            .Where(comboBox =>
                LocalizedAttribute(comboBox, "AutomationProperties.Name") !=
                LocalizedAttribute(comboBox, local + "RibbonTooltip.Title")!)
            .Select(comboBox => LocalizedAttribute(comboBox, local + "RibbonTooltip.Title")!)
            .ToList();

        missing.Should().BeEmpty("focusable ribbon combo box commands should announce the same command name shown in Excel-style tooltips");
    }

    [Fact]
    public void DataTabCommandTooltips_DoNotAdvertiseExcludedConnectors()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        static string DescriptionFor(
            XDocument document,
            XNamespace presentation,
            XNamespace local,
            string title) =>
            LocalizedAttribute(
                document.Descendants(presentation + "Button")
                    .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == title),
                local + "RibbonTooltip.Description")!;

        var getData = DescriptionFor(document, presentation, local, "Get Data");
        var refreshAll = DescriptionFor(document, presentation, local, "Refresh All");

        getData.Should().Contain("local CSV file");
        getData.Should().Contain("excluded");
        refreshAll.Should().Contain("Recalculate formulas");
        refreshAll.Should().Contain("External data connections");
        refreshAll.Should().Contain("excluded");
    }

    [Fact]
    public void HomePasteButton_ExposesPasteSpecialMenuChoices()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var pasteButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Paste");

        var headers = pasteButton
            .Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToList();

        headers.Should().ContainInOrder([
            "Paste",
            "Values",
            "Formulas",
            "Formatting",
            "Transpose",
            "Paste Special..."
        ]);

        pasteButton.Descendants(presentation + "MenuItem")
            .Should().OnlyContain(item => item.Attribute(local + "RibbonTooltip.KeyTip") != null);
    }

    [Theory]
    [InlineData("SortAscButton_Click", "SortAscending")]
    [InlineData("SortDescButton_Click", "SortDescending")]
    [InlineData("FilterButton_Click", "Filter")]
    [InlineData("ClearFilterButton_Click", "Clear")]
    [InlineData("AdvancedFilterBtn_Click", "Filter")]
    public void DataSortFilterCommands_UseVectorRibbonIconsInsteadOfTextPlaceholders(
        string clickHandler,
        string expectedIconKind)
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var button = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == clickHandler);

        button
            .Descendants(local + "RibbonIcon")
            .Single()
            .Attribute("Kind")?.Value
            .Should().Be(expectedIconKind);

        button
            .Descendants(presentation + "TextBlock")
            .Where(element => element.Attribute("Tag")?.Value == "RibbonIcon")
            .Should()
            .BeEmpty("ribbon visuals should use vector icon controls instead of text placeholders");
    }

    [Fact]
    public void MainRibbon_DoesNotUseTextBlockIconPlaceholders()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var placeholders = document
            .Descendants(presentation + "TextBlock")
            .Where(element => element.Attribute("Tag")?.Value == "RibbonIcon")
            .Select(element => LocalizedAttribute(element, "Text") ?? "<unnamed>")
            .ToList();

        placeholders.Should().BeEmpty("the ribbon screenshot sweep should render actual SVG/vector icons, not text stand-ins");
    }

    [Fact]
    public void NestedRibbonMenuItems_HaveStagedKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "MenuItem")
            .Where(menuItem => menuItem.Descendants(presentation + "MenuItem").Any())
            .SelectMany(menuItem => menuItem
                .Elements(presentation + "MenuItem")
                .Where(child => child.Attribute(local + "RibbonTooltip.KeyTip") is null)
                .Select(child => $"{LocalizedAttribute(menuItem, "Header")}:{LocalizedAttribute(child, "Header")}"))
            .ToList();

        missing.Should().BeEmpty("nested ribbon menu choices should be reachable through staged Alt keytips");
    }

    [Fact]
    public void RibbonMenus_DoNotReuseKeyTipsWithinTheSameMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var duplicates = document
            .Descendants(presentation + "ContextMenu")
            .Concat(document.Descendants(presentation + "MenuItem")
                .Where(menuItem => menuItem.Elements(presentation + "MenuItem").Any()))
            .SelectMany(menu =>
                menu.Elements(presentation + "MenuItem")
                    .Where(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip") is not null)
                    .GroupBy(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip")!.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{LocalizedAttribute(menu, "Header") ?? "ContextMenu"}:{group.Key}"))
            .ToList();

        duplicates.Should().BeEmpty("menu-level keytips must be unique for deterministic staged Alt routing");
    }

    [Fact]
    public void RibbonMenus_DoNotUseKeyTipPrefixesWithinTheSameMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var collisions = document
            .Descendants(presentation + "ContextMenu")
            .Concat(document.Descendants(presentation + "MenuItem")
                .Where(menuItem => menuItem.Elements(presentation + "MenuItem").Any()))
            .SelectMany(menu =>
            {
                var items = menu.Elements(presentation + "MenuItem")
                    .Select(item => new
                    {
                        Header = LocalizedAttribute(item, "Header") ?? item.Attribute("Click")?.Value ?? "MenuItem",
                        KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.KeyTip))
                    .ToList();

                return items.SelectMany(item => items
                    .Where(other => !ReferenceEquals(item, other))
                    .Where(other => other.KeyTip!.StartsWith(item.KeyTip!, StringComparison.OrdinalIgnoreCase))
                    .Select(other => $"{LocalizedAttribute(menu, "Header") ?? "ContextMenu"}:{item.Header}:{item.KeyTip} prefixes {other.Header}:{other.KeyTip}"));
            })
            .ToList();

        collisions.Should().BeEmpty("menu-level keytips must not shadow longer sibling keytips");
    }

    [Fact]
    public void PageLayoutBreaksButton_OpensExcelStyleBreaksMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var breaksButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Breaks");

        breaksButton.Attribute("Click")?.Value.Should().Be("PageBreaksBtn_Click");
        breaksButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("BK");
        breaksButton.Descendants(presentation + "MenuItem")
            .Select(item => new
            {
                Header = LocalizedAttribute(item, "Header"),
                KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                Click = item.Attribute("Click")?.Value
            })
            .Should()
            .Equal([
                new { Header = (string?)"Insert Page Break", KeyTip = (string?)"I", Click = (string?)"InsertPageBreakMenuItem_Click" },
                new { Header = (string?)"Remove Page Break", KeyTip = (string?)"R", Click = (string?)"RemovePageBreakMenuItem_Click" },
                new { Header = (string?)"Reset All Page Breaks", KeyTip = (string?)"A", Click = (string?)"ResetAllPageBreaksMenuItem_Click" }
            ]);
    }

    [Fact]
    public void ViewWindowCommands_AreAllLiveWithDedicatedHandlersOnTheRibbon()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var windowGroup = document
            .Descendants(presentation + "Grid")
            .Single(grid => grid.Attribute(local + "RibbonMetadata.CatalogId")?.Value == "ViewWindowGroup");

        var windowCommands = windowGroup
            .Descendants()
            .Where(element => element.Name == presentation + "Button" || element.Name == presentation + "ToggleButton")
            .Where(button => CommandName(button, local) is not null)
            .ToDictionary(button => CommandName(button, local)!, button => button.Attribute("Click")?.Value);

        // The previously-removed commands are now live: each must be present with its dedicated handler.
        windowCommands.Should().Contain(new KeyValuePair<string, string?>("Hide", "ViewHideWindowBtn_Click"));
        windowCommands.Should().Contain(new KeyValuePair<string, string?>("Unhide", "ViewUnhideWindowBtn_Click"));
        windowCommands.Should().Contain(new KeyValuePair<string, string?>("Reset Window Position", "ViewResetWindowPositionBtn_Click"));
        windowCommands.Should().Contain(new KeyValuePair<string, string?>("View Side by Side", "ViewSideBySideBtn_Click"));
        windowCommands.Should().Contain(new KeyValuePair<string, string?>("Synchronous Scrolling", "ViewSynchronousScrollingBtn_Click"));

        document.Descendants()
            .Select(element => element.Attribute("Click")?.Value)
            .Should()
            .NotContain("ViewWindowCommandBtn_Click");
    }

    [Fact]
    public void ViewWindowLiveCommands_RouteEveryWindowCommandToRegistryAndPlannerBackedHandlers()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var registryHandlers = new[]
        {
            "ViewNewWindowBtn_Click",
            "ViewSwitchWindowsBtn_Click",
            "ViewHideWindowBtn_Click",
            "ViewUnhideWindowBtn_Click",
            "ViewResetWindowPositionBtn_Click",
            "ViewSideBySideBtn_Click",
            "ViewSynchronousScrollingBtn_Click",
        };

        var liveWindowCommands = document
            .Descendants()
            .Where(element => element.Name == presentation + "Button" || element.Name == presentation + "ToggleButton")
            .Where(button => registryHandlers.Contains(button.Attribute("Click")?.Value))
            .Select(button => new
            {
                Title = LocalizedAttribute(button, local + "RibbonTooltip.Title"),
                KeyTip = button.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                Click = button.Attribute("Click")?.Value,
                Description = LocalizedAttribute(button, local + "RibbonTooltip.Description")
            })
            .ToList();

        liveWindowCommands.Should().BeEquivalentTo(new[]
        {
            new
            {
                Title = (string?)"New Window",
                KeyTip = (string?)"NW",
                Click = (string?)"ViewNewWindowBtn_Click",
                Description = (string?)"Open another live window for this workbook."
            },
            new
            {
                Title = (string?)"Switch Windows",
                KeyTip = (string?)"W",
                Click = (string?)"ViewSwitchWindowsBtn_Click",
                Description = (string?)"Switch to another visible workbook window."
            },
            new
            {
                Title = (string?)"Hide",
                KeyTip = (string?)"H",
                Click = (string?)"ViewHideWindowBtn_Click",
                Description = (string?)"Hide this workbook window from view."
            },
            new
            {
                Title = (string?)"Unhide",
                KeyTip = (string?)"U",
                Click = (string?)"ViewUnhideWindowBtn_Click",
                Description = (string?)"Restore a hidden workbook window."
            },
            new
            {
                Title = (string?)"Reset Window Position",
                KeyTip = (string?)"RP",
                Click = (string?)"ViewResetWindowPositionBtn_Click",
                Description = (string?)"Reset this window to a standard size and position."
            },
            new
            {
                Title = (string?)"View Side by Side",
                KeyTip = (string?)"B",
                Click = (string?)"ViewSideBySideBtn_Click",
                Description = (string?)"Tile this window and another side by side to compare them."
            },
            new
            {
                Title = (string?)"Synchronous Scrolling",
                KeyTip = (string?)"SS",
                Click = (string?)"ViewSynchronousScrollingBtn_Click",
                Description = (string?)"Scroll both side-by-side windows together."
            }
        });
    }

    [Fact]
    public void ViewWindowState_UsesLocalizedLiveCommandTooltips()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));

        source.Should().Contain("UiText.Get(\"MainWindow_TooltipDescription_OpenAnotherLiveWindowForThisWorkbook\")");
        source.Should().Contain("UiText.Get(canSwitchWindows");
        source.Should().Contain("MainWindow_TooltipDescription_SwitchToAnotherVisibleWorkbookWindow");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableSwitchWindowsRequiresSecondVisibleWindow");

        // Hide / Unhide / Reset / Side by Side / Synchronous Scrolling state uses localized live tooltips too.
        source.Should().Contain("MainWindow_TooltipDescription_HideThisWorkbookWindowFromView");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableHideRequiresSecondVisibleWindow");
        source.Should().Contain("MainWindow_TooltipDescription_RestoreAHiddenWorkbookWindow");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableUnhideRequiresAHiddenWindow");
        source.Should().Contain("MainWindow_TooltipDescription_ResetThisWindowToAStandardSizeAndPosition");
        source.Should().Contain("MainWindow_TooltipDescription_TileThisWindowAndAnotherSideBySideToCompareThem");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableViewSideBySideRequiresSecondVisibleWindow");
        source.Should().Contain("MainWindow_TooltipDescription_ScrollBothSideBySideWindowsTogether");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableSynchronousScrollingRequiresViewSideBySide");

        source.Should().NotContain("ViewWindowCommandPlanner");
        source.Should().NotContain("ViewWindowCommandBtn_Click");
    }

    [Fact]
    public void PageLayoutThemesButton_OpensWorkbookThemeMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var themesButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Themes");

        themesButton.Attribute("Click")?.Value.Should().Be("ThemeBtn_Click");
        LocalizedAttribute(themesButton, local + "RibbonTooltip.Description").Should().NotContain("Deferred:");
        themesButton.Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should().Equal("Office", "FreeX Colorful", "Grayscale", "Customize...");
        themesButton.Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Customize...")
            .Attribute("Click")?.Value.Should().Be("ThemeCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeColorsButton_OpensColorSchemeMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var colorsButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Theme Colors");

        colorsButton.Attribute("Click")?.Value.Should().Be("ThemeColorsBtn_Click");
        LocalizedAttribute(colorsButton, local + "RibbonTooltip.Description").Should().NotContain("Deferred:");
        colorsButton.Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should().Equal("Office", "FreeX Colorful", "Grayscale", "Customize Colors...");
        colorsButton.Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Customize Colors...")
            .Attribute("Click")?.Value.Should().Be("ThemeColorsCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeFontsButton_OpensFontPairMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var fontsButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Theme Fonts");

        fontsButton.Attribute("Click")?.Value.Should().Be("ThemeFontsBtn_Click");
        LocalizedAttribute(fontsButton, local + "RibbonTooltip.Description").Should().NotContain("Deferred:");
        fontsButton.Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should().Equal("Office", "Arial", "Times New Roman", "Customize Fonts...");
        fontsButton.Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Customize Fonts...")
            .Attribute("Click")?.Value.Should().Be("ThemeFontsCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeEffectsButton_OpensEffectSetMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var effectsButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Theme Effects");

        effectsButton.Attribute("Click")?.Value.Should().Be("ThemeEffectsBtn_Click");
        LocalizedAttribute(effectsButton, local + "RibbonTooltip.Description").Should().NotContain("Deferred:");
        effectsButton.Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should().Equal("Office", "Subtle", "Refined", "Customize Effects...");
        effectsButton.Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Customize Effects...")
            .Attribute("Click")?.Value.Should().Be("ThemeEffectsCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeCommands_ExposeStableAutomationMetadata()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertThemeButton(document, local, presentation, "Themes", "PageLayoutThemesButton", "Open workbook theme presets and customization.");
        AssertThemeButton(document, local, presentation, "Theme Colors", "PageLayoutThemeColorsButton", "Open workbook theme color presets and customization.");
        AssertThemeButton(document, local, presentation, "Theme Fonts", "PageLayoutThemeFontsButton", "Open workbook theme font presets and customization.");
        AssertThemeButton(document, local, presentation, "Theme Effects", "PageLayoutThemeEffectsButton", "Open workbook theme effect presets and customization.");

        var expectedMenuItems = new (string Header, string AutomationName, string AutomationId)[]
        {
            ("Office", "Office theme", "PageLayoutThemeOfficeMenuItem"),
            ("FreeX Colorful", "FreeX Colorful theme", "PageLayoutThemeColorfulMenuItem"),
            ("Grayscale", "Grayscale theme", "PageLayoutThemeGrayscaleMenuItem"),
            ("Customize...", "Customize theme", "PageLayoutThemeCustomizeMenuItem"),
            ("Office", "Office theme colors", "PageLayoutThemeColorsOfficeMenuItem"),
            ("FreeX Colorful", "FreeX Colorful theme colors", "PageLayoutThemeColorsColorfulMenuItem"),
            ("Grayscale", "Grayscale theme colors", "PageLayoutThemeColorsGrayscaleMenuItem"),
            ("Customize Colors...", "Customize theme colors", "PageLayoutThemeColorsCustomizeMenuItem"),
            ("Office", "Office theme fonts", "PageLayoutThemeFontsOfficeMenuItem"),
            ("Arial", "Arial theme fonts", "PageLayoutThemeFontsArialMenuItem"),
            ("Times New Roman", "Times New Roman theme fonts", "PageLayoutThemeFontsTimesMenuItem"),
            ("Customize Fonts...", "Customize theme fonts", "PageLayoutThemeFontsCustomizeMenuItem"),
            ("Office", "Office theme effects", "PageLayoutThemeEffectsOfficeMenuItem"),
            ("Subtle", "Subtle theme effects", "PageLayoutThemeEffectsSubtleMenuItem"),
            ("Refined", "Refined theme effects", "PageLayoutThemeEffectsRefinedMenuItem"),
            ("Customize Effects...", "Customize theme effects", "PageLayoutThemeEffectsCustomizeMenuItem")
        };

        foreach (var expected in expectedMenuItems)
        {
            var menuItem = document
                .Descendants(presentation + "MenuItem")
                .Single(item =>
                    LocalizedAttribute(item, "Header") == expected.Header &&
                    item.Attribute("AutomationProperties.AutomationId")?.Value == expected.AutomationId);

            LocalizedAttribute(menuItem, "AutomationProperties.Name").Should().Be(expected.AutomationName);
            LocalizedAttribute(menuItem, "AutomationProperties.HelpText").Should().NotBeNullOrWhiteSpace();
        }

        static void AssertThemeButton(
            XDocument document,
            XNamespace local,
            XNamespace presentation,
            string tooltipTitle,
            string automationId,
            string helpText)
        {
            var button = document
                .Descendants(presentation + "Button")
                .Single(element => LocalizedAttribute(element, local + "RibbonTooltip.Title") == tooltipTitle);

            LocalizedAttribute(button, "AutomationProperties.Name").Should().Be(tooltipTitle);
            button.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be(automationId);
            LocalizedAttribute(button, "AutomationProperties.HelpText").Should().Be(helpText);
        }
    }

    [Fact]
    public void DrawFormatCropGradientEffectsButtons_ExposeAccessibleCommandsAndMenus()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var cropButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "PictureCropBtn_Click");
        var gradientButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "ObjectGradientBtn_Click");
        var effectsButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "ObjectEffectsBtn_Click");

        cropButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("C");
        cropButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"DrawCropPictureButton\"");
        gradientButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("G");
        gradientButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"DrawShapeGradientButton\"");
        effectsButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("FX");
        effectsButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"DrawShapeEffectsButton\"");

        var cropMenuItems = cropButton
            .Descendants(presentation + "MenuItem")
            .Select(item => new
            {
                Header = LocalizedAttribute(item, "Header"),
                KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                Click = item.Attribute("Click")?.Value,
                Markup = item.ToString()
            })
            .ToList();

        cropMenuItems.Should().Contain(item =>
            item.Header == "Crop..." &&
            item.KeyTip == "C" &&
            item.Click == "PictureCropDialogMenuItem_Click" &&
            item.Markup.Contains("AutomationProperties.AutomationId=\"DrawCropPictureMenuItem\""));
        cropMenuItems.Should().Contain(item =>
            item.Header == "Reset Crop" &&
            item.KeyTip == "R" &&
            item.Click == "PictureResetCropMenuItem_Click" &&
            item.Markup.Contains("AutomationProperties.AutomationId=\"DrawResetPictureCropMenuItem\""));
    }
}
