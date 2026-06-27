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
        new("Avalonia dependency", new(@"(?<![\w.])Avalonia(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
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

        var violations = PortableBoundaryGuard.FindSourceViolations(presentationRoot, presentationRoot, ForbiddenPatterns)
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
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");

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
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");

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
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");

        File.Exists(Path.Combine(presentationRoot, "Charts", "ChartInputParser.cs"))
            .Should()
            .BeTrue("chart source range parsing is shared by WPF, Avalonia, and sister app chart flows");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "ChartInputParser.cs"))
            .Should()
            .BeFalse("WPF host should use the shared chart range parser instead of carrying a renderer-local copy");
    }

    [Fact]
    public void WorkbookRangeTextCodec_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");

        File.Exists(Path.Combine(presentationRoot, "WorkbookRangeTextCodec.cs"))
            .Should()
            .BeTrue("workbook range text parsing is shared by chart, pivot, scenario, and data dialogs");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "WorkbookRangeTextCodec.cs"))
            .Should()
            .BeFalse("WPF host should use the shared workbook range codec instead of carrying a renderer-local copy");
    }

    [Fact]
    public void PageBreakDialogPlanner_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
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
}
