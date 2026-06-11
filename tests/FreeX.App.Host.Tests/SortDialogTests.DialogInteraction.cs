using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
    [Fact]
    public void DialogLayout_KeepsSortLevelToolbarAndActionButtonsSeparated()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SortDialog([
                new SortDialogLevel(0, true),
                new SortDialogLevel(1, false),
                new SortDialogLevel(2, true)
            ]);
            dialog.Width = dialog.MinWidth;
            dialog.Height = dialog.MinHeight;
            dialog.Show();

            try
            {
                dialog.UpdateLayout();

                dialog.ResizeMode.Should().Be(ResizeMode.CanResizeWithGrip);
                var root = dialog.Content.Should().BeOfType<Grid>().Subject;
                root.RowDefinitions.Should().HaveCount(4);
                root.RowDefinitions[1].Height.GridUnitType.Should().Be(GridUnitType.Star);

                var levelsGrid = GetControl<DataGrid>(dialog, "_levelsGrid");
                var add = GetControl<Button>(dialog, "_addLevelButton");
                var delete = GetControl<Button>(dialog, "_deleteLevelButton");
                var copy = GetControl<Button>(dialog, "_copyLevelButton");
                var moveUp = GetControl<Button>(dialog, "_moveUpButton");
                var moveDown = GetControl<Button>(dialog, "_moveDownButton");
                var options = GetControl<Button>(dialog, "_optionsButton");
                var ok = WpfTestTree.FindVisualDescendants<Button>(dialog).Single(button => button.IsDefault);
                var cancel = WpfTestTree.FindVisualDescendants<Button>(dialog).Single(button => button.IsCancel);
                var toolbarButtons = new[] { add, delete, copy, moveUp, moveDown, options };

                levelsGrid.ActualHeight.Should().BeGreaterThan(180);
                var gridBounds = BoundsRelativeTo(root, levelsGrid);
                toolbarButtons.Min(button => BoundsRelativeTo(root, button).Top)
                    .Should()
                    .BeGreaterThanOrEqualTo(gridBounds.Bottom - 0.5);
                var toolbarBottom = toolbarButtons.Max(button => BoundsRelativeTo(root, button).Bottom);
                BoundsRelativeTo(root, ok).Top.Should().BeGreaterThanOrEqualTo(toolbarBottom - 0.5);
                BoundsRelativeTo(root, cancel).Top.Should().BeGreaterThanOrEqualTo(toolbarBottom - 0.5);

                foreach (var element in toolbarButtons.Cast<FrameworkElement>().Append(ok).Append(cancel).Prepend(levelsGrid))
                {
                    element.ActualWidth.Should().BeGreaterThan(0);
                    element.ActualHeight.Should().BeGreaterThan(0);
                    AssertInside(root, element);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ToolbarButtons_EnableOnlyValidLevelActions()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SortDialog([
                new SortDialogLevel(0, true),
                new SortDialogLevel(1, true)
            ]);
            dialog.Show();
            try
            {
                var grid = GetControl<DataGrid>(dialog, "_levelsGrid");
                var delete = GetControl<Button>(dialog, "_deleteLevelButton");
                var copy = GetControl<Button>(dialog, "_copyLevelButton");
                var moveUp = GetControl<Button>(dialog, "_moveUpButton");
                var moveDown = GetControl<Button>(dialog, "_moveDownButton");

                grid.SelectedIndex.Should().Be(0);
                delete.IsEnabled.Should().BeTrue();
                copy.IsEnabled.Should().BeTrue();
                moveUp.IsEnabled.Should().BeFalse();
                moveDown.IsEnabled.Should().BeTrue();

                grid.SelectedIndex = 1;
                moveUp.IsEnabled.Should().BeTrue();
                moveDown.IsEnabled.Should().BeFalse();

                delete.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                grid.Items.Count.Should().Be(1);
                delete.IsEnabled.Should().BeFalse();
                copy.IsEnabled.Should().BeTrue();
                moveUp.IsEnabled.Should().BeFalse();
                moveDown.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
