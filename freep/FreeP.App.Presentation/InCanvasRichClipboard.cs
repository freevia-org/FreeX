using System.Text.Json;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record InCanvasRichClipboardTableBorder(
    int ColorRgb = 0,
    double WidthPt = 0.75,
    bool IsNone = false);

public sealed record InCanvasRichClipboardTableCellStyle(
    int? FillRgb = null,
    InCanvasRichClipboardTableBorder? Left = null,
    InCanvasRichClipboardTableBorder? Right = null,
    InCanvasRichClipboardTableBorder? Top = null,
    InCanvasRichClipboardTableBorder? Bottom = null,
    TableCellAnchor? Anchor = null,
    double? InsetLeftPt = null,
    double? InsetRightPt = null,
    double? InsetTopPt = null,
    double? InsetBottomPt = null,
    bool HorizontalMergeStart = false,
    bool HorizontalMergeContinuation = false,
    bool VerticalMergeStart = false,
    bool VerticalMergeContinuation = false,
    string? FillPattern = null,
    int? FillForegroundRgb = null,
    int? FillBackgroundRgb = null);

/// <summary>
/// One image payload carried by an external rich clipboard fragment. Width and height are
/// optional authored display extents in EMUs; older clipboard payloads and XAML images may omit
/// them and continue to use the normal insertion bounds.
/// </summary>
public sealed record InCanvasRichClipboardImage(
    byte[] Bytes,
    string ContentType,
    long? WidthEmu = null,
    long? HeightEmu = null);

/// <summary>
/// One embedded object payload carried by an external rich clipboard fragment.
/// ClassName preserves the source OLE class when a provider supplies one.
/// </summary>
public sealed record InCanvasRichClipboardObject(
    byte[] Bytes,
    string FileName,
    string? ClassName = null);

