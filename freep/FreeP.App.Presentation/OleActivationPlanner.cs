using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Portable activation input resolved from an embedded package object.</summary>
public sealed record OleActivationPlan(byte[] Payload, string FileName)
{
    public string Extension => Path.GetExtension(FileName).TrimStart('.');
}

/// <summary>Shared command identity for activating a selected embedded OLE object.</summary>
public static class OleActivationPlanner
{
    public const string OpenEmbeddedObjectCommandId = "freep.object.open-embedded";

    public static OleActivationPlan? TryBuild(OleObjectInfo? oleObject)
    {
        if (oleObject is null || oleObject.EmbeddedBytes.Length == 0)
            return null;

        var extension = OleActivationService.ResolveExtension(oleObject);
        return new OleActivationPlan(
            oleObject.EmbeddedBytes.ToArray(),
            SafeFileName(oleObject.FileName, extension));
    }

    public static OleActivationPlan? TryBuild(InlineOleObjectInfo? inlineObject)
    {
        if (inlineObject is null || inlineObject.EmbeddedBytes.Length == 0)
            return null;

        var extension = OleActivationService.ResolveExtension(inlineObject);
        return new OleActivationPlan(
            inlineObject.EmbeddedBytes.ToArray(),
            SafeFileName(inlineObject.FileName, extension));
    }

    /// <summary>
    /// Resolves the command against the active text editor before falling back to a selected
    /// slide-level OLE shape. PowerPoint uses the same open command for both object contexts.
    /// </summary>
    public static bool TryOpenInlineFirst(
        Func<bool>? tryOpenInlineObject,
        Func<bool> tryOpenSlideObject)
    {
        ArgumentNullException.ThrowIfNull(tryOpenSlideObject);
        return tryOpenInlineObject?.Invoke() == true || tryOpenSlideObject();
    }

    private static string SafeFileName(string? fileName, string extension)
    {
        var candidate = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate is "." or ".."
            || candidate.IndexOfAny(['/', '\\']) >= 0
            || candidate.Any(char.IsControl))
            candidate = $"Embedded.{extension}";

        var candidateExtension = Path.GetExtension(candidate).TrimStart('.');
        if (!string.Equals(candidateExtension, extension, StringComparison.OrdinalIgnoreCase))
            candidate = $"{Path.GetFileNameWithoutExtension(candidate)}.{extension}";

        return candidate;
    }
}
