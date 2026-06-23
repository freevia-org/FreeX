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

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".props",
        ".targets"
    };

    private static readonly (string Description, Regex Pattern)[] ForbiddenPatterns =
    [
        ("System.Windows namespace", new(@"(?<![\w.])System\.Windows(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        ("System.Printing namespace", new(@"(?<![\w.])System\.Printing(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        ("Microsoft.Win32 namespace", new(@"(?<![\w.])Microsoft\.Win32(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        ("WinRT Windows namespace", new(@"(?<![\w.])Windows\.[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?![\w.])", Options)),
        ("Avalonia dependency", new(@"(?<![\w.])Avalonia(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        ("AppKit namespace", new(@"(?<![\w.])AppKit(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        ("Foundation namespace", new(@"(?<![\w.])Foundation(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        ("FreeX.App.Host dependency", new(@"(?<![\w.])FreeX\.App\.Host(?![\w.])", Options)),
        ("FreeX.App.UI dependency", new(@"(?<![\w.])FreeX\.App\.UI(?![\w.])", Options)),
        ("FreeX.App.Avalonia dependency", new(@"(?<![\w.])FreeX\.App\.Avalonia(?![\w.])", Options)),
        ("UseWPF marker", new(@"(?<![\w])UseWPF(?![\w])", Options | RegexOptions.IgnoreCase)),
        ("UseWindowsForms marker", new(@"(?<![\w])UseWindowsForms(?![\w])", Options | RegexOptions.IgnoreCase)),
        ("Windows-targeted framework", new(@"(?<![\w.-])net\d+(?:\.\d+)?-windows(?:\d+(?:\.\d+)*)?(?![\w.-])", Options | RegexOptions.IgnoreCase)),
        ("WPF assembly reference", new(@"(?<![\w.])(?:PresentationCore|PresentationFramework|System\.Xaml|WindowsBase)(?![\w.])", Options)),
        ("ReachFramework assembly reference", new(@"(?<![\w.])ReachFramework(?![\w.])", Options)),
        ("System.Drawing dependency", new(@"(?<![\w.])System\.Drawing(?:\.[A-Za-z_]\w*)?(?![\w.])", Options))
    ];

    [Fact]
    public void PresentationLayer_StaysPortable()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");

        var violations = Directory.EnumerateFiles(presentationRoot, "*", SearchOption.AllDirectories)
            .Where(IsPortableSourceFile)
            .SelectMany(FindViolations)
            .OrderBy(v => v.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.LineNumber)
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

    private static bool IsPortableSourceFile(string path)
    {
        if (!SourceExtensions.Contains(Path.GetExtension(path)))
            return false;

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            && !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<Violation> FindViolations(string path)
    {
        var relativePath = Path.GetFileName(path);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            foreach (var (description, pattern) in ForbiddenPatterns)
            {
                if (pattern.IsMatch(line))
                    yield return new Violation(relativePath, lineNumber, description, line.Trim());
            }
        }
    }

    private readonly record struct Violation(string RelativePath, int LineNumber, string Description, string Source)
    {
        public override string ToString() => $"{RelativePath}:{LineNumber}: {Description}: {Source}";
    }
}
