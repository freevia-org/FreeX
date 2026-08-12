using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

public sealed class SlideShowTestSupportOwnershipTests
{
    private static readonly string[] TestMembers =
    [
        "get_PresenterInkOverlayVisualCount",
        "get_LastAnimationFramePlanForTest",
        "get_LastAnimationStepFrameEvidenceForTest",
        "get_LastAnimationStepPlaybackReadinessPlanForTest",
        "get_PlaybackRoute",
        "get_CurrentPresentationSlideIndex",
        "get_RevealedHiddenSlideForTest",
    ];

    [Fact]
    public void TestVariantContainsSlideshowAccessButNormalBinaryDoesNot()
    {
        var testMethods = typeof(SlideShowWindow)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var member in TestMembers)
            testMethods.Should().Contain(member);

        var normalAssemblyPath = NormalHostAssemblyPath();
        if (File.Exists(normalAssemblyPath))
        {
            var normalMethods = ReadMethodNames(normalAssemblyPath);
            foreach (var member in TestMembers)
                normalMethods.Should().NotContain(member);
            normalMethods.Should().NotContain("CaptionTextForTest");
            normalMethods.Should().NotContain("RefreshCaptionsForTest");
        }
    }

    [Fact]
    public void TestProjectOwnsConditionallyCompiledSlideshowAccess()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var hostDirectory = Path.Combine(root, "freep", "FreeP.App.Host");
        var supportDirectory = Path.Combine(root, "freep", "TestSupport", "SlideShow.Wpf");

        File.ReadAllText(Path.Combine(hostDirectory, "SlideShowWindow.cs"))
            .Should().NotContain("LastAnimationFramePlanForTest");
        File.ReadAllText(Path.Combine(hostDirectory, "SlideShowMediaController.cs"))
            .Should().NotContain("CaptionTextForTest");
        File.Exists(Path.Combine(supportDirectory, "SlideShowWindow.TestAccess.cs")).Should().BeTrue();
        File.Exists(Path.Combine(supportDirectory, "SlideShowMediaController.TestAccess.cs")).Should().BeTrue();
        File.ReadAllText(Path.Combine(hostDirectory, "FreeP.App.Host.csproj"))
            .Should().Contain("'$(FreePSlideShowTestSupport)' == 'true'");
        File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host.Tests", "FreeP.App.Host.Tests.csproj"))
            .Should().Contain("FreePSlideShowTestSupport=true");
    }

    private static string NormalHostAssemblyPath()
    {
        var testOutput = Path.GetDirectoryName(typeof(SlideShowTestSupportOwnershipTests).Assembly.Location)!;
        var targetFramework = Path.GetFileName(testOutput);
        var configuration = Directory.GetParent(testOutput)!.Name;
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "bin",
            configuration,
            targetFramework,
            "FreeP.App.Host.dll");
    }

    private static IReadOnlySet<string> ReadMethodNames(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        return metadata.MethodDefinitions
            .Select(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name))
            .ToHashSet(StringComparer.Ordinal);
    }
}
