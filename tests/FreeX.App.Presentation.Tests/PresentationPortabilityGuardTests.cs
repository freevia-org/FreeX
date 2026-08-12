using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Keeps FreeX.App.Presentation a portable (net10.0) layer: no UI framework (WPF or Avalonia),
/// no Windows-only APIs, no host-project dependencies. Any renderer or app must be able to consume
/// it, so the rendering itself stays out.
/// </summary>
public sealed class PresentationPortabilityGuardTests
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly PortableBoundaryPattern[] ForbiddenPatterns =
    [
        new("System.Windows namespace", new(@"(?<![\w.])System\.Windows(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("System.Printing namespace", new(@"(?<![\w.])System\.Printing(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("Microsoft.Win32 namespace", new(@"(?<![\w.])Microsoft\.Win32(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("WinRT Windows namespace", new(@"(?<![\w.])Windows\.[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?![\w.])", Options)),
        new(
            "Avalonia namespace",
            new(
                @"(?m)(?:^\s*(?:global\s+)?using\s+(?:global::)?Avalonia(?:[.;])|(?<![\w.])(?:global::)?Avalonia\.[A-Za-z_]\w*)",
                Options)),
        new("AppKit namespace", new(@"(?<![\w.])AppKit(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("Foundation namespace", new(@"(?<![\w.])Foundation(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("FreeX.App.Host dependency", new(@"(?<![\w.])FreeX\.App\.Host(?![\w.])", Options)),
        new("FreeX.App.UI dependency", new(@"(?<![\w.])FreeX\.App\.UI(?![\w.])", Options)),
        new("FreeX.App.Avalonia dependency", new(@"(?<![\w.])FreeX\.App\.Avalonia(?![\w.])", Options)),
        new("UseWPF marker", new(@"(?<![\w])UseWPF(?![\w])", Options | RegexOptions.IgnoreCase)),
        new("UseWindowsForms marker", new(@"(?<![\w])UseWindowsForms(?![\w])", Options | RegexOptions.IgnoreCase)),
        new("Windows-targeted framework", new(@"(?<![\w.-])net\d+(?:\.\d+)?-windows(?:\d+(?:\.\d+)*)?(?![\w.-])", Options | RegexOptions.IgnoreCase)),
        new("WPF assembly reference", new(@"(?<![\w.])(?:PresentationCore|PresentationFramework|System\.Xaml|WindowsBase)(?![\w.])", Options)),
        new("ReachFramework assembly reference", new(@"(?<![\w.])ReachFramework(?![\w.])", Options)),
        new("System.Drawing dependency", new(@"(?<![\w.])System\.Drawing(?:\.[A-Za-z_]\w*)?(?![\w.])", Options))
    ];

    [Fact]
    public void PresentationLayer_StaysPortable()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");

        var violations = PortableBoundaryGuard.FindSourceViolations(
                presentationRoot,
                presentationRoot,
                ForbiddenPatterns,
                shouldScanLine: PortableBoundaryGuard.IsNonCommentLine)
            .Select(v => v.ToString())
            .ToArray();

        violations.Should().BeEmpty(
            "FreeX.App.Presentation must stay a portable view-model/layout layer free of WPF, Avalonia, "
            + "Windows-only APIs, and host-project dependencies");
    }

    [Fact]
    public void CellReferenceInputParser_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(presentationRoot, "CellReferenceInputParser.cs"))
            .Should()
            .BeTrue("cell reference parsing is shared by Host dialogs and portable Text to Columns planning");
        File.Exists(Path.Combine(presentationRoot, "TextToColumns", "CellReferenceInputParser.cs"))
            .Should()
            .BeFalse("Text to Columns should use the shared presentation parser instead of carrying a local copy");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "CellReferenceInputParser.cs"))
            .Should()
            .BeFalse("WPF host should use the shared presentation parser instead of carrying a renderer-local copy");
    }

    [Fact]
    public void PrintSettingsPlanner_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(presentationRoot, "PageLayout", "PrintSettingsPlanner.cs"))
            .Should()
            .BeTrue("print settings planning is shared by WPF print preview, Avalonia, and sister apps");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "PrintSettingsPlanner.cs"))
            .Should()
            .BeFalse("WPF host should use the shared presentation planner instead of carrying a renderer-local copy");
    }

    [Fact]
    public void ChartInputParser_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(presentationRoot, "Charts", "ChartInputParser.cs"))
            .Should()
            .BeTrue("chart source range parsing is shared by WPF, Avalonia, and sister app chart flows");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ChartInputParser.cs"))
            .Should()
            .BeFalse("WPF host should use the shared chart range parser instead of carrying a renderer-local copy");
    }

    [Fact]
    public void ChartOptionCycler_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(presentationRoot, "Charts", "Editing", "ChartOptionCycler.cs"))
            .Should()
            .BeTrue("chart command cycling should be shared by WPF and portable chart flows");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ChartOptionCycler.cs"))
            .Should()
            .BeFalse("WPF host should use the shared chart command cycler instead of carrying a renderer-local copy");
    }

    [Fact]
    public void WorkbookRangeTextCodec_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(presentationRoot, "WorkbookRangeTextCodec.cs"))
            .Should()
            .BeTrue("workbook range text parsing is shared by chart, pivot, scenario, and data dialogs");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "WorkbookRangeTextCodec.cs"))
            .Should()
            .BeFalse("WPF host should use the shared workbook range codec instead of carrying a renderer-local copy");
    }

    [Fact]
    public void ShellWindowPlanners_AreSingleSharedPresentationImplementations()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        var sharedShellFiles = new[]
        {
            "ArrangeAllMenuPlanner.cs",
            "ShellFocusCyclePlanner.cs",
            "WorkbookTitleFormatter.cs",
            "WorkbookWindowOrdering.cs",
            "WorkbookWindowRegistryCore.cs",
            "WorkbookWindowSelectionPlanner.cs"
        };

        foreach (var fileName in sharedShellFiles)
        {
            File.Exists(Path.Combine(presentationRoot, "Shell", fileName))
                .Should()
                .BeTrue($"{fileName} should live in the shared Presentation shell layer");
            File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", fileName))
                .Should()
                .BeFalse($"WPF host should use the shared {fileName} instead of carrying a renderer-local copy");
        }
    }

    [Fact]
    public void SlicerTimelineAndSparklineRenderPlanners_AreSingleSharedPresentationImplementations()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(presentationRoot, "SlicerTimeline", "SlicerTimelineInteractionPlanner.cs"))
            .Should()
            .BeTrue("slicer/timeline hit-to-command planning is shared by renderers");
        File.Exists(Path.Combine(presentationRoot, "SlicerTimeline", "SlicerTimelineSourceReader.cs"))
            .Should()
            .BeTrue("slicer/timeline source resolution and date granularity are Presentation-owned");
        File.Exists(Path.Combine(presentationRoot, "SlicerTimeline", "SlicerItemResolver.cs"))
            .Should()
            .BeTrue("table and pivot-cache slicer item projection is Presentation-owned");
        File.Exists(Path.Combine(presentationRoot, "SparklineUI", "SparklineRenderPlanner.cs"))
            .Should()
            .BeTrue("sparkline render instruction planning is shared by renderers");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "SlicerTimelineInteractionPlanner.cs"))
            .Should()
            .BeFalse("Avalonia should use the shared slicer/timeline interaction planner instead of carrying a renderer-local copy");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "SlicerTimelineSourceReader.cs"))
            .Should()
            .BeFalse("Avalonia should use the Presentation source session");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.Core.Commands", "SlicerItemResolver.cs"))
            .Should()
            .BeFalse("renderer-facing slicer item projection should not live in Commands");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "SlicerTimelinePlanner.cs"))
            .Should()
            .BeFalse("WPF should use the Presentation planner directly without a host facade");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "SparklineRenderPlanner.cs"))
            .Should()
            .BeFalse("Avalonia should use the shared sparkline render planner instead of carrying a renderer-local copy");
    }

    [Fact]
    public void PageBreakDialogPlanner_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var hostDialogPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "PageBreakDialog.cs");

        File.Exists(Path.Combine(presentationRoot, "PageLayout", "PageBreakDialogPlanner.cs"))
            .Should()
            .BeTrue("page-break dialog result parsing and command planning should be shared by renderers");
        File.ReadAllText(hostDialogPath)
            .Should()
            .NotContain("public enum PageBreakDialogAction")
            .And
            .NotContain("public sealed record PageBreakDialogResult");
    }

    [Fact]
    public void NamedRangeDialogPlanning_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(presentationRoot, "NamedRanges", "NamedRangeInputParser.cs"))
            .Should()
            .BeTrue("named-range reference parsing should be shared by renderers");
        File.Exists(Path.Combine(presentationRoot, "NamedRanges", "NamedRangeDialogPlanner.cs"))
            .Should()
            .BeTrue("named-range filtering and row models should be shared by renderers");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "NamedRangeInputParser.cs"))
            .Should()
            .BeFalse("WPF host should use the shared named-range parser instead of carrying a renderer-local copy");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "NamedRangeDialogPlanner.cs"))
            .Should()
            .BeFalse("WPF host should use the shared named-range planner instead of carrying a renderer-local copy");
    }

    [Fact]
    public void PasteNamesPlanner_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(presentationRoot, "DefinedNames", "PasteNamesPlanner.cs"))
            .Should()
            .BeTrue("Paste Names projection and edit planning should live in the shared Presentation layer");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "PasteNamesPlanner.cs"))
            .Should()
            .BeFalse("WPF host should use the shared Paste Names planner instead of carrying a renderer-local facade");
    }

    [Fact]
    public void ProtectionDialogParsingAndResults_AreSharedPresentationImplementations()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var hostProtectionDialogsPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "ProtectionDialogs.cs");

        File.Exists(Path.Combine(presentationRoot, "Protection", "ProtectionInputParser.cs"))
            .Should()
            .BeFalse("AllowEditRangePlanner owns the live parsing path without a narrower parser facade");
        File.Exists(Path.Combine(presentationRoot, "Protection", "AllowEditRangePlanner.cs"))
            .Should()
            .BeTrue("allow-edit-range parsing and command planning should have one live shared owner");
        File.Exists(Path.Combine(presentationRoot, "Protection", "ProtectionDialogPlanner.cs"))
            .Should()
            .BeTrue("protect/unprotect result creation should be shared by renderers");
        File.Exists(Path.Combine(presentationRoot, "Protection", "ProtectionWorkflowSession.cs"))
            .Should()
            .BeTrue("protect/unprotect commands, outcomes, and state transitions should be shared by renderers");
        File.Exists(Path.Combine(presentationRoot, "Protection", "ProtectionWorkflowPlanner.cs"))
            .Should()
            .BeFalse("the shared protection session supersedes the narrower planner");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ProtectionInputParser.cs"))
            .Should()
            .BeFalse("WPF host should use the shared protection parser instead of carrying a renderer-local copy");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ProtectionDialogPlanner.cs"))
            .Should()
            .BeFalse("WPF host should call the shared protection dialog planner directly");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "AllowEditRangeDialogPlanner.cs"))
            .Should()
            .BeFalse("WPF host should use the shared allow-edit-range planner instead of carrying a renderer-local copy");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "SheetProtectionWorkflow.cs"))
            .Should()
            .BeFalse("WPF should execute sheet protection through the shared session");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "WorkbookProtectionWorkflow.cs"))
            .Should()
            .BeFalse("WPF should execute workbook protection through the shared session");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "SheetProtectionPermissionLabels.cs"))
            .Should()
            .BeFalse("permission identity and label keys should come from shared Presentation options");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "Dialogs", "ProtectionShellGlue.cs"))
            .Should()
            .BeFalse("Avalonia should execute protection through the shared session");
        File.ReadAllText(hostProtectionDialogsPath)
            .Should()
            .NotContain("public enum ProtectionDialogMode")
            .And
            .NotContain("public sealed record ProtectionDialogResult");
    }
}
