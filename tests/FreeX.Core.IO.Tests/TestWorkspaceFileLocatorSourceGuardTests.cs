using System.Text.RegularExpressions;
using FluentAssertions;

public sealed class TestWorkspaceFileLocatorSourceGuardTests
{
    [Fact]
    public void SentinelDirectoryLookupUsesTheSharedWorkspaceLocator()
    {
        var workspaceRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx")
            .Should().Be(TestWorkspaceFileLocator.FindContainingDirectory("FreeW.slnx"));
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx")
            .Should().Be(TestWorkspaceFileLocator.FindContainingDirectory("FreeP.slnx"));
    }

    [Fact]
    public void TestSourcesDoNotReintroducePrivateWorkspaceWalkers()
    {
        var workspaceRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sharedLocator = Path.Combine(
            workspaceRoot,
            "tests",
            "SharedTestInfrastructure",
            "TestWorkspaceFileLocator.cs");
        var sourceGuard = Path.Combine(
            workspaceRoot,
            "tests",
            "FreeX.Core.IO.Tests",
            "TestWorkspaceFileLocatorSourceGuardTests.cs");
        var manualWalkers = new[]
        {
            new Regex(
                @"new\s+(?:(?:global::)?System\.IO\.)?DirectoryInfo\s*\(\s*(?:System\.)?AppContext\.BaseDirectory\s*\)",
                RegexOptions.Compiled),
            new Regex(@"Directory\.GetParent\s*\(", RegexOptions.Compiled),
            new Regex(
                @"\b(?<name>directory|dir|current|path)\s*=\s*\k<name>\.Parent\b",
                RegexOptions.Compiled),
            new Regex(
                @"\b(?<name>directory|dir|current|path)\s*=\s*Path\.GetDirectoryName\s*\(\s*\k<name>\s*\)",
                RegexOptions.Compiled),
            new Regex(
                @"Path\.GetFullPath\s*\(\s*Path\.Combine\s*\([^)]*""\.\.",
                RegexOptions.Compiled | RegexOptions.Singleline),
            new Regex(
                @"Path\.Combine\s*\(\s*(?:System\.)?AppContext\.BaseDirectory\s*,[^)]*""\.\.",
                RegexOptions.Compiled | RegexOptions.Singleline),
        };

        EnumerateTestSourceFiles(workspaceRoot)
            .Where(file => !string.Equals(file, sharedLocator, StringComparison.OrdinalIgnoreCase))
            .Where(file => !string.Equals(file, sourceGuard, StringComparison.OrdinalIgnoreCase))
            .Select(file => new
            {
                File = Path.GetRelativePath(workspaceRoot, file),
                Source = File.ReadAllText(file),
            })
            .Where(sourceFile => manualWalkers.Any(pattern => pattern.IsMatch(sourceFile.Source)))
            .Select(sourceFile => sourceFile.File)
            .Should()
            .BeEmpty("workspace traversal belongs in TestWorkspaceFileLocator");
    }

    [Fact]
    public void SharedInfrastructureIsGloballyLinked()
    {
        var targets = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("Directory.Build.targets");

        targets.Should().Contain("tests\\SharedTestInfrastructure\\*.cs");
    }

    [Fact]
    public void AvaloniaTestsUseTheSharedCommandContext()
    {
        var workspaceRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var projectRoot = Path.Combine(workspaceRoot, "tests", "FreeX.App.Avalonia.Tests");
        var project = File.ReadAllText(Path.Combine(projectRoot, "FreeX.App.Avalonia.Tests.csproj"));
        var exactCopy = new Regex(
            @"class\s+TestCommandContext\(Workbook workbook\).*?Workbook\.GetSheet\(sheetId\)\s*\?\?\s*throw new KeyNotFoundException\(\$""Sheet \{sheetId\} not found""\)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        project.Should().Contain("..\\CommandTestInfrastructure\\TestCommandContext.cs");
        EnumerateSourceFiles(projectRoot)
            .Where(file => exactCopy.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(workspaceRoot, file))
            .Should().BeEmpty("exact ICommandContext test doubles belong in CommandTestInfrastructure");
    }

    [Fact]
    public void ServicesTestsUseTheSharedTemporaryDirectory()
    {
        var workspaceRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var projectRoot = Path.Combine(workspaceRoot, "tests", "FreeX.App.Services.Tests");
        var localCopy = new Regex(@"class\s+TestTemporaryDirectory\b", RegexOptions.Compiled);

        EnumerateSourceFiles(projectRoot)
            .Where(file => localCopy.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(workspaceRoot, file))
            .Should().BeEmpty("temporary-directory lifetime belongs in SharedTestInfrastructure");
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file)
                .Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"));

    private static IEnumerable<string> EnumerateTestSourceFiles(string workspaceRoot) =>
        EnumerateSourceFiles(Path.Combine(workspaceRoot, "tests"))
            .Concat(EnumerateSisterAppTestSourceFiles(workspaceRoot, "freew"))
            .Concat(EnumerateSisterAppTestSourceFiles(workspaceRoot, "freep"));

    private static IEnumerable<string> EnumerateSisterAppTestSourceFiles(
        string workspaceRoot,
        string appDirectory) =>
        EnumerateSourceFiles(Path.Combine(workspaceRoot, appDirectory))
            .Where(file => Path.GetRelativePath(workspaceRoot, file)
                .Split(Path.DirectorySeparatorChar)
                .Any(part => part.EndsWith(".Tests", StringComparison.Ordinal)));
}
