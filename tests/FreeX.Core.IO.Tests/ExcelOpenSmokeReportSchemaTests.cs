using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class ExcelOpenSmokeReportSchemaTests
{
    [Fact]
    public void MachineReadableReport_IncludesExcelAuthoredSourceFlag()
    {
        var modelsSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "SmokeModels.cs");
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");

        modelsSource.Should().Contain("bool GenerateWithExcel = false");
        programSource.Should().Contain("GenerateWithExcel: true");
        programSource.Should().Contain("generatedWithExcel = result.Input.GenerateWithExcel");
        programSource.Should().Contain("sourceAuthorship = result.Input.GenerateWithExcel ? \"excel-authored\" : \"external-or-freex-authored\"");
    }

    [Fact]
    public void SaveReopenValidation_CoversCorePackageHealthOnBothSavedPaths()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");

        AssertCoreValidationCalls(programSource, "freeXSave.SavedPath", "FreeX-saved workbook", "input.SourcePath");
        AssertCoreValidationCalls(programSource, "excelSavedPath", "Excel-saved workbook", "stagedPath");
    }

    [Fact]
    public void PublicCorpusWarningTolerance_DoesNotAllowSupportedThreadedCommentsWarnings()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var manifest = TestWorkspaceFiles.ReadWorkspaceText("test-corpus", "manifest.csv");

        manifest.Should().Contain("generated-threaded-comments-001,generated/threaded-comments-001.xlsx,generated,local,2026-06-08,FreeX-generated,threaded-comments,,supported-metadata-pass");
        programSource.Should().NotContain("tags.Contains(\"threaded-comments\")");
        programSource.Should().Contain("tags.Contains(\"unsupported-sheet-types\")");
    }

    [Fact]
    public void MetadataPassHeaderFooterLegacyDrawing_RowRequiresPositiveSmokeCounter()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var manifest = TestWorkspaceFiles.ReadWorkspaceText("test-corpus", "manifest.csv");
        var expectationBlock = ExtractExpectationBlock(programSource, "generated-header-footer-legacy-drawing-001");

        manifest.Should().Contain("generated-header-footer-legacy-drawing-001,generated/header-footer-legacy-drawing-001.xlsx,generated,local,2026-05-26,FreeX-generated,header-footer legacy-drawing vml-drawing,,supported-metadata-pass");
        expectationBlock.Should().Contain("RequiredFreeXSavedPackageParts");
        expectationBlock.Should().Contain("RequiredExcelSavedPackageRelationships");
        expectationBlock.Should().Contain("MinExcelOpenedHeaderFooterSheets = 1");
        expectationBlock.Should().Contain("MinExcelReopenedHeaderFooterSheets = reopen");
    }

    private static void AssertCoreValidationCalls(
        string source,
        string pathExpression,
        string label,
        string sourcePathExpression)
    {
        source.Should().Contain($"AssertPackageHealth({pathExpression}, \"{label}\", {sourcePathExpression});");
        source.Should().Contain($"AssertNoExcelRecoveryLog({pathExpression}, \"{label}\", {sourcePathExpression});");
        source.Should().Contain($"AssertOpenXmlValid({pathExpression}, \"{label}\");");
        source.Should().Contain($"AssertWorkbookPackageRoot({pathExpression}, \"{label}\", {sourcePathExpression});");
    }

    private static string ExtractExpectationBlock(string source, string rowId)
    {
        var start = source.IndexOf($"row.Id, \"{rowId}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the smoke tool must special-case corpus row {rowId}");

        var nextElse = source.IndexOf("else if (string.Equals(row.Id", start + rowId.Length, StringComparison.Ordinal);
        nextElse.Should().BeGreaterThan(start, $"the smoke expectation block for {rowId} should be bounded by another row block");
        return source[start..nextElse];
    }
}
