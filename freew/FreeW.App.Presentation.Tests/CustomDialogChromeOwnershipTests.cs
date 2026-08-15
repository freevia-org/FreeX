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

    [Fact]
    public void CommonWpfDialogsFlowThroughTheSharedWindowBase()
    {
        var baseWindow = ReadSource("FreeW.App.Host", "FreeWDialogWindow.cs");
        baseWindow.Should().Contain("class FreeWDialogWindow : DialogWindow");

        var routes = new[]
        {
            "FontDialog.cs",
            "ParagraphBreaksDialog.cs",
            "PasteSpecialDialog.cs",
            "MultilevelListDialog.cs",
            "StyleDialog.cs",
        };

        foreach (var fileName in routes)
        {
            var source = ReadSource("FreeW.App.Host", fileName);
            source.Should().Contain("new FreeWDialogWindow", $"{fileName} must consume shared WPF dialog chrome")
                .And.NotContain("new Window", $"{fileName} must not bypass the shared WPF dialog base");
        }
    }

    [Fact]
    public void WpfRibbonModalRoutesFlowThroughTheSharedWindowBase()
    {
        var source = ReadSource("FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");

        source.Should().NotContain("new Window", "ribbon modal routes must not bypass shared WPF dialog chrome");
        Count(source, "new FreeWDialogWindow").Should().Be(
            13,
            "all color, note, caption, Quick Part, source, author, header/footer, and text prompt routes are modal dialogs");
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadSource(string project, params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        return File.ReadAllText(Path.Combine([root, "freew", project, .. parts]));
    }
}
