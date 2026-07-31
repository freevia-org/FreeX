namespace FreeP.App.Compositor;

/// <summary>Shared command identity for activating a selected embedded OLE object.</summary>
public static class OleActivationPlanner
{
    public const string OpenEmbeddedObjectCommandId = "freep.object.open-embedded";

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
}
