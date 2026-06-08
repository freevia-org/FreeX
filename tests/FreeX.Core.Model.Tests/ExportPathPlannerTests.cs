using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class ExportPathPlannerTests
{
    [Theory]
    [InlineData("report.xps", ExportFileFormat.Xps)]
    [InlineData("report.XPS", ExportFileFormat.Xps)]
    [InlineData("report.pdf", ExportFileFormat.Pdf)]
    [InlineData("report", ExportFileFormat.Pdf)]
    [InlineData("report.export", ExportFileFormat.Pdf)]
    public void InferFormat_UsesXpsOnlyForXpsExtension(string path, ExportFileFormat expected)
    {
        ExportPathPlanner.InferFormat(path).Should().Be(expected);
    }

    [Fact]
    public void InferFormat_DefaultsToPdfForMalformedPath()
    {
        ExportPathPlanner.InferFormat("bad\0report.xps").Should().Be(ExportFileFormat.Pdf);
    }

    [Fact]
    public void Plan_AppendsPdfExtensionForExtensionlessPdfRequests()
    {
        var plan = ExportPathPlanner.Plan("report");

        plan.Should().Be(new ExportPathPlan("report.pdf", ExportFileFormat.Pdf));
        plan.ActualPath.Should().Be("report.pdf");
        plan.UsesXpsFallback.Should().BeFalse();
    }

    [Theory]
    [InlineData("report.export", "report.pdf")]
    [InlineData("report.xlsx", "report.pdf")]
    public void Plan_NormalizesMismatchedExtensionForInferredPdfRequests(string path, string expectedPath)
    {
        var plan = ExportPathPlanner.Plan(path);

        plan.Should().Be(new ExportPathPlan(expectedPath, ExportFileFormat.Pdf));
    }

    [Fact]
    public void Plan_KeepsMalformedInferredPdfPathForHandledExportFailure()
    {
        var path = "bad\0report.xlsx";

        var plan = ExportPathPlanner.Plan(path);

        plan.Should().Be(new ExportPathPlan(path, ExportFileFormat.Pdf));
        plan.ActualPath.Should().Be(path);
    }

    [Theory]
    [InlineData("report.pdf", ExportFileFormat.Xps, "report.xps")]
    [InlineData("report.xps", ExportFileFormat.Pdf, "report.pdf")]
    [InlineData("report.export", ExportFileFormat.Pdf, "report.pdf")]
    [InlineData("report.export", ExportFileFormat.Xps, "report.xps")]
    public void Plan_NormalizesMismatchedExtensionForExplicitFormatRequests(
        string path,
        ExportFileFormat format,
        string expectedPath)
    {
        var plan = ExportPathPlanner.Plan(path, format);

        plan.Should().Be(new ExportPathPlan(expectedPath, format));
    }

    [Fact]
    public void ShouldPromptForNormalizedOverwrite_WhenDialogPathChangesToExistingTarget()
    {
        var plan = ExportPathPlanner.Plan("report.txt", ExportFileFormat.Pdf);

        ExportPathPlanner.ShouldPromptForNormalizedOverwrite(
                "report.txt",
                plan,
                path => path.Equals("report.pdf", StringComparison.OrdinalIgnoreCase))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ShouldPromptForNormalizedOverwrite_UsesPlatformPathComparison()
    {
        var requestedPath = Path.GetFullPath("report.pdf");
        var plan = new ExportPathPlan(Path.GetFullPath("REPORT.pdf"), ExportFileFormat.Pdf);

        ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, plan, _ => true)
            .Should()
            .Be(!OperatingSystem.IsWindows());
    }

    [Theory]
    [InlineData("report.pdf", "report.xps")]
    [InlineData("report", "report.xps")]
    [InlineData("report.output", "report.xps")]
    public void GetFallbackXpsPath_ChangesRequestedPathToXps(string requestedPath, string expected)
    {
        ExportPathPlanner.GetFallbackXpsPath(requestedPath).Should().Be(expected);
    }
}
