using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeP.App.Compositor.Tests;

public sealed class FreePRendererLocalizationExhaustionTests
{
    private static readonly Regex StringLiteralPattern = new(
        "\"((?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<SourceLiteral, string> AllowedCatalogValueLiterals =
        new Dictionary<SourceLiteral, string>
        {
            [Literal("FreeP.App.Avalonia/Backstage/BackstageView.cs", "Print")] =
                "Backstage route identifier",
            [Literal("FreeP.App.Avalonia/Backstage/BackstageView.cs", "Options")] =
                "Backstage route identifier",
            [Literal("FreeP.App.Avalonia/Printing/CupsPrintDialog.cs", "Cancel")] =
                "resource-key suffix passed to PrintDialogText",
            [Literal("FreeP.App.Host/Backstage/BackstageView.cs", "Options")] =
                "Backstage route identifier",
            [Literal("FreeP.App.Host/MainWindow.cs", "P")] =
                "FreeP application badge",
            [Literal("FreeP.App.Host/MainWindow.cs", "Print")] =
                "Backstage route identifier",
            [Literal("FreeP.App.Host/OsClipboardService.cs", "N")] =
                "Guid format specifier",
            [Literal("FreeP.App.Rendering.Avalonia/SlideCanvas.cs", "ellipse")] =
                "picture-frame geometry identifier",
            [Literal("FreeP.App.Rendering.Avalonia/SlideCanvas.cs", "M")] =
                "font measurement sentinel",
            [Literal("FreeP.App.Rendering.Wpf/SlideCanvas.cs", "ellipse")] =
                "picture-frame geometry identifier",
        };

    [Fact]
    public void RendererSources_DoNotEmbedCatalogTextOutsidePreciseNonUiAllowlist()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var catalogValues = ReadCatalogValues(
            Path.Combine(root, "freep", "FreeP.App.Localization", "Resources", "Strings.resx"),
            Path.Combine(root, "shared", "Free.Shared.Localization", "Resources", "Strings.resx"));
        var observedAllowlist = new HashSet<SourceLiteral>();
        var violations = new List<string>();

        foreach (var project in new[]
                 {
                     "FreeP.App.Host",
                     "FreeP.App.Avalonia",
                     "FreeP.App.Rendering.Wpf",
                     "FreeP.App.Rendering.Avalonia",
                 })
        {
            var projectRoot = Path.Combine(root, "freep", project);
            foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (IsGeneratedOutput(file))
                    continue;

                var relativePath = Path.GetRelativePath(Path.Combine(root, "freep"), file)
                    .Replace('\\', '/');
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                        continue;

                    foreach (Match match in StringLiteralPattern.Matches(line))
                    {
                        var value = DecodeSimpleStringLiteral(match.Groups[1].Value);
                        if (!catalogValues.Contains(value))
                            continue;

                        var candidate = new SourceLiteral(relativePath, value);
                        if (AllowedCatalogValueLiterals.ContainsKey(candidate))
                        {
                            observedAllowlist.Add(candidate);
                            continue;
                        }

                        violations.Add($"{relativePath}:{lineNumber}: {value}");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "renderer-visible neutral text must resolve through FreeP or shared resources");
        observedAllowlist.Should().BeEquivalentTo(
            AllowedCatalogValueLiterals.Keys,
            "the non-UI allowlist must remain exact and stale entries must be removed");
    }

    private static HashSet<string> ReadCatalogValues(params string[] catalogPaths) =>
        catalogPaths
            .SelectMany(path => XDocument.Load(path).Root!.Elements("data"))
            .Select(data => data.Element("value")?.Value)
            .Where(static value => !string.IsNullOrEmpty(value))
            .Select(static value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsGeneratedOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string DecodeSimpleStringLiteral(string value) =>
        value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static SourceLiteral Literal(string path, string value) => new(path, value);

    private sealed record SourceLiteral(string Path, string Value);
}
