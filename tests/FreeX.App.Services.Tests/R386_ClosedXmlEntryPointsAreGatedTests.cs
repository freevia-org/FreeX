using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r386: every way into ClosedXML must pass through the process-wide gate.
///
/// <para>ClosedXML's <c>XLWorkbook</c> construction and population touch process-global static
/// state, so a background startup prewarm or a second window can corrupt a concurrent load. The
/// adapter serialises on a single static <c>ClosedXmlGate</c>, and the design is right: the lock sits
/// at ONE chokepoint per direction (<c>LoadCore</c>, <c>SaveCore</c>), with every public overload
/// delegating inward, rather than being repeated at each caller.</para>
///
/// <para>That is precisely the shape a later edit erodes -- a new overload, or a caller reaching past
/// the wrapper to the unlocked core, and the fence still looks present while no longer covering the
/// hazard. This pins the two properties that make it work: the cores are called from exactly one
/// place each, and that place holds the lock.</para>
///
/// <para>Pinned on source rather than by racing threads deliberately. A stress test cannot fail
/// reliably -- 48 concurrent load/save cycles pass with the gate removed OR present, depending on
/// timing -- so it would be a test that reports green for a broken invariant, which this program has
/// spent a lot of rounds removing.</para>
/// </summary>
public sealed class R386_ClosedXmlEntryPointsAreGatedTests
{
    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static int CountCalls(string source, string method) =>
        Regex.Matches(source, $@"(?<![A-Za-z0-9_]){Regex.Escape(method)}\s*\(").Count;

    [Fact]
    public void TheUnlockedLoadCoreIsCalledOnlyFromInsideTheGate()
    {
        var source = Read("src/FreeX.Core.IO/XlsxFileAdapter.cs");

        // One declaration plus one call site; the call site is inside `lock (ClosedXmlGate)`.
        CountCalls(source, "LoadCore").Should().Be(2,
            "LoadCore is declared once and called once -- a second caller is how the gate gets bypassed");

        source.Should().MatchRegex(
            @"lock \(ClosedXmlGate\)[\s\S]{0,400}?LoadCore\(",
            "the single call must sit inside the gate");
    }

    [Fact]
    public void TheUnlockedSaveCoreIsCalledOnlyFromInsideTheGate()
    {
        var source = Read("src/FreeX.Core.IO/XlsxFileAdapter.Save.cs");

        CountCalls(source, "SaveCoreUnlocked").Should().Be(2,
            "SaveCoreUnlocked is declared once and called once");

        source.Should().MatchRegex(
            @"lock \(ClosedXmlGate\)[\s\S]{0,400}?SaveCoreUnlocked\(",
            "the single call must sit inside the gate");
    }

    [Fact]
    public void EveryPublicSaveEntryPointRoutesThroughTheLockedCore()
    {
        var source = Read("src/FreeX.Core.IO/XlsxFileAdapter.Save.cs");

        // Save, SaveWithWarnings, and the internal macro-preserving pair all delegate to SaveCore,
        // which is what makes one lock enough.
        foreach (var entryPoint in new[]
                 {
                     "public void Save(",
                     "public XlsxSaveResult SaveWithWarnings(",
                     "internal void SavePreservingVbaProject(",
                 })
        {
            source.Should().Contain(entryPoint);
        }

        Regex.Matches(source, @"SaveCore\(").Count.Should().BeGreaterThanOrEqualTo(3,
            "each public and internal save entry point delegates to the locked SaveCore");
    }

    [Fact]
    public void NoOtherProductionFileConstructsAClosedXmlWorkbook()
    {
        // The gate can only cover what lives behind it. A new XLWorkbook anywhere else is outside it
        // by construction, however carefully that file locks.
        // XlsxFileAdapter's own partials are excluded because their constructions sit inside
        // LoadCore/SaveCoreUnlocked, whose single-call-site-inside-the-lock property the three tests
        // above pin. One of the seven lives in the .Save.cs partial, which is why this excludes the
        // FILE PREFIX rather than the one file I first thought held them all.
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var offenders = new List<string>();

        foreach (var directory in new[] { "src", "shared", "tools" })
        {
            var path = Path.Combine(root, directory);
            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    Path.GetFileName(file).StartsWith("XlsxFileAdapter.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains("new XLWorkbook(", StringComparison.Ordinal))
                    offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        offenders.Should().BeEmpty(
            "every XLWorkbook is built inside XlsxFileAdapter's gated load chain; one built elsewhere " +
            "races the gate no matter what that code does. Offenders:\n" + string.Join("\n", offenders));
    }
}
