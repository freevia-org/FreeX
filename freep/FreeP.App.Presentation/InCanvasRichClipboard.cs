using System.Text.Json;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Renderer-neutral rich clipboard payload used by both desktop editors. The model fragment is
/// intentionally narrower than a full shape: it carries only the selected text and its run and
/// paragraph semantics, which makes it safe to paste into another in-canvas editor.
/// </summary>
public sealed record InCanvasRichClipboardPayload(
    TextBody Body,
    string PlainText,
    Run? TypingRun = null)
{
    public static InCanvasRichClipboardPayload FromPlainText(
        string? text,
        InCanvasEditorTextStyleState? typingStyle = null)
    {
        var body = new TextBody();
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        foreach (var line in normalized.Split('\n'))
        {
            body.Paragraphs.Add(new Paragraph
            {
                Runs = { new Run { Text = line } },
            });
        }

        if (body.Paragraphs.Count == 0)
            body.Paragraphs.Add(new Paragraph { Runs = { new Run() } });

        return new InCanvasRichClipboardPayload(
            body,
            InCanvasTextEditPlanner.ExtractPlainText(body),
            RunFromStyle(typingStyle));
    }

    internal InCanvasRichClipboardPayload DeepClone() => new(
        TextBodyModelCloner.CloneTextBody(Body)!,
        PlainText,
        TypingRun is null ? null : TextBodyModelCloner.CloneRun(TypingRun));

    internal static Run? RunFromStyle(InCanvasEditorTextStyleState? style) => style is null
        ? null
        : new Run
        {
            FontFamily = style.FontFamily,
            FontSizePt = style.FontSizePt,
            Bold = style.Bold == true,
            Italic = style.Italic == true,
            Underline = style.Underline == true,
            Strikethrough = style.Strikethrough == true,
            Color = style.Color,
        };
}

/// <summary>Creates, serializes, and applies in-canvas rich clipboard fragments.</summary>
public static class InCanvasRichClipboardPlanner
{
    public const int CurrentVersion = 1;

    public static InCanvasRichClipboardPayload Capture(
        TextBody source,
        InCanvasEditorTextSelection selection,
        Run? typingRun = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var range = NormalizeSelection(source, selection);
        var body = range is null
            ? EmptyFragment(source)
            : ExtractFragment(source, range.Value.Start, range.Value.End);
        return new InCanvasRichClipboardPayload(
            body,
            InCanvasTextEditPlanner.ExtractPlainText(body),
            typingRun is null ? null : TextBodyModelCloner.CloneRun(typingRun));
    }

