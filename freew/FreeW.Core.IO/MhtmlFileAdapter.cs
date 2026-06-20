using System.Text;
using FreeW.Core.Model;
using MimeKit;

namespace FreeW.Core.IO;

public sealed class MhtmlFileAdapter : IDocumentFileAdapter
{
    public string Extension => ".mhtml";
    public string FormatName => "MHTML document";

    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".mhtml", "MHTML document"),
        new(".mht", "MHTML document"),
    ];

    public TextDocument Load(Stream stream)
    {
        var message = MimeMessage.Load(stream);
        var images = new Dictionary<string, InlineImage>(StringComparer.OrdinalIgnoreCase);
        string? html = null;

        foreach (var entity in message.BodyParts)
        {
            if (entity is TextPart textPart &&
                textPart.ContentType.MediaType.Equals("text", StringComparison.OrdinalIgnoreCase) &&
                textPart.ContentType.MediaSubtype.Equals("html", StringComparison.OrdinalIgnoreCase))
            {
                html ??= textPart.Text;
                continue;
            }

            if (entity is MimePart part &&
                part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase) &&
                part.Content is not null)
            {
                using var buffer = new MemoryStream();
                part.Content.DecodeTo(buffer);
                var bytes = buffer.ToArray();
                var format = InlineImage.FormatForExtension(part.FileName) ?? InlineImage.DetectFormat(bytes);
                foreach (var key in GetImageLookupKeys(part))
                    images[key] = new InlineImage(bytes, 72, 72, format);
            }
        }

        html ??= message.HtmlBody;
        if (html is null && message.Body is TextPart bodyText)
            html = bodyText.Text;
        html ??= string.Empty;

        return HtmlFileAdapter.LoadHtml(html, cid =>
        {
            cid = cid.Trim('<', '>');
            return images.TryGetValue(cid, out var image) ? image : null;
        });
    }

    private static IEnumerable<string> GetImageLookupKeys(MimePart part)
    {
        if (!string.IsNullOrWhiteSpace(part.ContentId))
            yield return part.ContentId.Trim('<', '>');

        foreach (var rawKey in new[]
                 {
                     part.ContentLocation?.ToString(),
                     part.Headers["Content-Location"],
                     part.FileName
                 })
        {
            if (string.IsNullOrWhiteSpace(rawKey))
                continue;

            var key = rawKey.Trim('<', '>');
            yield return key;

            var fileName = Path.GetFileName(key.Replace('\\', '/'));
            if (!string.IsNullOrWhiteSpace(fileName) &&
                !string.Equals(fileName, key, StringComparison.OrdinalIgnoreCase))
            {
                yield return fileName;
            }
        }
    }

    public void Save(TextDocument document, Stream stream)
    {
        var result = HtmlFileAdapter.WriteHtml(document, HtmlImageMode.Cid);
        var message = new MimeMessage();
        message.Subject = "FreeW HTML document";
        message.Body = BuildBody(result);
        message.WriteTo(stream);
    }

    private static MimeEntity BuildBody(HtmlWriteResult result)
    {
        var related = new MultipartRelated();
        related.Add(new TextPart("html")
        {
            Text = result.Html,
            ContentTransferEncoding = ContentEncoding.QuotedPrintable,
        });

        foreach (var image in result.Images)
        {
            var (type, subtype) = SplitMimeType(image.MimeType);
            var part = new MimePart(type, subtype)
            {
                ContentId = image.ContentId,
                Content = new MimeContent(new MemoryStream(image.Bytes), ContentEncoding.Default),
                ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = $"{image.ContentId.Split('@')[0]}.{image.Extension}",
            };
            related.Add(part);
        }

        return related;
    }

    private static (string Type, string Subtype) SplitMimeType(string mimeType)
    {
        var slash = mimeType.IndexOf('/');
        return slash > 0 ? (mimeType[..slash], mimeType[(slash + 1)..]) : ("application", "octet-stream");
    }
}
