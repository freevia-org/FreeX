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
            Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.Backstage.cs"),
            Read(repoRoot, "tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"),
        };

        sources.Should().OnlyContain(source => source.Contains(".Resolve(", StringComparison.Ordinal));
        string.Join(Environment.NewLine, sources).Should().NotContain("value.TextKey is");
    }

    [Fact]
    public void CompletionStatusAndFocusPolicies_AreNotReintroducedInRenderers()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var fill = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.FillSeries.cs");
        var main = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.cs");
        var chartLayout = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.ChartTabs.cs");
        var chartQuick = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.ChartFormatTextTabs.cs");
        var print = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.Print.cs");

        fill.Should().Contain("FillSeriesPlanner.DescribeNoSeed");
        fill.Should().MatchRegex(@"FillSeriesPlanner\s*\.DescribeCommandFailure");
        fill.Should().MatchRegex(@"FillSeriesPlanner\s*\.DescribeSuccess");
        fill.Should().NotContain("\"FillSeries_NoSeed\"");
        fill.Should().NotContain("\"FillSeries_Failed\"");
        fill.Should().NotContain("\"FillSeries_Filled\"");

        main.Should().Contain("GoalSeekStatusDialogPlanner.DescribeExecutionFailure");
        main.Should().NotContain("Goal Seek request for {setCell} is invalid.");
        main.Should().NotContain("Goal Seek result for {changingCell} could not be applied.");

        chartLayout.Should().MatchRegex(@"ChartWorkflowCommandCatalog\s*\.DescribeCommandResult");
        chartQuick.Should().MatchRegex(@"ChartWorkflowCommandCatalog\s*\.DescribeCommandResult");
        (chartLayout + chartQuick).Should().NotContain("CommandAppliedStatusResourceKey");
        (chartLayout + chartQuick).Should().NotContain("CommandFailedStatusResourceKey");

        print.Should().Contain("PrintSettingsPlanner.InitialDialogFocusTarget");
        print.Should().NotContain("dialog.Opened += (_, _) => printButton.Focus()");
    }

    [Fact]
    public void BackstageFrameRenderers_UseSharedNavigationKeyResolution()
    {
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpf = Read(repoRoot, "src", "FreeX.App.Host", "MainWindow.BackstageFrame.cs");
        var avalonia = Read(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.LiveBackstage.cs");
        var combined = wpf + Environment.NewLine + avalonia;

        combined.Should().Contain("FreeXBackstageTextValue.ResolveKey");
        combined.Should().Contain("FreeXBackstageTextValue.ResolveOptionalKey");
        combined.Should().NotContain("ResolveOptionalBackstageText");
        combined.Should().NotContain("ResolveOptionalLiveBackstageText");
        combined.Should().NotContain("key is null ? null :");
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
