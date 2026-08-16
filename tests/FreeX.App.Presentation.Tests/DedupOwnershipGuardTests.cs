using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class DedupOwnershipGuardTests
{
    [Fact]
    public void ConditionalFormatMathAndReferenceGrammarHaveSingleProductionOwners()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var productionSources = EnumerateProductionSources(Path.Combine(root, "src"));
        var mathOwners = productionSources.Where(path => File.ReadAllText(path).Contains("class ConditionalFormatEvaluationMath", StringComparison.Ordinal));
        var referenceTokenOwners = productionSources.Where(path => File.ReadAllText(path).Contains("IReadOnlyList<string> SplitReferences", StringComparison.Ordinal));
        var numberFormatOwners = productionSources.Where(path => File.ReadAllText(path).Contains("class NumberFormatSectionTokenizer", StringComparison.Ordinal));

        mathOwners.Should().ContainSingle().Which.Should().EndWith(
            Path.Combine("src", "FreeX.Core.Model", "ConditionalFormatEvaluationMath.cs"));
        referenceTokenOwners.Should().ContainSingle().Which.Should().EndWith(
            Path.Combine("src", "FreeX.App.Presentation", "WorkbookRangeTextCodec.cs"));
        numberFormatOwners.Should().ContainSingle().Which.Should().EndWith(
            Path.Combine("src", "FreeX.Core.Model", "NumberFormatSectionTokenizer.cs"));

        var numberFormatter = File.ReadAllText(
            Path.Combine(root, "src", "FreeX.Core.Formula", "NumberFormatter.Sections.cs"));
        numberFormatter.Should().Contain("NumberFormatSectionTokenizer.Split(format)");
        numberFormatter.Should().NotContain("bool inBracket");
    }

    [Fact]
    public void StaleServicePlannerOwnersDoNotReturn()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var servicesRoot = Path.Combine(root, "src", "FreeX.App.Services");
        File.Exists(Path.Combine(servicesRoot, "SymbolPickerSelectionPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(servicesRoot, "WorkbookSheetNameGenerator.cs")).Should().BeFalse();

        var dataValidation = File.ReadAllText(Path.Combine(servicesRoot, "DataValidationPresetPlanner.cs"));
        var dataTable = File.ReadAllText(Path.Combine(servicesRoot, "DataTablePlanner.cs"));
        dataValidation.Should().NotContain("CreateDefaultRule(");
        dataTable.Should().NotContain("public static CellAddress GetDefaultFormulaCell(");
    }

    /// <summary>
    /// Checked-in production sources only. Enumerating every .cs under src/ also walks obj/ and bin/,
    /// which is wrong twice over: those directories hold generated files that a concurrent build can
    /// delete between enumeration and read (this guard failed with a FileNotFoundException on a
    /// localization satellite's generated resources.cs), and a build artifact mirroring a source file
    /// would be counted as a second "owner" and fail the guard for no real reason.
    /// </summary>
    private static string[] EnumerateProductionSources(string sourceRoot) =>
        Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path, sourceRoot))
            .ToArray();

    private static bool IsBuildArtifact(string path, string sourceRoot) =>
        Path.GetRelativePath(sourceRoot, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase));
}
