using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ScreenshotHarnessScriptTests
{
    [Theory]
    [InlineData("screenshot_excel.ps1", "screenshots_excel", "excel_<WidthLabel>_<RibbonTab>.png", "excel_$($widthSpec.Label)_$safe.png")]
    [InlineData("screenshot_ribbon.ps1", "screenshots", "ribbon_<WidthLabel>_<RibbonTab>.png", "ribbon_$($widthSpec.Label)_$safe.png")]
    public void ScreenshotScripts_RecordOutputNamingAndWindowBoundsInEvidenceManifest(
        string scriptName,
        string outputDirectory,
        string namingPattern,
        string filePattern)
    {
        var script = ReadScript(scriptName);

        script.Should().Contain($"Join-Path $PSScriptRoot \"{outputDirectory}\"");
        script.Should().Contain("function Write-ScreenshotEvidenceManifest");
        script.Should().Contain("screenshot_manifest.json");
        script.Should().Contain($"OutputNaming = \"{namingPattern}\"");
        script.Should().Contain($"CatalogEvidenceTarget = \"docs/UI_TEST_CATALOG.md\"");
        script.Should().Contain("WidthSource = \"RibbonScreenshotTourPlanner.DefaultWidths\"");
        script.Should().Contain("PlannedCaptureCount = $tabNames.Count * $widths.Count");
        script.Should().Contain("WindowBounds = [pscustomobject]");
        script.Should().Contain("CaptureLogicalHeight = $captureLogicalHeight");
        script.Should().Contain("CapturePhysicalHeight = $capturePhysicalHeight");
        script.Should().Contain("Widths = $widths");
        script.Should().Contain("Captures = $files");
        script.Should().Contain($"$fileName = \"{filePattern}\"");
        script.Should().Contain("$path = Join-Path $outDir $fileName");
    }

    [Theory]
    [InlineData("screenshot_excel.ps1")]
    [InlineData("screenshot_ribbon.ps1")]
    public void ScreenshotScripts_DeclareRibbonTabsAndPopupLimitations(string scriptName)
    {
        var script = ReadScript(scriptName);

        script.Should().Contain("trap {");
        script.Should().Contain("$tabNames = @(\"Home\", \"Insert\", \"Draw\", \"Page Layout\", \"Formulas\", \"Data\", \"Review\", \"View\", \"Help\")");
        script.Should().Contain("foreach ($tabName in $tabNames)");
        script.Should().Contain("Transient popups, dropdowns, native dialogs, and context menus require separate guarded captures.");
        script.Should().Contain("Ribbon tab captures cover the top window band only.");
        script.Should().Contain("Global input is blocked unless the expected process and window title own the foreground window.");
    }

    [Theory]
    [InlineData("screenshot_excel.ps1")]
    [InlineData("screenshot_ribbon.ps1")]
    public void ScreenshotScripts_DefaultWidthsMatchRibbonScreenshotTourPlanner(string scriptName)
    {
        var script = ReadScript(scriptName);

        script.Should().Contain("[string]$Widths = $env:FREEX_SS_TOUR_WIDTHS");
        script.Should().Contain("$defaultCaptureWidths = @(");
        script.Should().Contain("$captureWidths = @(Resolve-CaptureWidths $Widths)");
        script.Should().Contain("foreach ($widthSpec in $captureWidths)");
        script.Should().Contain("Screenshot-Tab $tabName $widthSpec");

        foreach (var width in RibbonScreenshotTourPlanner.DefaultWidths)
        {
            script.Should().Contain($"Label = \"{width.Label}\"");
            script.Should().Contain(width.WindowWidth is null
                ? "WindowLogicalWidth = $null"
                : $"WindowLogicalWidth = {width.WindowWidth.Value.ToString("0.0", CultureInfo.InvariantCulture)}");
            script.Should().Contain(width.EvidencePurpose());
        }
    }

    [Theory]
    [InlineData("screenshot_excel.ps1")]
    [InlineData("screenshot_ribbon.ps1")]
    public void ScreenshotScripts_AbortWhenPlannedRibbonTabCannotBeCaptured(string scriptName)
    {
        var script = ReadScript(scriptName);
        var missingTabBranch = Regex.Match(
            script,
            @"if \(\$tabEl -eq \$null\) \{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);

        missingTabBranch.Success.Should().BeTrue($"{scriptName} should make missing planned tabs a hard failure");
        missingTabBranch.Groups["body"].Value.Should().Contain("Get-ChildItem $outDir -Filter \"*.png\"");
        missingTabBranch.Groups["body"].Value.Should().Contain("Remove-Item -Force");
        missingTabBranch.Groups["body"].Value.Should().Contain("throw \"Ribbon screenshot tab '$tabName' was not found");
        missingTabBranch.Groups["body"].Value.Should().NotContain("Write-Warning");
        missingTabBranch.Groups["body"].Value.Should().NotContain("return");
    }

    [Theory]
    [InlineData(
        "screenshot_excel.ps1",
        "excel",
        "Microsoft Excel",
        "freex",
        "screenshot_ribbon.ps1",
        "ribbon_<WidthLabel>_<RibbonTab>.png",
        "ribbon_$($widthSpec.Label)_$safe.png")]
    [InlineData(
        "screenshot_ribbon.ps1",
        "freex",
        "FreeX",
        "excel",
        "screenshot_excel.ps1",
        "excel_<WidthLabel>_<RibbonTab>.png",
        "excel_$($widthSpec.Label)_$safe.png")]
    public void ScreenshotScripts_EmitPairableRibbonEvidenceMetadata(
        string scriptName,
        string evidenceSubject,
        string evidenceApp,
        string counterpartSubject,
        string counterpartTool,
        string counterpartOutputNaming,
        string counterpartFileName)
    {
        var script = ReadScript(scriptName);

        script.Should().Contain("EvidenceFamily = \"ribbon\"");
        script.Should().Contain($"EvidenceSubject = \"{evidenceSubject}\"");
        script.Should().Contain($"EvidenceApp = \"{evidenceApp}\"");
        script.Should().Contain("Pairing = [pscustomobject]");
        script.Should().Contain("PairKeyPattern = \"ribbon:<WidthLabel>:<TabFileName>\"");
        script.Should().Contain($"CounterpartSubject = \"{counterpartSubject}\"");
        script.Should().Contain($"CounterpartTool = \"{counterpartTool}\"");
        script.Should().Contain($"CounterpartOutputNaming = \"{counterpartOutputNaming}\"");
        script.Should().Contain("PairKey = \"ribbon:$($widthSpec.Label):$safe\"");
        script.Should().Contain("TabFileName = $safe");
        script.Should().Contain("WidthLabel = $widthSpec.Label");
        script.Should().Contain("WindowLogicalWidth = $widthSpec.WindowLogicalWidth");
        script.Should().Contain("EvidencePurpose = $widthSpec.EvidencePurpose");
        script.Should().Contain($"CounterpartFileName = \"{counterpartFileName}\"");
    }

    [Fact]
    public void FreeXScreenshotScript_TabListMatchesRibbonScreenshotTourPlanner()
    {
        var script = ReadScript("screenshot_ribbon.ps1");
        var match = Regex.Match(script, @"\$tabNames\s*=\s*@\((?<tabs>[^)]*)\)");

        match.Success.Should().BeTrue("the guarded FreeX screenshot harness should declare an explicit tab sweep");

        var scriptTabs = Regex
            .Matches(match.Groups["tabs"].Value, "\"(?<tab>[^\"]+)\"")
            .Select(item => item.Groups["tab"].Value)
            .ToArray();

        scriptTabs.Should().Equal(
            RibbonScreenshotTourPlanner.DefaultTabs.Select(tab => tab.Header),
            "the foreground-gated PowerShell harness should not drift from the CI-safe in-app ribbon tour");
    }

    [Theory]
    [InlineData("screenshot_excel.ps1")]
    [InlineData("screenshot_ribbon.ps1")]
    public void ScreenshotScripts_CapturePhysicalBoundsFromTheWindowRectangle(string scriptName)
    {
        var script = ReadScript(scriptName);

        script.Should().Contain("GetWindowRect($hwnd");
        script.Should().Contain("$w = $wrect.Right - $wrect.Left");
        script.Should().Contain("CopyFromScreen($wrect.Left, $wrect.Top, 0, 0");
        script.Should().Contain("Width = $w");
        script.Should().Contain("Height = $captureH");
    }

    [Fact]
    public void FreeXScreenshotScript_FailsFastWhenReleaseHostIsMissing()
    {
        var script = ReadScript("screenshot_ribbon.ps1");

        script.Should().Contain("Test-Path -LiteralPath $exe");
        script.Should().Contain("FreeX executable was not found at $exe. Build the Release host before running tools\\screenshot_ribbon.ps1.");
        script.Should().Contain("$proc = Start-Process -FilePath $exe -PassThru");
    }

    [Fact]
    public void ExcelScreenshotScript_FailsFastWhenExcelIsMissing()
    {
        var script = ReadScript("screenshot_excel.ps1");

        script.Should().Contain("Test-Path -LiteralPath $exe");
        script.Should().Contain("Excel executable was not found at $exe. Install Microsoft Excel or update tools\\screenshot_excel.ps1 before running this capture.");
        script.Should().Contain("Start-Process -FilePath $exe -ArgumentList \"/e\"");
    }

    private static string ReadScript(string scriptName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("tools", scriptName));
}
