namespace FreeP.Core.Model;

/// <summary>
/// Owns detached-copy semantics for the presentation text model.
/// </summary>
public static class TextBodyModelCloner
{
    public static TextBody? CloneTextBody(TextBody? source)
    {
        if (source is null)
            return null;

        var copy = new TextBody
        {
            Anchor = source.Anchor,
            DefaultParaAlign = source.DefaultParaAlign,
            DefaultParaRightToLeft = source.DefaultParaRightToLeft,
            InsetLeftPt = source.InsetLeftPt,
            InsetRightPt = source.InsetRightPt,
            InsetTopPt = source.InsetTopPt,
            InsetBottomPt = source.InsetBottomPt,
            Wrap = source.Wrap,
            AutoFitKind = source.AutoFitKind,
            FontScalePPT = source.FontScalePPT,
            LnSpcReductionPPT = source.LnSpcReductionPPT,
            LstStyle = CloneTextStyleLevels(source.LstStyle),
            VerticalType = source.VerticalType,
            WarpPreset = source.WarpPreset,
            Text3dEffects = PresentationModelCloneHelper.CloneShapeEffects(source.Text3dEffects),
            ColumnCount = source.ColumnCount,
            ColumnSpacingEmu = source.ColumnSpacingEmu,
        };

        foreach (var adjust in source.WarpAdjusts)
            copy.WarpAdjusts.Add(adjust);
        foreach (var paragraph in source.Paragraphs)
            copy.Paragraphs.Add(CloneParagraph(paragraph));

        return copy;
    }

    public static Paragraph CloneParagraph(Paragraph source)
    {
        var copy = CloneParagraphMetadata(source);
        foreach (var run in source.Runs)
            copy.Runs.Add(CloneRun(run));
        return copy;
    }

    public static Paragraph CloneParagraphMetadata(
        Paragraph source,
        bool clearAutoNumStartAtSpecified = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        var copy = new Paragraph
        {
            Align = source.Align,
            RightToLeft = source.RightToLeft,
            Level = source.Level,
            BulletKind = source.BulletKind,
            BulletSuppressed = source.BulletSuppressed,
            BulletChar = source.BulletChar,
            BulletImage = CloneImagePart(source.BulletImage),
            AutoNumType = source.AutoNumType,
            AutoNumStartAt = source.AutoNumStartAt,
            AutoNumStartAtSpecified = source.AutoNumStartAtSpecified && !clearAutoNumStartAtSpecified,
            AutoNumTextTemplate = source.AutoNumTextTemplate,
            MarginLeftEmu = source.MarginLeftEmu,
            IndentEmu = source.IndentEmu,
            BulletColor = CloneThemeAwareColor(source.BulletColor),
            BulletColorFollowsText = source.BulletColorFollowsText,
            BulletSizePct = source.BulletSizePct,
            BulletSizePt = source.BulletSizePt,
            BulletSizeFollowsText = source.BulletSizeFollowsText,
            BulletFontFamily = source.BulletFontFamily,
            BulletFontFollowsText = source.BulletFontFollowsText,
            SpaceBeforePt = source.SpaceBeforePt,
            SpaceAfterPt = source.SpaceAfterPt,
        };

        foreach (var tabStop in source.TabStops)
        {
            copy.TabStops.Add(new TabStop
            {
                PositionEmu = tabStop.PositionEmu,
                Alignment = tabStop.Alignment,
                Leader = tabStop.Leader,
            });
        }

        return copy;
    }

