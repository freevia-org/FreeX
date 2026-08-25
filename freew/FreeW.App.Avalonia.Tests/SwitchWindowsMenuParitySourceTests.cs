using Avalonia.Controls;

namespace FreeW.App.Avalonia.Tests;

public sealed class SwitchWindowsMenuParitySourceTests
{
    [Fact]
    public void Switch_windows_menu_marks_the_active_window_as_a_checkable_item()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("IsChecked = ReferenceEquals(target, this)");
        source.Should().Contain("ToggleType = MenuItemToggleType.CheckBox");
    }
}
