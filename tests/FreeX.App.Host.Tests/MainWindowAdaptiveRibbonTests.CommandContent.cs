using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowAdaptiveRibbonTests
{
    [Fact]
    public void RibbonCommandButtonLabelRefresh_PreservesExistingIconContent()
    {
        StaTestRunner.Run(() =>
        {
            var createContent = typeof(MainWindow)
                .GetMethod("CreateRibbonCommandContent", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateRibbonCommandContent");
            var refreshLabel = typeof(MainWindow)
                .GetMethod("SetRibbonCommandButtonLabel", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetRibbonCommandButtonLabel");
            var content = (FrameworkElement)createContent.Invoke(
                null,
                ["Protect Sheet", "Protect Sheet", RibbonCommandLayoutKind.Large])!;
            var button = new Button { Content = content };

            refreshLabel.Invoke(null, [button, "Unprotect Sheet"]);

            button.Content.Should().BeSameAs(content);
            WpfTestTree.FindVisualSelfAndDescendants<DependencyObject>((DependencyObject)button.Content)
                .Concat(WpfTestTree.FindLogicalDescendants<DependencyObject>((DependencyObject)button.Content))
                .Distinct()
                .OfType<FrameworkElement>()
                .Should().Contain(element => RibbonMetadata.IsCommandIcon(element));
            WpfTestTree.FindVisualSelfAndDescendants<DependencyObject>((DependencyObject)button.Content)
                .Concat(WpfTestTree.FindLogicalDescendants<DependencyObject>((DependencyObject)button.Content))
                .Distinct()
                .OfType<TextBlock>()
                .Single(RibbonMetadata.IsCommandLabel)
                .Text.Should().Be("Unprotect Sheet");
        });
    }

    [Fact]
    public void IconOnlyRibbonMenuButtonsPlaceChevronInSideSegment()
    {
        StaTestRunner.Run(() =>
        {
            var grid = new Grid { Width = 24, Height = 24 };
            var icon = new Border { Width = 24, Height = 24 };
            RibbonMetadata.SetRole(icon, RibbonMetadataRole.CommandIcon);
            RibbonMetadata.SetCommandContentLayout(grid, RibbonCommandContentLayout.IconOnly);
            grid.Children.Add(icon);
            var button = new Button { Content = grid, Width = 38, Height = 24 };
            var addChevron = typeof(MainWindow)
                .GetMethod("AddRibbonDropdownChevronToGrid", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "AddRibbonDropdownChevronToGrid");

            addChevron.Invoke(null, [button, grid, RibbonCommandContentLayout.IconOnly]);

            var chevron = grid.Children
                .OfType<FrameworkElement>()
                .Single(RibbonMetadata.IsDropdownChevron);
            grid.ColumnDefinitions.Should().HaveCount(2);
            grid.ColumnDefinitions[0].Width.Value.Should().Be(24);
            grid.ColumnDefinitions[1].Width.Value.Should().Be(14);
            Grid.GetColumn(icon).Should().Be(0);
            Grid.GetColumn(chevron).Should().Be(1);
            chevron.Width.Should().Be(8);
            grid.Width.Should().BeGreaterThanOrEqualTo(38);
        });
    }

    [Fact]
    public void SmallRibbonMenuButtonsReserveIconOnlySplitWidth()
    {
        StaTestRunner.Run(() =>
        {
            var createContent = typeof(MainWindow)
                .GetMethod("CreateRibbonCommandContent", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateRibbonCommandContent");
            var ensureChevron = typeof(MainWindow)
                .GetMethod("EnsureRibbonDropdownChevron", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EnsureRibbonDropdownChevron");
            var content = (Grid)createContent.Invoke(
                null,
                ["Sort & Filter", "Sort & Filter", RibbonCommandLayoutKind.Small])!;
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(4, 2, 4, 2),
                BorderThickness = new Thickness(0),
                Width = 128,
                Height = 24
            };
            RibbonMetadata.SetCompactWidths(button, 128, 24);

            ensureChevron.Invoke(null, [button]);

            RibbonMetadata.TryGetCompactWidths(button, out var fullWidth, out var compactWidth).Should().BeTrue();
            fullWidth.Should().Be(128);
            compactWidth.Should().Be(38);
            content.ColumnDefinitions[^1].Width.Value.Should().Be(20);
        });
    }

    [Fact]
    public void SmallRibbonMenuButtonDropdownZoneAlignsChevronLaneToRightEdge()
    {
        StaTestRunner.Run(() =>
        {
            var createContent = typeof(MainWindow)
                .GetMethod("CreateRibbonCommandContent", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateRibbonCommandContent");
            var ensureChevron = typeof(MainWindow)
                .GetMethod("EnsureRibbonDropdownChevron", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EnsureRibbonDropdownChevron");
            var content = (Grid)createContent.Invoke(
                null,
                ["Sort & Filter", "Sort & Filter", RibbonCommandLayoutKind.Small])!;
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(4, 2, 4, 2),
                BorderThickness = new Thickness(0),
                Width = 128,
                Height = 24,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            ensureChevron.Invoke(null, [button]);
            var window = ShowStandaloneRibbonButton(button, 128, 24);
            try
            {
                var chevron = WpfTestTree.FindVisualSelfAndDescendants<DependencyObject>(button)
                    .Concat(WpfTestTree.FindLogicalDescendants<DependencyObject>(button))
                    .OfType<FrameworkElement>()
                    .Distinct()
                    .Single(RibbonMetadata.IsDropdownChevron);
                var chevronBounds = chevron.TransformToAncestor(button)
                    .TransformBounds(new Rect(0, 0, chevron.ActualWidth, chevron.ActualHeight));
                var dropdownBounds = GetRibbonDropdownZoneBounds(button);

                var chevronCenterX = chevronBounds.Left + chevronBounds.Width / 2;
                var dropdownCenterX = dropdownBounds.Left + dropdownBounds.Width / 2;

                dropdownBounds.Width.Should().BeApproximately(20, 0.5);
                dropdownBounds.Height.Should().BeApproximately(button.ActualHeight, 0.5);
                dropdownBounds.Right.Should().BeApproximately(button.ActualWidth, 0.5);
                chevronCenterX.Should().BeApproximately(dropdownCenterX, 0.75);
                dropdownBounds.Left.Should().BeLessThanOrEqualTo(chevronBounds.Left + 0.5);
                dropdownBounds.Right.Should().BeGreaterThanOrEqualTo(chevronBounds.Right - 0.5);
                chevronBounds.Right.Should().BeGreaterThan(button.ActualWidth - button.Padding.Right - 12);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void LargeRibbonMenuButtonDropdownBandCentersChevronBelowLabel()
    {
        StaTestRunner.Run(() =>
        {
            var createContent = typeof(MainWindow)
                .GetMethod("CreateRibbonCommandContent", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateRibbonCommandContent");
            var ensureChevron = typeof(MainWindow)
                .GetMethod("EnsureRibbonDropdownChevron", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EnsureRibbonDropdownChevron");
            var content = (StackPanel)createContent.Invoke(null, ["Paste", "Paste", RibbonCommandLayoutKind.Large])!;
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(3, 2, 3, 2),
                BorderThickness = new Thickness(0),
                Width = 70,
                Height = 76,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            ensureChevron.Invoke(null, [button]);
            var window = ShowStandaloneRibbonButton(button, 70, 76);
            try
            {
                var descendants = WpfTestTree.FindVisualSelfAndDescendants<DependencyObject>(button)
                    .Concat(WpfTestTree.FindLogicalDescendants<DependencyObject>(button))
                    .Distinct()
                    .ToList();
                var chevron = descendants
                    .OfType<FrameworkElement>()
                    .Single(RibbonMetadata.IsDropdownChevron);
                var label = descendants
                    .OfType<TextBlock>()
                    .Single(RibbonMetadata.IsCommandLabel);
                var chevronBounds = chevron.TransformToAncestor(button)
                    .TransformBounds(new Rect(0, 0, chevron.ActualWidth, chevron.ActualHeight));
                var labelBounds = label.TransformToAncestor(button)
                    .TransformBounds(new Rect(0, 0, label.ActualWidth, label.ActualHeight));
                var dropdownBounds = GetRibbonDropdownZoneBounds(button);

                var chevronCenterY = chevronBounds.Top + chevronBounds.Height / 2;
                var dropdownCenterY = dropdownBounds.Top + dropdownBounds.Height / 2;

                dropdownBounds.Height.Should().BeApproximately(20, 0.5);
                dropdownBounds.Top.Should().BeGreaterThanOrEqualTo(labelBounds.Bottom - 0.5);
                chevronCenterY.Should().BeApproximately(dropdownCenterY, 1.25);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