    public static TextBody Apply(
        TextBody destination,
        InCanvasEditorTextSelection selection,
        InCanvasRichClipboardPayload payload,
        out int caret)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(payload);
        int textLength = InCanvasTextEditPlanner.ExtractPlainText(destination).Length;
        int start = Math.Clamp(Math.Min(selection.Start, selection.End), 0, textLength);
        int end = Math.Clamp(Math.Max(selection.Start, selection.End), 0, textLength);
        caret = start + payload.PlainText.Length;
        return RichTextBodyMutationPlanner.ReplaceWithFragment(
            destination,
            start,
            end - start,
            payload.Body);
    }

    public static byte[] Serialize(InCanvasRichClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.SerializeToUtf8Bytes(ToDto(payload));
    }

    public static InCanvasRichClipboardPayload? Deserialize(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
            return null;

        try
        {
            var dto = JsonSerializer.Deserialize<ClipboardPayloadDto>(bytes);
            if (dto is null || dto.Version != CurrentVersion || dto.Body is null)
                return null;
            var body = FromDto(dto.Body);
            var plainText = InCanvasTextEditPlanner.ExtractPlainText(body);
            return new InCanvasRichClipboardPayload(
                body,
                plainText,
                FromDto(dto.TypingRun));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static (int Start, int End)? NormalizeSelection(
        TextBody source,
        InCanvasEditorTextSelection selection)
    {
        int length = InCanvasTextEditPlanner.ExtractPlainText(source).Length;
        int start = Math.Clamp(Math.Min(selection.Start, selection.End), 0, length);
        int end = Math.Clamp(Math.Max(selection.Start, selection.End), 0, length);
        return start == end ? null : (start, end);
    }

    private static TextBody EmptyFragment(TextBody source)
    {
        var body = TextBodyModelCloner.CloneTextBody(source)!;
        body.Paragraphs.Clear();
        body.Paragraphs.Add(new Paragraph { Runs = { new Run() } });
        return body;
    }

    private sealed record FragmentToken(
        char? Character,
        Run? Run,
        int ParagraphIndex,
        int? NextParagraphIndex)
    {
        public bool IsParagraphBreak => Character is null;
    }

    private static TextBody ExtractFragment(TextBody source, int start, int end)
    {
        var sourceTokens = Flatten(source);
        int startParagraph = ParagraphAt(sourceTokens, source.Paragraphs.Count, start);
        var result = TextBodyModelCloner.CloneTextBody(source)!;
        result.Paragraphs.Clear();

        var paragraph = TextBodyModelCloner.CloneParagraph(source.Paragraphs[startParagraph]);
        paragraph.Runs.Clear();
        Run? activeRun = null;
        var activeText = new System.Text.StringBuilder();

        void FlushRun()
        {
            if (activeRun is null || activeText.Length == 0)
                return;
            var run = TextBodyModelCloner.CloneRun(activeRun);
            run.Text = activeText.ToString();
            paragraph.Runs.Add(run);
            activeRun = null;
            activeText.Clear();
        }

        void FlushParagraph(int nextParagraphIndex)
        {
            FlushRun();
            EnsureRun(paragraph);
            result.Paragraphs.Add(paragraph);
            paragraph = TextBodyModelCloner.CloneParagraph(
                source.Paragraphs[Math.Clamp(nextParagraphIndex, 0, source.Paragraphs.Count - 1)]);
            paragraph.Runs.Clear();
        }

        for (int index = start; index < end && index < sourceTokens.Count; index++)
        {
            var token = sourceTokens[index];
            if (token.IsParagraphBreak)
            {
                FlushParagraph(token.NextParagraphIndex ?? startParagraph);
                continue;
            }

            if (token.Run is null)
                continue;
            if (!ReferenceEquals(activeRun, token.Run))
            {
                FlushRun();
                activeRun = token.Run;
            }
            activeText.Append(token.Character!.Value);
        }

        FlushRun();
        EnsureRun(paragraph);
        result.Paragraphs.Add(paragraph);
        return result;
    }

    private static List<FragmentToken> Flatten(TextBody body)
    {
        var result = new List<FragmentToken>();
        for (int paragraphIndex = 0; paragraphIndex < body.Paragraphs.Count; paragraphIndex++)
        {
            foreach (var run in body.Paragraphs[paragraphIndex].Runs)
            {
                foreach (var character in run.Text)
                    result.Add(new FragmentToken(character, run, paragraphIndex, null));
            }

            if (paragraphIndex + 1 < body.Paragraphs.Count)
            {
                result.Add(new FragmentToken(
                    null,
                    null,
                    paragraphIndex,
                    paragraphIndex + 1));
            }
        }
        return result;
    }

    private static int ParagraphAt(
        IReadOnlyList<FragmentToken> tokens,
        int paragraphCount,
        int position)
    {
        if (tokens.Count == 0)
            return 0;
        if (position < tokens.Count)
            return Math.Clamp(tokens[position].IsParagraphBreak
                ? tokens[position].NextParagraphIndex ?? 0
                : tokens[position].ParagraphIndex, 0, paragraphCount - 1);
        return Math.Clamp(tokens[^1].ParagraphIndex, 0, paragraphCount - 1);
    }

    private static void EnsureRun(Paragraph paragraph)
    {
        if (paragraph.Runs.Count == 0)
            paragraph.Runs.Add(new Run());
    }

    private static ClipboardPayloadDto ToDto(InCanvasRichClipboardPayload payload) => new()
    {
        Version = CurrentVersion,
        Body = ToDto(payload.Body),
        TypingRun = payload.TypingRun is null ? null : ToDto(payload.TypingRun),
    };

    private static ClipboardBodyDto ToDto(TextBody body) => new()
    {
        DefaultParaAlign = body.DefaultParaAlign,
        InsetLeftPt = body.InsetLeftPt,
        InsetRightPt = body.InsetRightPt,
        InsetTopPt = body.InsetTopPt,
        InsetBottomPt = body.InsetBottomPt,
        Wrap = body.Wrap,
        AutoFitKind = body.AutoFitKind,
        VerticalType = body.VerticalType,
        ColumnCount = body.ColumnCount,
        ColumnSpacingEmu = body.ColumnSpacingEmu,
        Paragraphs = body.Paragraphs.Select(ToDto).ToList(),
    };

    private static ClipboardParagraphDto ToDto(Paragraph paragraph) => new()
    {
        Align = paragraph.Align,
        Level = paragraph.Level,
        BulletKind = paragraph.BulletKind,
        BulletSuppressed = paragraph.BulletSuppressed,
        BulletChar = paragraph.BulletChar,
        AutoNumType = paragraph.AutoNumType,
        AutoNumStartAt = paragraph.AutoNumStartAt,
        AutoNumStartAtSpecified = paragraph.AutoNumStartAtSpecified,
        MarginLeftEmu = paragraph.MarginLeftEmu,
        IndentEmu = paragraph.IndentEmu,
        BulletColor = ToDto(paragraph.BulletColor),
        BulletColorFollowsText = paragraph.BulletColorFollowsText,
        BulletSizePct = paragraph.BulletSizePct,
        BulletSizePt = paragraph.BulletSizePt,
        BulletSizeFollowsText = paragraph.BulletSizeFollowsText,
        BulletFontFamily = paragraph.BulletFontFamily,
        BulletFontFollowsText = paragraph.BulletFontFollowsText,
        SpaceBeforePt = paragraph.SpaceBeforePt,
        SpaceAfterPt = paragraph.SpaceAfterPt,
        TabStops = paragraph.TabStops.Select(stop => new ClipboardTabStopDto
        {
            PositionEmu = stop.PositionEmu,
            Alignment = stop.Alignment,
        }).ToList(),
        BulletImage = paragraph.BulletImage is null ? null : new ClipboardImageDto
        {
            ContentType = paragraph.BulletImage.ContentType,
            Bytes = paragraph.BulletImage.Bytes,
        },
        Runs = paragraph.Runs.Select(ToDto).ToList(),
    };

    private static ClipboardRunDto ToDto(Run run) => new ClipboardRunDto
    {
        Text = run.Text,
        FontFamily = run.FontFamily,
        FontSizePt = run.FontSizePt,
        BaselineOffset = run.BaselineOffset,
        Bold = run.Bold,
        Italic = run.Italic,
        BoldSet = run.BoldSet,
        ItalicSet = run.ItalicSet,
        Underline = run.Underline,
        Strikethrough = run.Strikethrough,
        Caps = run.Caps,
        Color = ToDto(run.Color),
        Hyperlink = run.Hyperlink is null ? null : new ClipboardHyperlinkDto
        {
            Url = run.Hyperlink.Url,
            TargetSlideId = run.Hyperlink.TargetSlideId,
            Tooltip = run.Hyperlink.Tooltip,
        },
        Field = run.Field is null ? null : new ClipboardFieldDto
        {
            FieldType = run.Field.FieldType,
            CachedText = run.Field.CachedText,
            FontFamily = run.Field.FontFamily,
            FontSizePt = run.Field.FontSizePt,
            Bold = run.Field.Bold,
            Italic = run.Field.Italic,
            Color = run.Field.Color,
        },
        Math = run.Math is null ? null : new ClipboardMathDto
        {
            RawXml = run.Math.RawXml,
            IsAlternateContent = run.Math.IsAlternateContent,
        },
    };

    private static ClipboardColorDto? ToDto(ThemeAwareColor? color) => color is null ? null : new()
    {
        R = color.Resolved.R,
        G = color.Resolved.G,
        B = color.Resolved.B,
        Alpha = color.Alpha,
        SchemeColor = color.SchemeColor is null ? null : new ClipboardSchemeColorDto
        {
            RoleName = color.SchemeColor.RoleName,
            Slot = color.SchemeColor.Slot,
            LumMod = color.SchemeColor.LumMod,
            LumOff = color.SchemeColor.LumOff,
            Tint = color.SchemeColor.Tint,
            Shade = color.SchemeColor.Shade,
        },
    };

    private static TextBody FromDto(ClipboardBodyDto dto)
    {
        var body = new TextBody
        {
            DefaultParaAlign = dto.DefaultParaAlign,
            InsetLeftPt = dto.InsetLeftPt,
            InsetRightPt = dto.InsetRightPt,
            InsetTopPt = dto.InsetTopPt,
            InsetBottomPt = dto.InsetBottomPt,
            Wrap = dto.Wrap,
            AutoFitKind = dto.AutoFitKind,
            VerticalType = dto.VerticalType,
            ColumnCount = dto.ColumnCount,
            ColumnSpacingEmu = dto.ColumnSpacingEmu,
        };
        foreach (var paragraph in dto.Paragraphs ?? [])
            body.Paragraphs.Add(FromDto(paragraph));
        if (body.Paragraphs.Count == 0)
            body.Paragraphs.Add(new Paragraph { Runs = { new Run() } });
        return body;
    }

    private static Paragraph FromDto(ClipboardParagraphDto dto)
    {
        var paragraph = new Paragraph
        {
            Align = dto.Align,
            Level = dto.Level,
            BulletKind = dto.BulletKind,
            BulletSuppressed = dto.BulletSuppressed,
            BulletChar = dto.BulletChar,
            AutoNumType = dto.AutoNumType,
            AutoNumStartAt = dto.AutoNumStartAt,
            AutoNumStartAtSpecified = dto.AutoNumStartAtSpecified,
            MarginLeftEmu = dto.MarginLeftEmu,
            IndentEmu = dto.IndentEmu,
            BulletColor = FromDto(dto.BulletColor),
            BulletColorFollowsText = dto.BulletColorFollowsText,
            BulletSizePct = dto.BulletSizePct,
            BulletSizePt = dto.BulletSizePt,
            BulletSizeFollowsText = dto.BulletSizeFollowsText,
            BulletFontFamily = dto.BulletFontFamily,
            BulletFontFollowsText = dto.BulletFontFollowsText,
            SpaceBeforePt = dto.SpaceBeforePt,
            SpaceAfterPt = dto.SpaceAfterPt,
        };
        foreach (var stop in dto.TabStops ?? [])
            paragraph.TabStops.Add(new TabStop { PositionEmu = stop.PositionEmu, Alignment = stop.Alignment });
        if (dto.BulletImage is { } image)
            paragraph.BulletImage = new ImagePart
            {
                ContentType = image.ContentType ?? "application/octet-stream",
                Bytes = image.Bytes ?? [],
            };
        foreach (var run in dto.Runs ?? [])
            paragraph.Runs.Add(FromDto(run));
        if (paragraph.Runs.Count == 0)
            paragraph.Runs.Add(new Run());
        return paragraph;
    }

    private static Run FromDto(ClipboardRunDto? dto) => dto is null
        ? new Run()
        : new Run
        {
            Text = dto.Text ?? string.Empty,
            FontFamily = dto.FontFamily,
            FontSizePt = dto.FontSizePt,
            BaselineOffset = dto.BaselineOffset,
            Bold = dto.Bold,
            Italic = dto.Italic,
            BoldSet = dto.BoldSet,
            ItalicSet = dto.ItalicSet,
            Underline = dto.Underline,
            Strikethrough = dto.Strikethrough,
            Caps = dto.Caps,
            Color = FromDto(dto.Color),
            Hyperlink = dto.Hyperlink is null ? null : new Hyperlink
            {
                Url = dto.Hyperlink.Url,
                TargetSlideId = dto.Hyperlink.TargetSlideId,
                Tooltip = dto.Hyperlink.Tooltip,
            },
            Field = dto.Field is null ? null : new FieldRun
            {
                FieldType = dto.Field.FieldType ?? string.Empty,
                CachedText = dto.Field.CachedText ?? string.Empty,
                FontFamily = dto.Field.FontFamily,
                FontSizePt = dto.Field.FontSizePt,
                Bold = dto.Field.Bold,
                Italic = dto.Field.Italic,
                Color = dto.Field.Color,
            },
            Math = dto.Math is null ? null : new MathRunInfo
            {
                RawXml = dto.Math.RawXml ?? string.Empty,
                IsAlternateContent = dto.Math.IsAlternateContent,
            },
        };

    private static ThemeAwareColor? FromDto(ClipboardColorDto? dto)
    {
        if (dto is null)
            return null;
        var resolved = new SrgbColor(dto.R, dto.G, dto.B);
        if (dto.SchemeColor is not { } scheme)
            return new ThemeAwareColor(resolved, dto.Alpha);
        return new ThemeAwareColor(resolved, new SchemeColorRef
        {
            RoleName = scheme.RoleName,
            Slot = scheme.Slot,
            LumMod = scheme.LumMod,
            LumOff = scheme.LumOff,
            Tint = scheme.Tint,
            Shade = scheme.Shade,
        }, dto.Alpha);
    }

    private sealed class ClipboardPayloadDto
    {
        public int Version { get; set; }
        public ClipboardBodyDto? Body { get; set; }
        public ClipboardRunDto? TypingRun { get; set; }
    }

    private sealed class ClipboardBodyDto
    {
        public TextAlign? DefaultParaAlign { get; set; }
        public double? InsetLeftPt { get; set; }
        public double? InsetRightPt { get; set; }
        public double? InsetTopPt { get; set; }
        public double? InsetBottomPt { get; set; }
        public bool Wrap { get; set; }
        public TextAutoFitKind AutoFitKind { get; set; }
        public TextVerticalType VerticalType { get; set; }
        public int ColumnCount { get; set; }
        public long ColumnSpacingEmu { get; set; }
        public List<ClipboardParagraphDto>? Paragraphs { get; set; }
    }

    private sealed class ClipboardParagraphDto
    {
        public TextAlign? Align { get; set; }
        public int Level { get; set; }
        public BulletKind BulletKind { get; set; }
        public bool BulletSuppressed { get; set; }
        public string? BulletChar { get; set; }
        public AutoNumType AutoNumType { get; set; }
        public int AutoNumStartAt { get; set; }
        public bool AutoNumStartAtSpecified { get; set; }
        public long? MarginLeftEmu { get; set; }
        public long? IndentEmu { get; set; }
        public ClipboardColorDto? BulletColor { get; set; }
        public bool BulletColorFollowsText { get; set; }
        public int? BulletSizePct { get; set; }
        public double? BulletSizePt { get; set; }
        public bool BulletSizeFollowsText { get; set; }
        public string? BulletFontFamily { get; set; }
        public bool BulletFontFollowsText { get; set; }
        public double? SpaceBeforePt { get; set; }
        public double? SpaceAfterPt { get; set; }
        public List<ClipboardTabStopDto>? TabStops { get; set; }
        public ClipboardImageDto? BulletImage { get; set; }
        public List<ClipboardRunDto>? Runs { get; set; }
    }

    private sealed class ClipboardTabStopDto
    {
        public long PositionEmu { get; set; }
        public TabStopAlignment Alignment { get; set; }
    }

    private sealed class ClipboardImageDto
    {
        public string? ContentType { get; set; }
        public byte[]? Bytes { get; set; }
    }

    private sealed class ClipboardRunDto
    {
        public string? Text { get; set; }
        public string? FontFamily { get; set; }
        public double? FontSizePt { get; set; }
        public int? BaselineOffset { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool BoldSet { get; set; }
        public bool ItalicSet { get; set; }
        public bool Underline { get; set; }
        public bool Strikethrough { get; set; }
        public RunTextCaps Caps { get; set; }
        public ClipboardColorDto? Color { get; set; }
        public ClipboardHyperlinkDto? Hyperlink { get; set; }
        public ClipboardFieldDto? Field { get; set; }
        public ClipboardMathDto? Math { get; set; }
    }

    private sealed class ClipboardHyperlinkDto
    {
        public string? Url { get; set; }
        public string? TargetSlideId { get; set; }
        public string? Tooltip { get; set; }
    }

    private sealed class ClipboardFieldDto
    {
        public string? FieldType { get; set; }
        public string? CachedText { get; set; }
        public string? FontFamily { get; set; }
        public double? FontSizePt { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public SrgbColor? Color { get; set; }
    }

    private sealed class ClipboardMathDto
    {
        public string? RawXml { get; set; }
        public bool IsAlternateContent { get; set; }
    }

    private sealed class ClipboardColorDto
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte Alpha { get; set; }
        public ClipboardSchemeColorDto? SchemeColor { get; set; }
    }

    private sealed class ClipboardSchemeColorDto
    {
        public string? RoleName { get; set; }
        public ThemeColorSlot Slot { get; set; }
        public double LumMod { get; set; } = 1.0;
        public double LumOff { get; set; }
        public double Tint { get; set; } = 1.0;
        public double Shade { get; set; } = 1.0;
    }
}
