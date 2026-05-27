using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ScreenshotHarnessScriptTests
{
    [Theory]
    [InlineData("screenshot_excel.ps1", "screenshots_excel", "excel_<RibbonTab>.png", "excel_$safe.png")]
    [InlineData("screenshot_ribbon.ps1", "screenshots", "ribbon_<RibbonTab>.png", "ribbon_$safe.png")]
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
        script.Should().Contain("WindowBounds = [pscustomobject]");
        script.Should().Contain("CaptureLogicalHeight = $captureLogicalHeight");
        script.Should().Contain("CapturePhysicalHeight = $capturePhysicalHeight");
        script.Should().Contain("Captures = $files");
        script.Should().Contain($"$path = \"$outDir\\{filePattern}\"");
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
    public void ScreenshotScripts_CapturePhysicalBoundsFromTheWindowRectangle(string scriptName)
    {
        var script = ReadScript(scriptName);

        script.Should().Contain("GetWindowRect($hwnd");
        script.Should().Contain("$w = $wrect.Right - $wrect.Left");
        script.Should().Contain("CopyFromScreen($wrect.Left, $wrect.Top, 0, 0");
        script.Should().Contain("Width = $w");
        script.Should().Contain("Height = $captureH");
    }

    private static string ReadScript(string scriptName) =>
        File.ReadAllText(WorkspaceFileLocator.Find("tools", scriptName));
}
