using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class BackstageVisualContractTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_backstage_chrome_uses_the_neutral_pane_metrics_and_theme()
    {
        await Session.Dispatch(() =>
        {
            var style = AvaloniaBackstageChromeStyle.FromContract();
            var heading = AvaloniaBackstageChrome.CreateHeading("Heading", style);
            var section = AvaloniaBackstageChrome.CreateSectionHeader("Section", style);
            var detail = AvaloniaBackstageChrome.CreateDetailGrid();
            AvaloniaBackstageChrome.AddDetailRow(detail, "Label", "Value", "ValueId", style);

            heading.FontSize.Should().Be(BackstageVisualContract.Pane.HeadingFontSize);
            heading.Margin.Should().Be(ToThickness(BackstageVisualContract.Pane.HeadingMargin));
            ((SolidColorBrush)heading.Foreground!).Color.Should().Be(ToColor(BackstageVisualContract.Theme.PrimaryText));
            section.FontSize.Should().Be(BackstageVisualContract.Pane.SectionHeaderFontSize);
            section.Margin.Should().Be(ToThickness(BackstageVisualContract.Pane.SectionHeaderMargin));
            detail.Margin.Should().Be(ToThickness(BackstageVisualContract.Pane.DetailGridMargin));
            detail.ColumnDefinitions[0].Width.Value.Should().Be(BackstageVisualContract.Pane.DetailLabelColumnWidth);
            detail.Children.OfType<TextBlock>().Should().AllSatisfy(text =>
            {
                text.FontSize.Should().Be(BackstageVisualContract.Pane.DetailFontSize);
            });
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_backstage_frame_uses_the_neutral_navigation_geometry()
    {
        await Session.Dispatch(() =>
        {
            var frame = new AvaloniaBackstageFrame(
                new AvaloniaBackstageAccent(
                    Colors.Black,
                    Colors.Gray,
                    Colors.Blue,
                    Colors.White),
                Array.Empty<SisterBackstageEntryPlan<Control>>());

            var layout = Assert.IsType<Grid>(frame.Content);
            var rail = Assert.IsType<DockPanel>(layout.Children[0]);
            var bottomNav = Assert.IsType<StackPanel>(rail.Children[1]);
            var contentArea = Assert.IsType<Border>(layout.Children[1]);
            var scroll = Assert.IsType<ScrollViewer>(contentArea.Child);

            layout.ColumnDefinitions[0].Width.Value.Should().Be(BackstageVisualContract.Frame.RailWidth);
            bottomNav.Margin.Should().Be(ToThickness(BackstageVisualContract.Frame.BottomNavigationMargin));
            scroll.Padding.Should().Be(ToThickness(BackstageVisualContract.Frame.ContentPadding));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_backstage_navigation_uses_contract_hit_targets_and_preserves_selection_behavior()
    {
        await Session.Dispatch(() =>
        {
            var frame = new AvaloniaBackstageFrame(
                new AvaloniaBackstageAccent(Colors.Black, Colors.Gray, Colors.Blue, Colors.White),
                [
                    SisterBackstageEntryPlan<Control>.Pane(
                        "Info",
                        BackstageIconKind.Info,
                        () => new Border()),
                    SisterBackstageEntryPlan<Control>.Pane(
                        "Options",
                        BackstageIconKind.View,
                        () => new Border(),
                        dockBottom: true),
                ]);

            var layout = Assert.IsType<Grid>(frame.Content);
            var rail = Assert.IsType<DockPanel>(layout.Children[0]);
            var back = Assert.IsType<Button>(rail.Children[0]);
            var backIcon = Assert.IsType<TextBlock>(back.Content);
            var topScroll = Assert.IsType<ScrollViewer>(rail.Children[2]);
            var topNav = Assert.IsType<StackPanel>(topScroll.Content);
            var nav = Assert.IsType<Button>(topNav.Children.Single());
            var row = Assert.IsType<StackPanel>(nav.Content);
            var icon = Assert.IsType<TextBlock>(row.Children[0]);
            var label = Assert.IsType<TextBlock>(row.Children[1]);

            back.Padding.Should().Be(ToThickness(BackstageVisualContract.Frame.BackButtonPadding));
            back.FontSize.Should().Be(BackstageVisualContract.Frame.BackButtonFontSize);
            backIcon.Width.Should().Be(BackstageVisualContract.Frame.BackButtonIconSize);
            backIcon.Height.Should().Be(BackstageVisualContract.Frame.BackButtonIconSize);
            icon.Width.Should().Be(BackstageVisualContract.Frame.NavigationIconSize);
            icon.Height.Should().Be(BackstageVisualContract.Frame.NavigationIconSize);
            row.Spacing.Should().Be(BackstageVisualContract.Frame.NavigationIconLabelGap);
            nav.Padding.Should().Be(ToThickness(BackstageVisualContract.Frame.NavigationButtonPadding));
            label.FontSize.Should().Be(BackstageVisualContract.Frame.NavigationFontSize);

            frame.Show("Info");
            frame.CurrentPaneLabel.Should().Be("Info");
            nav.Background.Should().BeOfType<SolidColorBrush>().Which.Color.Should().Be(Colors.Blue);
            frame.HandleKey(Key.Escape).Should().BeTrue();
            frame.IsOpen.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_backstage_frame_projects_entry_metadata_and_owns_activation_dismissal()
    {
        await Session.Dispatch(() =>
        {
            AvaloniaBackstageFrame? frame = null;
            var commandObservedDismissedFrame = false;
            var info = SisterBackstageEntryPlan<Control>.Pane(
                "Localized info",
                BackstageIconKind.Info,
                () => new Border()) with
            {
                StableId = "pane.info",
                KeyTip = "I",
                AutomationId = "InfoAutomationId",
                AutomationName = "Workbook information",
                AutomationHelpText = "Inspect workbook properties.",
                TooltipTitle = "Info",
                TooltipDescription = "Inspect this workbook.",
            };
            var save = SisterBackstageEntryPlan<Control>.Command(
                "Localized save",
                BackstageIconKind.Save,
                () => commandObservedDismissedFrame = !frame!.IsOpen) with
            {
                StableId = "command.save",
                AutomationId = "SaveAutomationId",
            };

            frame = new AvaloniaBackstageFrame(
                new AvaloniaBackstageAccent(Colors.Black, Colors.Gray, Colors.Blue, Colors.White),
                [info, save]);

            frame.Show("pane.info");
            frame.CurrentEntryId.Should().Be("pane.info");
            frame.CurrentPaneLabel.Should().Be("Localized info");
            frame.Entries.Single(entry => entry.StableId == "pane.info").KeyTip.Should().Be("I");

            var infoButton = frame.GetEntryButton("pane.info")!;
            AutomationProperties.GetAutomationId(infoButton).Should().Be("InfoAutomationId");
            AutomationProperties.GetName(infoButton).Should().Be("Workbook information");
            AutomationProperties.GetHelpText(infoButton).Should().Be("Inspect workbook properties.");
            ToolTip.GetTip(infoButton).Should().Be("Info\nInspect this workbook.");

            var saveButton = frame.GetEntryButton("SaveAutomationId")!;
            saveButton.IsEnabled = false;
            frame.TryActivateEntry("command.save").Should().BeFalse();
            frame.IsOpen.Should().BeTrue();

            saveButton.IsEnabled = true;
            frame.TryActivateEntry("command.save").Should().BeTrue();
            commandObservedDismissedFrame.Should().BeTrue();
            frame.IsOpen.Should().BeFalse();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_backstage_rail_ink_defaults_to_white_and_follows_the_accent_foreground()
    {
        await Session.Dispatch(() =>
        {
            SisterBackstageEntryPlan<Control>[] Entries() =>
            [
                SisterBackstageEntryPlan<Control>.Pane("Info", BackstageIconKind.Info, () => new Border()),
                SisterBackstageEntryPlan<Control>.Pane("Print", BackstageIconKind.Print, () => new Border()),
            ];

            static (Button Back, TextBlock BackIcon, TextBlock Icon, TextBlock Label) Rail(
                AvaloniaBackstageFrame frame,
                string entry)
            {
                var layout = Assert.IsType<Grid>(frame.Content);
                var rail = Assert.IsType<DockPanel>(layout.Children[0]);
                var back = Assert.IsType<Button>(rail.Children[0]);
                var row = Assert.IsType<StackPanel>(frame.GetEntryButton(entry)!.Content);
                return (
                    back,
                    Assert.IsType<TextBlock>(back.Content),
                    Assert.IsType<TextBlock>(row.Children[0]),
                    Assert.IsType<TextBlock>(row.Children[1]));
            }

            static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

            // Default accent: the dark-rail look sister apps rely on stays untouched.
            var dark = new AvaloniaBackstageFrame(
                new AvaloniaBackstageAccent(Colors.Black, Colors.Gray, Colors.Blue, Colors.DimGray),
                Entries());
            var darkRail = Rail(dark, "Info");
            ColorOf(darkRail.BackIcon.Foreground).Should().Be(Colors.White);
            ColorOf(darkRail.Icon.Foreground).Should().Be(Colors.White);
            ColorOf(darkRail.Label.Foreground).Should().Be(Colors.White);
            darkRail.Back.HorizontalAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Stretch);
            dark.Show("Info");
            dark.GetEntryButton("Info")!.FontWeight.Should().Be(FontWeight.Normal);

            // Light rail (the FreeX WPF File screen): dark ink everywhere plus the WPF button states.
            var ink = Color.FromRgb(0x24, 0x2A, 0x31);
            var selected = Color.FromRgb(0xD8, 0xE5, 0xE9);
            var light = new AvaloniaBackstageFrame(
                new AvaloniaBackstageAccent(
                    Sidebar: Color.FromRgb(0xF3, 0xF4, 0xF6),
                    Hover: Color.FromRgb(0xE5, 0xE9, 0xED),
                    Selected: selected,
                    Separator: Color.FromRgb(0xD7, 0xDC, 0xE2),
                    Foreground: ink)
                {
                    FlatRailChrome = true,
                },
                Entries());
            var lightRail = Rail(light, "Info");
            ColorOf(lightRail.BackIcon.Foreground).Should().Be(ink);
            ColorOf(lightRail.Icon.Foreground).Should().Be(ink);
            ColorOf(lightRail.Label.Foreground).Should().Be(ink);
            lightRail.Back.HorizontalAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Left);
            lightRail.Back.FocusAdorner.Should().BeNull();

            var infoButton = light.GetEntryButton("Info")!;
            var printButton = light.GetEntryButton("Print")!;
            infoButton.FocusAdorner.Should().BeNull();

            light.Show("Info");
            ColorOf(infoButton.Background).Should().Be(selected);
            infoButton.FontWeight.Should().Be(FontWeight.SemiBold);
            printButton.FontWeight.Should().Be(FontWeight.Normal);

            light.TryActivateEntry("Print").Should().BeTrue();
            ColorOf(printButton.Background).Should().Be(selected);
            printButton.FontWeight.Should().Be(FontWeight.SemiBold);
            ColorOf(infoButton.Background).Should().Be(Colors.Transparent);
            infoButton.FontWeight.Should().Be(FontWeight.Normal);
        }, CancellationToken.None);
    }

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

    private static Color ToColor(BackstageVisualColor color) => Color.FromRgb(color.Red, color.Green, color.Blue);
}
