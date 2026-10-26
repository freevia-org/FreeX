using System.Text.RegularExpressions;
using FluentAssertions;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r278: <c>AppStoragePathPlanner</c> is shared by all three apps but its options-file APIs are
/// FreeX-shaped -- <c>OptionsFileName</c> is the constant <c>"options.json"</c>, while FreeW stores
/// <c>settings.json</c> and each app roots its own store elsewhere. Its directory APIs are fine:
/// <c>ProductDirectoryName</c> is ambient per app.
///
/// <para>Three separate source contracts already ban the label variant, one per app --
/// <c>DiagnosticsOptionsPathParityTests</c> (FreeW), the FreeP ownership tests, and the FreeW host
/// notes. r277 showed why that shape is fragile: a fence written per caller protects the callers it
/// names and nothing else. Here it was three fences guarding a method with no production callers at
/// all, while the underlying <c>GetOptionsFilePath</c> stayed public, equally FreeX-shaped, and
/// unfenced.</para>
///
/// <para>So the label method is deleted -- the compiler now enforces what three tests were asserting
/// -- and this contract replaces the per-app bans with one rule at the boundary: no FreeW or FreeP
/// source may reach for this planner's options-FILE paths. Both apps read their live options store
/// instead, which is what the r169 rounds concluded and what every corrected call site already
/// does.</para>
/// </summary>
public sealed class R278_SharedStoragePlannerOptionsFileApisAreFreeXOnlyContractTests
{
    /// <summary>
    /// Options-FILE members only. The directory members are legitimately shared, so naming them here
    /// would forbid correct code -- the distinction this contract exists to draw.
    /// </summary>
    private static readonly string[] FreeXShapedMembers =
    [
        "GetOptionsFilePath",
        "ResolveOptionsFilePath",
        "OptionsFileName",
        "GetOptionsFilePathLabelOrFallback",
    ];

    private static readonly string[] SisterAppRoots = ["freew", "freep"];

    [Fact]
    public void NoSisterAppSourceUsesThePlannersOptionsFilePaths()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        var scannedFiles = 0;

        foreach (var app in SisterAppRoots)
        {
            var directory = Path.Combine(root, app);
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                scannedFiles++;
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var trimmed = line.TrimStart();

                    // Comments and the existing per-app fences NAME these members deliberately; a
                    // contract that flagged its own predecessors would be unusable.
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal)
                        || line.Contains("NotContain", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var member in FreeXShapedMembers)
                    {
                        if (!Regex.IsMatch(line, @"AppStoragePathPlanner\s*\.\s*" + Regex.Escape(member) + @"\b"))
                            continue;

                        offenders.Add(
                            $"{Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')}:{i + 1} "
                            + $"-- AppStoragePathPlanner.{member}");
                    }
                }
            }
        }

        scannedFiles.Should().BeGreaterThan(100,
            "the scan must walk the sister apps; a collapsed count means the roots moved and this "
            + "passed while checking nothing");

        offenders.Should().BeEmpty(
            "AppStoragePathPlanner.OptionsFileName is the constant \"options.json\", which is FreeX's "
            + "file name -- FreeW stores settings.json. A sister app that asks this planner where its "
            + "options live is told about a file that has never existed on that install. Read the "
            + "live options store instead (_optionsStore.StorePath).\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// The premise the contract rests on. If <c>OptionsFileName</c> ever became ambient per app the
    /// rule above would be forbidding correct code, and this test says so instead of letting the ban
    /// quietly outlive its reason.
    /// </summary>
    [Fact]
    public void ThePlannersOptionsFileNameIsStillAFixedFreeXConstant()
    {
        AppStoragePathPlanner.OptionsFileName.Should().Be("options.json",
            "the ban above exists only because this name is fixed rather than per-app; if it becomes "
            + "ambient, delete the contract instead of working around it");
    }

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
