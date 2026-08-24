using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class ToolHarnessDedupSourceTests
{
    [Fact]
    public void GeneratedEvidenceTools_UseNeutralSharedRunner()
    {
        var runner = TestWorkspaceFiles.ReadRepoText(
            "tools", "Free.ToolsShared", "GeneratedEvidenceToolRunner.cs");
        var programs = new[]
        {
            TestWorkspaceFiles.ReadRepoText("tools", "FreeP.KeyboardContextEvidence", "Program.cs"),
            TestWorkspaceFiles.ReadRepoText("tools", "FreeP.RandomTransitionEvidence", "Program.cs"),
            TestWorkspaceFiles.ReadRepoText("tools", "FreeW.BackstageParityEvidence", "Program.cs"),
        };

        runner.Should().Contain("public static int Run(");
        runner.Should().Contain("Generated evidence is stale:");
        runner.Should().Contain("new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)");
        programs.Should().OnlyContain(program =>
            program.Contains("GeneratedEvidenceToolRunner.Run(", StringComparison.Ordinal) &&
            !program.Contains("string? GetOption(", StringComparison.Ordinal) &&
            !program.Contains("static string FindRepositoryRoot(", StringComparison.Ordinal));
    }

    [Fact]
    public void ToolRepositoryRootDiscovery_UsesNeutralSharedLocator()
    {
        var locator = TestWorkspaceFiles.ReadRepoText(
            "tools", "Free.ToolsShared", "RepositoryRootLocator.cs");
        var runner = TestWorkspaceFiles.ReadRepoText(
            "tools", "Free.ToolsShared", "GeneratedEvidenceToolRunner.cs");
        var freePProgram = TestWorkspaceFiles.ReadRepoText(
            "tools", "FreeP.RenderSlideshowMediaParityEvidence", "Program.cs");
        var parityOptions = TestWorkspaceFiles.ReadRepoText(
            "tools", "FreeX.ParityCompare", "CliOptions.cs");
        var parityProgram = TestWorkspaceFiles.ReadRepoText(
            "tools", "FreeX.ParityCompare", "Program.cs");
        var smartArtEvidence = TestWorkspaceFiles.ReadRepoText(
            "tools", "FreeP.RenderCompare.Tests", "SmartArtFixtureEvidenceTests.cs");

        locator.Should().Contain("public static string? Find(string startDirectory, string marker)");
        runner.Should().Contain("RepositoryRootLocator.Find(AppContext.BaseDirectory, spec.RepositoryMarker)");
        freePProgram.Should().Contain("RepositoryRootLocator.Find(AppContext.BaseDirectory, \"FreeP.slnx\")");
        parityProgram.Should().Contain("RepositoryRootLocator.Find(AppContext.BaseDirectory, \"FreeX.slnx\")");
        smartArtEvidence.Should().Contain("RepositoryRootLocator.Find(AppContext.BaseDirectory, \"FreeX.slnx\")");
        runner.Should().NotContain("private static string FindRepositoryRoot(");
        freePProgram.Should().NotContain("static string FindRoot()");
        parityOptions.Should().NotContain("class RepoLocator");
        smartArtEvidence.Should().NotContain("new DirectoryInfo(");
        smartArtEvidence.Should().NotContain("FindRepositoryRoot()");
    }

    [Fact]
    public void FormatHarnesses_UseSharedFileNameSanitizer()
    {
        var sanitizer = TestWorkspaceFiles.ReadRepoText("tools", "Free.ToolsShared", "ToolFileNameSanitizer.cs");
        var chainRunner = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.FormatFidelity", "ChainRunner.cs");
        var crossCheckRunner = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.FormatCrossCheck", "CrossCheckRunner.cs");
        var chartExamples = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelExamplesCharts", "Program.cs");

        sanitizer.Should().Contain("ReplaceNonAlphaNumericWithUnderscore");
        chainRunner.Should().Contain("using Free.ToolsShared;");
        crossCheckRunner.Should().Contain("using Free.ToolsShared;");
        chartExamples.Should().Contain("using Free.ToolsShared;");
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

        foregroundCapture.Should().Contain("startInfo.Environment.Remove(\"DOTNET_ROOT\")");
        foregroundCapture.Should().Contain("startInfo.Environment.Remove(\"DOTNET_ROOT_X64\")");
        foregroundCapture.Should().Contain("WindowFinder.WaitForMainWindow(process, exePath, options.LaunchTimeout)");
        foregroundCapture.Should().Contain("WindowFinder.DescribeLaunchWindowCandidates(process.Id, exePath)");
        foregroundCapture.Should().Contain("IsLaunchMainWindowCandidate(candidate, process.Id, expectedProcessName, expectedExePath)");
        foregroundCapture.Should().Contain("IsSameExecutableProcess(candidate.ProcessId, expectedProcessName, expectedExePath)");
        foregroundCapture.Should().Contain("windowProcessId = window.ProcessId;");
        foregroundCapture.Should().Contain("ForegroundGuard.FocusAndVerify(handle, windowProcessId.Value, \"FreeX\", options.FocusTimeout)");
        foregroundCapture.Should().Contain("WindowFinder.FindProcessPopup(windowProcessId.Value, window.Handle, options.PopupTimeout, 120, 80)");
        foregroundCapture.Should().Contain("Visible window candidates:");
    }

    [Fact]
    public void ForegroundCapture_PopupDiscoveryRejectsFocusedOwnerChildren()
    {
        var foregroundCapture = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ForegroundCapture", "Program.cs");

        foregroundCapture.Should().Contain("IsDistinctTopLevelWindow(foreground, ownerHandle)");
        foregroundCapture.Should().Contain("NativeMethods.GetAncestor(new IntPtr(candidate.Handle), NativeMethods.GA_ROOT)");
        foregroundCapture.Should().Contain("public const uint GA_ROOT = 2;");
        foregroundCapture.Should().Contain("GetVisibleExcelSheetTabElements");
        foregroundCapture.Should().Contain(".Where(element => Equals(element.Current.ControlType, ControlType.TabItem))");
    }

    [Fact]
    public void ForegroundCapture_FormatCellsUsesTheSharedSeedFixture()
    {
        var foregroundCapture = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ForegroundCapture", "Program.cs");

        foregroundCapture.Should().Contain("const string seed = \"score\\r\\n1\\r\\n2\\r\\n3\";");
        foregroundCapture.Should().Contain("PasteCellText(handle, process.Id, a1Bounds, seed)");
        foregroundCapture.Should().Contain("WaitForCellValue(handle, \"Cell_A1\", \"score\", TimeSpan.FromSeconds(3), out var observedSeedValue)");
    }

    [Fact]
    public void ForegroundCapture_SheetTabMenuCapturesTheDetectedPopup()
    {
        var foregroundCapture = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ForegroundCapture", "Program.cs");

        foregroundCapture.Should().Contain("SeedSheetsWithAddButton(handle, processId, 4)");
        foregroundCapture.Should().Contain("GuardedClickElement(options.Scenario, processId, handle, addButton, MouseButtonKind.Left)");
        foregroundCapture.Should().Contain("return CaptureWindow(scenario, \"freex\", _lastCaptureWindow ?? refreshedWindow, guard, \"complete\", _lastResultValidation);");
        foregroundCapture.Should().Contain("_lastCaptureWindow = popup;");
        foregroundCapture.Should().Contain("_lastCaptureWindow = null;");
    }

    [Fact]
    public void ForegroundCapture_ReportsTheWindowsLockScreenExplicitly()
    {
        var foregroundCapture = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ForegroundCapture", "Program.cs");

        foregroundCapture.Should().Contain("IsWindowsLockScreen(current)");
        foregroundCapture.Should().Contain("Windows lock screen is active; unlock the interactive console before running foreground capture.");
        foregroundCapture.Should().Contain("Windows Default Lock Screen");
    }

    [Fact]
    public void ToolScripts_UseCanonicalSharedSupportEntryPoints()
    {
        var support = TestWorkspaceFiles.ReadRepoText("tools", "ToolScriptSupport.ps1");
        var screenshotSupport = TestWorkspaceFiles.ReadRepoText("tools", "ScreenshotCaptureSupport.ps1");
        var inventoryScripts = new[]
        {
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-FreePCommandParityInventory.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-FreeWCommandInventory.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-CommandInventoryDocs.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-DialogParityInventory.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "Test-GeneratedDocs.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-ConditionalFormatOpenedStateEvidence.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-CrossAppParityDashboard.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-DialogVisualEvidenceSummary.ps1"),
        };
        var screenshotScripts = new[]
        {
            TestWorkspaceFiles.ReadRepoText("tools", "screenshot_excel.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "screenshot_ribbon.ps1"),
        };

        support.Should().Contain("function Resolve-ToolRepoPath");
        support.Should().Contain("function Test-ToolGeneratedFileContentMatches");
        inventoryScripts.Should().OnlyContain(script => script.Contains("ToolScriptSupport.ps1", StringComparison.Ordinal));
        inventoryScripts.Should().OnlyContain(script => !script.Contains("function Resolve-RepoPath", StringComparison.Ordinal));
        screenshotSupport.Should().Contain("function Resolve-CaptureWidths");
        screenshotScripts.Should().OnlyContain(script => script.Contains("ScreenshotCaptureSupport.ps1", StringComparison.Ordinal));
        screenshotScripts.Should().OnlyContain(script => !script.Contains("function Resolve-CaptureWidths", StringComparison.Ordinal));
        screenshotScripts.Should().OnlyContain(script => !script.Contains("function Capture-ScreenRectangle", StringComparison.Ordinal));
    }

    [Fact]
    public void CommandInventoryGenerators_ConsumeSharedRecursiveMenuTraversalEmitter()
    {
        var support = TestWorkspaceFiles.ReadRepoText("tools", "ToolScriptSupport.ps1");
        var generators = new[]
        {
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-FreePCommandParityInventory.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-FreeWCommandInventory.ps1"),
        };

        support.Should().Contain("function Get-ToolCommandInventoryMenuTraversalSource");
        support.Should().Contain("foreach (var child in MenuItems(item.Children))");
        support.Should().Contain("yield return child");

        generators.Should().OnlyContain(script =>
            script.Contains("Get-ToolCommandInventoryMenuTraversalSource", StringComparison.Ordinal) &&
            !script.Contains("private static IEnumerable<(string CommandId, CommandLocation Location)> MenuLocations", StringComparison.Ordinal) &&
            !script.Contains("private static IEnumerable<RibbonMenuItem> MenuItems", StringComparison.Ordinal));
    }

    [Fact]
    public void CommandInventoryMenuTraversal_EmitsParentBeforeNestedChildren()
    {
        var support = TestWorkspaceFiles.ReadRepoText("tools", "ToolScriptSupport.ps1");
        var parentYield = support.IndexOf("yield return item", StringComparison.Ordinal);
        var childTraversal = support.IndexOf("foreach (var child in MenuItems(item.Children))", StringComparison.Ordinal);
        var childYield = support.IndexOf("yield return child", StringComparison.Ordinal);

        parentYield.Should().BeGreaterThanOrEqualTo(0);
        childTraversal.Should().BeGreaterThan(parentYield);
        childYield.Should().BeGreaterThan(childTraversal);
    }

    [Fact]
    public void CommandInventoryGenerators_UseSharedGeneratedProjectOrchestration()
    {
        var support = TestWorkspaceFiles.ReadRepoText("tools", "ToolScriptSupport.ps1");
        var generators = new[]
        {
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-FreePCommandParityInventory.ps1"),
            TestWorkspaceFiles.ReadRepoText("tools", "Generate-FreeWCommandInventory.ps1"),
        };

        support.Should().Contain("function Invoke-ToolGeneratedProject");
        support.Should().Contain("<Project Sdk=\"Microsoft.NET.Sdk\">");
        support.Should().Contain("<ProjectReference Include=\"$($Options.Reference)\" />");
        support.Should().Contain("Test-ToolGeneratedFileContentMatches");
        support.Should().Contain("Copy-Item -LiteralPath $generatedFile.TempPath");

        generators.Should().OnlyContain(script =>
            script.Contains("Invoke-ToolGeneratedProject @", StringComparison.Ordinal) &&
            script.Contains("Outputs = [ordered]@", StringComparison.Ordinal) &&
            script.Contains("Arguments = {", StringComparison.Ordinal) &&
            !script.Contains("& dotnet run", StringComparison.Ordinal) &&
            !script.Contains("Test-ToolGeneratedFileContentMatches", StringComparison.Ordinal) &&
            !script.Contains("Copy-Item -LiteralPath $temp", StringComparison.Ordinal));
    }

    [Fact]
    public void FreePRenderCompare_UsesSharedWpfBitmapDecodeHelpers()
    {
        var project = TestWorkspaceFiles.ReadRepoText("tools", "FreeP.RenderCompare", "FreeP.RenderCompare.csproj");
        var imageDiff = TestWorkspaceFiles.ReadRepoText("tools", "FreeP.RenderCompare", "ImageDiff.cs");

        project.Should().Contain("FreeX.ToolsShared.Wpf.csproj");
        imageDiff.Should().Contain("WpfImageDiff.LoadBitmap(pathA)");
        imageDiff.Should().Contain("WpfImageDiff.GetBgra32Pixels(bmpA, widthA, heightA)");
        imageDiff.Should().NotContain("private static BitmapSource LoadAsBgra32");
        imageDiff.Should().NotContain("private static byte[] GetBgra32Pixels");
    }

    [Fact]
    public void WpfImageDiff_UsesChecked64BitBufferSizing()
    {
        var imageDiff = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ToolsShared.Wpf", "WpfImageDiff.cs");

        imageDiff.Should().Contain("var pixelCount = checked((long)width * height);");
        imageDiff.Should().Contain("var stride = checked((long)width * 4);");
        imageDiff.Should().Contain("var bufferLength = checked(pixelCount * 4);");
        imageDiff.Should().Contain("new byte[layout.BufferLength]");
        imageDiff.Should().Contain("bitmap.CopyPixels(pixels, layout.Stride, 0)");
        imageDiff.Should().NotContain("new byte[width * height * 4]");
        imageDiff.Should().NotContain("bitmap.CopyPixels(pixels, width * 4, 0)");
    }

    [Fact]
    public void FreePWholeWindowEvidenceManifest_AccountsForRichEditorSelectionArtifacts()
    {
        var generator = TestWorkspaceFiles.ReadRepoText(
            "tools",
            "Generate-FreePWholeWindowVisualEvidenceManifest.ps1");

        generator.Should().Contain("$selectionArtifactCount = 3");
        generator.Should().Contain("diff/editor.rich-text-selection.wpf-selection.png");
        generator.Should().Contain("diff/editor.rich-text-selection.avalonia-selection.png");
        generator.Should().Contain("diff/editor.rich-text-selection.selection.png");
        generator.Should().Contain("$artifact.diffPngCount -ne ($expectedScenarioCount + $selectionArtifactCount)");
    }
}