    public static Run CloneRun(Run source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Run
        {
            Text = source.Text,
            Language = source.Language,
            AlternateLanguage = source.AlternateLanguage,
            Kumimoji = source.Kumimoji,
            SmartTagClean = source.SmartTagClean,
            NormalizeHeight = source.NormalizeHeight,
            CharacterSpacingHundredthsPt = source.CharacterSpacingHundredthsPt,
            KerningThresholdHundredthsPt = source.KerningThresholdHundredthsPt,
            UnderlineStyleToken = source.UnderlineStyleToken,
            StrikeStyleToken = source.StrikeStyleToken,
            Dirty = source.Dirty,
            NoProof = source.NoProof,
            Error = source.Error,
            InlineImage = CloneImagePart(source.InlineImage),
            InlineImageWidthEmu = source.InlineImageWidthEmu,
            InlineImageHeightEmu = source.InlineImageHeightEmu,
            InlineOleObject = CloneInlineOleObject(source.InlineOleObject),
            InlineTable = source.InlineTable?.Clone(),
            FontFamily = source.FontFamily,
            EastAsiaFontFamily = source.EastAsiaFontFamily,
            ComplexScriptFontFamily = source.ComplexScriptFontFamily,
            FontSizePt = source.FontSizePt,
            BaselineOffset = source.BaselineOffset,
            Bold = source.Bold,
            BoldSet = source.BoldSet,
            Italic = source.Italic,
            ItalicSet = source.ItalicSet,
            Underline = source.Underline,
            Strikethrough = source.Strikethrough,
            RightToLeft = source.RightToLeft,
            Caps = source.Caps,
            Color = CloneThemeAwareColor(source.Color),
            Hyperlink = CloneHyperlink(source.Hyperlink),
            Field = source.Field?.Clone(),
            TextFill = CloneShapeFill(source.TextFill),
            TextOutline = CloneShapeOutline(source.TextOutline),
            TextShadow = CloneRunShadow(source.TextShadow),
            TextReflection = CloneRunReflection(source.TextReflection),
            TextGlow = CloneRunGlow(source.TextGlow),
            TextSoftEdge = CloneRunSoftEdge(source.TextSoftEdge),
            Math = CloneMath(source.Math),
        };
    }

    /// <summary>
    /// Clones a run as a text fragment. Atomic field, math, and inline-object payloads are
    /// retained only when the text is unchanged, preventing duplicate native identities.
    /// </summary>
    public static Run CloneRunWithText(Run source, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var clone = CloneRun(source);
        clone.Text = text;

        // Formatting descriptors remain one authored style across sibling fragments.
        clone.Color = source.Color;
        clone.Hyperlink = source.Hyperlink;
        clone.TextFill = source.TextFill;
        clone.TextOutline = source.TextOutline;
        clone.TextShadow = source.TextShadow;
        clone.TextReflection = source.TextReflection;
        clone.TextGlow = source.TextGlow;
        clone.TextSoftEdge = source.TextSoftEdge;

        if (string.Equals(text, source.Text, StringComparison.Ordinal))
            return clone;

        clone.InlineImage = null;
        clone.InlineImageWidthEmu = null;
        clone.InlineImageHeightEmu = null;
        clone.InlineOleObject = null;
        clone.InlineTable = null;
        clone.Field = null;
        clone.Math = null;
        return clone;
    }

    public static Hyperlink? CloneHyperlink(Hyperlink? source) =>
        source is null
            ? null
            : new Hyperlink
            {
                Url = source.Url,
                TargetSlideId = source.TargetSlideId,
                Action = source.Action,
                Tooltip = source.Tooltip,
            };

    public static bool ColorsEqual(ThemeAwareColor? left, ThemeAwareColor? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return left.Resolved == right.Resolved
            && left.Alpha == right.Alpha
            && SchemeColorRefsEqual(left.SchemeColor, right.SchemeColor);
    }

    public static bool InlineTablesEqual(InlineTableInfo? left, InlineTableInfo? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        var leftTable = left.Table;
        var rightTable = right.Table;
        if (leftTable.RichTextLeftIndentPt != rightTable.RichTextLeftIndentPt
            || leftTable.RichTextCellSpacingPt != rightTable.RichTextCellSpacingPt
            || !leftTable.ColumnWidthsEmu.SequenceEqual(rightTable.ColumnWidthsEmu)
            || leftTable.Rows.Count != rightTable.Rows.Count)
            return false;

        for (var rowIndex = 0; rowIndex < leftTable.Rows.Count; rowIndex++)
        {
            var leftRow = leftTable.Rows[rowIndex];
            var rightRow = rightTable.Rows[rowIndex];
            if (leftRow.HeightEmu != rightRow.HeightEmu
                || leftRow.HeightRule != rightRow.HeightRule
                || leftRow.HorizontalAlignment != rightRow.HorizontalAlignment
                || leftRow.Cells.Count != rightRow.Cells.Count)
                return false;

            for (var cellIndex = 0; cellIndex < leftRow.Cells.Count; cellIndex++)
            {
                var leftCell = leftRow.Cells[cellIndex];
                var rightCell = rightRow.Cells[cellIndex];
                if (leftCell.GridSpan != rightCell.GridSpan
                    || leftCell.RowSpan != rightCell.RowSpan
                    || leftCell.HMerge != rightCell.HMerge
                    || leftCell.VMerge != rightCell.VMerge
                    || !TextBodiesEqualForInlineTable(leftCell.TextBody, rightCell.TextBody))
                    return false;
            }
        }

        return true;
    }

