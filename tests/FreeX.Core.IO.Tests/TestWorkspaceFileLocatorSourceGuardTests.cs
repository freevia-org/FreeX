using System.Text.RegularExpressions;
using FluentAssertions;

public sealed class TestWorkspaceFileLocatorSourceGuardTests
{
    [Fact]
    public void SentinelDirectoryLookupUsesTheSharedWorkspaceLocator()
    {
        var workspaceRoot = Path.GetDirectoryName(
            TestWorkspaceFileLocator.FindFromWorkspaceRoot("FreeX.slnx"));

        workspaceRoot.Should().NotBeNull();
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx")
            .Should().Be(TestWorkspaceFileLocator.FindContainingDirectory("FreeW.slnx"));
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx")
            .Should().Be(TestWorkspaceFileLocator.FindContainingDirectory("FreeP.slnx"));
    }

    [Fact]
    public void SisterAppTestsDoNotReintroduceExactSentinelRootWalkers()
    {
        var workspaceRoot = Path.GetDirectoryName(
            TestWorkspaceFileLocator.FindFromWorkspaceRoot("FreeX.slnx"))!;
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(workspaceRoot, "freew"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(workspaceRoot, "freep"), "*.cs", SearchOption.AllDirectories));
        var exactSentinelWalk = new Regex(
            @"(?:for \(var directory = new DirectoryInfo\(AppContext\.BaseDirectory\);.*?directory = directory\.Parent\)|var directory = new DirectoryInfo\(AppContext\.BaseDirectory\);.*?while \(directory is not null\).*?directory = directory\.Parent).*?if \(File\.Exists\(Path\.Combine\(directory\.FullName, ""Free(?:W|P|X)\.slnx""\)\)\).*?return directory\.FullName;",
            RegexOptions.Compiled | RegexOptions.Singleline);

        sourceFiles.Should().OnlyContain(file =>
            !exactSentinelWalk.IsMatch(File.ReadAllText(file)),
            "exact sentinel-root walks belong in TestWorkspaceFileLocator");
    }

    [Fact]
    public void SharedInfrastructureIsGloballyLinked()
    {
        var targets = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("Directory.Build.targets");

        targets.Should().Contain("tests\\SharedTestInfrastructure\\*.cs");
    }

    [Fact]
    public void TestsDoNotReintroduceManualBaseDirectoryWorkspaceWalkers()
    {
        var workspaceRoot = Path.GetDirectoryName(
            TestWorkspaceFileLocator.FindFromWorkspaceRoot("FreeX.slnx"))!;
        var testsRoot = Path.Combine(workspaceRoot, "tests");
        var sharedLocator = Path.Combine(testsRoot, "SharedTestInfrastructure", "TestWorkspaceFileLocator.cs");
        var manualWalker = "new DirectoryInfo" + "(AppContext.BaseDirectory)";

        var violations = EnumerateSourceFiles(testsRoot)
            .Where(file => !string.Equals(file, sharedLocator, StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains(manualWalker, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(workspaceRoot, file));

        violations.Should().BeEmpty("base-directory workspace walks belong in TestWorkspaceFileLocator");
    }

    [Fact]
    public void AvaloniaTestsUseTheSharedCommandContext()
    {
        var workspaceRoot = Path.GetDirectoryName(
            TestWorkspaceFileLocator.FindFromWorkspaceRoot("FreeX.slnx"))!;
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
        var workspaceRoot = Path.GetDirectoryName(
            TestWorkspaceFileLocator.FindFromWorkspaceRoot("FreeX.slnx"))!;
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
}
