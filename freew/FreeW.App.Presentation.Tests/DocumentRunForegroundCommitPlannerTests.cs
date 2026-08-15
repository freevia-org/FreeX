using System.IO;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentRunForegroundCommitPlannerTests
{
    [Fact]
    public void InheritedEffectiveColor_DoesNotBecomeExplicitModelFormatting()
    {
        DocumentRunForegroundCommitPlanner.ResolveColorHex(
                retainedColorHex: null,
                localColorHex: null,
                isVisuallyHidden: false)
            .Should().BeNull();
    }

    [Fact]
    public void LocalColor_IsTheAuthoredCommitValue()
    {
        DocumentRunForegroundCommitPlanner.ResolveColorHex(
                retainedColorHex: "#112233",
                localColorHex: "#AABBCC",
                isVisuallyHidden: false)
            .Should().Be("#AABBCC");
    }

    [Fact]
    public void RemovingLocalColor_ClearsPreviouslyRetainedExplicitColor()
    {
        DocumentRunForegroundCommitPlanner.ResolveColorHex(
                retainedColorHex: "#112233",
                localColorHex: null,
                isVisuallyHidden: false)
            .Should().BeNull("Automatic removes explicit run color and restores theme inheritance");
    }

    [Fact]
    public void HiddenPresentationChrome_PreservesRetainedModelColor()
    {
        DocumentRunForegroundCommitPlanner.ResolveColorHex(
                retainedColorHex: "#445566",
                localColorHex: null,
                isVisuallyHidden: true)
            .Should().Be("#445566");
    }

    [Fact]
    public void WpfCommitAdapter_UsesLocalForegroundAndSharedPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "Editing",
            "DocumentView.cs"));

        source.Should().Contain("run.ReadLocalValue(TextElement.ForegroundProperty)")
            .And.Contain("DocumentRunForegroundCommitPlanner.ResolveColorHex(")
            .And.Contain("normalizedColor is null ? null! : new SolidColorBrush(color)")
            .And.NotContain("normalizedColor is null ? Brushes.Black : new SolidColorBrush(color)");
    }
}
