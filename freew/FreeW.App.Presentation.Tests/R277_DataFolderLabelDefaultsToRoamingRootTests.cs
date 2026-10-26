using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;

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
/// <summary>
/// Skips <see cref="R277_DataFolderLabelDefaultsToRoamingRootTests.TheParameterlessOverloadDoesNotResolveTheLocalRoot"/>
/// on platforms where the roaming and local application-data roots are the same directory.
/// .NET maps <c>ApplicationData</c> and <c>LocalApplicationData</c> to one path on macOS/Linux, so
/// the r169 roaming-vs-local distinction this test pins simply does not exist there and the test
/// cannot discriminate. Skipping via the attribute (the same mechanism as <c>UiE2eFactAttribute</c>)
/// keeps the non-discriminating platform visible in the run instead of failing it or, worse,
/// passing vacuously. On Windows the roots differ, so the test still runs and asserts for real.
/// </summary>
internal sealed class RoamingAndLocalRootsDistinctFactAttribute : FactAttribute
{
    public RoamingAndLocalRootsDistinctFactAttribute()
    {
        var local = AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
            PlatformApplicationDataPathProvider.LocalInstance);
        var roaming = AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
            PlatformApplicationDataPathProvider.Instance);

        if (string.Equals(local, roaming, StringComparison.Ordinal))
        {
            Skip = "ApplicationData and LocalApplicationData resolve to the same root on this "
                + "platform, so the roaming/local distinction this test pins does not exist here.";
        }
    }
}

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

    [RoamingAndLocalRootsDistinctFact]
    public void TheParameterlessOverloadDoesNotResolveTheLocalRoot()
    {
        var local = AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
            PlatformApplicationDataPathProvider.LocalInstance);
        var roaming = AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(
            PlatformApplicationDataPathProvider.Instance);

        // Guard the guard: if the two roots coincide the assertion below would pass without
        // meaning anything, which is the vacuous-green shape earlier rounds hit. The attribute
        // above skips the test outright when they coincide, so reaching this point means the
        // platform really does distinguish them and the assertion is meaningful.

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
