using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;
using Xunit.Sdk;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// r277: r169's fence guards the callers, not the hazard.
///
/// <para><c>DataFolderLabelParityTests</c> asserts that four named shell files call
/// <c>ResolveDataFolderLabel(_optionsStore.StorePath)</c> and never the parameterless overload,
/// because that overload defaulted to <c>PlatformApplicationDataPathProvider.LocalInstance</c> and
/// therefore reported <c>%LOCALAPPDATA%</c> while every app stores its options under
/// <c>%APPDATA%</c>. That contract is scoped to four hardcoded file paths: a fifth caller anywhere
/// else -- another dialog, FreeX, a new shell -- got the wrong folder and no test could see it.</para>
///
/// <para><c>AppStoragePathPlanner</c> already recorded the correct policy in r169's own follow-up
/// comment: "every sister app resolves its data directory through
/// <c>PlatformApplicationDataPathProvider.Instance</c> ... the honest placeholder is
/// <c>%APPDATA%</c>". That reasoning fixed the exception branch and left the convenience defaults
/// pointing at the local root, so the success branch kept returning the wrong directory.</para>
///
/// <para>The defaults now name <c>Instance</c>. These tests pin the BEHAVIOUR, so the class is
/// closed at the hazard rather than at the list of places that touch it.</para>
/// </summary>
public sealed class R277_DataFolderLabelDefaultsToRoamingRootTests
{
    [Fact]
    public void TheParameterlessOverloadResolvesTheRoamingApplicationDataRoot()
    {
        FreeWApplicationFrameDescriptor.ResolveDataFolderLabel()
            .Should().Be(
                AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
                    PlatformApplicationDataPathProvider.Instance),
                "the label names the folder the app actually reads and writes, and every sister app "
                + "stores its options under the roaming root");
    }

    [Fact]
    public void TheParameterlessOverloadDoesNotResolveTheLocalRoot()
    {
        var local = AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
            PlatformApplicationDataPathProvider.LocalInstance);
        var roaming = AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
            PlatformApplicationDataPathProvider.Instance);

        // Guard the guard: if the two roots coincide the assertion below would pass without
        // meaning anything, which is the vacuous-green shape earlier rounds hit. On macOS/Linux
        // .NET maps ApplicationData and LocalApplicationData to the SAME directory, so the roots
        // legitimately coincide there and this test cannot discriminate at all. Skip explicitly
        // rather than fail (the r169 behaviour under test is a Windows-only roaming/local
        // distinction) -- a skip keeps the non-discriminating run visible instead of silently green.
        if (string.Equals(local, roaming, StringComparison.Ordinal))
        {
            throw SkipException.ForSkip(
                "ApplicationData and LocalApplicationData resolve to the same root on this platform, "
                + "so the roaming/local distinction this test pins does not exist here.");
        }

        FreeWApplicationFrameDescriptor.ResolveDataFolderLabel().Should().NotBe(local,
            "reporting %LOCALAPPDATA% sends the user to a folder their options are not in -- the r169 "
            + "bug, which survived on the convenience overload after the call sites were corrected");
    }

    /// <summary>
    /// The store-path overload only consults its provider when the supplied path yields no directory.
    /// That fallback carried the same wrong default, so it is pinned too rather than assumed.
    /// </summary>
    [Fact]
    public void TheStorePathOverloadFallsBackToTheRoamingRootWhenThePathHasNoDirectory()
    {
        FreeWApplicationFrameDescriptor.ResolveDataFolderLabel("options.json")
            .Should().Be(
                AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
                    PlatformApplicationDataPathProvider.Instance),
                "a bare filename leaves no directory to report, and the fallback must name the same "
                + "root the apps store under");
    }
}
