using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace Free.Shared.Ribbon.Wpf.Tests;

[Trait("Category", "RibbonUiLane")]
public sealed class RibbonAdaptiveWpfSurfaceTests
{
    [Fact]
    public void LegacySurfaceDiscovery_SelectsLargestHorizontalGroupPanelAndIgnoresOverflowContent()
    {
        StaTestRunner.Run(() =>
        {
            var root = new Grid();
            var smallerPanel = CreateGroupPanel("One");
            var expectedPanel = CreateGroupPanel("One", "Two");
            var overflowButton = new Button();
            RibbonMetadata.SetRole(overflowButton, RibbonMetadataRole.CollapsedGroupButton);
            overflowButton.Content = CreateGroupPanel("PopupOne", "PopupTwo", "PopupThree");

            root.Children.Add(smallerPanel);
            root.Children.Add(expectedPanel);
            root.Children.Add(overflowButton);

            RibbonAdaptiveWpfSurface.FindLegacyAdaptivePanel(root).Should().BeSameAs(expectedPanel);
            RibbonAdaptiveWpfSurface.GetAdaptiveGroups(expectedPanel)
                .Select(element => RibbonMetadata.GetGroupName(element))
                .Should()
                .Equal("One", "Two");
        });
    }

    [Fact]
    public void MeasurementHelpers_KeepRendererStateAndProductProfileDataSeparate()
    {
        StaTestRunner.Run(() =>
        {
            var first = new Grid();
            RibbonMetadata.SetGroupName(first, "Font");
            RibbonMetadata.SetCatalogId(first, "HomeFontGroup");
            var second = new Grid();
            RibbonMetadata.SetGroupName(second, "Editing");

            var key = RibbonAdaptiveWpfSurface.CreateMeasurementCacheKey(
                "HomeTab",
                new FrameworkElement[] { first, second },
                element => RibbonMetadata.GetGroupName(element),
                element => RibbonMetadata.GetCatalogId(element));
            var profileKeys = RibbonAdaptiveWpfSurface.CreateGroupProfileKeys(
                new[]
                {
                    new RibbonAdaptiveGroup("Font", 100, 80, 50, 40, "HomeFontGroup"),
                    new RibbonAdaptiveGroup("Editing", 90, 70, 45, 40)
                });

            key.Should().StartWith("HomeTab|2|Font:HomeFontGroup:");
            key.Should().Contain(";Editing::");
            profileKeys.Should().Equal("HomeFontGroup", "Editing");
            RibbonAdaptiveWpfSurface.RoundWidthToTenths(12.25).Should().Be(122);
        });
    }

    [Fact]
    public void StateComparison_RecognizesOnlyAdditionalCollapse()
    {
        RibbonAdaptiveWpfSurface.StatesAreMoreCollapsedThan(
                new[] { RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Collapsed },
                new[] { RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.IconOnly })
            .Should()
            .BeTrue();
        RibbonAdaptiveWpfSurface.StatesAreMoreCollapsedThan(
                new[] { RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.SmallWithLabels },
                new[] { RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.IconOnly })
            .Should()
            .BeFalse();
    }

    [Fact]
    public void StateCacheSignatures_PreserveStatesBeyondThePackedPrefix()
    {
        var first = Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 65).ToArray();
        var second = first.ToArray();
        second[64] = RibbonAdaptiveGroupState.Collapsed;

