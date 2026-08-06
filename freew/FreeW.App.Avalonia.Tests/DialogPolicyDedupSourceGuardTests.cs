using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class DialogPolicyDedupSourceGuardTests
{
    [Fact]
    public void Sort_adapters_delegate_catalog_and_result_construction()
    {
        var wpf = Read("FreeW.App.Host", "SortDialog.cs");
        var avalonia = Read("FreeW.App.Avalonia", "ParagraphCommandDialogs.cs");

        wpf.Should().Contain("SortDialogPlanner.TypeChoices");
        wpf.Should().Contain("SortDialogPlanner.BuildResult(");
        wpf.Should().NotContain("record struct SortChoice");
        wpf.Should().NotContain("SortKind KindOf");
        avalonia.Should().Contain("SortDialogPlanner.BuildResult(");
    }

    [Fact]
    public void Combine_adapters_delegate_projection_validation_and_result_construction()
    {
        foreach (var source in new[]
        {
            Read("FreeW.App.Host", "CombineDocumentsDialog.cs"),
            Read("FreeW.App.Avalonia", "ReviewCompareCombineDialogs.cs")
        })
        {
            source.Should().Contain("ReviewCompareCombineWorkflow.BuildCombineDialogPlan(");
            source.Should().Contain("ReviewCompareCombineWorkflow.TryBuildCombineDialogResult(");
            source.Should().NotContain("new CombineDocumentsDialogResult(");
            source.Should().NotContain("TruncatePath(");
        }
    }

    [Fact]
    public void Information_dialog_adapters_delegate_copy_formatting_and_grouping()
    {
        var statisticsSources = new[]
        {
            Read("FreeW.App.Host", "StatisticsDialog.cs"),
            Read("FreeW.App.Avalonia", "WordCountDialog.cs")
        };
        foreach (var source in statisticsSources)
        {
            source.Should().Contain("StatisticsDialogPlanner.Build(");
            source.Should().NotContain("FormatReadingTime(");
            source.Should().NotContain("DescribeEase(");
        }

        var accessibilitySources = new[]
        {
            Read("FreeW.App.Host", "AccessibilityReportDialog.cs"),
            Read("FreeW.App.Avalonia", "SafetyDialogs.cs")
        };
        foreach (var source in accessibilitySources)
        {
            source.Should().Contain("AccessibilityReportDialogPlanner.Build(");
            source.Should().NotContain("report.IsClean");
            source.Should().NotContain("report.Issues.Where");
        }
    }

    [Fact]
    public void Caption_and_header_footer_adapters_delegate_catalog_and_copy()
    {
        var caption = Read("FreeW.App.Avalonia", "CaptionDialog.cs");
        var headerFooter = Read("FreeW.App.Avalonia", "HeaderFooterTextDialog.cs");

        caption.Should().Contain("CaptionDialogPlanner.Build(");
        caption.Should().Contain("CaptionDialogPlanner.BuildResult(");
        caption.Should().NotContain("CaptionLabel[] Labels");
        headerFooter.Should().Contain("HeaderFooterTextDialogPlanner.Build(");
        headerFooter.Should().Contain("HeaderFooterTextDialogPlanner.BuildResult(");
    }

    private static string Read(string project, string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", project, fileName));
    }
}
