namespace FreeW.Core.Model;

/// <summary>
/// Resolves every comment id in a document to its top-level comment thread.
/// </summary>
public static class CommentThreadIndex
{
    /// <summary>
    /// Builds a lookup from direct comment dictionary keys and threaded reply ids to their top-level
    /// comments. Direct dictionary keys take precedence; otherwise the first thread that contains a
    /// malformed duplicate id wins.
    /// </summary>
    public static IReadOnlyDictionary<int, Comment> BuildTopLevelByCommentId(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var topLevelByCommentId = new Dictionary<int, Comment>(document.Comments.Count);

        // Dictionary keys are the direct, persisted comment references and take priority over a reply
        // that reuses the same id in malformed input.
        foreach (var (rootId, root) in document.Comments)
            topLevelByCommentId[rootId] = root;

        // Thread ids are normally globally unique. TryAdd preserves the earliest root when malformed
        // input reuses a reply id across threads.
        foreach (var root in document.Comments.Values)
        {
            foreach (var comment in root.ThreadInOrder())
                topLevelByCommentId.TryAdd(comment.Id, root);
        }

        return topLevelByCommentId;
    }
}