    private static bool TextBodiesEqualForInlineTable(TextBody? left, TextBody? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        if (left.Paragraphs.Count != right.Paragraphs.Count)
            return false;

        for (var paragraphIndex = 0; paragraphIndex < left.Paragraphs.Count; paragraphIndex++)
        {
            var leftParagraph = left.Paragraphs[paragraphIndex];
            var rightParagraph = right.Paragraphs[paragraphIndex];
            if (leftParagraph.Align != rightParagraph.Align
                || leftParagraph.Runs.Count != rightParagraph.Runs.Count)
                return false;

            for (var runIndex = 0; runIndex < leftParagraph.Runs.Count; runIndex++)
            {
                var leftRun = leftParagraph.Runs[runIndex];
                var rightRun = rightParagraph.Runs[runIndex];
                if (leftRun.Text != rightRun.Text
                    || !InlineTablesEqual(leftRun.InlineTable, rightRun.InlineTable))
                    return false;
            }
        }

        return true;
    }

    private static ImagePart? CloneImagePart(ImagePart? source) =>
        source is null
            ? null
            : new ImagePart
            {
                Bytes = source.Bytes.ToArray(),
                ContentType = source.ContentType,
            };

    private static InlineOleObjectInfo? CloneInlineOleObject(InlineOleObjectInfo? source) =>
        source is null
            ? null
            : new InlineOleObjectInfo
            {
                EmbeddedBytes = source.EmbeddedBytes.ToArray(),
                FileName = source.FileName,
                ClassName = source.ClassName,
            };

    private static MathRunInfo? CloneMath(MathRunInfo? source) =>
        source is null
            ? null
            : new MathRunInfo
            {
                RawXml = source.RawXml,
                IsAlternateContent = source.IsAlternateContent,
                ContainingProperties = source.ContainingProperties,
            };

    private static RunTextShadow? CloneRunShadow(RunTextShadow? source) =>
        source is null
            ? null
            : new RunTextShadow
            {
                Color = CloneThemeAwareColor(source.Color)!,
                Alpha = source.Alpha,
                BlurPt = source.BlurPt,
                DistPt = source.DistPt,
                DirDeg = source.DirDeg,
            };

    private static RunTextReflection? CloneRunReflection(RunTextReflection? source) =>
        source is null
            ? null
            : new RunTextReflection
            {
                Alpha = source.Alpha,
                BlurPt = source.BlurPt,
                DistPt = source.DistPt,
                DirDeg = source.DirDeg,
                ScaleY = source.ScaleY,
                EndPos = source.EndPos,
            };

    private static RunTextGlow? CloneRunGlow(RunTextGlow? source) =>
        source is null
            ? null
            : new RunTextGlow
            {
                Color = CloneThemeAwareColor(source.Color)!,
                Alpha = source.Alpha,
                RadiusPt = source.RadiusPt,
            };

    private static RunTextSoftEdge? CloneRunSoftEdge(RunTextSoftEdge? source) =>
        source is null
            ? null
            : new RunTextSoftEdge { RadiusPt = source.RadiusPt };

    private static ThemeAwareColor? CloneThemeAwareColor(ThemeAwareColor? source) =>
        source is null
            ? null
            : source.SchemeColor is { } scheme
                ? new ThemeAwareColor(source.Resolved, CloneSchemeColorRef(scheme), source.Alpha)
                : new ThemeAwareColor(source.Resolved, source.Alpha);

    private static SchemeColorRef CloneSchemeColorRef(SchemeColorRef source) => new()
    {
        RoleName = source.RoleName,
        Slot = source.Slot,
        LumMod = source.LumMod,
        LumOff = source.LumOff,
        Tint = source.Tint,
        Shade = source.Shade,
    };