/// <summary>
/// Renderer-neutral rich clipboard payload used by both desktop editors. The model fragment is
/// intentionally narrower than a full shape: it carries only the selected text and its run and
/// paragraph semantics, which makes it safe to paste into another in-canvas editor.
/// </summary>
public sealed record InCanvasRichClipboardPayload(
    TextBody Body,
    string PlainText,
    Run? TypingRun = null,
    byte[]? ImageBytes = null,
    string? ImageContentType = null,
    bool ContainsTable = false,
    IReadOnlyList<long>? TableColumnWidthsEmu = null,
    IReadOnlyList<InCanvasRichClipboardTableCellStyle>? TableCellStyles = null,
    IReadOnlyList<InCanvasRichClipboardImage>? ImagePayloads = null,
    IReadOnlyList<InCanvasRichClipboardObject>? ObjectPayloads = null)
{
    public bool HasImage => ImagePayloads is { Count: > 0 }
        || ImageBytes is { Length: > 0 };

    /// <summary>Returns all images, including the legacy single-image fields.</summary>
    public IReadOnlyList<InCanvasRichClipboardImage> GetImagePayloads()
    {
        if (ImagePayloads is { Count: > 0 })
            return ImagePayloads;
        if (ImageBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(ImageContentType))
            return [new InCanvasRichClipboardImage(ImageBytes, ImageContentType)];
        return Array.Empty<InCanvasRichClipboardImage>();
    }

    public IReadOnlyList<InCanvasRichClipboardObject> GetObjectPayloads() =>
        ObjectPayloads is { Count: > 0 }
            ? ObjectPayloads
            : Array.Empty<InCanvasRichClipboardObject>();

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
        TypingRun is null ? null : TextBodyModelCloner.CloneRun(TypingRun),
        ImageBytes?.ToArray(),
        ImageContentType,
        ContainsTable,
        TableColumnWidthsEmu?.ToArray(),
        TableCellStyles?.ToArray(),
        ImagePayloads?.Select(image => new InCanvasRichClipboardImage(
            image.Bytes.ToArray(),
            image.ContentType,
            image.WidthEmu,
            image.HeightEmu)).ToArray(),
        ObjectPayloads?.Select(obj => new InCanvasRichClipboardObject(
            obj.Bytes.ToArray(),
            obj.FileName,
            obj.ClassName)).ToArray());

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
    public const int CurrentVersion = 2;
    private const int LegacyVersion = 1;

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

    /// <summary>
    /// Creates the body used by the slide-level fallback when inline images or objects are
    /// emitted as separate shapes. The rich editor keeps replacement characters and payloads
    /// together; a slide text box must not receive those markers as visible text.
    /// </summary>
    public static TextBody CloneBodyForSlideFallback(TextBody source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var body = TextBodyModelCloner.CloneTextBody(source)!;

        foreach (var paragraph in body.Paragraphs)
        {
            var cleanedRuns = new List<Run>(paragraph.Runs.Count);
            foreach (var run in paragraph.Runs)
            {
                if (run.InlineImage is null
                    && run.InlineOleObject is null
                    && run.InlineTable is null
                    && !run.Text.Contains('\uFFFC', StringComparison.Ordinal))
                {
                    cleanedRuns.Add(run);
                    continue;
                }

                var text = run.Text.Replace("\uFFFC", string.Empty, StringComparison.Ordinal);
                if (text.Length == 0)
                    continue;

                run.Text = text;
                run.InlineImage = null;
                run.InlineImageWidthEmu = null;
                run.InlineImageHeightEmu = null;
                run.InlineOleObject = null;
                run.InlineTable = null;
                cleanedRuns.Add(run);
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(cleanedRuns);
        }

        TextBodyRunMutationPlanner.MergeAdjacentRunsWithSameFormat(body);
        return body;
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
            if (dto is null
                || (dto.Version != LegacyVersion && dto.Version != CurrentVersion)
                || dto.Body is null)
                return null;
            var body = FromDto(dto.Body);
            var plainText = InCanvasTextEditPlanner.ExtractPlainText(body);
            var imagePayloads = dto.ImagePayloads?
                .Where(image => image.Bytes is { Length: > 0 }
                    && !string.IsNullOrWhiteSpace(image.ContentType))
                .Select(image => new InCanvasRichClipboardImage(
                    image.Bytes!,
                    image.ContentType!,
                    image.WidthEmu,
                    image.HeightEmu))
                .ToArray();
            var firstImage = imagePayloads?.FirstOrDefault();
            return new InCanvasRichClipboardPayload(
                body,
                plainText,
                TypingRun: FromDto(dto.TypingRun),
                ImageBytes: firstImage?.Bytes,
                ImageContentType: firstImage?.ContentType,
                ContainsTable: dto.ContainsTable,
                TableColumnWidthsEmu: dto.TableColumnWidthsEmu,
                TableCellStyles: dto.TableCellStyles,
                ImagePayloads: imagePayloads,
                ObjectPayloads: dto.ObjectPayloads?
                    .Where(obj => obj.Bytes is { Length: > 0 }
                        && !string.IsNullOrWhiteSpace(obj.FileName))
                    .Select(obj => new InCanvasRichClipboardObject(
                        obj.Bytes!,
                        obj.FileName!,
                        obj.ClassName))
                    .ToArray());
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
        ContainsTable = payload.ContainsTable,
        TableColumnWidthsEmu = payload.TableColumnWidthsEmu?.ToList(),
        TableCellStyles = payload.TableCellStyles?.ToList(),
        ImagePayloads = payload.GetImagePayloads().Select(image => new ClipboardImageDto
        {
            ContentType = image.ContentType,
            Bytes = image.Bytes.ToArray(),
            WidthEmu = image.WidthEmu,
            HeightEmu = image.HeightEmu,
        }).ToList(),
        ObjectPayloads = payload.GetObjectPayloads().Select(obj => new ClipboardObjectDto
        {
            FileName = obj.FileName,
            Bytes = obj.Bytes.ToArray(),
            ClassName = obj.ClassName,
        }).ToList(),
    };

    private static ClipboardBodyDto ToDto(TextBody body) => new()
    {
        DefaultParaAlign = body.DefaultParaAlign,
        DefaultParaRightToLeft = body.DefaultParaRightToLeft,
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

    private static ClipboardInlineTableDto? ToDto(InlineTableInfo? info)
    {
        if (info is null)
            return null;

        return new ClipboardInlineTableDto
        {
            ColumnWidthsEmu = info.Table.ColumnWidthsEmu.ToList(),
            Rows = info.Table.Rows.Select(row => new ClipboardInlineTableRowDto
            {
                HeightEmu = row.HeightEmu,
                HeightRule = row.HeightRule,
                Cells = row.Cells.Select(cell => new ClipboardInlineTableCellDto
                {
                    Body = cell.TextBody is null ? null : ToDto(cell.TextBody),
                    GridSpan = cell.GridSpan,
                    RowSpan = cell.RowSpan,
                    HMerge = cell.HMerge,
                    VMerge = cell.VMerge,
                    Style = ToDto(cell),
                }).ToList(),
            }).ToList(),
        };
    }

    private static ClipboardInlineTableStyleDto? ToDto(TableCell cell)
    {
        var style = new ClipboardInlineTableStyleDto
        {
            FillRgb = cell.Fill is ShapeFill.Solid solid
                ? Rgb(solid.Color.Resolved)
                : null,
            Anchor = cell.Anchor,
            InsetLeftPt = cell.InsetLeftPt,
            InsetRightPt = cell.InsetRightPt,
            InsetTopPt = cell.InsetTopPt,
            InsetBottomPt = cell.InsetBottomPt,
        };
        if (cell.Borders is { } borders)
        {
            style.Left = ToInlineTableDto(borders.Left);
            style.Right = ToInlineTableDto(borders.Right);
            style.Top = ToInlineTableDto(borders.Top);
            style.Bottom = ToInlineTableDto(borders.Bottom);
        }
        return style;
    }

    private static ClipboardInlineTableBorderDto? ToInlineTableDto(ShapeOutline? outline) => outline switch
    {
        ShapeOutline.None => new ClipboardInlineTableBorderDto { IsNone = true },
        ShapeOutline.Visible visible => new ClipboardInlineTableBorderDto
        {
            ColorRgb = Rgb(visible.Color.Resolved),
            WidthPt = visible.WidthPt,
        },
        _ => null,
    };

    private static InlineTableInfo? FromDto(ClipboardInlineTableDto? dto)
    {
        if (dto is null)
            return null;

        var table = new TableShape();
        table.ColumnWidthsEmu.AddRange(dto.ColumnWidthsEmu ?? []);
        foreach (var rowDto in dto.Rows ?? [])
        {
            var row = new TableRow
            {
                HeightEmu = rowDto.HeightEmu,
                HeightRule = rowDto.HeightRule,
            };
            foreach (var cellDto in rowDto.Cells ?? [])
            {
                var cell = new TableCell
                {
                    TextBody = cellDto.Body is null ? null : FromDto(cellDto.Body),
                    GridSpan = Math.Max(1, cellDto.GridSpan),
                    RowSpan = Math.Max(1, cellDto.RowSpan),
                    HMerge = cellDto.HMerge,
                    VMerge = cellDto.VMerge,
                };
                ApplyInlineTableStyle(cell, cellDto.Style);
                row.Cells.Add(cell);
            }
            table.Rows.Add(row);
        }

        return new InlineTableInfo { Table = table };
    }

    private static void ApplyInlineTableStyle(
        TableCell cell,
        ClipboardInlineTableStyleDto? style)
    {
        if (style is null)
            return;
        if (style.FillRgb is { } fill)
            cell.Fill = new ShapeFill.Solid(ToColor(fill));
        cell.Anchor = style.Anchor;
        cell.InsetLeftPt = style.InsetLeftPt;
        cell.InsetRightPt = style.InsetRightPt;
        cell.InsetTopPt = style.InsetTopPt;
        cell.InsetBottomPt = style.InsetBottomPt;
        if (style.Left is not null || style.Right is not null
            || style.Top is not null || style.Bottom is not null)
        {
            cell.Borders = new TableCellBorders
            {
                Left = FromDto(style.Left),
                Right = FromDto(style.Right),
                Top = FromDto(style.Top),
                Bottom = FromDto(style.Bottom),
            };
        }
    }

    private static ShapeOutline? FromDto(ClipboardInlineTableBorderDto? border) => border switch
    {
        null => null,
        { IsNone: true } => ShapeOutline.None.Instance,
        _ => new ShapeOutline.Visible(ToColor(border.ColorRgb), border.WidthPt <= 0 ? 0.75 : border.WidthPt),
    };

    private static int Rgb(SrgbColor color) => (color.R << 16) | (color.G << 8) | color.B;

    private static SrgbColor ToColor(int rgb) => new(
        (byte)((rgb >> 16) & 0xFF),
        (byte)((rgb >> 8) & 0xFF),
        (byte)(rgb & 0xFF));

    private static ClipboardParagraphDto ToDto(Paragraph paragraph) => new()
    {
        Align = paragraph.Align,
        RightToLeft = paragraph.RightToLeft,
        Level = paragraph.Level,
        BulletKind = paragraph.BulletKind,
        BulletSuppressed = paragraph.BulletSuppressed,
        BulletChar = paragraph.BulletChar,
        AutoNumType = paragraph.AutoNumType,
        AutoNumStartAt = paragraph.AutoNumStartAt,
        AutoNumStartAtSpecified = paragraph.AutoNumStartAtSpecified,
        AutoNumTextTemplate = paragraph.AutoNumTextTemplate,
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
            Leader = stop.Leader,
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
        InlineImage = run.InlineImage is null ? null : new ClipboardImageDto
        {
            ContentType = run.InlineImage.ContentType,
            Bytes = run.InlineImage.Bytes.ToArray(),
            WidthEmu = run.InlineImageWidthEmu,
            HeightEmu = run.InlineImageHeightEmu,
        },
        InlineOleObject = run.InlineOleObject is null ? null : new ClipboardObjectDto
        {
            FileName = run.InlineOleObject.FileName,
            Bytes = run.InlineOleObject.EmbeddedBytes.ToArray(),
            ClassName = run.InlineOleObject.ClassName,
        },
        InlineTable = ToDto(run.InlineTable),
        FontFamily = run.FontFamily,
        FontSizePt = run.FontSizePt,
        BaselineOffset = run.BaselineOffset,
        Bold = run.Bold,
        Italic = run.Italic,
        BoldSet = run.BoldSet,
        ItalicSet = run.ItalicSet,
        Underline = run.Underline,
        Strikethrough = run.Strikethrough,
        RightToLeft = run.RightToLeft,
        Caps = run.Caps,
        Color = ToDto(run.Color),
        TextFill = ToDto(run.TextFill),
        TextOutline = ToDto(run.TextOutline),
        TextShadow = run.TextShadow is null ? null : new ClipboardRunShadowDto
        {
            Color = ToDto(run.TextShadow.Color),
            Alpha = run.TextShadow.Alpha,
            BlurPt = run.TextShadow.BlurPt,
            DistPt = run.TextShadow.DistPt,
            DirDeg = run.TextShadow.DirDeg,
        },
        TextReflection = run.TextReflection is null ? null : new ClipboardRunReflectionDto
        {
            Alpha = run.TextReflection.Alpha,
            BlurPt = run.TextReflection.BlurPt,
            DistPt = run.TextReflection.DistPt,
            DirDeg = run.TextReflection.DirDeg,
            ScaleY = run.TextReflection.ScaleY,
            EndPos = run.TextReflection.EndPos,
        },
        TextGlow = run.TextGlow is null ? null : new ClipboardRunGlowDto
        {
            Color = ToDto(run.TextGlow.Color),
            Alpha = run.TextGlow.Alpha,
            RadiusPt = run.TextGlow.RadiusPt,
        },
        TextSoftEdge = run.TextSoftEdge is null ? null : new ClipboardRunSoftEdgeDto
        {
            RadiusPt = run.TextSoftEdge.RadiusPt,
        },
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
            DefaultParaRightToLeft = dto.DefaultParaRightToLeft,
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
            RightToLeft = dto.RightToLeft,
            Level = dto.Level,
            BulletKind = dto.BulletKind,
            BulletSuppressed = dto.BulletSuppressed,
            BulletChar = dto.BulletChar,
            AutoNumType = dto.AutoNumType,
            AutoNumStartAt = dto.AutoNumStartAt,
            AutoNumStartAtSpecified = dto.AutoNumStartAtSpecified,
            AutoNumTextTemplate = dto.AutoNumTextTemplate,
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
            paragraph.TabStops.Add(new TabStop
            {
                PositionEmu = stop.PositionEmu,
                Alignment = stop.Alignment,
                Leader = stop.Leader,
            });
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
            InlineImage = dto.InlineImage is { Bytes.Length: > 0 } image
                ? new ImagePart
                {
                    ContentType = image.ContentType ?? "image/png",
                    Bytes = image.Bytes!,
                }
                : null,
            InlineImageWidthEmu = dto.InlineImage?.WidthEmu,
            InlineImageHeightEmu = dto.InlineImage?.HeightEmu,
            InlineOleObject = dto.InlineOleObject is { Bytes.Length: > 0 } obj
                ? new InlineOleObjectInfo
                {
                    EmbeddedBytes = obj.Bytes!,
                    FileName = obj.FileName ?? "Embedded.bin",
                    ClassName = obj.ClassName,
                }
                : null,
            InlineTable = FromDto(dto.InlineTable),
            FontFamily = dto.FontFamily,
            FontSizePt = dto.FontSizePt,
            BaselineOffset = dto.BaselineOffset,
            Bold = dto.Bold,
            Italic = dto.Italic,
            BoldSet = dto.BoldSet,
            ItalicSet = dto.ItalicSet,
            Underline = dto.Underline,
            Strikethrough = dto.Strikethrough,
            RightToLeft = dto.RightToLeft,
            Caps = dto.Caps,
            Color = FromDto(dto.Color),
            TextFill = FromDto(dto.TextFill),
            TextOutline = FromDto(dto.TextOutline),
            TextShadow = dto.TextShadow is null ? null : new RunTextShadow
            {
                Color = FromDto(dto.TextShadow.Color) ?? ThemeAwareColor.Black,
                Alpha = dto.TextShadow.Alpha,
                BlurPt = dto.TextShadow.BlurPt,
                DistPt = dto.TextShadow.DistPt,
                DirDeg = dto.TextShadow.DirDeg,
            },
            TextReflection = dto.TextReflection is null ? null : new RunTextReflection
            {
                Alpha = dto.TextReflection.Alpha,
                BlurPt = dto.TextReflection.BlurPt,
                DistPt = dto.TextReflection.DistPt,
                DirDeg = dto.TextReflection.DirDeg,
                ScaleY = dto.TextReflection.ScaleY,
                EndPos = dto.TextReflection.EndPos,
            },
            TextGlow = dto.TextGlow is null ? null : new RunTextGlow
            {
                Color = FromDto(dto.TextGlow.Color) ?? ThemeAwareColor.Black,
                Alpha = dto.TextGlow.Alpha,
                RadiusPt = dto.TextGlow.RadiusPt,
            },
            TextSoftEdge = dto.TextSoftEdge is null ? null : new RunTextSoftEdge
            {
                RadiusPt = dto.TextSoftEdge.RadiusPt,
            },
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

    private static ClipboardFillDto? ToDto(ShapeFill? fill) => fill switch
    {
        null => null,
        ShapeFill.None => new ClipboardFillDto { Kind = "none" },
        ShapeFill.Solid solid => new ClipboardFillDto
        {
            Kind = "solid",
            Color = ToDto(solid.Color),
        },
        ShapeFill.Gradient gradient => new ClipboardFillDto
        {
            Kind = "gradient",
            GradientKind = gradient.Kind,
            AngleDegrees = gradient.AngleDegrees,
            Stops = gradient.Stops.Select(stop => new ClipboardGradientStopDto
            {
                Position = stop.Position,
                Color = ToDto(stop.Color),
            }).ToList(),
        },
        ShapeFill.Picture picture => new ClipboardFillDto
        {
            Kind = "picture",
            ImageBytes = picture.ImageBytes.ToArray(),
            ContentType = picture.ContentType,
            Tile = picture.Tile,
        },
        ShapeFill.Pattern pattern => new ClipboardFillDto
        {
            Kind = "pattern",
            Preset = pattern.Preset,
            ForegroundColor = ToDto(pattern.ForegroundColor),
            BackgroundColor = ToDto(pattern.BackgroundColor),
        },
        _ => throw new NotSupportedException($"Unsupported text fill type '{fill.GetType().FullName}'."),
    };

    private static ShapeFill? FromDto(ClipboardFillDto? dto)
    {
        if (dto is null)
            return null;

        return dto.Kind?.ToLowerInvariant() switch
        {
            "none" => ShapeFill.None.Instance,
            "solid" => new ShapeFill.Solid(FromDto(dto.Color) ?? ThemeAwareColor.Black),
            "gradient" => new ShapeFill.Gradient(
                (dto.Stops ?? []).Select(stop => new GradientStop(
                    stop.Position,
                    FromDto(stop.Color) ?? ThemeAwareColor.Black)).ToArray(),
                dto.GradientKind,
                dto.AngleDegrees),
            "picture" => new ShapeFill.Picture(
                dto.ImageBytes ?? [],
                dto.ContentType ?? "image/png",
                dto.Tile),
            "pattern" => new ShapeFill.Pattern(
                dto.Preset ?? string.Empty,
                FromDto(dto.ForegroundColor) ?? ThemeAwareColor.Black,
                FromDto(dto.BackgroundColor) ?? ThemeAwareColor.White),
            _ => throw new JsonException($"Unsupported text fill kind '{dto.Kind}'."),
        };
    }

    private static ClipboardOutlineDto? ToDto(ShapeOutline? outline) => outline switch
    {
        null => null,
        ShapeOutline.None => new ClipboardOutlineDto { Kind = "none" },
        ShapeOutline.Visible visible => new ClipboardOutlineDto
        {
            Kind = "visible",
            WidthPt = visible.WidthPt,
            Dash = visible.Dash,
            Color = ToDto(visible.Color),
            BeginLineEnd = ToDto(visible.BeginLineEnd),
            EndLineEnd = ToDto(visible.EndLineEnd),
        },
        ShapeOutline.GradientVisible gradient => new ClipboardOutlineDto
        {
            Kind = "gradient-visible",
            WidthPt = gradient.WidthPt,
            Dash = gradient.Dash,
            Gradient = ToDto(gradient.Gradient),
            BeginLineEnd = ToDto(gradient.BeginLineEnd),
            EndLineEnd = ToDto(gradient.EndLineEnd),
        },
        _ => throw new NotSupportedException($"Unsupported text outline type '{outline.GetType().FullName}'."),
    };

    private static ShapeOutline? FromDto(ClipboardOutlineDto? dto)
    {
        if (dto is null)
            return null;

        return dto.Kind?.ToLowerInvariant() switch
        {
            "none" => ShapeOutline.None.Instance,
            "visible" => new ShapeOutline.Visible(
                FromDto(dto.Color) ?? ThemeAwareColor.Black,
                dto.WidthPt,
                dto.Dash,
                FromDto(dto.BeginLineEnd),
                FromDto(dto.EndLineEnd)),
            "gradient-visible" => new ShapeOutline.GradientVisible(
                FromDto(dto.Gradient)
                    ?? throw new JsonException("Gradient text outline is missing its gradient."),
                dto.WidthPt,
                dto.Dash,
                FromDto(dto.BeginLineEnd),
                FromDto(dto.EndLineEnd)),
            _ => throw new JsonException($"Unsupported text outline kind '{dto.Kind}'."),
        };
    }

    private static ClipboardGradientDto? ToDto(ShapeFill.Gradient? gradient) => gradient is null
        ? null
        : new ClipboardGradientDto
        {
            Kind = gradient.Kind,
            AngleDegrees = gradient.AngleDegrees,
            Stops = gradient.Stops.Select(stop => new ClipboardGradientStopDto
            {
                Position = stop.Position,
                Color = ToDto(stop.Color),
            }).ToList(),
        };

    private static ShapeFill.Gradient? FromDto(ClipboardGradientDto? dto) => dto is null
        ? null
        : new ShapeFill.Gradient(
            (dto.Stops ?? []).Select(stop => new GradientStop(
                stop.Position,
                FromDto(stop.Color) ?? ThemeAwareColor.Black)).ToArray(),
            dto.Kind,
            dto.AngleDegrees);

    private static ClipboardLineEndDto? ToDto(ShapeLineEnd? lineEnd) => lineEnd is null
        ? null
        : new ClipboardLineEndDto { Kind = lineEnd.Kind };

    private static ShapeLineEnd? FromDto(ClipboardLineEndDto? dto) => dto is null
        ? null
        : new ShapeLineEnd(dto.Kind);

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
        public bool ContainsTable { get; set; }
        public List<long>? TableColumnWidthsEmu { get; set; }
        public List<InCanvasRichClipboardTableCellStyle>? TableCellStyles { get; set; }
        public List<ClipboardImageDto>? ImagePayloads { get; set; }
        public List<ClipboardObjectDto>? ObjectPayloads { get; set; }
    }

    private sealed class ClipboardBodyDto
    {
        public TextAlign? DefaultParaAlign { get; set; }
        public bool? DefaultParaRightToLeft { get; set; }
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
        public bool? RightToLeft { get; set; }
        public int Level { get; set; }
        public BulletKind BulletKind { get; set; }
        public bool BulletSuppressed { get; set; }
        public string? BulletChar { get; set; }
        public AutoNumType AutoNumType { get; set; }
        public int AutoNumStartAt { get; set; }
        public bool AutoNumStartAtSpecified { get; set; }
        public string? AutoNumTextTemplate { get; set; }
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
        public TabStopLeader Leader { get; set; }
    }

    private sealed class ClipboardImageDto
    {
        public string? ContentType { get; set; }
        public byte[]? Bytes { get; set; }
        public long? WidthEmu { get; set; }
        public long? HeightEmu { get; set; }
    }

    private sealed class ClipboardObjectDto
    {
        public string? FileName { get; set; }
        public byte[]? Bytes { get; set; }
        public string? ClassName { get; set; }
    }

    private sealed class ClipboardRunDto
    {
        public string? Text { get; set; }
        public ClipboardImageDto? InlineImage { get; set; }
        public ClipboardObjectDto? InlineOleObject { get; set; }
        public ClipboardInlineTableDto? InlineTable { get; set; }
        public string? FontFamily { get; set; }
        public double? FontSizePt { get; set; }
        public int? BaselineOffset { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool BoldSet { get; set; }
        public bool ItalicSet { get; set; }
        public bool Underline { get; set; }
        public bool Strikethrough { get; set; }
        public bool? RightToLeft { get; set; }
        public RunTextCaps Caps { get; set; }
        public ClipboardColorDto? Color { get; set; }
        public ClipboardFillDto? TextFill { get; set; }
        public ClipboardOutlineDto? TextOutline { get; set; }
        public ClipboardRunShadowDto? TextShadow { get; set; }
        public ClipboardRunReflectionDto? TextReflection { get; set; }
        public ClipboardRunGlowDto? TextGlow { get; set; }
        public ClipboardRunSoftEdgeDto? TextSoftEdge { get; set; }
        public ClipboardHyperlinkDto? Hyperlink { get; set; }
        public ClipboardFieldDto? Field { get; set; }
        public ClipboardMathDto? Math { get; set; }
    }

    private sealed class ClipboardInlineTableDto
    {
        public List<long>? ColumnWidthsEmu { get; set; }
        public List<ClipboardInlineTableRowDto>? Rows { get; set; }
    }

    private sealed class ClipboardInlineTableRowDto
    {
        public long HeightEmu { get; set; }
        public TableRowHeightRule? HeightRule { get; set; }
        public List<ClipboardInlineTableCellDto>? Cells { get; set; }
    }

    private sealed class ClipboardInlineTableCellDto
    {
        public ClipboardBodyDto? Body { get; set; }
        public int GridSpan { get; set; } = 1;
        public int RowSpan { get; set; } = 1;
        public bool HMerge { get; set; }
        public bool VMerge { get; set; }
        public ClipboardInlineTableStyleDto? Style { get; set; }
    }

    private sealed class ClipboardInlineTableStyleDto
    {
        public int? FillRgb { get; set; }
        public ClipboardInlineTableBorderDto? Left { get; set; }
        public ClipboardInlineTableBorderDto? Right { get; set; }
        public ClipboardInlineTableBorderDto? Top { get; set; }
        public ClipboardInlineTableBorderDto? Bottom { get; set; }
        public TableCellAnchor? Anchor { get; set; }
        public double? InsetLeftPt { get; set; }
        public double? InsetRightPt { get; set; }
        public double? InsetTopPt { get; set; }
        public double? InsetBottomPt { get; set; }
    }

    private sealed class ClipboardInlineTableBorderDto
    {
        public int ColorRgb { get; set; }
        public double WidthPt { get; set; }
        public bool IsNone { get; set; }
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

    private sealed class ClipboardRunShadowDto
    {
        public ClipboardColorDto? Color { get; set; }
        public byte Alpha { get; set; }
        public double BlurPt { get; set; }
        public double DistPt { get; set; }
        public double DirDeg { get; set; }
    }

    private sealed class ClipboardRunReflectionDto
    {
        public byte Alpha { get; set; }
        public double BlurPt { get; set; }
        public double DistPt { get; set; }
        public double DirDeg { get; set; }
        public double ScaleY { get; set; }
        public double EndPos { get; set; }
    }

    private sealed class ClipboardRunGlowDto
    {
        public ClipboardColorDto? Color { get; set; }
        public byte Alpha { get; set; }
        public double RadiusPt { get; set; }
    }

    private sealed class ClipboardRunSoftEdgeDto
    {
        public double RadiusPt { get; set; }
    }

    private sealed class ClipboardFillDto
    {
        public string? Kind { get; set; }
        public ClipboardColorDto? Color { get; set; }
        public GradientKind GradientKind { get; set; }
        public double AngleDegrees { get; set; }
        public List<ClipboardGradientStopDto>? Stops { get; set; }
        public byte[]? ImageBytes { get; set; }
        public string? ContentType { get; set; }
        public bool Tile { get; set; }
        public string? Preset { get; set; }
        public ClipboardColorDto? ForegroundColor { get; set; }
        public ClipboardColorDto? BackgroundColor { get; set; }
    }

    private sealed class ClipboardGradientDto
    {
        public GradientKind Kind { get; set; }
        public double AngleDegrees { get; set; }
        public List<ClipboardGradientStopDto>? Stops { get; set; }
    }

    private sealed class ClipboardGradientStopDto
    {
        public double Position { get; set; }
        public ClipboardColorDto? Color { get; set; }
    }

    private sealed class ClipboardOutlineDto
    {
        public string? Kind { get; set; }
        public double WidthPt { get; set; }
        public OutlineDash Dash { get; set; }
        public ClipboardColorDto? Color { get; set; }
        public ClipboardGradientDto? Gradient { get; set; }
        public ClipboardLineEndDto? BeginLineEnd { get; set; }
        public ClipboardLineEndDto? EndLineEnd { get; set; }
    }

    private sealed class ClipboardLineEndDto
    {
        public ShapeLineEndKind Kind { get; set; }
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