        RibbonAdaptiveWpfSurface.CreateStateSignature(first)
            .Should()
            .NotBe(RibbonAdaptiveWpfSurface.CreateStateSignature(second));
        RibbonAdaptiveWpfSurface.CreateCorrectionKey("Home", 900.04, first)
            .Should()
            .Be(RibbonAdaptiveWpfSurface.CreateCorrectionKey("Home", 900.04, first));
    }

    [Fact]
    public void MeasuredFallback_AppliesProfileSelectedTransitionsUntilTheSurfaceFits()
    {
        var states = new[] { RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full };
        var applied = new List<(int Index, RibbonAdaptiveGroupState State)>();

        var changed = RibbonAdaptiveWpfFallback.ApplyFallbackUntilFits(
            states,
            preserveFirstGroup: true,
            protectedGroupIndexes: null,
            current => current[1] != RibbonAdaptiveGroupState.Collapsed,
            (RibbonAdaptiveGroupState[] current, bool preserveFirst, IReadOnlySet<int>? _, out int changedIndex, out RibbonAdaptiveGroupState previousState) =>
            {
                preserveFirst.Should().BeTrue();
                changedIndex = 1;
                previousState = current[1];
                current[1] = RibbonAdaptiveGroupState.Collapsed;
                return true;
            },
            (index, state, _) =>
            {
                applied.Add((index, state));
                return true;
            });

        changed.Should().BeTrue();
        states.Should().Equal(RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Collapsed);
        applied.Should().Equal((1, RibbonAdaptiveGroupState.Collapsed));
    }

    [Fact]
    public void MeasuredExpansion_RollsBackTheFirstStateThatOverflows()
    {
        var states = new[] { RibbonAdaptiveGroupState.Collapsed };
        var applied = new List<RibbonAdaptiveGroupState>();

        var changed = RibbonAdaptiveWpfFallback.ApplyExpansionPass(
            states,
            new[] { 0 },
            current => current[0] == RibbonAdaptiveGroupState.Full,
            (_, state, _) =>
            {
                applied.Add(state);
                return true;
            });

        changed.Should().BeTrue();
        states.Should().Equal(RibbonAdaptiveGroupState.SmallWithLabels);
        applied.Should().Equal(
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.SmallWithLabels,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.SmallWithLabels);
    }

    [Fact]
    public void CollapsedGroupReconciliation_ReusesButtonsAndRestoresGroupOrder()
    {
        StaTestRunner.Run(() =>
        {
            var font = CreateGroup("Font");
            var editing = CreateGroup("Editing");
            var reusable = CreateCollapsedButton("Font", "FO");
            var duplicate = CreateCollapsedButton("Font", "F2");
            var stale = CreateCollapsedButton("Styles", "ST");
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(reusable);
            panel.Children.Add(font);
            panel.Children.Add(stale);
            panel.Children.Add(editing);
            panel.Children.Add(duplicate);

            var buttons = RibbonCollapsedGroupOverflow.ReconcileButtons(
                panel,
                new FrameworkElement[] { font, editing },
                element => RibbonMetadata.GetGroupName(element),
                (group, usedKeyTips) =>
                {
                    usedKeyTips.Should().Contain("FO");
                    return CreateCollapsedButton(RibbonMetadata.GetGroupName(group), "ED");
                });

            buttons.Should().HaveCount(2);
            buttons[0].Should().BeSameAs(reusable);
            panel.Children.Cast<UIElement>().Should().Equal(font, reusable, editing, buttons[1]);
        });
    }

    [Fact]
    public void CollapsedGroupMenu_PopulatesLazilyAndInvokesSourceButton()
    {
        StaTestRunner.Run(() =>
        {
            var invocationCount = 0;
            var source = new Button { Content = "Refresh" };
            RibbonTooltip.SetTitle(source, "Refresh");
            RibbonTooltip.SetKeyTip(source, "R");
            source.Click += (_, _) => invocationCount++;
            var group = new Grid();
            group.Children.Add(source);

            var menu = RibbonCollapsedGroupOverflow.CreateLazyMenu(
                group,
                _ => "Data",
                item => item,
                (_, _) => { },
                _ => { });

            menu.Items.Should().BeEmpty();
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            var item = menu.Items.OfType<MenuItem>().Single();
            item.Header.Should().Be("Refresh");
            RibbonTooltip.GetKeyTip(item).Should().Be("R");

            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            invocationCount.Should().Be(1);
        });
    }

    private static StackPanel CreateGroupPanel(params string[] groupNames)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var groupName in groupNames)
            panel.Children.Add(CreateGroup(groupName));
        return panel;
    }

    private static FrameworkElement CreateGroup(string groupName)
    {
        var group = new Grid();
        RibbonMetadata.SetGroupName(group, groupName);
        return group;
    }

    private static Button CreateCollapsedButton(string title, string keyTip)
    {
        var button = new Button();
        RibbonMetadata.SetRole(button, RibbonMetadataRole.CollapsedGroupButton);
        RibbonTooltip.SetTitle(button, title);
        RibbonTooltip.SetKeyTip(button, keyTip);
        return button;
    }

    private static class StaTestRunner
    {
        public static void Run(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
