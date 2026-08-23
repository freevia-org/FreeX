namespace FreeX.Core.Model;

public enum ThreadedCommentIdPolicy
{
    Preserve,
    Reset,
}

/// <summary>Clones threaded comments while making destination identity handling explicit.</summary>
public static class ThreadedCommentCloner
{
    public static ThreadedComment Clone(ThreadedComment source, ThreadedCommentIdPolicy idPolicy)
    {
        ArgumentNullException.ThrowIfNull(source);

        return idPolicy switch
        {
            ThreadedCommentIdPolicy.Preserve => source with
            {
                Replies = source.Replies.Select(reply => reply with { }).ToList(),
            },
            ThreadedCommentIdPolicy.Reset => source with
            {
                Id = null,
                Replies = source.Replies.Select(reply => reply with { Id = null }).ToList(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(idPolicy), idPolicy, null),
        };
    }
}
