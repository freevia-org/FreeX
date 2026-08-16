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
        script.Should().Contain("function Write-RibbonScreenshotEvidenceManifest");
        script.Should().Contain("screenshot_manifest.json");
        script.Should().Contain($"-OutputNaming \"{namingPattern}\"");
        script.Should().Contain($"CatalogEvidenceTarget = \"docs/testing/ui-test-catalog.md\"");
        script.Should().Contain("WidthSource = \"RibbonScreenshotTourPlanner.DefaultWidths\"");
        script.Should().Contain("$plannedCaptureCount = $Tabs.Count * $Widths.Count");
        script.Should().Contain("PlannedCaptureCount = $plannedCaptureCount");
        script.Should().Contain("ActualCaptureCount = $Captures.Count");
        script.Should().Contain("CaptureStatus = \"complete\"");
        script.Should().Contain("CaptureMethod = \"CopyFromScreen-window-rectangle-top-band\"");
        script.Should().Contain("ForegroundGuard = [pscustomobject]");
        script.Should().Contain("ExpectedProcessId = $ExpectedProcessId");
        script.Should().Contain("ExpectedWindowTitle = $ExpectedWindowTitle");
        script.Should().Contain("WindowBounds = [pscustomobject]");
        script.Should().Contain("CaptureLogicalHeight = $CaptureLogicalHeight");
        script.Should().Contain("CapturePhysicalHeight = $CapturePhysicalHeight");
        script.Should().Contain("Widths = $Widths");
        script.Should().Contain("$manifest[\"Captures\"] = $Captures");
        script.Should().Contain("CaptureSequence = $script:capturedFiles.Count + 1");
        script.Should().Contain("CaptureKey = \"ribbon:$($widthSpec.Label):$safe\"");
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
        if (scriptName == "screenshot_excel.ps1")
        {
            script.Should().Contain("foreach ($tabName in $script:requestedTabNames)");
            script.Should().Contain("foreach ($tabName in $script:availableTabNames)");
            script.Should().Contain("SkippedTabs = $script:skippedTabNames");
            script.Should().Contain("$manifest[\"SkippedCaptures\"] = $skippedCaptures");
        }
        else
        {
            script.Should().Contain("foreach ($tabName in $tabNames)");
        }
        script.Should().Contain("Transient popups, dropdowns, native dialogs, and context menus require separate guarded captures.");
        script.Should().Contain("Ribbon tab captures cover the top window band only.");
        script.Should().Contain("Global input and screen capture are blocked unless the expected process and window title own the foreground window.");
    }

    [Theory]
    [InlineData("screenshot_excel.ps1", "excel", "freex")]
    [InlineData("screenshot_ribbon.ps1", "freex", "excel")]
    public void ScreenshotScripts_EmitInteractiveCapturePlanForTransientSurfaces(
        string scriptName,
        string evidenceSubject,
        string counterpartSubject)
    {
        var script = ReadScript(scriptName);

        script.Should().Contain("$interactiveCapturePlan = @(");
        script.Should().Contain("-InteractiveCapturePlan $interactiveCapturePlan");
        script.Should().Contain("ScenarioId = \"popup:table-autofilter-dropdown\"");
        script.Should().Contain("ScenarioId = \"dropdown:home-number-format\"");
        script.Should().Contain("ScenarioId = \"context-menu:worksheet-cell\"");
        script.Should().Contain("ScenarioId = \"native-dialog:open-workbook\"");
        script.Should().Contain("ScenarioFileName = \"table_autofilter_dropdown\"");
        script.Should().Contain($"EvidenceSubject = \"{evidenceSubject}\"");
        script.Should().Contain($"CounterpartSubject = \"{counterpartSubject}\"");
        script.Should().Contain("CaptureStatus = \"planned-separate-foreground-guarded-capture\"");
        script.Should().Contain("CaptureOutputNaming = \"interactive_<ScenarioFileName>_<State>.png\"");
        script.Should().Contain("PairKeyPattern = \"interactive:table-autofilter-dropdown:<State>\"");
        script.Should().Contain("Capture the active popup/dialog/menu bounds, not the owner-window ribbon band.");
        script.Should().Contain("ForegroundGuard = ");
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
        missingTabBranch.Groups["body"].Value.Should().Contain("Clear-ScreenshotEvidenceArtifacts");
        var expectedMessage = scriptName == "screenshot_excel.ps1"
            ? "throw \"Ribbon screenshot tab '$tabName' was discovered during preflight but was not found during capture"
            : "throw \"Ribbon screenshot tab '$tabName' was not found";
        missingTabBranch.Groups["body"].Value.Should().Contain(expectedMessage);
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
        script.Should().Contain($"-CounterpartTool \"{counterpartTool}\"");
        script.Should().Contain($"-CounterpartOutputNaming \"{counterpartOutputNaming}\"");
        script.Should().Contain("PairKey = \"ribbon:$($widthSpec.Label):$safe\"");
        script.Should().Contain("CaptureKey = \"ribbon:$($widthSpec.Label):$safe\"");
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
        script.Should().Contain("CopyFromScreen($left, $top, 0, 0");
        script.Should().Contain("Width = $w");
        script.Should().Contain("Height = $captureH");
    }

    [Theory]
    [InlineData("screenshot_excel.ps1")]
    [InlineData("screenshot_ribbon.ps1")]
    public void ScreenshotScripts_ClearPngsAndManifestWhenEvidenceIsInvalidated(string scriptName)
    {
        var script = ReadScript(scriptName);

        script.Should().Contain("function Clear-ScreenshotEvidenceArtifacts");
        script.Should().Contain("Get-ChildItem $OutputDirectory -Filter \"*.png\" -ErrorAction SilentlyContinue");
        script.Should().Contain("Remove-Item -LiteralPath (Join-Path $OutputDirectory \"screenshot_manifest.json\")");
        Regex.Matches(script, "Clear-ScreenshotEvidenceArtifacts")
            .Count
            .Should()
            .BeGreaterThanOrEqualTo(4, "run start, foreground failure, missing tabs, and incomplete matrix should discard stale evidence");
    }

    [Theory]
    [InlineData("screenshot_excel.ps1")]
    [InlineData("screenshot_ribbon.ps1")]
    public void ScreenshotScripts_CheckForegroundOwnershipImmediatelyBeforeScreenCopy(string scriptName)
    {
        var lines = File.ReadAllLines(WorkspaceFileLocator.Find("tools", scriptName));
        var copyLine = Array.FindIndex(lines, line => line.Contains("Capture-ScreenRectangle $wrect.Left", StringComparison.Ordinal));

        copyLine.Should().BeGreaterThan(0);
        var precedingCaptureBlock = string.Join(
            Environment.NewLine,
            lines.Skip(Math.Max(0, copyLine - 8)).Take(8));

        precedingCaptureBlock.Should().Contain("Assert-ForegroundWindowOwnership");
        precedingCaptureBlock.Should().Contain("\"screen capture\"");
    }

    [Fact]
    public void FreeXScreenshotScript_ChecksForegroundOwnershipBeforeUiaTabSelection()
    {
        var script = ReadScript("screenshot_ribbon.ps1");

        script.Should().Contain("Assert-ForegroundWindowOwnership $proc.Id $expectedTitle \"ribbon tab selection\"");
        script.Should().Contain("if ($selPat -ne $null) { $selPat.Select() }");
    }

    [Theory]
    [InlineData(
        "screenshot_excel.ps1",
        "Set-ExcelForegroundWindow",
        "Set-ExcelForegroundWindow $hwnd $wpid $expectedTitle \"initial capture setup\"",
        "Set-ExcelForegroundWindow $windowHandle $wpid $expectedTitle \"window resize capture setup\"")]
    [InlineData(
        "screenshot_ribbon.ps1",
        "Set-FreeXForegroundWindow",
        "Set-FreeXForegroundWindow $hwnd $proc.Id $expectedTitle \"initial capture setup\"",
        "Set-FreeXForegroundWindow $windowHandle $proc.Id $expectedTitle \"window resize capture setup\"")]
    public void ScreenshotScripts_RetryForegroundActivationBeforeRibbonResizeSetup(
        string scriptName,
        string helperName,
        string initialActivation,
        string resizeActivation)
    {
        var script = ReadScript(scriptName);

        script.Should().Contain($"function {helperName}");
        script.Should().Contain("New-Object -ComObject WScript.Shell");
        script.Should().Contain("SetWindowPos($");
        script.Should().Contain("SetForegroundWindow($");
        script.Should().Contain("AppActivate([int]");
        script.Should().Contain(initialActivation);
        script.Should().Contain(resizeActivation);
    }

    [Fact]
    public void FreeXScreenshotScript_AcceptsExplicitExePathAndLaunchesTheMatchingAssemblyThroughDotNet()
    {
        var script = ReadScript("screenshot_ribbon.ps1");

        script.Should().Contain("[string]$ExePath = $env:FREEX_RIBBON_EXE_PATH");
        script.Should().Contain("function Resolve-FreeXExecutablePath");
        script.Should().Contain("Test-Path -LiteralPath $resolvedRequestedExePath -PathType Leaf");
        script.Should().Contain("Pass -ExePath with an existing FreeX.ParityCapture.Wpf.exe");
        script.Should().Contain("Get-ChildItem -LiteralPath $binRoot -Recurse -Filter \"FreeX.ParityCapture.Wpf.exe\" -File");
        script.Should().Contain("Sort-Object LastWriteTimeUtc -Descending");
        script.Should().Contain("$exe = Resolve-FreeXExecutablePath $ExePath");
        script.Should().Contain("function Resolve-FreeXCaptureAssemblyPath");
        script.Should().Contain("[System.IO.Path]::ChangeExtension($executablePath, \".dll\")");
        script.Should().Contain("function Resolve-DotNetHostPath");
        script.Should().Contain("$captureAssembly = Resolve-FreeXCaptureAssemblyPath $exe");
        script.Should().Contain("$dotnetHost = Resolve-DotNetHostPath");
        script.Should().Contain("$proc = Start-Process -FilePath $dotnetHost -ArgumentList @($captureAssembly) -WorkingDirectory (Split-Path -Parent $captureAssembly) -PassThru");
    }

    [Fact]
    public void FreeXParityCaptureHost_UsesTheGlobalDesktopRuntimeForItsDirectAppHost()
    {
        var project = File.ReadAllText(WorkspaceFileLocator.Find(
            "tools", "FreeX.ParityCapture.Wpf", "FreeX.ParityCapture.Wpf.csproj"));

        project.Should().Contain("<AppHostDotNetSearch>Global</AppHostDotNetSearch>");
        project.Should().Contain("Target Name=\"ConfigureCaptureBuildAppHostRuntimeSearch\"");
        project.Should().Contain("AfterTargets=\"_CreateAppHost\"");
        project.Should().Contain("DotNetSearchLocations=\"$(AppHostDotNetSearch)\"");
        project.Should().Contain("AppHostDestinationPath=\"$(AppHostIntermediatePath)\"");
    }

    [Fact]
    public void FreeXScreenshotScript_ProvidesOptInOpenWorkbookDialogTour()
    {
        var script = ReadScript("screenshot_ribbon.ps1");

        script.Should().Contain("[string]$OpenWorkbookDialogTour = $env:FREEX_OPEN_WORKBOOK_DIALOG_TOUR");
        script.Should().Contain("if ($OpenWorkbookDialogTour -eq \"1\")");
        script.Should().Contain("function Invoke-FreeXOpenWorkbookDialogTour");
        script.Should().Contain("function Find-FreeXOpenWorkbookDialogWindow");
        script.Should().Contain("function Capture-ScreenRectangle");
        script.Should().Contain("GetDpiForWindow");
        script.Should().Contain("Join-Path $outDir \"open-workbook-dialog-tour\"");
        script.Should().Contain("freex_open_workbook_dialog_tour_manifest.json");
        script.Should().Contain("freex_open_workbook_dialog_opened.png");
        script.Should().Contain("Tool = \"FREEX_OPEN_WORKBOOK_DIALOG_TOUR\"");
        script.Should().Contain("EvidenceFamily = \"native-dialog\"");
        script.Should().Contain("ScenarioId = \"native-dialog:open-workbook\"");
        script.Should().Contain("DialogClassName = \"#32770\"");
        script.Should().Contain("EntryPath = \"Ctrl+O\"");
        script.Should().Contain("CaptureScale = $dialogScale");
        script.Should().Contain("[System.Windows.Forms.SendKeys]::SendWait(\"^o\")");
        script.Should().Contain("Find-FreeXOpenWorkbookDialogWindow $expectedPid $ownerWindowHandle");
        script.Should().Contain("PairKey = \"interactive:open-workbook-dialog:opened\"");
        script.Should().Contain("CounterpartTool = \"FREEX_EXCEL_OPEN_WORKBOOK_DIALOG_TOUR\"");
        script.Should().Contain("CounterpartFileName = \"interactive_open_workbook_dialog_opened.png\"");
        script.Should().Contain("Assert-ForegroundWindowOwnership $expectedPid $expectedTitle \"FreeX native Open dialog keyboard input\"");
        script.Should().Contain("Assert-ForegroundProcessOwnership $expectedPid \"FreeX native Open dialog screen capture\"");
    }

    [Fact]
    public void FreeXScreenshotScript_ProvidesOptInSaveAsWorkbookDialogTour()
    {
        var script = ReadScript("screenshot_ribbon.ps1");

        script.Should().Contain("[string]$SaveAsWorkbookDialogTour = $env:FREEX_SAVE_AS_WORKBOOK_DIALOG_TOUR");
        script.Should().Contain("if ($SaveAsWorkbookDialogTour -eq \"1\")");
        script.Should().Contain("function Invoke-FreeXSaveAsWorkbookDialogTour");
        script.Should().Contain("function Find-FreeXSaveAsWorkbookDialogWindow");
        script.Should().Contain("Join-Path $outDir \"save-as-workbook-dialog-tour\"");
        script.Should().Contain("freex_save_as_workbook_dialog_tour_manifest.json");
        script.Should().Contain("freex_save_as_workbook_dialog_opened.png");
        script.Should().Contain("Tool = \"FREEX_SAVE_AS_WORKBOOK_DIALOG_TOUR\"");
        script.Should().Contain("EvidenceFamily = \"native-dialog\"");
        script.Should().Contain("ScenarioId = \"native-dialog:save-as-workbook\"");
        script.Should().Contain("DialogClassName = \"#32770\"");
        script.Should().Contain("EntryPath = \"F12\"");
        script.Should().Contain("[System.Windows.Forms.SendKeys]::SendWait(\"{F12}\")");
        script.Should().Contain("Find-FreeXSaveAsWorkbookDialogWindow $expectedPid $ownerWindowHandle");
        script.Should().Contain("PairKey = \"interactive:save-as-workbook-dialog:opened\"");
        script.Should().Contain("CounterpartTool = \"FREEX_EXCEL_SAVE_AS_WORKBOOK_DIALOG_TOUR\"");
        script.Should().Contain("CounterpartFileName = \"interactive_save_as_workbook_dialog_opened.png\"");
        script.Should().Contain("Assert-ForegroundWindowOwnership $expectedPid $expectedTitle \"FreeX native Save As dialog keyboard input\"");
        script.Should().Contain("Assert-ForegroundProcessOwnership $expectedPid \"FreeX native Save As dialog screen capture\"");
    }

    [Fact]
    public void ExcelScreenshotScript_FailsFastWhenExcelIsMissing()
    {
        var script = ReadScript("screenshot_excel.ps1");

        script.Should().Contain("Test-Path -LiteralPath $exe");
        script.Should().Contain("Excel executable was not found at $exe. Install Microsoft Excel or update tools\\screenshot_excel.ps1 before running this capture.");
        script.Should().Contain("Start-Process -FilePath $exe -ArgumentList @(\"/x\", \"/e\") -PassThru");
    }

    [Fact]
    public void ExcelScreenshotScript_ProvidesOptInAutoFilterFlyoutTour()
    {
        var script = ReadScript("screenshot_excel.ps1");

        script.Should().Contain("[string]$AutoFilterFlyoutTour = $env:FREEX_EXCEL_AUTOFILTER_FLYOUT_TOUR");
        script.Should().Contain("if ($AutoFilterFlyoutTour -eq \"1\")");
        script.Should().Contain("function Invoke-ExcelAutoFilterFlyoutTour");
        script.Should().Contain("function New-ExcelAutoFilterSampleWorkbook");
        script.Should().Contain("Join-Path $outDir \"autofilter-flyout-tour\"");
        script.Should().Contain("excel_autofilter_flyout_tour_manifest.json");
        script.Should().Contain("interactive_table_autofilter_dropdown_opened.png");
        script.Should().Contain("Tool = \"FREEX_EXCEL_AUTOFILTER_FLYOUT_TOUR\"");
        script.Should().Contain("EvidenceFamily = \"popup\"");
        script.Should().Contain("EvidenceSubject = \"excel\"");
        script.Should().Contain("EvidenceApp = \"Microsoft Excel\"");
        script.Should().Contain("OutputNaming = \"interactive_table_autofilter_dropdown_opened.png\"");
        script.Should().Contain("CatalogEvidenceTarget = \"docs/testing/ui-test-catalog.md\"");
        script.Should().Contain("HeaderCell = \"A1\"");
        script.Should().Contain("HeaderText = \"score\"");
        script.Should().Contain("AutoFilterRange = \"A1:D6\"");
        script.Should().Contain("FilterColumnOffset = 0");
        script.Should().Contain("CaptureStatus = \"complete\"");
        script.Should().Contain("State = \"opened\"");
        script.Should().Contain("function Click-ExcelAutoFilterHeaderDropdown");
        script.Should().Contain("[ScreenshotWin32]::SetProcessDPIAware() | Out-Null");
        script.Should().Contain("[ScreenshotWin32]::SetCursorPos($clickX, $clickY) | Out-Null");
        script.Should().Contain("$pointToScreenScale = 2.0");
        script.Should().Contain("$clickX = [int]($left + ($header.Width * $pointToScreenScale) - 12)");
        script.Should().Contain("function Set-ExcelForegroundWindow");
        script.Should().Contain("New-Object -ComObject WScript.Shell");
        script.Should().Contain("[ScreenshotWin32]::SetWindowPos($excelHwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0043) | Out-Null");
        script.Should().Contain("Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle \"Excel AutoFilter flyout setup\"");
        script.Should().Contain("PairKey = \"interactive:table-autofilter-dropdown:opened\"");
        script.Should().Contain("CounterpartTool = \"FREEX_AUTOFILTER_FLYOUT_TOUR\"");
        script.Should().Contain("CounterpartFileName = \"freex_table_autofilter_dropdown.png\"");
        script.Should().Contain("SampleRange = \"A1:D6\"");
        script.Should().Contain("SampleValues = @(\"1\", \"2\", \"3\", \"4\", \"(Blanks)\")");
        script.Should().Contain("Assert-ForegroundWindowOwnership $excelPid $excelTitle \"Excel AutoFilter flyout setup\"");
        script.Should().Contain("Click-ExcelAutoFilterHeaderDropdown $excelApp $worksheet \"A1\" $excelPid $excelTitle");
        script.Should().Contain("Excel AutoFilter flyout tour did not detect a foreground Excel popup window");
        script.Should().Contain("Assert-ForegroundProcessOwnership $excelPid \"Excel AutoFilter flyout screen capture\"");
        script.Should().Contain("Find-ExcelAutoFilterPopupWindow $excelPid $excelHwnd");
    }

    [Fact]
    public void ExcelScreenshotScript_ProvidesOptInHomeNumberFormatDropdownTour()
    {
        var script = ReadScript("screenshot_excel.ps1");

        script.Should().Contain("[string]$NumberFormatDropdownTour = $env:FREEX_EXCEL_NUMBER_FORMAT_DROPDOWN_TOUR");
        script.Should().Contain("if ($NumberFormatDropdownTour -eq \"1\")");
        script.Should().Contain("function Invoke-ExcelNumberFormatDropdownTour");
        script.Should().Contain("function New-ExcelNumberFormatSampleWorkbook");
        script.Should().Contain("function Expand-ExcelNumberFormatDropdown");
        script.Should().Contain("Join-Path $outDir \"home-number-format-dropdown-tour\"");
        script.Should().Contain("excel_home_number_format_dropdown_tour_manifest.json");
        script.Should().Contain("interactive_home_number_format_opened.png");
        script.Should().Contain("Tool = \"FREEX_EXCEL_NUMBER_FORMAT_DROPDOWN_TOUR\"");
        script.Should().Contain("EvidenceFamily = \"dropdown\"");
        script.Should().Contain("EvidenceSubject = \"excel\"");
        script.Should().Contain("EvidenceApp = \"Microsoft Excel\"");
        script.Should().Contain("OutputNaming = \"interactive_home_number_format_opened.png\"");
        script.Should().Contain("CatalogEvidenceTarget = \"docs/testing/ui-test-catalog.md\"");
        script.Should().Contain("SelectedCell = \"A1\"");
        script.Should().Contain("SelectedFormat = \"General\"");
        script.Should().Contain("CaptureStatus = \"complete\"");
        script.Should().Contain("State = \"opened\"");
        script.Should().Contain("NumberFormatGallery");
        script.Should().Contain("[System.Windows.Automation.ExpandCollapsePattern]::Pattern");
        script.Should().Contain("$pattern.Expand()");
        script.Should().Contain("Find-ExcelPopupWindow $excelPid $excelHwnd 120 120");
        script.Should().Contain("Set-ExcelForegroundWindow $excelHwnd $excelPid $excelTitle \"Excel Number Format dropdown setup\"");
        script.Should().Contain("PairKey = \"interactive:home-number-format:opened\"");
        script.Should().Contain("CounterpartTool = \"FREEX_HOME_NUMBER_FORMAT_DROPDOWN_TOUR\"");
        script.Should().Contain("CounterpartFileName = \"freex_dropdown_home_number_format_opened.png\"");
        script.Should().Contain("SampleValue = \"1234.56\"");
        script.Should().Contain("Assert-ForegroundWindowOwnership $excelPid $excelTitle \"Excel Number Format dropdown setup\"");
        script.Should().Contain("Assert-ForegroundProcessOwnership $excelPid \"Excel Number Format dropdown screen capture\"");
    }

    [Fact]
    public void ExcelScreenshotScript_ProvidesOptInHomeBordersDropdownTour()
    {
        var script = ReadScript("screenshot_excel.ps1");

        script.Should().Contain("[string]$HomeBordersDropdownTour = $env:FREEX_EXCEL_HOME_BORDERS_DROPDOWN_TOUR");
        script.Should().Contain("if ($HomeBordersDropdownTour -eq \"1\")");
        script.Should().Contain("function Invoke-ExcelHomeBordersDropdownTour");
        script.Should().Contain("function Open-ExcelHomeBordersDropdown");
        script.Should().Contain("Join-Path $outDir \"home-borders-dropdown-tour\"");
        script.Should().Contain("excel_home_borders_dropdown_tour_manifest.json");
        script.Should().Contain("interactive_home_borders_opened.png");
        script.Should().Contain("Tool = \"FREEX_EXCEL_HOME_BORDERS_DROPDOWN_TOUR\"");
        script.Should().Contain("EvidenceFamily = \"dropdown\"");
        script.Should().Contain("ScenarioId = \"dropdown:home-borders\"");
        script.Should().Contain("EntryPath = \"Alt,H,B\"");
        script.Should().Contain("[System.Windows.Forms.SendKeys]::SendWait(\"%h\")");
        script.Should().Contain("[System.Windows.Forms.SendKeys]::SendWait(\"b\")");
        script.Should().Contain("Find-ExcelPopupWindow $excelPid $excelHwnd 120 160");
        script.Should().Contain("detected an oversized candidate window");
        script.Should().Contain("PairKey = \"interactive:home-borders:opened\"");
        script.Should().Contain("CounterpartTool = \"FREEX_HOME_BORDERS_DROPDOWN_TOUR\"");
        script.Should().Contain("CounterpartFileName = \"freex_dropdown_home_borders_opened.png\"");
        script.Should().Contain("Assert-ForegroundWindowOwnership $excelPid $excelTitle \"Excel Home Borders dropdown setup\"");
        script.Should().Contain("Assert-ForegroundProcessOwnership $excelPid \"Excel Home Borders dropdown screen capture\"");
    }

    [Fact]
    public void ExcelScreenshotScript_ProvidesOptInWorksheetContextMenuTour()
    {
        var script = ReadScript("screenshot_excel.ps1");

        script.Should().Contain("[string]$WorksheetContextMenuTour = $env:FREEX_EXCEL_WORKSHEET_CONTEXT_MENU_TOUR");
        script.Should().Contain("if ($WorksheetContextMenuTour -eq \"1\")");
        script.Should().Contain("function Invoke-ExcelWorksheetContextMenuTour");
        script.Should().Contain("function New-ExcelWorksheetContextMenuSampleWorkbook");
        script.Should().Contain("function Open-ExcelWorksheetContextMenu");
        script.Should().Contain("Join-Path $outDir \"worksheet-context-menu-tour\"");
        script.Should().Contain("excel_worksheet_context_menu_tour_manifest.json");
        script.Should().Contain("interactive_worksheet_cell_context_menu_opened.png");
        script.Should().Contain("Tool = \"FREEX_EXCEL_WORKSHEET_CONTEXT_MENU_TOUR\"");
        script.Should().Contain("EvidenceFamily = \"context-menu\"");
        script.Should().Contain("ScenarioId = \"context-menu:worksheet-cell\"");
        script.Should().Contain("SelectedCell = \"B2\"");
        script.Should().Contain("EntryPath = \"Shift+F10\"");
        script.Should().Contain("[System.Windows.Forms.SendKeys]::SendWait(\"+{F10}\")");
        script.Should().Contain("Find-ExcelPopupWindow $excelPid $excelHwnd 120 120");
        script.Should().Contain("PairKey = \"interactive:worksheet-cell-context-menu:opened\"");
        script.Should().Contain("CounterpartTool = \"FREEX_WORKSHEET_CONTEXT_MENU_TOUR\"");
        script.Should().Contain("CounterpartFileName = \"freex_context_menu_worksheet_cell_opened.png\"");
        script.Should().Contain("Assert-ForegroundWindowOwnership $excelPid $excelTitle \"Excel worksheet context menu setup\"");
        script.Should().Contain("Assert-ForegroundProcessOwnership $excelPid \"Excel worksheet context menu screen capture\"");
    }

    [Fact]
    public void ExcelScreenshotScript_ProvidesOptInOpenWorkbookDialogTour()
    {
        var script = ReadScript("screenshot_excel.ps1");

        script.Should().Contain("[string]$OpenWorkbookDialogTour = $env:FREEX_EXCEL_OPEN_WORKBOOK_DIALOG_TOUR");
        script.Should().Contain("if ($OpenWorkbookDialogTour -eq \"1\")");
        script.Should().Contain("function Invoke-ExcelOpenWorkbookDialogTour");
        script.Should().Contain("function Find-ExcelOpenWorkbookDialogWindow");
        script.Should().Contain("function Open-ExcelNativeOpenDialog");
        script.Should().Contain("Join-Path $outDir \"open-workbook-dialog-tour\"");
        script.Should().Contain("excel_open_workbook_dialog_tour_manifest.json");
        script.Should().Contain("interactive_open_workbook_dialog_opened.png");
        script.Should().Contain("Tool = \"FREEX_EXCEL_OPEN_WORKBOOK_DIALOG_TOUR\"");
        script.Should().Contain("EvidenceFamily = \"native-dialog\"");
        script.Should().Contain("ScenarioId = \"native-dialog:open-workbook\"");
        script.Should().Contain("DialogTitle = \"Open\"");
        script.Should().Contain("DialogClassName = \"#32770\"");
        script.Should().Contain("EntryPath = \"Ctrl+F12\"");
        script.Should().Contain("[System.Windows.Forms.SendKeys]::SendWait(\"^{F12}\")");
        script.Should().Contain("Find-ExcelOpenWorkbookDialogWindow $excelPid $excelHwnd");
        script.Should().Contain("PairKey = \"interactive:open-workbook-dialog:opened\"");
        script.Should().Contain("CounterpartTool = \"FREEX_OPEN_WORKBOOK_DIALOG_TOUR\"");
        script.Should().Contain("CounterpartFileName = \"freex_open_workbook_dialog_opened.png\"");
        script.Should().Contain("Assert-ForegroundWindowOwnership $excelPid $excelTitle \"Excel native Open dialog setup\"");
        script.Should().Contain("Assert-ForegroundProcessOwnership $excelPid \"Excel native Open dialog screen capture\"");
    }

    [Fact]
    public void ExcelScreenshotScript_ProvidesOptInSaveAsWorkbookDialogTour()
    {
        var script = ReadScript("screenshot_excel.ps1");

        script.Should().Contain("[string]$SaveAsWorkbookDialogTour = $env:FREEX_EXCEL_SAVE_AS_WORKBOOK_DIALOG_TOUR");
        script.Should().Contain("if ($SaveAsWorkbookDialogTour -eq \"1\")");
        script.Should().Contain("function Invoke-ExcelSaveAsWorkbookDialogTour");
        script.Should().Contain("function Find-ExcelSaveAsWorkbookDialogWindow");
        script.Should().Contain("function Open-ExcelNativeSaveAsDialog");
        script.Should().Contain("Join-Path $outDir \"save-as-workbook-dialog-tour\"");
        script.Should().Contain("excel_save_as_workbook_dialog_tour_manifest.json");
        script.Should().Contain("interactive_save_as_workbook_dialog_opened.png");
        script.Should().Contain("Tool = \"FREEX_EXCEL_SAVE_AS_WORKBOOK_DIALOG_TOUR\"");
        script.Should().Contain("EvidenceFamily = \"native-dialog\"");
        script.Should().Contain("ScenarioId = \"native-dialog:save-as-workbook\"");
        script.Should().Contain("($_.ClassName -eq \"NUIDialog\" -or ($_.ClassName -eq \"#32770\" -and $_.Title -eq \"Save As\"))");
        script.Should().Contain("DialogTitle = $dialog.Title");
        script.Should().Contain("DialogClassName = $dialog.ClassName");
        script.Should().Contain("EntryPath = \"F12\"");
        script.Should().Contain("[System.Windows.Forms.SendKeys]::SendWait(\"{F12}\")");
        script.Should().Contain("Find-ExcelSaveAsWorkbookDialogWindow $excelPid $excelHwnd");
        script.Should().Contain("PairKey = \"interactive:save-as-workbook-dialog:opened\"");
        script.Should().Contain("CounterpartTool = \"FREEX_SAVE_AS_WORKBOOK_DIALOG_TOUR\"");
        script.Should().Contain("CounterpartFileName = \"freex_save_as_workbook_dialog_opened.png\"");
        script.Should().Contain("Assert-ForegroundWindowOwnership $excelPid $excelTitle \"Excel native Save As dialog setup\"");
        script.Should().Contain("Assert-ForegroundProcessOwnership $excelPid \"Excel native Save As dialog screen capture\"");
    }

    private static string ReadScript(string scriptName)
    {
        var script = WorkspaceFileLocator.ReadAllText("tools", scriptName);
        script.Should().Contain(". (Join-Path $PSScriptRoot \"ScreenshotCaptureSupport.ps1\")");

        return string.Join(
            Environment.NewLine,
            WorkspaceFileLocator.ReadAllText("tools", "ScreenshotCaptureSupport.ps1"),
            script);
    }
}
