using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowAdaptiveRibbonTests
{
    [Fact]
    public void HomeRibbon_CollapsesGroupsIntoGroupButtonsAtNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(220);

            harness.CollapsedRibbonGroupNames.Should().Contain("Editing", harness.DebugRibbonChildren);
            harness.CollapsedRibbonGroupMenus.Should().Contain(menu => menu.Items.Count > 0);
            harness.CollapsedMenuHeaders("Editing").Should().Contain(["AutoSum", "Fill", "Clear", "Sort & Filter", "Find & Select"]);
        });
    }

    [Fact]
    public void HomeRibbon_KeepsPrimaryCommandsExpandedAtNormalNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(900);

            harness.CollapsedRibbonGroupNames.Should().NotContain("Clipboard", harness.DebugRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Paste", "Cut", "Copy"],
                "Excel keeps the primary Clipboard commands expanded at normal narrow window widths and collapses lower-priority groups first");
        });
    }

    [Fact]
    public void HomeRibbon_CollapsesEditingBeforeLabelsClipAtWideWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(1465);

            harness.CollapsedRibbonGroupNames.Should().Contain("Editing", harness.DebugRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().NotContain("Find & Select", harness.DebugRibbonChildren);
        });
    }

    [Fact]
    public void HomeRibbon_KeepsCellsVisibleBeforeEditingAtCommonWideWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(1366);
            if (!harness.CanUseRequestedRibbonWidth(1366))
                return;

            harness.CollapsedRibbonGroupNames.Should().NotContain("Cells", harness.DebugRibbonChildren);
            harness.CollapsedRibbonGroupNames.Should().Contain("Editing", harness.DebugRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Insert", "Delete", "Format"],
                "Excel keeps the Cells group visible at common wide widths and collapses Editing first");
        });
    }

    [Fact]
    public void FormulasRibbon_KeepsFunctionLibraryExpandedAtNormalWideWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Formulas", 1465);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Function Library", harness.DebugActiveRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Insert Function", "AutoSum"],
                "Excel keeps the primary Formulas command block available before collapsing lower-priority groups");
        });
    }

    [Fact]
    public void IconOnlyRibbonCommandsRemainCenterAligned()
    {
        StaTestRunner.Run(() =>
        {
            var label = new TextBlock { Text = "Paste", Tag = "RibbonLabel" };
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Children = { new TextBlock { Text = "\uE16D", Tag = "RibbonIcon" }, label }
            };
            var button = new Button
            {
                Tag = "RibbonCompact:72:32",
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right,
                Content = content
            };

            var compactLevel = typeof(MainWindow).GetNestedType("RibbonCompactLevel", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "RibbonCompactLevel");
            var iconOnly = Enum.Parse(compactLevel, "IconOnly");
            var setCompact = typeof(MainWindow)
                .GetMethod("SetRibbonButtonCompact", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetRibbonButtonCompact");

            setCompact.Invoke(null, [button, iconOnly]);

            button.HorizontalContentAlignment.Should().Be(System.Windows.HorizontalAlignment.Center);
            content.HorizontalAlignment.Should().Be(System.Windows.HorizontalAlignment.Center);
        });
    }

    [Fact]
    public void InsertRibbon_HidesChartFormattingCommands()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Insert", 800);

            harness.VisibleRibbonCommandLabels.Should().NotContain("Label Border", harness.DebugActiveRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().NotContain("Y Bounds", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveRibbonGroupNames.Should().Contain("Charts", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveMenuHeaders("Charts").Should().Contain("Column Chart", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveMenuHeaders("Charts").Should().NotContain("Data Label Border", harness.DebugActiveRibbonChildren);
        });
    }

    [Fact]
    public void InsertRibbon_KeepsTablesExpandedAtNormalNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Insert", 900);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Tables", harness.DebugActiveRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["PivotTable", "Recommended", "Table"],
                "Excel keeps the first Insert groups expanded at normal narrow widths before collapsing gallery-heavy groups");
        });
    }

    [Theory]
    [InlineData("Formulas", "Function Library")]
    [InlineData("Data", "Get & Transform Data")]
    [InlineData("Review", "Proofing")]
    [InlineData("View", "Workbook Views")]
    public void RibbonTabs_KeepPrimaryGroupExpandedAtNormalNarrowWidths(string tab, string primaryGroup)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab(tab, 900);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                primaryGroup,
                $"{tab} should collapse lower-priority groups before the first Excel-style primary group at normal narrow widths");
        });
    }

    [Fact]
    public void DataRibbon_KeepsSortFilterExpandedAtMediumWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Data", 1120);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                "Sort & Filter",
                "Data should keep the second Excel-style group available at medium widths and collapse later utility groups first");
        });
    }

    [Fact]
    public void DataRibbon_KeepsDataToolsAndForecastVisibleAtMediumWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Data", 1120);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Data Tools", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Forecast", harness.DebugActiveRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Data Validation", "What-If Analysis", "Forecast Sheet"],
                "Excel keeps the medium-priority Data Tools and Forecast affordances visible around 1120px");
        });
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1100)]
    public void DataRibbon_DataToolsCommandLabelsDoNotClipAtNormalNarrowWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Data", width);

            harness.ActiveRibbonGroupClippedCommandLabels("Data Tools").Should().BeEmpty(
                "Excel keeps the visible Data Tools command labels readable instead of clipping names such as Remove Duplicates");
        });
    }

    [Fact]
    public void ViewRibbon_KeepsShowWithZoomAndWindowAtMediumWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", 1366);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Show", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Zoom", harness.DebugActiveRibbonChildren);
            harness.VisibleViewShowCheckBoxLabels.Should().Contain(
                ["Gridlines", "Headings", "Formula Bar"],
                "Excel keeps the Show checkbox group visible at medium workbook widths before collapsing Macros");
        });
    }

    [Fact]
    public void RibbonTabs_RemainSingleRowAtNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(640);

            harness.VisibleRibbonTabHeaderRows.Should().HaveCount(1, "Excel keeps the main ribbon tabs on one row while the command groups collapse");
        });
    }

    [Fact]
    public void RibbonTabSelection_SchedulesFallbackCompactionBeforeRender()
    {
        var source = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Ribbon.cs"));

        var method = source.Substring(
            source.IndexOf("private void NormalizeRibbonSurfaceAfterTabSelection", StringComparison.Ordinal),
            source.IndexOf("private void ConfigureInsertRibbonSurface", StringComparison.Ordinal) -
            source.IndexOf("private void NormalizeRibbonSurfaceAfterTabSelection", StringComparison.Ordinal));

        method.Should().Contain("DispatcherPriority.Send");
        method.Should().NotContain("DispatcherPriority.Loaded");
    }

    [Fact]
    public void WindowResize_SchedulesRibbonFallbackCompactionBeforeRender()
    {
        var source = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.WorkbookUiState.cs"));

        var method = source.Substring(
            source.IndexOf("private void MainWindow_SizeChanged", StringComparison.Ordinal),
            source.IndexOf("private string FormatCellReference", StringComparison.Ordinal) -
            source.IndexOf("private void MainWindow_SizeChanged", StringComparison.Ordinal));

        method.Should().Contain("NormalizeRibbonSurfaceAfterLayoutChange");
        method.Should().NotContain("UpdateRibbonCompactMode();");
    }

    [Fact]
    public void CollapsedRibbonMenuItems_MirrorSourceMenuStateAndOpenedUpdates()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", 640);
            var arrangeAll = harness.CollapsedActiveMenuItem("Window", "Arrange All");

            arrangeAll.Should().NotBeNull(harness.DebugActiveRibbonChildren);
            var tiled = arrangeAll!.Items.OfType<MenuItem>()
                .First(item => string.Equals(item.Header?.ToString(), "Tiled", StringComparison.Ordinal));

            tiled.IsCheckable.Should().BeTrue();
            tiled.InputGestureText.Should().Be("T");

            arrangeAll.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, arrangeAll));

            tiled.IsChecked.Should().BeTrue("the clone should run the source menu's Opened state refresh before display");
            arrangeAll.Items.OfType<MenuItem>()
                .Where(item => !ReferenceEquals(item, tiled))
                .Should().OnlyContain(item => item.IsChecked == false);
        });
    }

    [Fact]
    public void CollapsedRibbonMenuItems_RefreshSourceButtonEnabledStateWhenOpened()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(220);
            var sourceButton = harness.VisibleOrCollapsedRibbonButton("Find & Select");
            var menu = harness.CollapsedMenu("Editing");
            var item = harness.CollapsedMenuItem("Editing", "Find & Select");

            sourceButton.Should().NotBeNull(harness.DebugRibbonChildren);
            item.Should().NotBeNull(harness.DebugRibbonChildren);

            sourceButton!.IsEnabled = false;
            menu!.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));

            item!.IsEnabled.Should().BeFalse("collapsed overflow commands should use the current enabled state of their source ribbon controls");
        });
    }

    [Fact]
    public void CollapsedRibbonNestedMenuItem_ClickRoutesOnlyToMatchingSourceItem()
    {
        StaTestRunner.Run(() =>
        {
            var parentInvocations = 0;
            var childInvocations = 0;
            var sourceParent = new MenuItem { Header = "Sort & Filter" };
            var sourceChild = new MenuItem { Header = "Sort A to Z" };
            sourceParent.Items.Add(sourceChild);
            sourceParent.Click += (_, args) =>
            {
                if (ReferenceEquals(args.OriginalSource, sourceParent))
                    parentInvocations++;
            };
            sourceChild.Click += (_, _) => childInvocations++;
            var cloneMethod = typeof(MainWindow)
                .GetMethod("CloneRibbonMenuItem", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CloneRibbonMenuItem");

            var clonedParent = (MenuItem)cloneMethod.Invoke(null, [sourceParent])!;
            var clonedChild = clonedParent.Items.OfType<MenuItem>().Single();
            clonedChild.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, clonedChild));

            childInvocations.Should().Be(1);
            parentInvocations.Should().Be(0, "a nested collapsed overflow command should not also invoke its parent menu command");
        });
    }

    [Fact]
    public void DenseRibbonCommandColumns_UseShortRowButtons()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1465);

                harness.DenseColumnButtonHeights.Should().OnlyContain(
                    height => height <= 24,
                    $"{tab} dense ribbon columns should use Excel-like short row commands instead of tall large-button footprints");
            }
        });
    }

    [Fact]
    public void PageLayoutPageSetup_KeepsCommandsInsideRibbonRow()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Page Layout", 1465);

            harness.ActiveRibbonGroupCommandOverflow("Page Setup").Should().BeLessThanOrEqualTo(
                0.5,
                "Excel lays out Page Setup as compact command rows instead of letting the command stack clip behind the group label");
            harness.ActiveRibbonGroupDenseCommandRows("Page Setup").Should().Contain(
                3,
                "Excel-like Page Setup commands should use three short rows, not one tall vertical stack that clips");
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Margins", "Orientation", "Size", "Print Area", "Breaks", "Background", "Print Titles"]);
        });
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1100)]
    public void PageLayoutRibbon_KeepsPageSetupExpandedAtNormalNarrowWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Page Layout", width);

            harness.CollapsedActiveRibbonGroupNames.Should().NotContain(
                "Page Setup",
                "Excel keeps the primary Page Setup commands directly reachable at normal narrow widths");
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Margins", "Orientation", "Size"],
                "Page Layout should collapse lower-priority groups before the primary Page Setup group");
            harness.ActiveRibbonGroupCommandOverflow("Page Setup").Should().BeLessThanOrEqualTo(
                0.5,
                "Page Setup should keep all command rows above the group-label strip at normal narrow widths");
        });
    }

    [Fact]
    public void VerticallyStackedRibbonCommands_AlignIconSlots()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1465);

                harness.VerticallyStackedRibbonIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Select(offset => Math.Round(offset, 1)).Distinct().Count() == 1,
                    $"{tab} vertical command stacks should put small command icons directly above one another");

                harness.DirectVerticalButtonStackIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Select(offset => Math.Round(offset, 1)).Distinct().Count() == 1,
                    $"{tab} direct XAML vertical button stacks should align small command icons in a fixed column");
            }
        });
    }

    [Fact]
    public void ViewRibbon_ShowCheckBoxLabelsShareLeftEdge()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", 2200);

            harness.ViewShowCheckBoxLabelOffsets
                .Select(offset => Math.Round(offset.Offset, 1))
                .Distinct()
                .Should()
                .HaveCount(1, "Excel keeps View tab checkbox labels in one tidy left-aligned column after the checkbox glyphs");

            harness.ViewShowCheckBoxContentAlignments.Should().OnlyContain(
                alignment => alignment == System.Windows.HorizontalAlignment.Left,
                "ribbon checkbox rows should not center short labels inside the widest checkbox row");
        });
    }

    [Fact]
    public void ViewRibbon_RulerCheckBoxIsEnabledOnlyInPageLayoutView()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", 1465);

            harness.ViewRulerCheckBoxIsEnabled.Should().BeFalse("Excel disables Ruler outside Page Layout view");

            harness.ClickActiveRibbonButton("Page Layout");

            harness.ViewRulerCheckBoxIsEnabled.Should().BeTrue("Excel enables Ruler in Page Layout view");

            harness.ClickActiveRibbonButton("Normal");

            harness.ViewRulerCheckBoxIsEnabled.Should().BeFalse("returning to Normal view should disable Ruler again");
        });
    }

    [Fact]
    public void RibbonScrollViewers_HideHorizontalScrollBarsWithoutDisablingFallbackScroll()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 640);

                harness.RibbonHorizontalScrollBarModes.Should().OnlyContain(
                    mode => mode == ScrollBarVisibility.Hidden,
                    $"{tab} should keep the ribbon face clean while preserving hidden horizontal fallback scrolling");
            }
        });
    }

    [Theory]
    [InlineData(750)]
    [InlineData(900)]
    [InlineData(1100)]
    public void HelpRibbon_DoesNotClipAtExcelWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Help", width);

            harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                0.5,
                $"Help at {width}px should fit without exposing hidden horizontal scroll; {harness.DebugActiveRibbonChildren}");
            harness.VisibleRibbonCommandLabels.Should().Contain(
                ["Help Online", "Feedback", "Copy Diagnostics", "Check for Updates", "About Freexcel"],
                "the enabled Help commands should remain directly usable at common Excel widths");
            harness.CollapsedActiveRibbonGroupNames.Should().NotContain("Help", harness.DebugActiveRibbonChildren);
        });
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1120)]
    [InlineData(1280)]
    [InlineData(1366)]
    [InlineData(1465)]
    public void RibbonTabs_DoNotClipActiveCommandRowAtCommonExcelWidths(double width)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, width);

                harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(
                    0.5,
                    $"{tab} at {width}px should collapse groups before the hidden ribbon scroll surface clips visible commands; {harness.DebugActiveRibbonChildren}");
            }
        });
    }

    [Fact]
    public void CollapsedRibbonGroupKeyTips_AreUniqueWithinSelectedTab()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 220);

                harness.CollapsedActiveRibbonGroupKeyTips
                    .GroupBy(pair => pair.KeyTip, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Should()
                    .BeEmpty($"{tab} collapsed group keytips should remain routable without duplicate generated group badges");
            }
        });
    }

    [Fact]
    public void CollapsedRibbonGroups_ShowGroupCaptionsAtNormalNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Review", 900);

            harness.CollapsedActiveRibbonGroupNames.Should().Contain(["Notes", "Protect"], harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveRibbonGroupVisibleLabels.Should().Contain(
                ["Notes", "Protect"],
                "Excel keeps collapsed group captions visible at common 900px workbook widths so icon-only fallbacks remain identifiable");
        });
    }

    [Fact]
    public void CollapsedRibbonGroupButtons_ShowDropdownGlyph()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 220);

                harness.CollapsedActiveRibbonGroupsWithoutDropdownGlyph.Should().BeEmpty(
                    $"{tab} collapsed group buttons should visibly advertise their overflow menu like Excel");
            }
        });
    }

    [Fact]
    public void RibbonScrollViewers_DefaultToHiddenHorizontalScrollBarsInXaml()
    {
        var xaml = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        xaml.Should().NotContain(
            "HorizontalScrollBarVisibility=\"Auto\"",
            "ribbon tabs should not briefly show a horizontal scrollbar before runtime normalization collapses groups");
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private static MainWindow? SharedWindow;

        private readonly MainWindow _window;
        private readonly MethodInfo _updateRibbonCompactMode;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _updateRibbonCompactMode = typeof(MainWindow)
                .GetMethod("UpdateRibbonCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
        }

        public IReadOnlyList<string> CollapsedRibbonGroupNames =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveRibbonGroupNames =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveRibbonGroupVisibleLabels =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => EnumerateSelfAndVisualDescendants(button)
                    .Concat(EnumerateLogicalDescendants(button))
                    .OfType<TextBlock>()
                    .Any(textBlock =>
                        textBlock.Tag?.ToString() == "RibbonLabel" &&
                        IsEffectivelyVisible(textBlock) &&
                        string.Equals(textBlock.Text, RibbonTooltip.GetTitle(button), StringComparison.Ordinal)))
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<CollapsedGroupKeyTip> CollapsedActiveRibbonGroupKeyTips =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Select(button => new CollapsedGroupKeyTip(RibbonTooltip.GetTitle(button) ?? "", RibbonTooltip.GetKeyTip(button) ?? ""))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.GroupName) && !string.IsNullOrWhiteSpace(pair.KeyTip))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveRibbonGroupsWithoutDropdownGlyph =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => System.Windows.Documents.AdornerLayer.GetAdornerLayer(button)
                    ?.GetAdorners(button)
                    ?.Any(adorner => adorner.GetType().Name == "RibbonCollapsedGroupChevronAdorner") != true)
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<ContextMenu> CollapsedRibbonGroupMenus =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Select(button => button.ContextMenu)
                .Where(menu => menu is not null)
                .Cast<ContextMenu>()
                .ToList();

        public IReadOnlyList<string> CollapsedMenuHeaders(string groupName) =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .SelectMany(button => button.ContextMenu?.Items.OfType<MenuItem>() ?? [])
                .Select(item => item.Header?.ToString() ?? "")
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveMenuHeaders(string groupName) =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .SelectMany(button => button.ContextMenu?.Items.OfType<MenuItem>() ?? [])
                .Select(item => item.Header?.ToString() ?? "")
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .ToList();

        public MenuItem? CollapsedActiveMenuItem(string groupName, string header) =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .SelectMany(button => button.ContextMenu?.Items.OfType<MenuItem>() ?? [])
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

        public ContextMenu? CollapsedMenu(string groupName) =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .Select(button => button.ContextMenu)
                .FirstOrDefault(menu => menu is not null);

        public MenuItem? CollapsedMenuItem(string groupName, string header) =>
            CollapsedMenu(groupName)?.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

        public Button? VisibleOrCollapsedRibbonButton(string title) =>
            HomeRibbonChildren
                .OfType<DependencyObject>()
                .SelectMany(EnumerateSelfAndVisualDescendants)
                .Concat(HomeRibbonChildren.OfType<DependencyObject>().SelectMany(EnumerateLogicalDescendants))
                .OfType<Button>()
                .Distinct()
                .FirstOrDefault(button => string.Equals(RibbonTooltip.GetTitle(button), title, StringComparison.Ordinal));

        private IEnumerable<UIElement> HomeRibbonChildren =>
            (_window.FindName("HomeRibbonPanel") as StackPanel)?.Children.Cast<UIElement>() ?? [];

        public string DebugRibbonChildren =>
            string.Join(", ", HomeRibbonChildren.Select(child =>
                child is FrameworkElement fe
                    ? $"{child.GetType().Name}:{fe.Tag}:{fe.Visibility}:{RibbonTooltip.GetTitle(fe) ?? fe.Name}"
                    : child.GetType().Name));

        public string DebugActiveRibbonChildren =>
            $"RibbonTabs={(_window.FindName("RibbonTabs") as TabControl)?.ActualWidth:0.0}, " +
            $"ActivePanelDesired={ActiveRibbonPanel?.DesiredSize.Width:0.0}, " +
            string.Join(", ", ActiveRibbonPanel?.Children.Cast<UIElement>().Select(child =>
                child is FrameworkElement fe
                    ? $"{child.GetType().Name}:{fe.Tag}:{fe.Visibility}:{RibbonTooltip.GetTitle(fe) ?? fe.Name}:{fe.DesiredSize.Width:0.0}/{fe.ActualWidth:0.0}"
                    : child.GetType().Name) ?? []);

        public IReadOnlyList<string> VisibleRibbonCommandLabels =>
            (SelectedRibbonTab is null
                ? []
                : EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                    .Concat(EnumerateLogicalDescendants(SelectedRibbonContentRoot))
                    .OfType<Button>()
                    .Distinct()
                    .Where(IsEffectivelyVisible)
                    .Select(GetButtonLabel)
                    .Where(label => !string.IsNullOrWhiteSpace(label)))
            .ToList();

        public IReadOnlyList<int> VisibleRibbonTabHeaderRows =>
            _window.FindName("RibbonTabs") is TabControl tabs
                ? EnumerateSelfAndVisualDescendants(tabs)
                    .OfType<TabItem>()
                    .Where(item => item.Visibility == Visibility.Visible && item.ActualHeight > 0)
                    .Select(item => (int)Math.Round(item.TransformToAncestor(tabs).Transform(new Point(0, 0)).Y))
                    .Distinct()
                    .OrderBy(row => row)
                    .ToList()
                : [];

        public IReadOnlyList<double> DenseColumnButtonHeights =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .OfType<UniformGrid>()
                .Where(grid => grid.Rows == 3 && grid.Children.OfType<Button>().Count() > 3)
                .SelectMany(grid => grid.Children.OfType<Button>())
                .Where(IsEffectivelyVisible)
                .Select(button => button.Height)
                .ToList();

        public double ActiveRibbonGroupCommandOverflow(string groupName)
        {
            if (FindActiveRibbonGroup(groupName) is not { } group)
                return 0;

            var labelTop = group.Children
                .OfType<Border>()
                .Where(border => Grid.GetRow(border) == 1)
                .Select(border => border.TransformToAncestor(group).Transform(new Point(0, 0)).Y)
                .DefaultIfEmpty(group.ActualHeight)
                .Min();

            var maxCommandBottom = EnumerateSelfAndVisualDescendants(group)
                .OfType<Button>()
                .Where(IsEffectivelyVisible)
                .Where(button => button.Tag is not string tag || tag != "RibbonCollapsedGroupButton")
                .Select(button =>
                {
                    var top = button.TransformToAncestor(group).Transform(new Point(0, 0)).Y;
                    return top + button.ActualHeight;
                })
                .DefaultIfEmpty(0)
                .Max();

            return maxCommandBottom - labelTop;
        }

        public IReadOnlyList<int> ActiveRibbonGroupDenseCommandRows(string groupName) =>
            FindActiveRibbonGroup(groupName) is { } group
                ? EnumerateSelfAndVisualDescendants(group)
                    .OfType<UniformGrid>()
                    .Where(grid => grid.Children.OfType<Button>().Count() > 3)
                    .Select(grid => grid.Rows)
                    .ToList()
                : [];

        public IReadOnlyList<string> ActiveRibbonGroupClippedCommandLabels(string groupName) =>
            FindActiveRibbonGroup(groupName) is { } group
                ? EnumerateSelfAndVisualDescendants(group)
                    .Concat(EnumerateLogicalDescendants(group))
                    .OfType<TextBlock>()
                    .Distinct()
                    .Where(textBlock => textBlock.Tag?.ToString() == "RibbonLabel")
                    .Where(IsEffectivelyVisible)
                    .Where(IsTextVisuallyClipped)
                    .Select(textBlock => textBlock.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList()
                : [];

        public IReadOnlyList<RibbonIconStackOffsets> VerticallyStackedRibbonIconOffsets =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .OfType<Panel>()
                .SelectMany(GetVerticalIconStacks)
                .ToList();

        public IReadOnlyList<RibbonIconStackOffsets> DirectVerticalButtonStackIconOffsets =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .OfType<StackPanel>()
                .Where(panel => panel.Orientation == Orientation.Vertical)
                .SelectMany(GetDirectVerticalButtonStacks)
                .ToList();

        public IReadOnlyList<CheckBoxLabelOffset> ViewShowCheckBoxLabelOffsets =>
            ViewShowCheckBoxes
                .Select(checkBox => new CheckBoxLabelOffset(
                    checkBox.Name,
                    GetCheckBoxLabelOffset(checkBox)))
                .ToList();

        public IReadOnlyList<System.Windows.HorizontalAlignment> ViewShowCheckBoxContentAlignments =>
            ViewShowCheckBoxes
                .Select(checkBox => checkBox.HorizontalContentAlignment)
                .ToList();

        public IReadOnlyList<string> VisibleViewShowCheckBoxLabels =>
            ViewShowCheckBoxes
                .Select(checkBox => checkBox.Content?.ToString() ?? "")
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToList();

        public bool? ViewRulerCheckBoxIsEnabled =>
            (_window.FindName("ViewRulerChk") as CheckBox)?.IsEnabled;

        public IReadOnlyList<ScrollBarVisibility> RibbonHorizontalScrollBarModes =>
            _window.FindName("RibbonTabs") is TabControl tabs
                ? EnumerateSelfAndVisualDescendants(tabs)
                    .OfType<ScrollViewer>()
                    .Where(IsEffectivelyVisible)
                    .Select(scrollViewer => scrollViewer.HorizontalScrollBarVisibility)
                    .ToList()
                : [];

        public double ActiveRibbonPanelOverflow
        {
            get
            {
                if (ActiveRibbonPanel is not { } panel)
                    return 0;

                panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var viewport = FindVisualAncestor<ScrollViewer>(panel)?.ActualWidth;
                if (viewport is null or <= 0)
                    viewport = (_window.FindName("RibbonTabs") as TabControl)?.ActualWidth;

                return panel.DesiredSize.Width - Math.Max(0, (viewport ?? 0) - 4);
            }
        }

        private TabItem? SelectedRibbonTab =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem as TabItem;

        private DependencyObject SelectedRibbonContentRoot =>
            SelectedRibbonTab?.Content as DependencyObject ??
            (DependencyObject?)SelectedRibbonTab ??
            _window;

        private IReadOnlyList<CheckBox> ViewShowCheckBoxes =>
            new[] { "ViewGridlinesChk", "ViewHeadersChk", "ViewRulerChk", "ViewFormulaBarChk" }
                .Select(name => _window.FindName(name))
                .OfType<CheckBox>()
                .Where(IsEffectivelyVisible)
                .ToList();

        private StackPanel? ActiveRibbonPanel =>
            SelectedRibbonTab is { } tabItem
                ? EnumerateSelfAndVisualDescendants(tabItem.Content as DependencyObject ?? tabItem)
                    .Concat(EnumerateLogicalDescendants(tabItem.Content as DependencyObject ?? tabItem))
                    .OfType<StackPanel>()
                    .Distinct()
                    .Where(panel => FindVisualAncestor<Button>(panel) is not { Tag: "RibbonCollapsedGroupButton" })
                    .OrderByDescending(panel => panel.Children.OfType<Grid>().Count(IsRibbonGroupGrid))
                    .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                             panel.Children.OfType<Grid>().Any(IsRibbonGroupGrid))
                : null;

        private Grid? FindActiveRibbonGroup(string groupName) =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Grid>()
                .FirstOrDefault(grid => grid.Children
                    .OfType<Border>()
                    .Any(border => Grid.GetRow(border) == 1 &&
                                   border.Child is TextBlock label &&
                                   string.Equals(label.Text, groupName, StringComparison.Ordinal)));

        public void SetRibbonWidth(double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl tabs)
                tabs.SelectedIndex = 1;
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public bool CanUseRequestedRibbonWidth(double width) =>
            _window.ActualWidth >= width - 1;

        public void SelectRibbonTab(string header, double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl tabs)
            {
                tabs.SelectedItem = tabs.Items
                    .OfType<TabItem>()
                    .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
            PumpDispatcher();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void ClickActiveRibbonButton(string title)
        {
            var button = EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .Concat(EnumerateLogicalDescendants(SelectedRibbonContentRoot))
                .OfType<Button>()
                .Distinct()
                .FirstOrDefault(button => string.Equals(RibbonTooltip.GetTitle(button), title, StringComparison.Ordinal));

            button.Should().NotBeNull(DebugActiveRibbonChildren);
            button!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public static MainWindowHarness Create()
        {
            var window = SharedWindow ??= CreateSharedWindow();
            if (!window.IsVisible)
                window.Show();

            window.WindowState = WindowState.Normal;
            window.Width = 1280;
            window.Height = 720;
            if (window.FindName("RibbonTabs") is TabControl tabs)
                tabs.SelectedIndex = 1;
            window.UpdateLayout();
            PumpDispatcher();
            var harness = new MainWindowHarness(window);
            harness.ResetUiState();
            return harness;
        }

        private static MainWindow CreateSharedWindow()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook);

            window.Width = 1280;
            window.Height = 720;
            window.Show();
            PumpDispatcher();
            return window;
        }

        public void Dispose()
        {
            ResetUiState();
        }

        private void ResetUiState()
        {
            foreach (var menu in CollapsedRibbonGroupMenus)
                menu.IsOpen = false;
            if (VisibleOrCollapsedRibbonButton("Find & Select") is { } findSelect)
                findSelect.IsEnabled = true;
            if (_window.FindName("RibbonTabs") is TabControl tabs)
                tabs.SelectedIndex = 1;
            _window.UpdateLayout();
            PumpDispatcher();
        }

        private static IEnumerable<DependencyObject> EnumerateSelfAndVisualDescendants(DependencyObject root)
        {
            yield return root;

            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                foreach (var descendant in EnumerateSelfAndVisualDescendants(child))
                    yield return descendant;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject dependencyObject)
                    continue;

                yield return dependencyObject;

                foreach (var descendant in EnumerateLogicalDescendants(dependencyObject))
                    yield return descendant;
            }
        }

        private static string GetButtonLabel(Button button)
        {
            if (button.Content is string text)
                return text;

            return EnumerateSelfAndVisualDescendants(button)
                .Concat(EnumerateLogicalDescendants(button))
                .OfType<TextBlock>()
                .FirstOrDefault(textBlock => string.Equals(textBlock.Tag?.ToString(), "RibbonLabel", StringComparison.Ordinal))
                ?.Text ?? "";
        }

        private static double GetIconSlotOffset(Visual ancestor, Button button)
        {
            if (!TryGetCommandIconSlot(button, out var iconSlot))
            {
                return double.NaN;
            }

            return iconSlot.TransformToAncestor(ancestor).Transform(new Point(0, 0)).X;
        }

        private static IEnumerable<RibbonIconStackOffsets> GetVerticalIconStacks(Panel panel)
        {
            if (panel is StackPanel { Orientation: Orientation.Vertical })
            {
                var buttons = GetSmallCommandButtons(panel).ToArray();
                if (buttons.Length >= 2)
                    yield return CreateIconStackOffsets(panel, buttons);

                yield break;
            }

            if (panel is UniformGrid { Rows: > 0 } grid)
            {
                var buttons = GetSmallCommandButtons(grid).ToArray();
                if (buttons.Length < 2)
                    yield break;

                var columns = (int)Math.Ceiling(buttons.Length / (double)grid.Rows);
                for (var column = 0; column < columns; column++)
                {
                    var columnButtons = buttons
                        .Skip(column)
                        .Where((_, index) => index % columns == 0)
                        .ToArray();
                    if (columnButtons.Length >= 2)
                        yield return CreateIconStackOffsets(grid, columnButtons);
                }
            }
        }

        private static IEnumerable<RibbonIconStackOffsets> GetDirectVerticalButtonStacks(StackPanel panel)
        {
            var buttons = panel.Children
                .OfType<Button>()
                .Where(IsEffectivelyVisible)
                .Where(button => TryGetCommandIconSlot(button, out _))
                .ToArray();

            if (buttons.Length < 2)
                yield break;

            yield return new RibbonIconStackOffsets(
                buttons.Select(GetButtonLabel).ToArray(),
                buttons.Select(button => GetDirectIconSlotCenterOffset(panel, button)).ToArray());
        }

        private static IEnumerable<Button> GetSmallCommandButtons(Panel panel) =>
            panel.Children.OfType<Button>()
                .Where(IsEffectivelyVisible)
                .Where(button => button.Content is FrameworkElement content &&
                                 string.Equals(content.Tag?.ToString(), "RibbonCommandContent:S", StringComparison.Ordinal) &&
                                 TryGetCommandIconSlot(button, out _));

        private static RibbonIconStackOffsets CreateIconStackOffsets(Visual ancestor, IReadOnlyList<Button> buttons) =>
            new(
                buttons.Select(GetButtonLabel).ToArray(),
                buttons.Select(button => GetIconSlotOffset(ancestor, button)).ToArray());

        private static double GetDirectIconSlotCenterOffset(Visual ancestor, Button button)
        {
            if (!TryGetCommandIconSlot(button, out var iconSlot))
            {
                return double.NaN;
            }

            var point = iconSlot.TransformToAncestor(ancestor).Transform(new Point(0, 0));
            return point.X + iconSlot.ActualWidth / 2;
        }

        private static bool TryGetCommandIconSlot(Button button, out FrameworkElement iconSlot)
        {
            iconSlot = null!;
            if (button.Content is not Panel { Children.Count: > 0 } content ||
                content.Children[0] is not FrameworkElement firstChild)
            {
                return false;
            }

            iconSlot = firstChild;
            return true;
        }

        private static bool IsTextVisuallyClipped(TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return textBlock.DesiredSize.Width > textBlock.ActualWidth + 0.5;
        }

        private static double GetCheckBoxLabelOffset(CheckBox checkBox)
        {
            var presenter = EnumerateSelfAndVisualDescendants(checkBox)
                .OfType<ContentPresenter>()
                .FirstOrDefault(contentPresenter => Equals(contentPresenter.Content, checkBox.Content));
            presenter.Should().NotBeNull($"the {checkBox.Name} checkbox should expose a content presenter for its label");

            var stack = FindVisualAncestor<StackPanel>(checkBox);
            stack.Should().NotBeNull($"the {checkBox.Name} checkbox should be hosted in the View tab Show stack");

            return presenter!.TransformToAncestor(stack!).Transform(new Point(0, 0)).X;
        }

        private static T? FindVisualAncestor<T>(DependencyObject element)
            where T : DependencyObject
        {
            for (var current = System.Windows.Media.VisualTreeHelper.GetParent(element);
                 current is not null;
                 current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            {
                if (current is T match)
                    return match;
            }

            return null;
        }

        private static bool IsRibbonGroupGrid(Grid grid) =>
            grid.Children.OfType<Border>().Any(border =>
                Grid.GetRow(border) == 1 &&
                border.Child is TextBlock groupLabel &&
                !string.IsNullOrWhiteSpace(groupLabel.Text));

        private static bool IsEffectivelyVisible(DependencyObject element)
        {
            var current = element;
            while (current is not null)
            {
                if (current is UIElement { Visibility: not Visibility.Visible })
                    return false;

                current = System.Windows.Media.VisualTreeHelper.GetParent(current) ??
                          LogicalTreeHelper.GetParent(current);
            }

            return true;
        }
    }

    public sealed record RibbonIconStackOffsets(IReadOnlyList<string> Labels, IReadOnlyList<double> Offsets);

    public sealed record CheckBoxLabelOffset(string Name, double Offset);

    public sealed record CollapsedGroupKeyTip(string GroupName, string KeyTip);

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}
