namespace FreeP.Core.Model;

/// <summary>
/// Reversible add/edit/delete/resolve/reopen/reply mutation of a single comment on a slide's
/// <see cref="Slide.Comments"/> list. Reply is folded into a single whole-comment replace (the
/// clone carries the full updated <see cref="SlideComment.Replies"/> list), so undoing a reply
/// restores the whole thread as one unit rather than popping the reply off separately.
///
/// <list type="bullet">
/// <item><c>before is null, after is not null</c> — Add: apply inserts at <c>_index</c>, revert removes it.</item>
/// <item><c>before is not null, after is null</c> — Delete: apply removes at <c>_index</c>, revert re-inserts it.</item>
/// <item><c>before and after both set</c> — Edit/Resolve/Reopen/Reply: apply/revert replace the whole entry at <c>_index</c>.</item>
/// </list>
/// </summary>
public sealed class CommentMutationCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly int _index;
    private readonly SlideComment? _before;
    private readonly SlideComment? _after;
    private readonly string _label;

    public CommentMutationCommand(
        string label,
        int slideIndex,
        int index,
        SlideComment? before,
        SlideComment? after)
    {
        if (before is null && after is null)
        {
            throw new ArgumentException("At least one of before/after must be supplied.", nameof(after));
        }

        _label = label;
        _slideIndex = slideIndex;
        _index = index;
        _before = before is null ? null : SlideCloner.CloneComment(before);
        _after = after is null ? null : SlideCloner.CloneComment(after);
    }

    public string Label => _label;

    public void Apply(Presentation presentation)
    {
        var comments = GetComments(presentation);
        if (comments is null)
        {
            return;
        }

        if (_before is null)
        {
            // Add: insert the new comment at its recorded position.
            var insertAt = Math.Clamp(_index, 0, comments.Count);
            comments.Insert(insertAt, SlideCloner.CloneComment(_after!));
        }
        else if (_after is null)
        {
            // Delete: remove the comment that occupied _index.
            if (_index >= 0 && _index < comments.Count)
            {
                comments.RemoveAt(_index);
            }
        }
        else
        {
            // Edit / Resolve / Reopen / Reply: replace the whole entry.
            if (_index >= 0 && _index < comments.Count)
            {
                comments[_index] = SlideCloner.CloneComment(_after);
            }
        }
    }

    public void Revert(Presentation presentation)
    {
        var comments = GetComments(presentation);
        if (comments is null)
        {
            return;
        }

        if (_before is null)
        {
            // Undo Add: remove the comment we inserted.
            if (_index >= 0 && _index < comments.Count)
            {
                comments.RemoveAt(_index);
            }
        }
        else if (_after is null)
        {
            // Undo Delete: re-insert the removed comment at its original position.
            var insertAt = Math.Clamp(_index, 0, comments.Count);
            comments.Insert(insertAt, SlideCloner.CloneComment(_before));
        }
        else
        {
            // Undo Edit / Resolve / Reopen / Reply: restore the prior whole entry.
            if (_index >= 0 && _index < comments.Count)
            {
                comments[_index] = SlideCloner.CloneComment(_before);
            }
        }
    }

    private List<SlideComment>? GetComments(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
        {
            return null;
        }

        return presentation.Slides[_slideIndex].Comments;
    }
}
