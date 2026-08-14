using FluentAssertions;

namespace FreeW.App.Presentation.Tests;

public sealed class CustomDialogChromeOwnershipTests
{
    [Fact]
    public void CustomAvaloniaDialogStylesFlowThroughTheSharedWindowBase()
    {
        var baseWindow = ReadSource("FreeW.App.Avalonia", "FreeWDialogWindow.cs");
        baseWindow.Should().Contain(
            "protected FreeWDialogWindow(AvaloniaCompactDialogChromeStyle style)");
        baseWindow.Should().Contain(": base(style)");

        var routes = new Dictionary<string, string>
        {
            ["FontDialog.cs"] = ": base(DialogChromeStyle)",
            ["OptionsDialog.cs"] = ": base(DialogChromeStyle)",
            ["StyleDialog.cs"] = ": base(DialogChromeStyle)",
            ["MultilevelListDialog.cs"] = ": base(Chrome)",
        };

        foreach (var (fileName, constructor) in routes)
        {
            var source = ReadSource("FreeW.App.Avalonia", fileName);
            source.Should().Contain(constructor, $"{fileName} must give its authority style to the shared base");
            source.Should().NotContain(
                "ApplyDescendantChrome(this,",
                $"{fileName} must not repair a default-style pass after the window opens");
        }
    }

    private static string ReadSource(string project, string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", project, fileName));
    }
}
