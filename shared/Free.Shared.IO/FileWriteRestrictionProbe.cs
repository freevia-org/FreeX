namespace Free.Shared.IO;

/// <summary>
/// Best-effort check of whether a file on disk can currently be written back to, shared by every
/// sister app's open path (FreeX's <c>WorkbookReadOnlySession</c>, FreeW's
/// <c>DocumentPersistenceWorkflow</c>, FreeP's <c>PresentationFilePersistenceWorkflow</c>) so all
/// three classify a read-only source identically instead of each drifting its own copy.
/// </summary>
public static class FileWriteRestrictionProbe
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="filePath"/> exists but cannot be written back to.
    /// Checks the OS read-only attribute first (the common case: Explorer's Read-only checkbox, or
    /// <c>attrib +r</c>), then falls back to a lightweight open-for-write probe so a read-only
    /// network share, a read-only-mounted volume, or a denied ACL are caught too -- none of those
    /// necessarily set the DOS read-only attribute. A transient sharing violation (e.g. another
    /// process briefly holding an exclusive handle) is deliberately NOT treated as read-only: it
    /// says nothing about the file's durable write permission, and misclassifying it would force
    /// an otherwise-editable file through Save As.
    /// </summary>
    /// <remarks>
    /// Callers must run this BEFORE they open their own read handle on the file: the probe below
    /// requests <see cref="FileAccess.ReadWrite"/>, so a caller-held handle opened with the default
    /// <see cref="FileShare.Read"/> turns the probe into a self-inflicted sharing violation, which
    /// this method reports as "not restricted" -- silently defeating the check.
    /// </remarks>
    public static bool IsWriteRestricted(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            if (!File.Exists(filePath))
                return false;

            if (File.GetAttributes(filePath).HasFlag(FileAttributes.ReadOnly))
                return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            // Locked by another process, a network hiccup, etc. -- not necessarily a write
            // restriction, so don't force the file read-only on a transient failure.
            return false;
        }
    }
}
