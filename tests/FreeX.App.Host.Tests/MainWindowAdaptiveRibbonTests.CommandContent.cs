using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowAdaptiveRibbonTests
{
    [Fact]
    public void IconOnlyRibbonCommandsRemainCenterAligned()
    {
        StaTestRunner.Run(() =>
        {
            var icon = new TextBlock { Text = "\uE16D" };
            RibbonMetadata.SetRole(icon, RibbonMetadataRole.CommandIcon);
            var label = new TextBlock { Text = "Paste" };
            RibbonMetadata.SetRole(label, RibbonMetadataRole.CommandLabel);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Children = { icon, label }
            };
            var button = new Button
            {
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right,
                Content = content
            };
            RibbonMetadata.SetCompactWidths(button, 72, 32);

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
    public void IconOnlySmallRibbonCommandsCenterIconAndRemoveLabelSpacer()
    {
        StaTestRunner.Run(() =>
        {
            var createContent = typeof(MainWindow)
                .GetMethod("CreateRibbonCommandContent", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateRibbonCommandContent");
            var content = (Grid)createContent.Invoke(null, ["Text to Columns", "Text to Columns", RibbonCommandLayoutKind.Small])!;
            var button = new Button
            {
                Content = content,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left
            };
            RibbonMetadata.SetCompactWidths(button, 150, 24);
            var label = content.Children
                .OfType<TextBlock>()
                .First(RibbonMetadata.IsCommandLabel);

            var compactLevel = typeof(MainWindow).GetNestedType("RibbonCompactLevel", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "RibbonCompactLevel");
            var iconOnly = Enum.Parse(compactLevel, "IconOnly");
            var full = Enum.Parse(compactLevel, "Full");
            var setCompact = typeof(MainWindow)
                .GetMethod("SetRibbonButtonCompact", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetRibbonButtonCompact");

            setCompact.Invoke(null, [button, iconOnly]);

            button.HorizontalContentAlignment.Should().Be(System.Windows.HorizontalAlignment.Center);
            content.HorizontalAlignment.Should().Be(System.Windows.HorizontalAlignment.Center);
            content.ColumnDefinitions[1].Width.Value.Should().Be(0);
            label.Visibility.Should().Be(Visibility.Collapsed);

            setCompact.Invoke(null, [button, full]);

            button.HorizontalContentAlignment.Should().Be(System.Windows.HorizontalAlignment.Left);
            content.HorizontalAlignment.Should().Be(System.Windows.HorizontalAlignment.Left);
            content.ColumnDefinitions[1].Width.Value.Should().Be(5);
            label.Visibility.Should().Be(Visibility.Visible);
        });
    }

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
            var content = (FrameworkElement)createContent.Invoke(null, ["Protect Sheet", "Protect Sheet", RibbonCommandLayoutKind.Large])!;
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
            var content = (Grid)createContent.Invoke(null, ["Sort & Filter", "Sort & Filter", RibbonCommandLayoutKind.Small])!;
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
    public void IconOnlySplitButtonsKeepEnoughWidthForIconAndChevron()
    {
        StaTestRunner.Run(() =>
        {
            var createContent = typeof(MainWindow)
                .GetMethod("CreateRibbonCommandContent", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateRibbonCommandContent");
            var ensureChevron = typeof(MainWindow)
                .GetMethod("EnsureRibbonDropdownChevron", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EnsureRibbonDropdownChevron");
            var setCompact = typeof(MainWindow)
                .GetMethod("SetRibbonButtonCompact", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetRibbonButtonCompact");
            var compactLevel = typeof(MainWindow).GetNestedType("RibbonCompactLevel", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "RibbonCompactLevel");
            var iconOnly = Enum.Parse(compactLevel, "IconOnly");
            var content = (Grid)createContent.Invoke(null, ["Fill", "Fill", RibbonCommandLayoutKind.Small])!;
            var button = new Button
            {
                Content = content,
                Width = 74,
                Height = 24
            };

            ensureChevron.Invoke(null, [button]);
            RibbonMetadata.SetCompactWidths(button, 74, 24);

            setCompact.Invoke(null, [button, iconOnly]);

            button.Width.Should().BeGreaterThanOrEqualTo(38,
                "icon-only split buttons need separate room for the 24px command icon and the chevron lane");
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
            var content = (Grid)createContent.Invoke(null, ["Sort & Filter", "Sort & Filter", RibbonCommandLayoutKind.Small])!;
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(4, 2, 4, 2),
                BorderThickness = new Thickness(0),
                Width = 128,
                Height = 24,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left
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
                chevronCenterX.Should().BeApproximately(dropdownCenterX, 0.75,
                    "the chevron should be centered inside the right-hand split hover lane");
                dropdownBounds.Left.Should().BeLessThanOrEqualTo(chevronBounds.Left + 0.5);
                dropdownBounds.Right.Should().BeGreaterThanOrEqualTo(chevronBounds.Right - 0.5);
                chevronBounds.Right.Should().BeGreaterThan(button.ActualWidth - button.Padding.Right - 12,
                    "the chevron should sit inside the right-aligned split lane instead of directly after the label");
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
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center
            };

            ensureChevron.Invoke(null, [button]);
            var window = ShowStandaloneRibbonButton(button, 70, 76);
            try
            {
                var chevron = WpfTestTree.FindVisualSelfAndDescendants<DependencyObject>(button)
                    .Concat(WpfTestTree.FindLogicalDescendants<DependencyObject>(button))
                    .OfType<FrameworkElement>()
                    .Distinct()
                    .Single(RibbonMetadata.IsDropdownChevron);
                var label = WpfTestTree.FindVisualSelfAndDescendants<DependencyObject>(button)
                    .Concat(WpfTestTree.FindLogicalDescendants<DependencyObject>(button))
                    .OfType<TextBlock>()
                    .Distinct()
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
                chevronCenterY.Should().BeApproximately(dropdownCenterY, 1.25,
                    "large split-button chevrons should be vertically centered in the lower dropdown band");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void IconOnlyRibbonMenuButtonDropdownZoneStartsAfterCommandIcon()
    {
        StaTestRunner.Run(() =>
        {
            var createContent = typeof(MainWindow)
                .GetMethod("CreateRibbonCommandContent", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateRibbonCommandContent");
            var ensureChevron = typeof(MainWindow)
                .GetMethod("EnsureRibbonDropdownChevron", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EnsureRibbonDropdownChevron");
            var setCompact = typeof(MainWindow)
                .GetMethod("SetRibbonButtonCompact", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetRibbonButtonCompact");
            var compactLevel = typeof(MainWindow).GetNestedType("RibbonCompactLevel", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "RibbonCompactLevel");
            var iconOnly = Enum.Parse(compactLevel, "IconOnly");
            var content = (Grid)createContent.Invoke(null, ["Sort & Filter", "Sort & Filter", RibbonCommandLayoutKind.Small])!;
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(4, 0, 4, 0),
                BorderThickness = new Thickness(0),
                Width = 38,
                Height = 24
            };
            RibbonMetadata.SetCompactWidths(button, 128, 38);

            ensureChevron.Invoke(null, [button]);
            setCompact.Invoke(null, [button, iconOnly]);
            content.ColumnDefinitions[^1].Width.Value.Should().Be(14);
            var window = ShowStandaloneRibbonButton(button, 38, 24);
            try
            {
                var icon = WpfTestTree.FindVisualSelfAndDescendants<DependencyObject>(button)
                    .OfType<FrameworkElement>()
                    .First(element => RibbonMetadata.IsCommandIcon(element));
                var iconBounds = icon.TransformToAncestor(button)
                    .TransformBounds(new Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
                var dropdownBounds = GetRibbonDropdownZoneBounds(button);

                dropdownBounds.X.Should().BeGreaterThanOrEqualTo(iconBounds.Right - 0.5);
                dropdownBounds.Height.Should().BeApproximately(button.ActualHeight, 0.5);
            }
            finally
            {
                window.Close();
            }
        });
    }

}
