using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Localization;

public sealed class RendererValidationPresentationOwnershipSourceGuardTests
{
    [Fact]
    public void Renderers_DoNotReintroduceValidationTextProjectionSwitches()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var rendererSources = new[]
        {
            Read(repoRoot, "src", "FreeX.App.Host", "GoalSeekInputParser.cs"),
            Read(repoRoot, "src", "FreeX.App.Host", "HyperlinkDialog.cs"),
            Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.PivotApplicationSession.cs"),
            Read(repoRoot, "src", "FreeX.App.Host", "ChartAxisFormatDialog.cs"),
            Read(repoRoot, "src", "FreeX.App.Host", "TextToColumnsDialog.cs"),
            Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs"),
            Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.PivotApplicationSession.cs"),
            Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.ChartFormatDialogs.cs"),
        };
        var combined = string.Join(Environment.NewLine, rendererSources);

        combined.Should().NotContain("GoalSeekRequestParseError.SetCellRequired =>");
        combined.Should().NotContain("HyperlinkDialogValidationError.MissingDocumentLocation =>");
        combined.Should().NotContain("PivotApplicationIssue.MissingSource =>");
        combined.Should().NotContain("ChartAxisFormatParseIssue.Maximum =>");
        combined.Should().NotContain("RefocusInvalidInputAfterWarning");
        combined.Should().Contain("DescribeValidationError");
        combined.Should().Contain("ChartValidationPresentationPlanner.Describe");
    }

    [Fact]
    public void BackstageRenderers_UseTheSharedTextValueResolver()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sources = new[]
        {
            Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.Backstage.cs"),
            Read(repoRoot, "src", "FreeX.App.Host", "LocalAccountPlanner.cs"),
            Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.Backstage.cs"),
            Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"),
        };

        sources.Should().OnlyContain(source => source.Contains(".Resolve(", StringComparison.Ordinal));
        string.Join(Environment.NewLine, sources).Should().NotContain("value.TextKey is");
    }

    [Fact]
    public void HyperlinkPlanner_IsOwnedByPresentation()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Presentation", "Hyperlinks", "HyperlinkDialogPlanner.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Services", "HyperlinkDialogPlanner.cs"))
            .Should().BeFalse();
    }

    private static string Read(string root, params string[] segments) =>
        File.ReadAllText(Path.Combine(segments.Prepend(root).ToArray()));
}
