using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class SisterAppPackagingSmokeTests
{
    [Fact]
    public void HasArgument_DetectsPackagingSmokeCaseInsensitively()
    {
        var found = SisterAppPackagingSmoke.HasArgument(["--other", "--PACKAGING-SMOKE"]);

        found.Should().BeTrue();
    }

    [Fact]
    public void FindReportPath_ReturnsValueAfterPackagingSmokeArgument()
    {
        var path = SisterAppPackagingSmoke.FindReportPath(["--other", "ignored", "--packaging-smoke", "report.txt"]);

        path.Should().Be("report.txt");
    }

    [Fact]
    public void FindReportPath_ReturnsNullWhenPackagingSmokeHasNoValue()
    {
        var path = SisterAppPackagingSmoke.FindReportPath(["--other", "--packaging-smoke"]);

        path.Should().BeNull();
    }

    [Fact]
    public void WriteReport_CreatesParentDirectoryAndWritesContent()
    {
        using var temp = new TestTemporaryDirectory();
        using var errors = new StringWriter();
        var report = Path.Combine(temp.Path, "nested", "packaging-smoke.txt");

        SisterAppPackagingSmoke.WriteReport(report, "packaging_smoke_status=passed\n", errors);

        File.ReadAllText(report).Should().Be("packaging_smoke_status=passed\n");
        errors.ToString().Should().BeEmpty();
    }
}
