using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Shell.Wpf;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class SisterAppWindowFrameBuilderTests
{
    [StaFact]
    public void Build_ComposesTitleBarAboveBodyAndBackstageOverlay()
    {
        var titleBar = new Border();
        var body = new Grid();
        var backstage = new UserControl();

        var result = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(titleBar, body, backstage));

        result.Root.RowDefinitions.Should().HaveCount(2);
        result.Root.RowDefinitions[0].Height.Should().Be(GridLength.Auto);
        result.Root.RowDefinitions[1].Height.Should().Be(new GridLength(1, GridUnitType.Star));
        result.Root.Children.Cast<UIElement>().Should().Equal(titleBar, result.BelowTitle);
        Grid.GetRow(titleBar).Should().Be(0);
        Grid.GetRow(result.BelowTitle).Should().Be(1);

        result.BelowTitle.Children.Cast<UIElement>().Should().Equal(body, backstage);
    }

    [StaFact]
    public void WindowTitleBinder_ComposesAndUpdatesWindowAndTitleText()
    {
        var window = new Window();
        var titleText = new TextBlock();

        var title = SisterWpfWindowTitleBinder.Update(
            window,
            titleText,
            new SisterWpfWindowTitleSpec(
                DisplayName: "Quarterly Review",
                ApplicationName: "FreeP",
                IsDirty: true,
                DirtyMarker: " *",
                Separator: " \u2014 "));

        title.Should().Be("Quarterly Review * \u2014 FreeP");
        window.Title.Should().Be(title);
        titleText.Text.Should().Be(title);
    }

    [StaFact]
    public void ClientFrameBuilder_ComposesChromeWorkAreaAndStatusRows()
    {
        var chrome = new Border();
        var workArea = new Grid();
        var statusBar = new Border();

        var result = SisterAppClientFrameBuilder.Build(new SisterAppClientFrameSpec(
            Chrome: chrome,
            WorkArea: workArea,
            StatusBar: statusBar));

        result.Root.RowDefinitions.Should().HaveCount(3);
        result.Root.RowDefinitions[0].Height.Should().Be(GridLength.Auto);
        result.Root.RowDefinitions[1].Height.Should().Be(new GridLength(1, GridUnitType.Star));
        result.Root.RowDefinitions[2].Height.Should().Be(GridLength.Auto);
        result.Root.Children.Cast<UIElement>().Should().Equal(chrome, workArea, statusBar);
        Grid.GetRow(chrome).Should().Be(0);
        Grid.GetRow(workArea).Should().Be(1);
        Grid.GetRow(statusBar).Should().Be(2);
    }

    [StaFact]
    public void ClientFrameBuilder_ComposesOptionalPanelRowsFromSharedContract()
    {
        var chrome = new Border();
        var topPanel1 = new Border();
        var topPanel2 = new Border();
        var workArea = new Grid();
        var bottomPanel1 = new Border();
        var bottomPanel2 = new Border();
        var statusBar = new Border();

        var result = SisterAppClientFrameBuilder.Build(new SisterAppClientFrameSpec(
            Chrome: chrome,
            WorkArea: workArea,
            StatusBar: statusBar,
            BottomPanelsAboveStatus: [bottomPanel1, bottomPanel2],
            TopPanelsBelowChrome: [topPanel1, topPanel2]));

        result.Root.RowDefinitions.Select(row => row.Height).Should().Equal(
            GridLength.Auto,
            GridLength.Auto,
            GridLength.Auto,
            new GridLength(1, GridUnitType.Star),
            GridLength.Auto,
            GridLength.Auto,
            GridLength.Auto);
        result.Root.Children.Cast<UIElement>().Should().Equal(
            chrome,
            topPanel1,
            topPanel2,
            workArea,
            bottomPanel1,
            bottomPanel2,
            statusBar);
        Grid.GetRow(chrome).Should().Be(0);
        Grid.GetRow(topPanel1).Should().Be(1);
        Grid.GetRow(topPanel2).Should().Be(2);
        Grid.GetRow(workArea).Should().Be(3);
        Grid.GetRow(bottomPanel1).Should().Be(4);
        Grid.GetRow(bottomPanel2).Should().Be(5);
        Grid.GetRow(statusBar).Should().Be(6);
    }

    [StaFact]
    public void StatusBarChrome_ComposesElasticLeftContentAndPinnedRightItems()
    {
        var background = new SolidColorBrush(Color.FromRgb(1, 2, 3));
        var left = new TextBlock();
        var right1 = new Button();
        var right2 = new Slider();

        var result = SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(background, left, [right1, right2]));

        result.Root.Background.Should().BeSameAs(background);
        result.Root.MinHeight.Should().Be(26);
        result.Root.Child.Should().BeSameAs(result.Layout);
        result.Layout.ColumnDefinitions.Should().HaveCount(3);
        result.Layout.ColumnDefinitions[0].Width.Should().Be(new GridLength(1, GridUnitType.Star));
        result.Layout.ColumnDefinitions[1].Width.Should().Be(GridLength.Auto);
        result.Layout.ColumnDefinitions[2].Width.Should().Be(GridLength.Auto);
        result.LeftHost.Children.Cast<UIElement>().Should().Equal(left);
        Grid.GetColumn(result.LeftHost).Should().Be(0);
        Grid.GetColumn(right1).Should().Be(1);
        Grid.GetColumn(right2).Should().Be(2);
    }

    [StaFact]
    public void StatusBarChrome_CreatesSharedInfoTextAndSeparatorStyles()
    {
        var text = SisterAppStatusBarChrome.CreateInfoText("Slides: 1");
        var separator = SisterAppStatusBarChrome.CreateSeparator();

        text.Text.Should().Be("Slides: 1");
        text.Foreground.Should().Be(Brushes.White);
        text.FontSize.Should().Be(12);
        text.VerticalAlignment.Should().Be(VerticalAlignment.Center);
        text.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);

        separator.Width.Should().Be(1);
        separator.Margin.Should().Be(new Thickness(8, 3, 8, 3));
        separator.VerticalAlignment.Should().Be(VerticalAlignment.Stretch);
        separator.Fill.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    }
}
