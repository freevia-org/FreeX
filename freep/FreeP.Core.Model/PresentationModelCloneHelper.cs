namespace FreeP.Core.Model;

internal static class PresentationModelCloneHelper
{
    internal static TableShape? FindTable(Presentation presentation, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= presentation.Slides.Count)
            return null;

        var shape = ShapeHelper.Find(presentation, slideIndex, shapeId);
        return shape?.Table;
    }

    internal static int GridColumnToCellIndex(TableRow row, int targetGridCol)
    {
        int gridPos = 0;
        for (int i = 0; i < row.Cells.Count; i++)
        {
            int span = Math.Max(1, row.Cells[i].GridSpan);
            if (targetGridCol < gridPos + span)
                return i;

            gridPos += span;
        }

        return -1;
    }

    internal static int CellGridStart(TableRow row, int cellIdx)
    {
        int gridPos = 0;
        for (int i = 0; i < cellIdx && i < row.Cells.Count; i++)
            gridPos += Math.Max(1, row.Cells[i].GridSpan);

        return gridPos;
    }

    internal static int RowGridWidth(TableRow row) =>
        row.Cells.Sum(c => Math.Max(1, c.GridSpan));

    internal static TableShape CloneTable(TableShape source)
    {
        var copy = new TableShape
        {
            Flags = CloneTableStyleFlags(source.Flags),
            TableStyleId = source.TableStyleId,
            StyleData = CloneTableStyleData(source.StyleData),
            RichTextLeftIndentPt = source.RichTextLeftIndentPt,
            RichTextCellSpacingPt = source.RichTextCellSpacingPt,
        };

        foreach (var width in source.ColumnWidthsEmu)
            copy.ColumnWidthsEmu.Add(width);

        foreach (var row in source.Rows)
            copy.Rows.Add(CloneTableRow(row));

        return copy;
    }

    internal static TableRow CloneTableRow(TableRow source)
    {
        var copy = new TableRow
        {
            HeightEmu = source.HeightEmu,
            HeightRule = source.HeightRule,
            HorizontalAlignment = source.HorizontalAlignment,
        };
        foreach (var cell in source.Cells)
            copy.Cells.Add(CloneTableCell(cell));

        return copy;
    }

    internal static TableCell CloneTableCell(TableCell source) => new()
    {
        TextBody = CloneTextBody(source.TextBody),
        Fill = source.Fill,
        Borders = CloneTableCellBorders(source.Borders),
        GridSpan = source.GridSpan,
        RowSpan = source.RowSpan,
        HMerge = source.HMerge,
        VMerge = source.VMerge,
        InsetLeftPt = source.InsetLeftPt,
        InsetRightPt = source.InsetRightPt,
        InsetTopPt = source.InsetTopPt,
        InsetBottomPt = source.InsetBottomPt,
        Anchor = source.Anchor,
    };

    internal static TextBody? CloneTextBody(TextBody? source)
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
            Text3dEffects = CloneShapeEffects(source.Text3dEffects),
            ColumnCount = source.ColumnCount,
            ColumnSpacingEmu = source.ColumnSpacingEmu,
        };

        foreach (var adjust in source.WarpAdjusts)
            copy.WarpAdjusts.Add(adjust);

        foreach (var paragraph in source.Paragraphs)
            copy.Paragraphs.Add(CloneParagraph(paragraph));

        return copy;
    }

    internal static ShapeEffects? CloneShapeEffects(ShapeEffects? source)
    {
        if (source is null)
            return null;

        return new ShapeEffects
        {
            HasOuterShadow = source.HasOuterShadow,
            OuterShadowColor = source.OuterShadowColor,
            OuterShadowAlpha = source.OuterShadowAlpha,
            OuterShadowBlurRadEmu = source.OuterShadowBlurRadEmu,
            OuterShadowDistEmu = source.OuterShadowDistEmu,
            OuterShadowDirDeg = source.OuterShadowDirDeg,
            HasInnerShadow = source.HasInnerShadow,
            InnerShadowColor = source.InnerShadowColor,
            InnerShadowAlpha = source.InnerShadowAlpha,
            InnerShadowBlurRadEmu = source.InnerShadowBlurRadEmu,
            InnerShadowDistEmu = source.InnerShadowDistEmu,
            InnerShadowDirDeg = source.InnerShadowDirDeg,
            HasGlow = source.HasGlow,
            GlowColor = source.GlowColor,
            GlowAlpha = source.GlowAlpha,
            GlowRadiusEmu = source.GlowRadiusEmu,
            HasSoftEdge = source.HasSoftEdge,
            SoftEdgeRadEmu = source.SoftEdgeRadEmu,
            BevelTop = CloneBevel(source.BevelTop),
            BevelBottom = CloneBevel(source.BevelBottom),
            ExtrusionHeightEmu = source.ExtrusionHeightEmu,
            ContourWidthEmu = source.ContourWidthEmu,
            PrstMaterial = source.PrstMaterial,
            ExtrusionColor = source.ExtrusionColor,
            ContourColor = source.ContourColor,
            Scene3d = source.Scene3d is null ? null : new Scene3dInfo
            {
                CameraPreset = source.Scene3d.CameraPreset,
                LightRig = source.Scene3d.LightRig,
                LightRigDir = source.Scene3d.LightRigDir,
            },
        };
    }

    private static BevelInfo? CloneBevel(BevelInfo? source) =>
        source is null ? null : new BevelInfo
        {
            WidthEmu = source.WidthEmu,
            HeightEmu = source.HeightEmu,
            PresetName = source.PresetName,
        };

    internal static Hyperlink? CloneHyperlink(Hyperlink? source) =>
        source is null
            ? null
            : new Hyperlink
            {
                Url = source.Url,
                TargetSlideId = source.TargetSlideId,
                Tooltip = source.Tooltip,
            };

    internal static void RestoreTableState(TableShape table, TableShape snapshot)
    {
        table.ColumnWidthsEmu.Clear();
        foreach (var width in snapshot.ColumnWidthsEmu)
            table.ColumnWidthsEmu.Add(width);

        table.Rows.Clear();
        foreach (var row in snapshot.Rows)
            table.Rows.Add(CloneTableRow(row));

        table.Flags = CloneTableStyleFlags(snapshot.Flags);
        table.TableStyleId = snapshot.TableStyleId;
        table.StyleData = CloneTableStyleData(snapshot.StyleData);
    }

    private static Paragraph CloneParagraph(Paragraph source)
    {
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
            AutoNumStartAtSpecified = source.AutoNumStartAtSpecified,
            AutoNumTextTemplate = source.AutoNumTextTemplate,
            MarginLeftEmu = source.MarginLeftEmu,
            IndentEmu = source.IndentEmu,
            BulletColor = source.BulletColor,
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
            copy.TabStops.Add(new TabStop
            {
                PositionEmu = tabStop.PositionEmu,
                Alignment = tabStop.Alignment,
                Leader = tabStop.Leader,
            });

        foreach (var run in source.Runs)
            copy.Runs.Add(CloneRun(run));

        return copy;
    }

    private static ImagePart? CloneImagePart(ImagePart? source) =>
        source is null
            ? null
            : new ImagePart
            {
                Bytes = source.Bytes.ToArray(),
                ContentType = source.ContentType
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

    private static Run CloneRun(Run source) => new()
    {
        Text = source.Text,
        Language = source.Language,
        AlternateLanguage = source.AlternateLanguage,
        Kumimoji = source.Kumimoji,
        SmartTagClean = source.SmartTagClean,
        NormalizeHeight = source.NormalizeHeight,
        Dirty = source.Dirty,
        NoProof = source.NoProof,
        Error = source.Error,
        InlineImage = CloneImagePart(source.InlineImage),
        InlineImageWidthEmu = source.InlineImageWidthEmu,
        InlineImageHeightEmu = source.InlineImageHeightEmu,
        InlineOleObject = CloneInlineOleObject(source.InlineOleObject),
        InlineTable = source.InlineTable?.Clone(),
        FontFamily = source.FontFamily,
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
        Color = source.Color,
        Hyperlink = CloneHyperlink(source.Hyperlink),
        Field = CloneField(source.Field),
        TextFill = source.TextFill,
        TextOutline = source.TextOutline,
        TextShadow = CloneRunShadow(source.TextShadow),
        TextReflection = CloneRunReflection(source.TextReflection),
        TextGlow = CloneRunGlow(source.TextGlow),
        TextSoftEdge = CloneRunSoftEdge(source.TextSoftEdge),
        Math = CloneMath(source.Math),
    };

    private static FieldRun? CloneField(FieldRun? source) =>
        source is null
            ? null
            : new FieldRun
            {
                FieldType = source.FieldType,
                Id = source.Id,
                Dirty = source.Dirty,
                Instruction = source.Instruction,
                CachedText = source.CachedText,
                FontFamily = source.FontFamily,
                FontSizePt = source.FontSizePt,
                Bold = source.Bold,
                Italic = source.Italic,
                Color = source.Color,
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
                Color = source.Color,
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
                Color = source.Color,
                Alpha = source.Alpha,
                RadiusPt = source.RadiusPt,
            };

    private static RunTextSoftEdge? CloneRunSoftEdge(RunTextSoftEdge? source) =>
        source is null
            ? null
            : new RunTextSoftEdge
            {
                RadiusPt = source.RadiusPt,
            };

    private static TableCellBorders? CloneTableCellBorders(TableCellBorders? source) =>
        source is null
            ? null
            : new TableCellBorders
            {
                Left = source.Left,
                Right = source.Right,
                Top = source.Top,
                Bottom = source.Bottom,
                DiagonalDown = source.DiagonalDown,
                DiagonalUp = source.DiagonalUp,
            };

    private static TableStyleFlags CloneTableStyleFlags(TableStyleFlags source) => new()
    {
        FirstRow = source.FirstRow,
        LastRow = source.LastRow,
        FirstCol = source.FirstCol,
        LastCol = source.LastCol,
        BandRow = source.BandRow,
        BandCol = source.BandCol,
    };

    private static TableStyleData? CloneTableStyleData(TableStyleData? source) =>
        source is null
            ? null
            : new TableStyleData
            {
                StyleId = source.StyleId,
                WholeTbl = CloneTableStyleEntry(source.WholeTbl),
                FirstRow = CloneTableStyleEntry(source.FirstRow),
                LastRow = CloneTableStyleEntry(source.LastRow),
                FirstCol = CloneTableStyleEntry(source.FirstCol),
                LastCol = CloneTableStyleEntry(source.LastCol),
                Band1H = CloneTableStyleEntry(source.Band1H),
                Band2H = CloneTableStyleEntry(source.Band2H),
                Band1V = CloneTableStyleEntry(source.Band1V),
                Band2V = CloneTableStyleEntry(source.Band2V),
            };

    private static TableStyleEntry? CloneTableStyleEntry(TableStyleEntry? source) =>
        source is null
            ? null
            : new TableStyleEntry
            {
                Fill = source.Fill,
                BorderOutline = source.BorderOutline,
                TextColor = source.TextColor,
            };

    private static TextStyleLevels? CloneTextStyleLevels(TextStyleLevels? source)
    {
        if (source is null)
            return null;

        var copy = new TextStyleLevels();
        for (int level = 0; level < 9; level++)
            copy[level] = CloneTextStyleLevel(source[level]);

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
                Color = source.Color,
                LatinFont = source.LatinFont,
                BulletKind = source.BulletKind,
                BulletChar = source.BulletChar,
                AutoNumType = source.AutoNumType,
                BulletColor = source.BulletColor,
                BulletColorFollowsText = source.BulletColorFollowsText,
                BulletSizePct = source.BulletSizePct,
                BulletSizePt = source.BulletSizePt,
                BulletSizeFollowsText = source.BulletSizeFollowsText,
                BulletFontFamily = source.BulletFontFamily,
                BulletFontFollowsText = source.BulletFontFollowsText,
            };
}
