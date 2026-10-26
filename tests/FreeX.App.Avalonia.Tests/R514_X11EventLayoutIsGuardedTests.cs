using System.IO;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// r514: X11WindowActivator marshals an XEvent whose field offsets and 192-byte union size are
/// hardcoded for LP64. Those hold on linux-x64 and linux-arm64 and nowhere else -- and linux-arm
/// (32-bit) is a supported .NET RID that Avalonia runs on, so this is not a hypothetical target.
/// On an ILP32 process every offset past Type is wrong, which corrupts the event being sent rather
/// than failing visibly.
///
/// <para>The guard cannot be exercised from a test: IntPtr.Size is 8 in every process that can run
/// this suite. A source contract is the only instrument available, so this test pins the guard's
/// presence -- it fails if someone removes the pointer-size check and leaves the LP64 layout behind,
/// which is precisely the silent-corruption regression the guard exists to prevent.</para>
/// </summary>
public sealed class R514_X11EventLayoutIsGuardedTests
{
    [Fact]
    public void Activate_RefusesToMarshalTheLp64EventLayoutOnAnIlp32Process()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FreeX.App.Avalonia", "Linux", "X11WindowActivator.cs"));

        // The layout the guard protects. If this constant ever stops being hardcoded, the guard is
        // no longer load-bearing and this test should be revisited rather than deleted.
        source.Should().Contain("Size = 192");
        source.Should().Contain("IntPtr.Size != 8");
    }

    private static string RepoRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