    private static ShapeFill? CloneShapeFill(ShapeFill? source) => source switch
    {
        null => null,
        ShapeFill.None => ShapeFill.None.Instance,
        ShapeFill.Solid solid => new ShapeFill.Solid(CloneThemeAwareColor(solid.Color)!),
        ShapeFill.Gradient gradient => new ShapeFill.Gradient(
            gradient.Stops.Select(stop => new GradientStop(
                stop.Position,
                CloneThemeAwareColor(stop.Color)!)).ToArray(),
            gradient.Kind,
            gradient.AngleDegrees),
        ShapeFill.Picture picture => new ShapeFill.Picture(
            picture.ImageBytes.ToArray(),
            picture.ContentType,
            picture.Tile),
        ShapeFill.Pattern pattern => new ShapeFill.Pattern(
            pattern.Preset,
            CloneThemeAwareColor(pattern.ForegroundColor)!,
            CloneThemeAwareColor(pattern.BackgroundColor)!),
        _ => throw new NotSupportedException($"Unsupported text fill type '{source.GetType().FullName}'."),
    };

    private static ShapeOutline? CloneShapeOutline(ShapeOutline? source) => source switch
    {
        null => null,
        ShapeOutline.None => ShapeOutline.None.Instance,
        ShapeOutline.Visible visible => new ShapeOutline.Visible(
            CloneThemeAwareColor(visible.Color)!,
            visible.WidthPt,
            visible.Dash,
            CloneLineEnd(visible.BeginLineEnd),
            CloneLineEnd(visible.EndLineEnd)),
        ShapeOutline.GradientVisible gradient => new ShapeOutline.GradientVisible(
            (ShapeFill.Gradient)CloneShapeFill(gradient.Gradient)!,
            gradient.WidthPt,
            gradient.Dash,
            CloneLineEnd(gradient.BeginLineEnd),
            CloneLineEnd(gradient.EndLineEnd)),
        _ => throw new NotSupportedException($"Unsupported text outline type '{source.GetType().FullName}'."),
    };

    private static ShapeLineEnd? CloneLineEnd(ShapeLineEnd? source) =>
        source is null ? null : new ShapeLineEnd(source.Kind);

    private static TextStyleLevels? CloneTextStyleLevels(TextStyleLevels? source)
    {
        if (source is null)
            return null;

        var copy = new TextStyleLevels();
        for (var index = 0; index < 9; index++)
            copy[index] = CloneTextStyleLevel(source[index]);
        return copy;
    }

    private static TextStyleLevel? CloneTextStyleLevel(TextStyleLevel? source) =>
        source is null
            ? null
            : new TextStyleLevel
            {
                Align = source.Align,
                RightToLeft = source.RightToLeft,
                MarginLeftEmu = source.MarginLeftEmu,
                IndentEmu = source.IndentEmu,
                FontSizePt = source.FontSizePt,
                Bold = source.Bold,
                Italic = source.Italic,
                Color = CloneThemeAwareColor(source.Color),
                LatinFont = source.LatinFont,
                BulletKind = source.BulletKind,
                BulletChar = source.BulletChar,
                AutoNumType = source.AutoNumType,
                BulletColor = CloneThemeAwareColor(source.BulletColor),
                BulletColorFollowsText = source.BulletColorFollowsText,
                BulletSizePct = source.BulletSizePct,
                BulletSizePt = source.BulletSizePt,
                BulletSizeFollowsText = source.BulletSizeFollowsText,
                BulletFontFamily = source.BulletFontFamily,
                BulletFontFollowsText = source.BulletFontFollowsText,
            };

