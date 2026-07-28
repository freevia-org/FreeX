using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AppServicesPortabilityGuardTests
{
    private static readonly PortableBoundaryPattern[] ForbiddenPatterns =
    [
        new("System.Windows/WPF namespace", new(@"\bSystem\.Windows\b", RegexOptions.Compiled)),
        new("Microsoft.Win32 dependency", new(@"\bMicrosoft\.Win32\b", RegexOptions.Compiled)),
        new("WinRT Windows namespace", new(@"\bWindows\.", RegexOptions.Compiled)),
        new("FreeX.App.Host dependency", new(@"\bFreeX\.App\.Host\b", RegexOptions.Compiled)),
        new("FreeX.App.UI dependency", new(@"\bFreeX\.App\.UI\b", RegexOptions.Compiled)),
        new("WPF project marker", new(@"\bUseWPF\b|\bUseWpf\b", RegexOptions.Compiled)),
        new("Windows desktop SDK", new(@"\bMicrosoft\.NET\.Sdk\.WindowsDesktop\b", RegexOptions.Compiled)),
        new("Windows-targeted framework", new(@"\bnet\d+(?:\.\d+)?-windows\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Windows Forms project marker", new(@"\bUseWindowsForms\b", RegexOptions.Compiled)),
        new("Avalonia framework dependency", new(@"\bAvalonia\.", RegexOptions.Compiled)),
        new("AppKit namespace", new(@"\bAppKit\b", RegexOptions.Compiled)),
        new("Foundation namespace", new(@"\bFoundation\b", RegexOptions.Compiled)),
        new("ObjCRuntime namespace", new(@"\bObjCRuntime\b", RegexOptions.Compiled)),
        new("NSUrl native type", new(@"\bNSUrl\b", RegexOptions.Compiled)),
        new("NSData native type", new(@"\bNSData\b", RegexOptions.Compiled)),
        new("NSError native type", new(@"\bNSError\b", RegexOptions.Compiled))
    ];

    [Fact]
    public void AppServicesSources_DoNotReferenceWindowsOnlyDesktopDependencies()
    {
        var projectPath = RepositoryFileLocator.Find("src", "FreeX.App.Services", "FreeX.App.Services.csproj");
        var servicesRoot = Path.GetDirectoryName(projectPath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(servicesRoot, "..", ".."));

        var violations = PortableBoundaryGuard.FindSourceViolations(
                servicesRoot,
                repositoryRoot,
                ForbiddenPatterns,
                shouldScanLine: PortableBoundaryGuard.IsNonCommentLine)
            .Select(violation => violation.ToString())
            .ToArray();

        violations.Should().BeEmpty(
            "FreeX.App.Services must stay portable for the Avalonia/macOS port; keep WPF and Windows-only dependencies in host/UI projects");
    }
}

public sealed class SharedPortableProjectPortabilityGuardTests
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly PortableBoundaryPattern[] ForbiddenSourcePatterns =
    [
        new("System.Windows namespace", new(@"(?<![\w.])(?:global::)?System\.Windows(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("System.Printing namespace", new(@"(?<![\w.])(?:global::)?System\.Printing(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("Microsoft.Win32 namespace", new(@"(?<![\w.])(?:global::)?Microsoft\.Win32(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("WinRT Windows namespace", new(@"(?<![\w.])(?:global::)?Windows\.[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?![\w.])", Options)),
        new("Avalonia dependency", new(@"(?<![\w.])(?:global::)?Avalonia(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("AppKit namespace", new(@"(?<![\w.])(?:global::)?AppKit(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("Foundation namespace", new(@"(?<![\w.])(?:global::)?Foundation(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("ObjCRuntime namespace", new(@"(?<![\w.])(?:global::)?ObjCRuntime(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("FreeX.App.Host dependency", new(@"(?<![\w.])(?:global::)?FreeX\.App\.Host(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("FreeX.App.UI dependency", new(@"(?<![\w.])(?:global::)?FreeX\.App\.UI(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("FreeX.App.Avalonia dependency", new(@"(?<![\w.])(?:global::)?FreeX\.App\.Avalonia(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("System.Drawing dependency", new(@"(?<![\w.])(?:global::)?System\.Drawing(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("WinForms dependency", new(@"(?<![\w.])(?:global::)?System\.Windows\.Forms(?:\.[A-Za-z_]\w*)?(?![\w.])", Options))
    ];

    private static readonly PortableBoundaryPattern[] ForbiddenProjectDependencyPatterns =
    [
        new("WindowsDesktop SDK", new(@"(?<![\w.])Microsoft\.NET\.Sdk\.WindowsDesktop(?![\w.])", Options)),
        new("Windows-targeted framework", new(@"(?<![\w.-])net\d+(?:\.\d+)?-windows(?:\d+(?:\.\d+)*)?(?![\w.-])", Options | RegexOptions.IgnoreCase)),
        new("UseWPF marker", new(@"(?<![\w])UseWPF(?![\w])", Options | RegexOptions.IgnoreCase)),
        new("UseWindowsForms marker", new(@"(?<![\w])UseWindowsForms(?![\w])", Options | RegexOptions.IgnoreCase)),
        new("WPF assembly reference", new(@"(?<![\w.])(?:PresentationCore|PresentationFramework|System\.Xaml|WindowsBase|WindowsFormsIntegration)(?![\w.])", Options)),
        new("WindowsDesktop framework reference", new(@"(?<![\w.])Microsoft\.WindowsDesktop\.App(?:\.(?:WPF|WindowsForms))?(?![\w.])", Options)),
        new("WinForms dependency marker", new(@"(?<![\w.])(?:System\.Windows\.Forms|WinForms|WindowsForms)(?![\w.])", Options | RegexOptions.IgnoreCase)),
        new("Avalonia package or project reference", new(@"(?<![\w.])Avalonia(?:\.[A-Za-z0-9_.-]+)?(?![\w.])", Options)),
        new("FreeX.App.Host dependency", new(@"(?<![\w.])FreeX\.App\.Host(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("FreeX.App.UI dependency", new(@"(?<![\w.])FreeX\.App\.UI(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("FreeX.App.Avalonia dependency", new(@"(?<![\w.])FreeX\.App\.Avalonia(?:\.[A-Za-z_]\w*)?(?![\w.])", Options)),
        new("Platform-specific shared project reference", new(@"(?<![\w.])Free\.Shared\.[^\\/;\s""]+\.(?:Wpf|Avalonia|Windows)(?:[\\/]|\.csproj|$)", Options | RegexOptions.IgnoreCase))
    ];

    [Fact]
    public void PortableSharedProjects_StayFreeOfUiAndPlatformDependencies()
    {
        var repositoryRoot = Path.GetDirectoryName(TestWorkspaceFileLocator.FindFromWorkspaceRoot("FreeX.slnx"))
            ?? throw new DirectoryNotFoundException("Could not locate workspace root.");
        var sharedRoot = Path.Combine(repositoryRoot, "shared");

        var portableProjectRoots = Directory.EnumerateDirectories(sharedRoot, "Free.Shared.*")
            .Where(IsPortableSharedProjectDirectory)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        portableProjectRoots.Should().NotBeEmpty(
            "portable shared projects define the dedup boundary under shared/Free.Shared.*");

        var sourceViolations = portableProjectRoots.SelectMany(projectRoot =>
            PortableBoundaryGuard.FindSourceViolations(
                projectRoot,
                repositoryRoot,
                ForbiddenSourcePatterns,
                isSourceFile: IsPortableSharedSourceFile,
                shouldScanLine: PortableBoundaryGuard.IsNonCommentLine));

        var projectViolations = portableProjectRoots.SelectMany(projectRoot =>
            Directory.EnumerateFiles(projectRoot, "*.csproj")
                .SelectMany(projectPath => PortableBoundaryGuard.FindProjectDependencyViolations(
                    projectPath,
                    repositoryRoot,
                    ForbiddenProjectDependencyPatterns)));

        var violations = sourceViolations
            .Concat(projectViolations)
            .OrderBy(violation => violation.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(violation => violation.LineNumber)
            .ThenBy(violation => violation.Description, StringComparer.Ordinal)
            .Select(violation => violation.ToString())
            .ToArray();

        violations.Should().BeEmpty(
            "portable shared projects should stay renderer-agnostic; WPF, Avalonia, Windows, Host, and UI dependencies belong in .Wpf, .Avalonia, or .Windows shared projects");
    }

    private static bool IsPortableSharedProjectDirectory(string path)
    {
        var name = Path.GetFileName(path);
        return !name.EndsWith(".Wpf", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".Avalonia", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".Windows", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPortableSharedSourceFile(string path) =>
        PortableBoundaryGuard.IsPortableSourceFile(path)
        && !Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase);
}
