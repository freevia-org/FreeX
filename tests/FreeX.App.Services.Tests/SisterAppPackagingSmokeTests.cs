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
    public void RemoveArgumentTokens_PreservesNonSmokeArgumentsInOrder()
    {
        var filtered = SisterAppPackagingSmoke.RemoveArgumentTokens(
            ["Book.csv", "--PACKAGING-SMOKE", "--other", "--packaging-smoke"]);

        filtered.Should().Equal("Book.csv", "--other");
    }

    [Fact]
    public void FreeXPackagingSmoke_ConsumesSharedArgumentScanningFromValidationTool()
    {
        var root = FindRepositoryRoot();
        var freeX = Read(root, "tools", "FreeX.Validation.Avalonia", "PackagingSmokeValidation.cs");
        var shippingProgram = Read(root, "src", "FreeX.App.Avalonia", "Program.cs");
        var parityProgram = Read(root, "tools", "FreeX.ParityCapture.Avalonia", "Capture", "Program.cs");
        var validationProgram = Read(root, "tools", "FreeX.Validation.Avalonia", "Program.cs");

        freeX.Should().Contain("public const string Argument = SisterAppPackagingSmoke.Argument;")
            .And.Contain("SisterAppPackagingSmoke.TryRun(args, output, error, Execute, out exitCode)")
            .And.Contain("new WorkbookStartupSmokeService().Run(startupArguments)")
            .And.NotContain("SisterAppPackagingSmoke.HasArgument(")
            .And.NotContain("SisterAppPackagingSmoke.RemoveArgumentTokens(")
            .And.NotContain("SisterAppPackagingSmoke.WriteReport(")
            .And.NotContain("public const string Argument = \"--packaging-smoke\"");
        shippingProgram.Should().NotContain("PackagingSmokeCommand");
        parityProgram.Should().NotContain("PackagingSmokeCommand");
        validationProgram.Should().Contain("PackagingSmokeCommand.TryRun");
        validationProgram.Should().Contain("ValidationHostCommandRouteExecutor.Immediate(");
        File.Exists(Path.Combine(root, "src", "FreeX.App.Services", "WorkbookStartupSmokeService.cs"))
            .Should().BeFalse();
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

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
