namespace FreeP.Core.Model;

/// <summary>
/// Deep-clone helpers for <see cref="Slide"/> and <see cref="SlideShape"/>. Used by undo-capture
/// and by <see cref="DuplicateSlideCommand"/> to produce an independent copy of a slide.
///
/// Cloning strategy:
/// - All value-type and string properties are copied directly (they are immutable / value-semantic).
/// - <see cref="ShapeFill"/> and <see cref="ShapeOutline"/> are immutable discriminated unions —
///   they are shared (not deep-copied) because they cannot be mutated in place.
/// - <see cref="ThemeAwareColor"/> is a struct — copied by value automatically.
/// - Collections (<see cref="Slide.Shapes"/>, <see cref="TextBody.Paragraphs"/>, etc.) are
///   deep-cloned so mutations on the copy do not affect the original.
/// - <see cref="ImagePart"/> bytes are shared (byte arrays are treated as immutable once loaded).
/// - <see cref="TableShape"/> and <see cref="ChartShape"/> data is deep-cloned.
/// </summary>
public static class SlideCloner
{
    // ── Public entry points ───────────────────────────────────────────────────────

    /// <summary>Returns a fully independent deep copy of <paramref name="slide"/>.</summary>
    public static Slide CloneSlide(Slide slide)
    {
        var copy = new Slide
        {
            Id      = Guid.NewGuid().ToString("N"), // new identity so it is truly a distinct slide
            LayoutId   = slide.LayoutId,
            Background = slide.Background,           // ShapeFill is immutable — share reference
            Notes      = slide.Notes is null ? null : CloneTextBody(slide.Notes),
            HfVisibility = slide.HfVisibility is null ? null : new HfFlags
            {
                ShowFooter   = slide.HfVisibility.ShowFooter,
                ShowDate     = slide.HfVisibility.ShowDate,
                ShowSlideNum = slide.HfVisibility.ShowSlideNum,
                ShowHeader   = slide.HfVisibility.ShowHeader,
            },
        };

        foreach (var shape in slide.Shapes)
            copy.Shapes.Add(CloneShape(shape));

        copy.Transition = slide.Transition is null ? null : CloneTransition(slide.Transition);
        foreach (var anim in slide.Animations)
            copy.Animations.Add(CloneAnimation(anim));

        foreach (var comment in slide.Comments)
            copy.Comments.Add(CloneComment(comment));

        return copy;
    }

