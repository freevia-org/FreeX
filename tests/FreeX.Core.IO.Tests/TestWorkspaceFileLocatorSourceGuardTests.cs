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
}
