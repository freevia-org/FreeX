namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonShortcutTextTests
{
    [Theory]
    [InlineData("Save (Ctrl+S)", "Save (Cmd+S)")]
    [InlineData("Undo (Ctrl+Z)", "Undo (Cmd+Z)")]
    [InlineData("Redo (Ctrl+Y)", "Redo (Shift+Cmd+Z)")]
    [InlineData("Ctrl+Shift+L", "Shift+Cmd+L")]
    [InlineData("Ctrl+Alt+V", "Cmd+Alt+V")]
    [InlineData("F2", "F2")]
    [InlineData("", "")]
    public void ForPlatform_MacOs_UsesCommandKey(string input, string expected) =>
        Assert.Equal(expected, RibbonShortcutText.ForPlatform(input, macOs: true));

    [Theory]
    [InlineData("Save (Ctrl+S)")]
    [InlineData("Redo (Ctrl+Y)")]
    [InlineData("Ctrl+Shift+L")]
    public void ForPlatform_WindowsAndLinux_KeepCtrl(string input) =>
        Assert.Equal(input, RibbonShortcutText.ForPlatform(input, macOs: false));

    [Fact]
    public void SisterQatCatalog_MacOsTooltips_UseCmdWhileWindowsKeepsCtrl()
    {
        Assert.Equal(
            ["Save (Cmd+S)", "Undo (Cmd+Z)", "Redo (Shift+Cmd+Z)"],
            SisterQuickAccessToolbarCatalog.GetDefaultCommands(macOs: true).Select(c => c.Tooltip).ToArray());
        Assert.Equal(
            ["Save (Ctrl+S)", "Undo (Ctrl+Z)", "Redo (Ctrl+Y)"],
            SisterQuickAccessToolbarCatalog.GetDefaultCommands(macOs: false).Select(c => c.Tooltip).ToArray());
    }
}