    /// <summary>Returns a fully independent deep copy of <paramref name="shape"/>.</summary>
    public static SlideShape CloneShape(SlideShape shape)
    {
        var copy = new SlideShape
        {
            Id             = shape.Id,
            Name           = shape.Name,
            Kind           = shape.Kind,
            AutoShapeKind  = shape.AutoShapeKind,
            OffsetXEmu     = shape.OffsetXEmu,
            OffsetYEmu     = shape.OffsetYEmu,
            ExtentCxEmu    = shape.ExtentCxEmu,
            ExtentCyEmu    = shape.ExtentCyEmu,
            RotationDeg    = shape.RotationDeg,
            FlipH          = shape.FlipH,
            FlipV          = shape.FlipV,
            Fill           = shape.Fill,      // immutable — share
            Outline        = shape.Outline,   // immutable — share
            Placeholder    = shape.Placeholder is null ? null : ClonePlaceholder(shape.Placeholder),
            Picture        = shape.Picture,   // byte[] treated as immutable
            PictureFormat  = shape.PictureFormat is null ? null : ClonePictureFormat(shape.PictureFormat),
            Media          = shape.Media,     // MediaInfo bytes are immutable once loaded — share reference
            LegacyFxpKind  = shape.LegacyFxpKind,
            TextBody       = shape.TextBody is null ? null : CloneTextBody(shape.TextBody),
            Table          = shape.Table    is null ? null : CloneTable(shape.Table),
            Chart          = shape.Chart    is null ? null : CloneChart(shape.Chart),
            SmartArt       = shape.SmartArt,  // SmartArtShape bytes are immutable once loaded — share
            Hyperlink      = CloneHyperlink(shape.Hyperlink),
        };

        // Theme 21: OLE — byte arrays are treated as immutable once loaded; share reference.
        copy.OleObject = shape.OleObject is null ? null : CloneOleObject(shape.OleObject);

        foreach (var child in shape.Children)
            copy.Children.Add(CloneShape(child));

        return copy;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────────

    private static Placeholder ClonePlaceholder(Placeholder p) =>
        new() { Type = p.Type, Idx = p.Idx };

    private static TextBody CloneTextBody(TextBody tb)
    {
        var copy = new TextBody
        {
            Anchor            = tb.Anchor,
            DefaultParaAlign  = tb.DefaultParaAlign,
            InsetLeftPt       = tb.InsetLeftPt,
            InsetRightPt      = tb.InsetRightPt,
            InsetTopPt        = tb.InsetTopPt,
            InsetBottomPt     = tb.InsetBottomPt,
            Wrap              = tb.Wrap,
            AutoFit           = tb.AutoFit,
            WarpPreset        = tb.WarpPreset,       // Wave 16A
        };

        // BA4: deep-copy warp adjust guides
        foreach (var adj in tb.WarpAdjusts)
            copy.WarpAdjusts.Add(adj);

        foreach (var para in tb.Paragraphs)
            copy.Paragraphs.Add(CloneParagraph(para));

        return copy;
    }

    private static Paragraph CloneParagraph(Paragraph para)
    {
        var copy = new Paragraph
        {
            Align        = para.Align,
            Level        = para.Level,
            BulletKind   = para.BulletKind,
            BulletChar   = para.BulletChar,
            SpaceBeforePt = para.SpaceBeforePt,
            SpaceAfterPt  = para.SpaceAfterPt,
        };

        foreach (var run in para.Runs)
            copy.Runs.Add(CloneRun(run));

        return copy;
    }

    private static RunTextShadow? CloneRunShadow(RunTextShadow? s) =>
        s is null ? null : new RunTextShadow
        {
            Color  = s.Color,
            Alpha  = s.Alpha,
            BlurPt = s.BlurPt,
            DistPt = s.DistPt,
            DirDeg = s.DirDeg,
        };

    private static Run CloneRun(Run run) => new()
    {
        Text          = run.Text,
        FontFamily    = run.FontFamily,
        FontSizePt    = run.FontSizePt,
        Bold          = run.Bold,
        BoldSet       = run.BoldSet,
        Italic        = run.Italic,
        ItalicSet     = run.ItalicSet,
        Underline     = run.Underline,
        Strikethrough = run.Strikethrough,
        Color         = run.Color,           // ThemeAwareColor is a struct — copied by value
        Hyperlink     = CloneHyperlink(run.Hyperlink),
        Field = run.Field is null ? null : new FieldRun
        {
            FieldType  = run.Field.FieldType,
            CachedText = run.Field.CachedText,
            FontFamily = run.Field.FontFamily,
            FontSizePt = run.Field.FontSizePt,
            Bold       = run.Field.Bold,
            Italic     = run.Field.Italic,
            Color      = run.Field.Color,
        },
        // Wave 16A: text effects — ShapeFill/ShapeOutline are immutable discriminated unions, share reference
        TextFill    = run.TextFill,
        TextOutline = run.TextOutline,
        TextShadow  = CloneRunShadow(run.TextShadow),
        // Theme 21: math — MathRunInfo is a small immutable-in-practice container; share reference
        Math        = run.Math,
    };

    private static Hyperlink? CloneHyperlink(Hyperlink? h) =>
        h is null ? null : new Hyperlink { Url = h.Url, TargetSlideId = h.TargetSlideId, Tooltip = h.Tooltip };

    private static TableShape CloneTable(TableShape src)
    {
        var copy = new TableShape
        {
            Flags        = CloneTableStyleFlags(src.Flags),
            TableStyleId = src.TableStyleId,
            StyleData    = src.StyleData,   // StyleData is read from XML and not mutated — share
        };

        foreach (var w in src.ColumnWidthsEmu)
            copy.ColumnWidthsEmu.Add(w);

        foreach (var row in src.Rows)
        {
            var rowCopy = new TableRow { HeightEmu = row.HeightEmu };
            foreach (var cell in row.Cells)
                rowCopy.Cells.Add(CloneTableCell(cell));
            copy.Rows.Add(rowCopy);
        }

        return copy;
    }

    private static TableStyleFlags CloneTableStyleFlags(TableStyleFlags f) => new()
    {
        FirstRow = f.FirstRow, LastRow = f.LastRow,
        FirstCol = f.FirstCol, LastCol = f.LastCol,
        BandRow  = f.BandRow,  BandCol = f.BandCol,
    };

    private static TableCell CloneTableCell(TableCell src) => new()
    {
        TextBody    = src.TextBody is null ? null : CloneTextBody(src.TextBody),
        Fill        = src.Fill,     // immutable — share
        Borders     = src.Borders,  // immutable — share
        GridSpan    = src.GridSpan,
        RowSpan     = src.RowSpan,
        HMerge      = src.HMerge,
        VMerge      = src.VMerge,
        InsetLeftPt  = src.InsetLeftPt,
        InsetRightPt = src.InsetRightPt,
        InsetTopPt   = src.InsetTopPt,
        InsetBottomPt = src.InsetBottomPt,
        Anchor       = src.Anchor,
    };

    private static ChartShape CloneChart(ChartShape src)
    {
        var copy = new ChartShape
        {
            ChartType    = src.ChartType,
            Title        = src.Title,
            Legend       = src.Legend,
            CategoryAxis = CloneChartAxis(src.CategoryAxis),
            ValueAxis    = CloneChartAxis(src.ValueAxis),
        };

        foreach (var c in src.Categories)
            copy.Categories.Add(c);

        foreach (var s in src.Series)
        {
            var sc = new ChartSeries
            {
                Name      = s.Name,
                FillColor = s.FillColor,
            };
            foreach (var v in s.Values)
                sc.Values.Add(v);
            foreach (var kv in s.PointColors)
                sc.PointColors[kv.Key] = kv.Value;
            copy.Series.Add(sc);
        }

        return copy;
    }

    private static ChartAxis CloneChartAxis(ChartAxis a) => new()
    {
        Title             = a.Title,
        Min               = a.Min,
        Max               = a.Max,
        HasMajorGridlines = a.HasMajorGridlines,
        Delete            = a.Delete,
    };

    private static SlideComment CloneComment(SlideComment c) => new()
    {
        AuthorId = c.AuthorId,
        Author   = c.Author,
        Initials = c.Initials,
        Text     = c.Text,
        DateTime = c.DateTime,
        Xemu     = c.Xemu,
        Yemu     = c.Yemu,
        Idx      = c.Idx,
    };

    private static SlideTransition CloneTransition(SlideTransition t) => new()
    {
        Kind            = t.Kind,
        Direction       = t.Direction,
        DurationMs      = t.DurationMs,
        AdvanceOnClick  = t.AdvanceOnClick,
        AdvanceAfterMs  = t.AdvanceAfterMs,
    };

    private static ShapeAnimation CloneAnimation(ShapeAnimation a)
    {
        var copy = new ShapeAnimation
        {
            ShapeId       = a.ShapeId,
            Kind          = a.Kind,
            Preset        = a.Preset,
            Trigger       = a.Trigger,
            DelayMs       = a.DelayMs,
            DurationMs    = a.DurationMs,
            Direction     = a.Direction,
            TriggerShapeId = a.TriggerShapeId,
        };

        if (a.Motion is not null)
        {
            var mp = new MotionPath
            {
                Origin   = a.Motion.Origin,
                PtsTypes = a.Motion.PtsTypes,
            };
            foreach (var seg in a.Motion.Segments)
                mp.Segments.Add(seg); // MotionPathSegment is immutable (init-only props)
            copy.Motion = mp;
        }

        return copy;
    }

    private static PictureFormat ClonePictureFormat(PictureFormat f) => new()
    {
        CropLeft          = f.CropLeft,
        CropTop           = f.CropTop,
        CropRight         = f.CropRight,
        CropBottom        = f.CropBottom,
        Grayscale         = f.Grayscale,
        BiLevelThreshold  = f.BiLevelThreshold,
        Brightness        = f.Brightness,
        Contrast          = f.Contrast,
        AlphaModPct       = f.AlphaModPct,
    };

    // Theme 21: OLE cloner — byte arrays are treated as immutable; share the reference.
    private static OleObjectInfo CloneOleObject(OleObjectInfo src) => new()
    {
        EmbeddedBytes        = src.EmbeddedBytes,   // immutable byte[]
        EmbeddedContentType  = src.EmbeddedContentType,
        ProgId               = src.ProgId,
        RelType              = src.RelType,
        OleObjXml            = src.OleObjXml,
        WasAlternateContent  = src.WasAlternateContent,
        EmbeddedExtension    = src.EmbeddedExtension,
    };
}
