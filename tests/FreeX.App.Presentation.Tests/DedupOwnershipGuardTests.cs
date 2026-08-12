using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class DedupOwnershipGuardTests
{
    [Fact]
    public void ConditionalFormatMathAndReferenceGrammarHaveSingleProductionOwners()
    {
        var root = FindRepositoryRoot();
        var productionSources = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);
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
        var root = FindRepositoryRoot();
        var servicesRoot = Path.Combine(root, "src", "FreeX.App.Services");
        File.Exists(Path.Combine(servicesRoot, "SymbolPickerSelectionPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(servicesRoot, "WorkbookSheetNameGenerator.cs")).Should().BeFalse();

        var dataValidation = File.ReadAllText(Path.Combine(servicesRoot, "DataValidationPresetPlanner.cs"));
        var dataTable = File.ReadAllText(Path.Combine(servicesRoot, "DataTablePlanner.cs"));
        dataValidation.Should().NotContain("CreateDefaultRule(");
        dataTable.Should().NotContain("public static CellAddress GetDefaultFormulaCell(");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