    private static bool SchemeColorRefsEqual(SchemeColorRef? left, SchemeColorRef? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.RoleName == right.RoleName
                && left.Slot == right.Slot
                && left.LumMod == right.LumMod
                && left.LumOff == right.LumOff
                && left.Tint == right.Tint
                && left.Shade == right.Shade;
}

/// <summary>
/// Portable selection-boundary mutation for formatted text runs.
/// </summary>
public static class TextBodyRunMutator
{
    public static IReadOnlyList<Run> SplitRunsAtSelection(TextBody body, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(body);

        var selected = new List<Run>();
        var cursor = 0;
        for (var paragraphIndex = 0; paragraphIndex < body.Paragraphs.Count; paragraphIndex++)
        {
            if (paragraphIndex > 0)
                cursor++;

            var paragraph = body.Paragraphs[paragraphIndex];
            var replacement = new List<Run>();
            foreach (var run in paragraph.Runs)
            {
                var runStart = cursor;
                var runEnd = runStart + run.Text.Length;
                cursor = runEnd;
                var overlapStart = Math.Max(runStart, start);
                var overlapEnd = Math.Min(runEnd, end);

                if (overlapEnd <= overlapStart)
                {
                    replacement.Add(run);
                    continue;
                }

                var beforeLength = overlapStart - runStart;
                var selectedLength = overlapEnd - overlapStart;
                var afterLength = runEnd - overlapEnd;
                if (beforeLength > 0)
                {
                    replacement.Add(TextBodyModelCloner.CloneRunWithText(
                        run,
                        run.Text[..beforeLength]));
                }

                var middle = TextBodyModelCloner.CloneRunWithText(
                    run,
                    run.Text.Substring(beforeLength, selectedLength));
                replacement.Add(middle);
                selected.Add(middle);

                if (afterLength > 0)
                {
                    replacement.Add(TextBodyModelCloner.CloneRunWithText(
                        run,
                        run.Text[(beforeLength + selectedLength)..]));
                }
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(replacement);
        }

        return selected;
    }

    public static void MergeAdjacentRunsWithSameFormat(TextBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        foreach (var paragraph in body.Paragraphs)
        {
            var merged = new List<Run>();
            foreach (var run in paragraph.Runs)
            {
                if (merged.Count > 0 && RunFormatsEqual(merged[^1], run))
                    merged[^1].Text += run.Text;
                else
                    merged.Add(run);
            }

            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(merged);
        }
    }

    private static bool RunFormatsEqual(Run left, Run right) =>
        left.Language == right.Language
        && left.AlternateLanguage == right.AlternateLanguage
        && left.Kumimoji == right.Kumimoji
        && left.SmartTagClean == right.SmartTagClean
        && left.NormalizeHeight == right.NormalizeHeight
        && left.CharacterSpacingHundredthsPt == right.CharacterSpacingHundredthsPt
        && left.KerningThresholdHundredthsPt == right.KerningThresholdHundredthsPt
        && left.UnderlineStyleToken == right.UnderlineStyleToken
        && left.StrikeStyleToken == right.StrikeStyleToken
        && left.Dirty == right.Dirty
        && left.NoProof == right.NoProof
        && left.Error == right.Error
        && left.InlineImage is null && right.InlineImage is null
        && left.InlineOleObject is null && right.InlineOleObject is null
        && left.InlineTable is null && right.InlineTable is null
        && left.FontFamily == right.FontFamily
        && left.FontSizePt == right.FontSizePt
        && left.BaselineOffset == right.BaselineOffset
        && left.Bold == right.Bold
        && left.BoldSet == right.BoldSet
        && left.Italic == right.Italic
        && left.ItalicSet == right.ItalicSet
        && left.Underline == right.Underline
        && left.Strikethrough == right.Strikethrough
        && left.RightToLeft == right.RightToLeft
        && left.Caps == right.Caps
        && TextBodyModelCloner.ColorsEqual(left.Color, right.Color)
        && HyperlinksEqual(left.Hyperlink, right.Hyperlink)
        && left.Field is null && right.Field is null
        && Equals(left.TextFill, right.TextFill)
        && Equals(left.TextOutline, right.TextOutline)
        && Equals(left.TextShadow, right.TextShadow)
        && Equals(left.TextReflection, right.TextReflection)
        && Equals(left.TextGlow, right.TextGlow)
        && Equals(left.TextSoftEdge, right.TextSoftEdge)
        && left.Math is null && right.Math is null;

    private static bool HyperlinksEqual(Hyperlink? left, Hyperlink? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.Url == right.Url
                && left.TargetSlideId == right.TargetSlideId
                && left.Tooltip == right.Tooltip;
}
