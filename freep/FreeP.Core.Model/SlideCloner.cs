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
/// - <see cref="TableShape"/>, <see cref="ChartShape"/>, and editable SmartArt data are deep-cloned.
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
            NumericId = null, // a duplicated slide receives a fresh package id when written
            LayoutId   = slide.LayoutId,
            Background = slide.Background,           // ShapeFill is immutable — share reference
            Notes      = PresentationModelCloneHelper.CloneTextBody(slide.Notes),
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
            AlternativeTextTitle = shape.AlternativeTextTitle,
            AlternativeText = shape.AlternativeText,
            IsDecorative   = shape.IsDecorative,
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
            Picture              = shape.Picture,   // byte[] treated as immutable
            PictureFormat        = shape.PictureFormat is null ? null : ClonePictureFormat(shape.PictureFormat),
            PictureFrameGeometry = shape.PictureFrameGeometry,  // Wave 26: string is immutable
            Media          = shape.Media,     // MediaInfo bytes are immutable once loaded — share reference
            LegacyFxpKind  = shape.LegacyFxpKind,
            TextBody       = PresentationModelCloneHelper.CloneTextBody(shape.TextBody),
            Table          = shape.Table is null ? null : PresentationModelCloneHelper.CloneTable(shape.Table),
            Chart          = shape.Chart    is null ? null : CloneChart(shape.Chart),
            SmartArt       = shape.SmartArt is null ? null : CloneSmartArt(shape.SmartArt),
            Hyperlink      = PresentationModelCloneHelper.CloneHyperlink(shape.Hyperlink),
        };

        foreach (var pair in shape.PresetGeometryAdjustments)
            copy.PresetGeometryAdjustments[pair.Key] = pair.Value;

        // Theme 21: OLE — byte arrays are treated as immutable once loaded; share reference.
        copy.OleObject = shape.OleObject is null ? null : CloneOleObject(shape.OleObject);

        // Wave 25A: preserved modern objects — byte arrays are immutable once loaded; share references.
        copy.PreservedObject = shape.PreservedObject is null ? null : ClonePreservedObject(shape.PreservedObject);

        // Connector attachments — small value-like objects, always deep-copied.
        copy.ConnectionStart = shape.ConnectionStart is null ? null : CloneConnectorAttachment(shape.ConnectionStart);
        copy.ConnectionEnd   = shape.ConnectionEnd   is null ? null : CloneConnectorAttachment(shape.ConnectionEnd);

        // Wave 26: elbow route — copy the waypoints list so mutations are independent.
        if (shape.ElbowRoute is not null)
            copy.ElbowRoute = new List<(long X, long Y)>(shape.ElbowRoute);

        foreach (var child in shape.Children)
            copy.Children.Add(CloneShape(child));

        return copy;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────────

    private static Placeholder ClonePlaceholder(Placeholder p) =>
        new() { Type = p.Type, Idx = p.Idx };

    private static ChartShape CloneChart(ChartShape src)
    {
        var copy = new ChartShape
        {
            ChartType    = src.ChartType,
            HasHighLowLines = src.HasHighLowLines,
            StyleId      = src.StyleId,
            Title        = src.Title,
            TextStyle    = CloneChartTextStyle(src.TextStyle),
            Legend       = src.Legend,
            CategoryAxis = CloneChartAxis(src.CategoryAxis),
            ValueAxis    = CloneChartAxis(src.ValueAxis),
            DataTable    = src.DataTable is null ? null : CloneChartDataTableSettings(src.DataTable),
            DisplayBlanksAs = src.DisplayBlanksAs,
            ShowDataLabelsOverMaximum = src.ShowDataLabelsOverMaximum,
            BarGapWidthPercent = src.BarGapWidthPercent,
            BarOverlapPercent = src.BarOverlapPercent,
            BarGapDepthPercent = src.BarGapDepthPercent,
            ThreeDStyle = src.ThreeDStyle,
            DoughnutHolePercent = src.DoughnutHolePercent,
            FirstSliceAngleDegrees = src.FirstSliceAngleDegrees,
            ScatterStyle = src.ScatterStyle,
            RadarStyle = src.RadarStyle,
            DataLabels = src.DataLabels,
            SecondaryValueAxis = src.SecondaryValueAxis is null ? null : CloneChartAxis(src.SecondaryValueAxis),
            RegenerateWorkbookOnSave = src.RegenerateWorkbookOnSave,
            SourcePartPath = src.SourcePartPath,
        };

        foreach (var c in src.Categories)
            copy.Categories.Add(c);

        foreach (var s in src.Series)
        {
            var sc = new ChartSeries
            {
                Name              = s.Name,
                FillColor         = s.FillColor,
                LineStyle         = CloneChartLineStyle(s.LineStyle),
                MarkerStyle       = CloneChartMarkerStyle(s.MarkerStyle),
                OnSecondaryAxis   = s.OnSecondaryAxis,
                DataLabels        = s.DataLabels,
                OverrideChartType = s.OverrideChartType,
            };
            foreach (var v in s.Values)
                sc.Values.Add(v);
            foreach (var v in s.XValues)
                sc.XValues.Add(v);
            foreach (var v in s.BubbleSizes)
                sc.BubbleSizes.Add(v);
            foreach (var kv in s.PointColors)
                sc.PointColors[kv.Key] = kv.Value;
            foreach (var kv in s.PointStyles)
                sc.PointStyles[kv.Key] = CloneChartPointStyle(kv.Value);
            copy.Series.Add(sc);
        }

        return copy;
    }

    private static ChartDataTableSettings CloneChartDataTableSettings(ChartDataTableSettings settings) => new()
    {
        ShowHorizontalBorder = settings.ShowHorizontalBorder,
        ShowVerticalBorder   = settings.ShowVerticalBorder,
        ShowOutlineBorder    = settings.ShowOutlineBorder,
        ShowLegendKeys       = settings.ShowLegendKeys,
        BackgroundFill       = settings.BackgroundFill,
        BorderOutline        = CloneShapeOutline(settings.BorderOutline),
        TextStyle            = CloneChartTextStyle(settings.TextStyle),
    };

    private static ChartTextStyle? CloneChartTextStyle(ChartTextStyle? style) =>
        style is null
            ? null
            : new ChartTextStyle
            {
                IsImplicitDefault = style.IsImplicitDefault,
                FontSizePt = style.FontSizePt,
                Bold       = style.Bold,
                Italic     = style.Italic,
                Color      = style.Color,
                FontFamily = style.FontFamily,
            };

    private static ChartLineStyle? CloneChartLineStyle(ChartLineStyle? style) =>
        style is null
            ? null
            : new ChartLineStyle
            {
                Color   = style.Color,
                WidthPt = style.WidthPt,
                Dash    = style.Dash,
                NoFill  = style.NoFill,
            };

    private static ChartMarkerStyle? CloneChartMarkerStyle(ChartMarkerStyle? style) =>
        style is null
            ? null
            : new ChartMarkerStyle
            {
                Symbol        = style.Symbol,
                SizePt        = style.SizePt,
                FillColor     = style.FillColor,
                StrokeColor   = style.StrokeColor,
                StrokeWidthPt = style.StrokeWidthPt,
                NoFill        = style.NoFill,
                NoStroke      = style.NoStroke,
            };

    private static ChartPointStyle CloneChartPointStyle(ChartPointStyle style) =>
        new()
        {
            FillColor     = style.FillColor,
            StrokeColor   = style.StrokeColor,
            StrokeWidthPt = style.StrokeWidthPt,
            Marker        = CloneChartMarkerStyle(style.Marker),
        };

    private static ShapeOutline? CloneShapeOutline(ShapeOutline? outline) => outline;

    private static ChartAxis CloneChartAxis(ChartAxis a) => new()
    {
        Title             = a.Title,
        Min               = a.Min,
        Max               = a.Max,
        HasMajorGridlines = a.HasMajorGridlines,
        Delete            = a.Delete,
    };

    private static SmartArtShape CloneSmartArt(SmartArtShape source)
    {
        var copy = new SmartArtShape
        {
            Data = source.Data is null ? null : CloneSmartArtData(source.Data),
            QuickStyle = source.QuickStyle is null ? null : CloneSmartArtQuickStyle(source.QuickStyle),
            Colors = source.Colors is null ? null : CloneSmartArtColors(source.Colors),
            DrawingPartPath = source.DrawingPartPath,
        };

        foreach (var fallbackShape in source.FallbackShapes)
            copy.FallbackShapes.Add(CloneShape(fallbackShape));

        foreach (var kv in source.DiagramRelIds)
            copy.DiagramRelIds[kv.Key] = kv.Value;

        foreach (var kv in source.Parts)
        {
            copy.Parts[kv.Key] = new DiagramPart
            {
                ContentType = kv.Value.ContentType,
                PartPath = kv.Value.PartPath,
                Bytes = kv.Value.Bytes,
            };
        }

        foreach (var kv in source.PartRels)
            copy.PartRels[kv.Key] = kv.Value;

        return copy;
    }

    private static SmartArtData CloneSmartArtData(SmartArtData source)
    {
        var copy = new SmartArtData
        {
            Family = source.Family,
            LayoutUniqueId = source.LayoutUniqueId,
            IsLiveLayoutSupported = source.IsLiveLayoutSupported,
        };

        foreach (var node in source.Nodes)
            copy.Nodes.Add(CloneSmartArtNode(node));

        return copy;
    }

    private static SmartArtNode CloneSmartArtNode(SmartArtNode source)
    {
        var copy = new SmartArtNode
        {
            ModelId = source.ModelId,
            Text = source.Text,
            Level = source.Level,
            IsAssistant = source.IsAssistant,
            Picture = source.Picture,
        };

        foreach (var child in source.Children)
            copy.Children.Add(CloneSmartArtNode(child));

        return copy;
    }

    private static SmartArtQuickStyleMetadata CloneSmartArtQuickStyle(SmartArtQuickStyleMetadata source)
    {
        var copy = new SmartArtQuickStyleMetadata
        {
            UniqueId = source.UniqueId,
            Title = source.Title,
            Category = source.Category,
        };

        foreach (var label in source.StyleLabels)
            copy.StyleLabels.Add(label);

        return copy;
    }

    private static SmartArtColorMetadata CloneSmartArtColors(SmartArtColorMetadata source)
    {
        var copy = new SmartArtColorMetadata
        {
            UniqueId = source.UniqueId,
            Title = source.Title,
            Category = source.Category,
        };

        foreach (var label in source.ColorLabels)
            copy.ColorLabels.Add(label);
        foreach (var color in source.Palette)
            copy.Palette.Add(color);

        return copy;
    }

    private static SlideComment CloneComment(SlideComment c)
    {
        var clone = new SlideComment
        {
            AuthorId = c.AuthorId,
            Author   = c.Author,
            Initials = c.Initials,
            Text     = c.Text,
            DateTime = c.DateTime,
            IsResolved       = c.IsResolved,
            ResolvedDateTime = c.ResolvedDateTime,
            ResolvedBy       = c.ResolvedBy,
            UsesModernCommentSchema = c.UsesModernCommentSchema,
            ModernCommentId = c.ModernCommentId,
            ModernAuthorId = c.ModernAuthorId,
            ModernAuthorUserId = c.ModernAuthorUserId,
            ModernAuthorProviderId = c.ModernAuthorProviderId,
            ModernAnchorKind = c.ModernAnchorKind,
            ModernAnchorXml = c.ModernAnchorXml,
            Xemu     = c.Xemu,
            Yemu     = c.Yemu,
            Idx      = c.Idx,
        };

        foreach (var reply in c.Replies)
        {
            clone.Replies.Add(new SlideCommentReply
            {
                AuthorId = reply.AuthorId,
                ModernReplyId = reply.ModernReplyId,
                ModernAuthorId = reply.ModernAuthorId,
                ModernAuthorUserId = reply.ModernAuthorUserId,
                ModernAuthorProviderId = reply.ModernAuthorProviderId,
                Author = reply.Author,
                Initials = reply.Initials,
                Text = reply.Text,
                DateTime = reply.DateTime,
            });
        }

        return clone;
    }

    private static SlideTransition CloneTransition(SlideTransition t) => new()
    {
        Kind            = t.Kind,
        Direction       = t.Direction,
        DurationMs      = t.DurationMs,
        AdvanceOnClick  = t.AdvanceOnClick,
        AdvanceAfterMs  = t.AdvanceAfterMs,
        RawXml          = t.RawXml,
        MorphOption     = t.MorphOption,
        Sound           = t.Sound is null ? null : new TransitionSound
        {
            AudioBytes  = t.Sound.AudioBytes is not null ? (byte[])t.Sound.AudioBytes.Clone() : null,
            ContentType = t.Sound.ContentType,
            RelId       = t.Sound.RelId,
            PartPath    = t.Sound.PartPath,
            Loop        = t.Sound.Loop,
            IsBuiltIn   = t.Sound.IsBuiltIn,
        },
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
            WheelSpokeCount = a.WheelSpokeCount,
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

    private static ConnectorAttachment CloneConnectorAttachment(ConnectorAttachment src) => new()
    {
        ShapeId   = src.ShapeId,
        SiteIndex = src.SiteIndex,
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

    // Wave 25A: PreservedObject cloner — share byte arrays (immutable once loaded).
    private static PreservedObjectInfo ClonePreservedObject(PreservedObjectInfo src)
    {
        var copy = new PreservedObjectInfo
        {
            ObjectKind          = src.ObjectKind,
            ZoomTargetSlideNumericId = src.ZoomTargetSlideNumericId,
            RawXml              = src.RawXml,
            WasAlternateContent = src.WasAlternateContent,
            McRequiresToken     = src.McRequiresToken,
            McRequiresNsUri     = src.McRequiresNsUri,
        };
        foreach (var kv in src.Parts)
            copy.Parts[kv.Key] = kv.Value;
        foreach (var kv in src.PartContentTypes)
            copy.PartContentTypes[kv.Key] = kv.Value;
        foreach (var kv in src.PartRels)
            copy.PartRels[kv.Key] = kv.Value;
        foreach (var kv in src.SlideRels)
            copy.SlideRels[kv.Key] = kv.Value;
        foreach (var kv in src.McRequiresNsUris)
            copy.McRequiresNsUris[kv.Key] = kv.Value;
        return copy;
    }
}
