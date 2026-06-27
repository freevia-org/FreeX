using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class PortableBoundaryGuard
{
    private static readonly HashSet<string> DefaultSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".props",
        ".targets"
    };

    public static IReadOnlyList<PortableBoundaryViolation> FindSourceViolations(
        string sourceRoot,
        string relativePathRoot,
        IEnumerable<PortableBoundaryPattern> forbiddenPatterns,
        Func<string, bool>? isSourceFile = null,
        Func<string, PortableBoundaryPattern, bool>? isAllowed = null,
        Func<string, bool>? shouldScanLine = null)
    {
        ArgumentNullException.ThrowIfNull(sourceRoot);
        ArgumentNullException.ThrowIfNull(relativePathRoot);
        ArgumentNullException.ThrowIfNull(forbiddenPatterns);

        var sourceFilePredicate = isSourceFile ?? IsPortableSourceFile;

        return Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(sourceFilePredicate)
            .SelectMany(path => FindSourceViolationsInFile(
                path,
                relativePathRoot,
                forbiddenPatterns,
                isAllowed,
                shouldScanLine))
            .OrderBy(violation => violation.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(violation => violation.LineNumber)
            .ThenBy(violation => violation.Description, StringComparer.Ordinal)
            .ToArray();
    }

    public static IEnumerable<PortableBoundaryViolation> FindProjectDependencyViolations(
        string projectPath,
        string relativePathRoot,
        IEnumerable<PortableBoundaryPattern> forbiddenPatterns)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(relativePathRoot);
        ArgumentNullException.ThrowIfNull(forbiddenPatterns);

        var project = XDocument.Load(projectPath);
        var relativePath = Path.GetRelativePath(relativePathRoot, projectPath);
        foreach (var marker in ProjectDependencyMarkers(project))
        {
            foreach (var pattern in forbiddenPatterns)
            {
                if (pattern.IsMatch(marker))
                    yield return new PortableBoundaryViolation(relativePath, 0, pattern.Description, marker);
            }
        }
    }

    public static bool IsPortableSourceFile(string path)
    {
        if (!DefaultSourceExtensions.Contains(Path.GetExtension(path)))
            return false;

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            && !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsNonCommentLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0
            && !trimmed.StartsWith("//", StringComparison.Ordinal)
            && !trimmed.StartsWith("/*", StringComparison.Ordinal)
            && !trimmed.StartsWith("*", StringComparison.Ordinal)
            && !trimmed.StartsWith("<!--", StringComparison.Ordinal);
    }

    public static IEnumerable<string> ProjectDependencyMarkers(XDocument project)
    {
        if (project.Root?.Attribute("Sdk")?.Value is { Length: > 0 } sdk)
            yield return $"Project Sdk={sdk}";

        foreach (var itemName in new[] { "ProjectReference", "PackageReference", "FrameworkReference", "Reference" })
        {
            foreach (var include in ProjectItemIncludes(project, itemName))
                yield return $"{itemName} Include={include}";
        }

        foreach (var propertyName in new[] { "TargetFramework", "TargetFrameworks", "UseWPF", "UseWindowsForms" })
        {
            foreach (var value in ProjectPropertyValues(project, propertyName))
                yield return $"{propertyName}={value}";
        }
    }

    public static IEnumerable<string> ProjectItemIncludes(XDocument project, string itemName) =>
        project
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!);

    public static IEnumerable<string> ProjectPropertyValues(XDocument project, string propertyName) =>
        ProjectPropertyElements(project, propertyName)
            .Select(element => element.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value));

    public static IEnumerable<XElement> ProjectPropertyElements(XDocument project, string propertyName) =>
        project
            .Descendants()
            .Where(element => element.Name.LocalName == propertyName);

    public static IEnumerable<XElement> ProjectItemElements(XDocument project, string itemName) =>
        project
            .Descendants()
            .Where(element => element.Name.LocalName == itemName);

    public static string? ProjectCondition(XElement element) =>
        element.Attribute("Condition")?.Value ?? element.Parent?.Attribute("Condition")?.Value;

    public static string ProjectReferenceName(string include)
    {
        var fileName = include.Split('\\', '/').Last();
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static IEnumerable<PortableBoundaryViolation> FindSourceViolationsInFile(
        string path,
        string relativePathRoot,
        IEnumerable<PortableBoundaryPattern> forbiddenPatterns,
        Func<string, PortableBoundaryPattern, bool>? isAllowed,
        Func<string, bool>? shouldScanLine)
    {
        var relativePath = Path.GetRelativePath(relativePathRoot, path);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (shouldScanLine is not null && !shouldScanLine(line))
                continue;

            foreach (var pattern in forbiddenPatterns)
            {
                if (!pattern.IsMatch(line) || isAllowed?.Invoke(relativePath, pattern) == true)
                    continue;

                yield return new PortableBoundaryViolation(
                    relativePath,
                    lineNumber,
                    pattern.Description,
                    line.Trim());
            }
        }
    }
}

internal readonly record struct PortableBoundaryPattern(string Description, Regex Pattern)
{
    public bool IsMatch(string sourceLine) => Pattern.IsMatch(sourceLine);
}

internal readonly record struct PortableBoundaryViolation(
    string RelativePath,
    int LineNumber,
    string Description,
    string SourceLine)
{
    public override string ToString() =>
        LineNumber > 0
            ? $"{RelativePath}:{LineNumber}: {Description}: {SourceLine}"
            : $"{RelativePath}: {Description}: {SourceLine}";
}
