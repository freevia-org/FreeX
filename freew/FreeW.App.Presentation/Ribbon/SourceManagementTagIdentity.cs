namespace FreeW.App.Presentation.Ribbon;

internal static class SourceManagementTagIdentity
{
    public static readonly StringComparer Comparer = StringComparer.Ordinal;

    public static string Canonicalize(string? tag) =>
        (tag ?? string.Empty).Trim();

    public static bool Equals(string? left, string? right) =>
        Comparer.Equals(Canonicalize(left), Canonicalize(right));
}
