using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowSourceHygieneTests
{
    [Fact]
    public void Wpf_async_void_handlers_are_limited_to_locally_guarded_boundaries()
    {
        var hostDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.xaml");
        var matches = Directory
            .EnumerateFiles(hostDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return Regex.Matches(
                        source,
                        @"(?m)^\s*private\s+async\s+void\s+(?<name>\w+)\s*\([^)]*\)")
                    .Select(match => new
                    {
                        Path = path,
                        Source = source,
                        Signature = match.Value.Trim(),
                        Name = match.Groups["name"].Value
                    });
            })
            .ToArray();

        matches.Select(match => match.Name).Should().BeEquivalentTo(
            "PictureButton_Click",
            "ExecuteQuickAccessToolbarCommand",
            "ExecuteWorksheetContextMenuAction",
            "RunGuardedUiCommand");

        foreach (var match in matches)
        {
            SourceMethodExtractor.ExtractMethodSource(match.Source, match.Signature)
                .Should().Contain("catch (Exception", $"{match.Name} is a void-only event boundary and must contain faults locally");
        }
    }
}
