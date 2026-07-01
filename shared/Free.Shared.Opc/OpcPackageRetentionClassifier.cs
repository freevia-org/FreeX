namespace Free.Shared.Opc;

/// <summary>
/// Classifies package parts and relationships that a writer regenerates, so
/// preserve-bag logic can skip stale source entries without duplicating OPC path
/// resolution rules at each caller.
/// </summary>
public sealed class OpcPackageRetentionClassifier
{
    private readonly HashSet<string> _regeneratedPartPaths;
    private readonly string[] _regeneratedPartPathPrefixes;
    private readonly HashSet<string> _regeneratedRelationshipTypes;

    public OpcPackageRetentionClassifier(
        IEnumerable<string> regeneratedPartPaths,
        IEnumerable<string> regeneratedPartPathPrefixes,
        IEnumerable<string> regeneratedRelationshipTypes)
    {
        ArgumentNullException.ThrowIfNull(regeneratedPartPaths);
        ArgumentNullException.ThrowIfNull(regeneratedPartPathPrefixes);
        ArgumentNullException.ThrowIfNull(regeneratedRelationshipTypes);

        _regeneratedPartPaths = regeneratedPartPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePartPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _regeneratedPartPathPrefixes = regeneratedPartPathPrefixes
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(NormalizePartPrefix)
            .Where(prefix => prefix.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _regeneratedRelationshipTypes = regeneratedRelationshipTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsRegeneratedPart(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = NormalizePartPath(path);
        return _regeneratedPartPaths.Contains(normalized) ||
               _regeneratedPartPathPrefixes.Any(prefix =>
                   normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsRegeneratedRelationship(string? sourcePartPath, OpcRelationship relationship) =>
        IsRegeneratedRelationship(
            sourcePartPath,
            relationship.Type,
            relationship.Target,
            relationship.IsExternal);

    public bool IsRegeneratedRelationship(
        string? sourcePartPath,
        string? relationshipType,
        string? target,
        bool external)
    {
        if (!string.IsNullOrWhiteSpace(relationshipType) &&
            _regeneratedRelationshipTypes.Contains(relationshipType))
        {
            return true;
        }

        if (external || string.IsNullOrWhiteSpace(target))
            return false;

        var sourceDir = string.IsNullOrWhiteSpace(sourcePartPath)
            ? string.Empty
            : OpcPathHelper.GetDirectoryName(sourcePartPath);
        var targetPath = OpcPathHelper.ResolveRelativeZipPath(sourceDir, target);
        return IsRegeneratedPart(targetPath);
    }

    private static string NormalizePartPath(string path) =>
        OpcPathHelper.ToZipEntryPath(path);

    private static string NormalizePartPrefix(string prefix)
    {
        var normalized = NormalizePartPath(prefix);
        return normalized.EndsWith('/')
            ? normalized
            : normalized + "/";
    }
}
