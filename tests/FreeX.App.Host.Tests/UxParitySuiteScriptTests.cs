using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class UxParitySuiteScriptTests
{
    [Fact]
    public void BootstrapRunner_UsesProcessScopedRuntimeResolutionAndSeparateWorkbookCopies()
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", "Run-UxParitySuite.ps1");

        script.Should().Contain("function Start-FreeXDesktopHost");
        script.Should().Contain("$startInfo.UseShellExecute = $false");
        script.Should().Contain("$startInfo.EnvironmentVariables.Remove($variableName)");
        script.Should().Contain("\"DOTNET_ROOT_X64\"");
        script.Should().Contain("\"DOTNET_ROOT_X86\"");
        script.Should().Contain("\"DOTNET_ROOT_ARM64\"");
        script.Should().NotContain("$env:DOTNET_ROOT =");
        script.Should().Contain("$freeXProcess = Start-FreeXDesktopHost $freeXPath $freeXWorkbookPath");

        script.Should().Contain("$excelWorkbookPath = Join-Path $runDir \"excel-workbook.xlsx\"");
        script.Should().Contain("$freeXWorkbookPath = Join-Path $runDir \"freex-workbook.xlsx\"");
        script.Should().Contain("[System.IO.File]::Copy($ExcelWorkbookPath, $FreeXWorkbookPath, $true)");
        script.Should().Contain("$Workbook.Close($false)");
        script.Should().Contain("$Excel.CalculateFullRebuild()");
        script.IndexOf("$excel.CalculateFullRebuild()", StringComparison.Ordinal)
            .Should().BeLessThan(script.IndexOf("$workbook.SaveAs($WorkbookPath, 51)", StringComparison.Ordinal));
        script.Should().Contain("Workbook = $Excel.Workbooks.Open($ExcelWorkbookPath)");
        script.Should().Contain("excelPath = $excelWorkbookPath");
        script.Should().Contain("freexPath = $freeXWorkbookPath");
        script.Should().Contain("copiesByteIdentical = $true");
        script.Should().Contain("function Get-LinkedDataTypePackageEntries");
        script.Should().Contain("Add-Type -AssemblyName System.IO.Compression");
        script.Should().Contain("Get-LinkedDataTypePackageEntries $ExcelWorkbookPath");
        script.Should().Contain("must-not-appear-in-default-manual-corpus");
        script.Should().Contain("dynamic-array metadata is exercised in a dedicated parity fixture");
        script.Should().Contain("startupWorkbook = $freeXWorkbookPath");
    }
}
