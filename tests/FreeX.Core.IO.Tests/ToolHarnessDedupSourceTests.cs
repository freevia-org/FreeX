using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class ToolHarnessDedupSourceTests
{
    [Fact]
    public void FormatHarnesses_UseSharedFileNameSanitizer()
    {
        var sanitizer = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ToolsShared", "ToolFileNameSanitizer.cs");
        var chainRunner = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.FormatFidelity", "ChainRunner.cs");
        var crossCheckRunner = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.FormatCrossCheck", "CrossCheckRunner.cs");
        var chartExamples = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelExamplesCharts", "Program.cs");

        sanitizer.Should().Contain("ReplaceNonAlphaNumericWithUnderscore");
        chainRunner.Should().Contain("ToolFileNameSanitizer.ReplaceNonAlphaNumericWithUnderscore(chain.Name)");
        crossCheckRunner.Should().Contain(
            "ToolFileNameSanitizer.ReplaceNonAlphaNumericWithUnderscore(Path.GetFileNameWithoutExtension(sourcePath))");
        chartExamples.Should().Contain("ToolFileNameSanitizer.ReplaceNonAlphaNumericWithUnderscore");
        chainRunner.Should().NotContain("private static string Sanitize");
        crossCheckRunner.Should().NotContain("private static string Sanitize");
        chartExamples.Should().NotContain("private static string Sanitize");
    }

    [Fact]
    public void ExcelExamplesCharts_UsesSharedWpfAndExcelAutomationHelpers()
    {
        var chartExamples = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelExamplesCharts", "Program.cs");
        var fidelityCompare = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.FidelityCompare", "ExcelInspector.cs");
        var foregroundCapture = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ForegroundCapture", "Program.cs");
        var chartInteropProgram = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ChartInteropCompare", "Program.cs");
        var chartInteropExcel = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ChartInteropCompare", "ChartInteropCompare.ExcelInterop.cs");
        var numberFormatParity = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.NumberFormatParity", "Program.cs");
        var wpfSideBySide = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ToolsShared.Wpf", "WpfSideBySidePng.cs");
        var excelAutomation = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ToolsShared.Wpf", "ExcelComAutomation.cs");

        chartExamples.Should().Contain("ExcelComAutomation.CreateExcelApplicationWithRetry");
        chartExamples.Should().Contain("ExcelComAutomation.GetNewExcelProcessIds");
        chartExamples.Should().Contain("ExcelComAutomation.KillExcelProcesses");
        fidelityCompare.Should().Contain("GetNewExcelProcessIds(baseline)");
        fidelityCompare.Should().Contain("KillExcelProcesses(_ownedPids");
        foregroundCapture.Should().Contain("ExcelComAutomation.CreateExcelApplication(");
        chartInteropProgram.Should().Contain("GetNewExcelProcessIds(baselineExcelPids)");
        chartInteropProgram.Should().Contain("WaitForExcelProcessesToExit(ownedExcelPids, 2000)");
        chartInteropProgram.Should().Contain("KillExcelProcesses(ownedExcelPids)");
        chartInteropExcel.Should().NotContain("Process.GetProcessesByName");
        numberFormatParity.Should().Contain("ExcelComAutomation.CreateExcelApplication(");
        numberFormatParity.Should().Contain("ExcelComAutomation.ReleaseComObject(");
        chartExamples.Should().Contain("WpfImageDiff.ComputeMeanPixelDiff(row.ExcelPng, row.FreeXPng!, 600, 400)");
        chartExamples.Should().Contain("WpfSideBySidePng.WriteHeaderOnly");
        wpfSideBySide.Should().Contain("public sealed record WpfHeaderSideBySidePngOptions");
        excelAutomation.Should().Contain("public static HashSet<int> GetNewExcelProcessIds");
        excelAutomation.Should().Contain("public static void WaitForExcelProcessesToExit");
        excelAutomation.Should().Contain("public static void KillExcelProcesses");
        chartExamples.Should().NotContain("private static object CreateExcel");
        foregroundCapture.Should().NotContain("Type.GetTypeFromProgID(\"Excel.Application\")");
        foregroundCapture.Should().NotContain("Activator.CreateInstance(excelType)");
        numberFormatParity.Should().NotContain("Type.GetTypeFromProgID(\"Excel.Application\")");
        numberFormatParity.Should().NotContain("Activator.CreateInstance(excelType)");
        chartExamples.Should().NotContain("private static BitmapSource LoadBitmap");
        chartExamples.Should().NotContain("private static BitmapSource ResizeTo");
        chartExamples.Should().NotContain("private static double ComputeMeanPixelDiff");
    }

    [Fact]
    public void ForegroundCapture_FollowsDesktopLauncherWindowHandoffs()
    {
        var foregroundCapture = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ForegroundCapture", "Program.cs");

        foregroundCapture.Should().Contain("WindowFinder.WaitForMainWindow(process, exePath, options.LaunchTimeout)");
        foregroundCapture.Should().Contain("WindowFinder.DescribeLaunchWindowCandidates(process.Id, exePath)");
        foregroundCapture.Should().Contain("IsLaunchMainWindowCandidate(candidate, process.Id, expectedProcessName, expectedExePath)");
        foregroundCapture.Should().Contain("IsSameExecutableProcess(candidate.ProcessId, expectedProcessName, expectedExePath)");
        foregroundCapture.Should().Contain("windowProcessId = window.ProcessId;");
        foregroundCapture.Should().Contain("ForegroundGuard.FocusAndVerify(handle, windowProcessId.Value, \"FreeX\", options.FocusTimeout)");
        foregroundCapture.Should().Contain("WindowFinder.FindProcessPopup(windowProcessId.Value, window.Handle, options.PopupTimeout, 120, 80)");
        foregroundCapture.Should().Contain("Visible window candidates:");
    }
}
