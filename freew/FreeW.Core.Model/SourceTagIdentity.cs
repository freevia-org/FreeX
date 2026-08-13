namespace FreeW.Core.Model;

/// <summary>
/// Defines the stable identity rules for bibliography source tags across document and application workflows.
/// </summary>
public static class SourceTagIdentity
{
    public static readonly StringComparer Comparer = StringComparer.Ordinal;

    public static string Canonicalize(string? tag) =>
        (tag ?? string.Empty).Trim();

    public static bool HasIdentity(string? tag) =>
        Canonicalize(tag).Length > 0;

    public static bool Equals(string? left, string? right) =>
        HasIdentity(left)
        && HasIdentity(right)
        && Comparer.Equals(Canonicalize(left), Canonicalize(right));
}
