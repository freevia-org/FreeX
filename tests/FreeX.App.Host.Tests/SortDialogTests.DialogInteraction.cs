using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
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
