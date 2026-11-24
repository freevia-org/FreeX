using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Keeps shared test infrastructure single-sourced: headless Avalonia test applications derive from
/// <c>HeadlessFluentApplication</c> and migrated tests clean up through <c>TestTemporaryDirectory</c>
/// instead of re-declaring either.
/// </summary>
public sealed class TestInfrastructureCentralizationTests
{
    private static readonly Regex InlineFluentInitialize = new(
        @"override\s+void\s+Initialize\(\)\s*=>\s*Styles\.Add\(new\s+(global::)?(Avalonia\.Themes\.Fluent\.)?FluentTheme\(\)\)",
        RegexOptions.CultureInvariant);

    [Fact]
    public void HeadlessAvaloniaTestApplications_DeriveFromTheSharedFluentApplication()
    {
        var root = WorkspaceFileLocator.FindWorkspaceRoot();
        var redeclared = new List<string>();
        foreach (var file in EnumerateTestSources(root))
        {
            if (InlineFluentInitialize.IsMatch(File.ReadAllText(file)))
            {
                redeclared.Add(Path.GetRelativePath(root, file));
            }
        }

        redeclared.Should().BeEmpty(
            "headless test applications derive from HeadlessFluentApplication (tests/SharedTestInfrastructure/Avalonia) instead of re-declaring the Fluent theme setup");

        var shared = WorkspaceFileLocator.ReadAllText("tests", "SharedTestInfrastructure", "Avalonia", "HeadlessFluentApplication.cs");
        shared.Should().Contain("class HeadlessFluentApplication : Application")
            .And.Contain("Styles.Add(new FluentTheme())");
    }

    [Theory]
    [InlineData("tests/Free.Shared.AppServices.Tests/AtomicExportExecutorTests.cs")]
    [InlineData("tests/Free.Shared.AppServices.Tests/AutosaveApplicationProfileTests.cs")]
    [InlineData("tests/Free.Shared.AppServices.Tests/AutosaveDocumentSessionTests.cs")]
    [InlineData("tests/Free.Shared.AppServices.Tests/AutosaveRecoveryWorkflowCoreTests.cs")]
    [InlineData("tests/Free.Shared.AppServices.Tests/R191_JsonSettingsStoreQuarantineTests.cs")]
    [InlineData("tests/Free.Shared.AppServices.Tests/R304_SisterAppOptionsPersistenceRoundTripTests.cs")]
    [InlineData("tests/Free.Shared.Shell.Avalonia.Tests/AvaloniaAutosaveHostTests.cs")]
    [InlineData("tests/Free.Shared.Shell.Wpf.Tests/WpfAutosaveHostTests.cs")]
    [InlineData("tests/FreeX.App.Avalonia.Tests/R119_FileOperationCancelTests.cs")]
    [InlineData("tests/FreeX.App.Avalonia.Tests/SharedProgressReportingF2StatusBarProgressBarTests.cs")]
    [InlineData("tests/FreeX.App.Services.Tests/R303_AppOptionsPersistenceRoundTripTests.cs")]
    [InlineData("tests/FreeX.App.Services.Tests/Updates/R617_UpdateFeedIsScopedToTheInstalledAppTests.cs")]
    public void MigratedTests_UseTheSharedTemporaryDirectoryHelper(string relativePath)
    {
        var source = WorkspaceFileLocator.ReadAllText(relativePath.Split('/'));

        source.Should().Contain("TestTemporaryDirectory")
            .And.NotContain("Directory.Delete(_")
            .And.NotContain("Path.GetTempPath()");
    }

    private static IEnumerable<string> EnumerateTestSources(string root)
    {
        foreach (var directory in new[] { "tests", "freew", "freep" })
        {
            var path = Path.Combine(root, directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || relative.Contains("SharedTestInfrastructure", StringComparison.Ordinal)
                    || !relative.Contains("Tests", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }
}
