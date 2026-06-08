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
}
