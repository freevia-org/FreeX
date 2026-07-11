using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static (CellAddress Address, string Text, string? Author, bool IsShown)? TryLoadComment(CommentDto? commentDto, SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(commentDto?.Address) || commentDto.Text is null)
            return null;

        try
        {
            var address = CellAddress.Parse(commentDto.Address, sheetId);
            return address.Sheet == sheetId
                ? (address, commentDto.Text, commentDto.Author, commentDto.IsShown)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static (CellAddress Address, ThreadedComment Comment)? TryLoadThreadedComment(
        ThreadedCommentDto? commentDto,
        SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(commentDto?.Address) || commentDto.Text is null)
            return null;

        try
        {
            var address = CellAddress.Parse(commentDto.Address, sheetId);
            if (address.Sheet != sheetId)
                return null;

            var replies = (commentDto.Replies ?? [])
                .OfType<CommentReplyDto>()
                .Where(reply => reply.Text is not null)
                .Select(reply => new CommentReply(
                    reply.Text!,
                    string.IsNullOrWhiteSpace(reply.Author) ? "FreeX" : reply.Author.Trim())
                {
                    CreatedAtUtc = ToUtc(reply.CreatedAtUtc),
                    ModifiedAtUtc = ToUtc(reply.ModifiedAtUtc),
                    Id = reply.Id,
                    MentionsXml = reply.MentionsXml,
                    SourcePersonId = reply.SourcePersonId,
                    MentionedPersonDisplayNames = reply.MentionedPersonDisplayNames
                })
                .ToList();
            var comment = new ThreadedComment(
                commentDto.Text,
                string.IsNullOrWhiteSpace(commentDto.Author) ? "FreeX" : commentDto.Author.Trim())
            {
                Replies = replies,
                IsResolved = commentDto.IsResolved,
                CreatedAtUtc = ToUtc(commentDto.CreatedAtUtc),
                ModifiedAtUtc = ToUtc(commentDto.ModifiedAtUtc),
                Id = commentDto.Id,
                MentionsXml = commentDto.MentionsXml,
                SourcePersonId = commentDto.SourcePersonId,
                MentionedPersonDisplayNames = commentDto.MentionedPersonDisplayNames
            };
            return (address, comment);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static (CellAddress Address, string Target, HyperlinkMetadata Metadata)? TryLoadHyperlink(HyperlinkDto? hyperlinkDto, SheetId sheetId)
    {
        if (string.IsNullOrWhiteSpace(hyperlinkDto?.Address) || hyperlinkDto.Target is null)
            return null;

        try
        {
            var address = CellAddress.Parse(hyperlinkDto.Address, sheetId);
            return address.Sheet == sheetId
                ? (address, hyperlinkDto.Target, ToHyperlinkMetadata(hyperlinkDto))
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static CommentDto ToCommentDto(Sheet sheet, KeyValuePair<CellAddress, string> pair)
    {
        sheet.CommentAuthors.TryGetValue(pair.Key, out var author);
        return new CommentDto
        {
            Address = pair.Key.ToA1(),
            Text = pair.Value,
            Author = string.IsNullOrEmpty(author) ? null : author,
            IsShown = sheet.ShownComments.Contains(pair.Key)
        };
    }

    private static ThreadedCommentDto ToThreadedCommentDto(KeyValuePair<CellAddress, ThreadedComment> pair) => new()
    {
        Address = pair.Key.ToA1(),
        Text = pair.Value.Text,
        Author = pair.Value.Author,
        IsResolved = pair.Value.IsResolved,
        CreatedAtUtc = ToUtc(pair.Value.CreatedAtUtc),
        ModifiedAtUtc = ToUtc(pair.Value.ModifiedAtUtc),
        Id = pair.Value.Id,
        MentionsXml = pair.Value.MentionsXml,
        SourcePersonId = pair.Value.SourcePersonId,
        MentionedPersonDisplayNames = ToMentionedPersonDisplayNamesDto(pair.Value.MentionedPersonDisplayNames),
        Replies = pair.Value.Replies
            .OfType<CommentReply>()
            .Select(reply => new CommentReplyDto
            {
                Text = reply.Text,
                Author = reply.Author,
                CreatedAtUtc = ToUtc(reply.CreatedAtUtc),
                ModifiedAtUtc = ToUtc(reply.ModifiedAtUtc),
                Id = reply.Id,
                MentionsXml = reply.MentionsXml,
                SourcePersonId = reply.SourcePersonId,
                MentionedPersonDisplayNames = ToMentionedPersonDisplayNamesDto(reply.MentionedPersonDisplayNames)
            })
            .ToList()
    };

    private static Dictionary<string, string>? ToMentionedPersonDisplayNamesDto(
        IReadOnlyDictionary<string, string>? mentionedPersonDisplayNames) =>
        mentionedPersonDisplayNames is null ? null : new Dictionary<string, string>(mentionedPersonDisplayNames);

    private static HyperlinkDto ToHyperlinkDto(Sheet sheet, KeyValuePair<CellAddress, string> pair)
    {
        sheet.HyperlinkMetadata.TryGetValue(pair.Key, out var metadata);
        metadata ??= new HyperlinkMetadata();
        return new HyperlinkDto
        {
            Address = pair.Key.ToA1(),
            Target = pair.Value,
            LinkType = metadata.LinkType,
            ScreenTip = string.IsNullOrWhiteSpace(metadata.ScreenTip) ? null : metadata.ScreenTip,
            Bookmark = string.IsNullOrWhiteSpace(metadata.Bookmark) ? null : metadata.Bookmark
        };
    }

    private static HyperlinkMetadata ToHyperlinkMetadata(HyperlinkDto dto) =>
        new(
            dto.LinkType is { } linkType && Enum.IsDefined(linkType)
                ? linkType
                : HyperlinkTargetKind.ExistingFileOrWebPage,
            (dto.ScreenTip ?? "").Trim(),
            (dto.Bookmark ?? "").Trim());

    private static DateTimeOffset? ToUtc(DateTimeOffset? timestamp) =>
        timestamp?.ToUniversalTime();
}
