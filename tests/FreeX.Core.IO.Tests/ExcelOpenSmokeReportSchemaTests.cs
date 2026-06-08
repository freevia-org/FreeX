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
    public void SupportedMetadataExpectations_EnforceDataValidationCountPackageReopenedCounter()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");

        programSource.Should().Contain("generated-dv-count-package-003");
        programSource.Should().Contain("MinFreeXReopenedDataValidations = saveReopen ? 10 : 0");
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
}
