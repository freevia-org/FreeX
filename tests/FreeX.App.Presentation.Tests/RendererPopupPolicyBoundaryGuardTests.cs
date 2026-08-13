using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class RendererPopupPolicyBoundaryGuardTests
{
    private static readonly string[] SharedOwnerFiles =
    [
        Path.Combine("src", "FreeX.App.Presentation", "Filtering", "AutoFilterDropdownMenuPlanner.cs"),
        Path.Combine("src", "FreeX.App.Presentation", "Filtering", "AutoFilterMenuCatalog.cs"),
        Path.Combine("src", "FreeX.App.Presentation", "Ribbon", "HomeFontBorderPopupCatalogPlanner.cs"),
        Path.Combine("src", "FreeX.App.Presentation", "ConditionalFormatting", "ConditionalFormatPresetGalleryPlanner.cs"),
        Path.Combine("src", "FreeX.App.Presentation", "ConditionalFormatting", "ConditionalFormatIconSetCatalog.cs"),
        Path.Combine("src", "FreeX.App.Services", "HomeNumberFormatDropdownPlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "FormatCellsNumberFormatPlanner.cs"),
        Path.Combine("src", "FreeX.App.Services", "Ribbon", "WorksheetContextMenuPlanner.cs")
    ];

    private static readonly string[] GuardedOwnerTypeNames =
    [
        "AutoFilterDropdownMenuPlanner",
        "AutoFilterMenuCatalog",
        "HomeFontBorderPopupCatalogPlanner",
        "ConditionalFormatPresetGalleryPlanner",
        "ConditionalFormatIconSetCatalog",
        "HomeNumberFormatDropdownPlanner",
        "FormatCellsNumberFormatPlanner",
        "WorksheetContextMenuPlanner"
    ];

    [Fact]
    public void PopupAndCommandPolicyOwners_StayInSharedPresentationOrServices()
    {
        var repoRoot = ResolveRepositoryRoot();

        foreach (var ownerFile in SharedOwnerFiles)
        {
            File.Exists(Path.Combine(repoRoot, ownerFile))
                .Should()
                .BeTrue($"{ownerFile} is the shared owner for the popup or command policy slice");
        }

        var violations = RendererSourceFiles(repoRoot)
            .SelectMany(file => GuardedOwnerTypeNames.SelectMany(typeName => FindLocalPolicyOwnerDeclarations(repoRoot, file, typeName)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        violations.Should().BeEmpty(
            "WPF and Avalonia must stay thin renderers/adapters for popup contents, gallery catalogs, "
            + "number-format choices, AutoFilter menu policy, and worksheet context-menu command policy");
    }

    [Fact]
    public void RendererLayers_ConsumeSharedPopupAndCommandPolicyOwners()
    {
        var repoRoot = ResolveRepositoryRoot();
        var wpfSource = ReadProjectSource(repoRoot, "FreeX.App.Host");
        var avaloniaSource = ReadProjectSource(repoRoot, "FreeX.App.Avalonia");

        wpfSource.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan");
        wpfSource.Should().Contain("WorksheetContextMenuPlanner.BuildCommands");
        wpfSource.Should().Contain("HomeNumberFormatDropdownPlanner.Options");
        wpfSource.Should().Contain("FormatCellsNumberFormatPlanner.Categories");
        wpfSource.Should().Contain("ConditionalFormatPresetGalleryPlanner.DataBarGroups");
        wpfSource.Should().Contain("ConditionalFormatIconSetCatalog.CreateRule");

        avaloniaSource.Should().Contain("AutoFilterDropdownMenuPlanner.CreateMenuPlan");
        avaloniaSource.Should().Contain("WorksheetContextMenuPlanner.BuildCommands");
        avaloniaSource.Should().Contain("FormatCellsNumberFormatPlanner.Categories");
        avaloniaSource.Should().Contain("ConditionalFormatIconSetCatalog.DefaultStyle");
    }

    private static IEnumerable<string> FindLocalPolicyOwnerDeclarations(
        string repoRoot,
        string file,
        string typeName)
    {
        if (string.Equals(Path.GetFileNameWithoutExtension(file), typeName, StringComparison.Ordinal))
        {
            yield return $"{Path.GetRelativePath(repoRoot, file)} declares renderer-local {typeName} by file name";
            yield break;
        }

        var declarationPattern = new Regex(
            $@"\b(?:class|record|struct|enum)\s+{Regex.Escape(typeName)}\b",
            RegexOptions.CultureInvariant);

        var lineNumber = 0;
        foreach (var line in File.ReadLines(file))
        {
            lineNumber++;
            if (declarationPattern.IsMatch(line))
                yield return $"{Path.GetRelativePath(repoRoot, file)}:{lineNumber} declares renderer-local {typeName}";
        }
    }

    private static IEnumerable<string> RendererSourceFiles(string repoRoot)
    {
        foreach (var projectName in new[] { "FreeX.App.Host", "FreeX.App.Avalonia" })
        {
            var projectRoot = Path.Combine(repoRoot, "src", projectName);
            foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            {
                var parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
                    parts.Contains("obj", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string ReadProjectSource(string repoRoot, string projectName) =>
        string.Join(
            Environment.NewLine,
            RendererSourceFiles(repoRoot)
                .Where(file => file.Contains(Path.Combine("src", projectName), StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));

    private static string ResolveRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
