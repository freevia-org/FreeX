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
/// - Regular <see cref="SlideShape.Picture"/> bytes are shared (byte arrays are treated as
///   immutable once loaded); editable SmartArt, OLE, and preserved-object package payloads are
///   copied so a duplicate can be changed without mutating the source package.
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
            IsHidden   = slide.IsHidden,
            ColorMapOverride = slide.ColorMapOverride is null
                ? null
                : new Dictionary<string, string>(slide.ColorMapOverride, StringComparer.OrdinalIgnoreCase),
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
        copy.AnimationBuildListXml = slide.AnimationBuildListXml;
        foreach (var anim in slide.Animations)
            copy.Animations.Add(CloneAnimation(anim));

        foreach (var comment in slide.Comments)
            copy.Comments.Add(CloneComment(comment));

        return copy;
    }

    /// <summary>
    /// Returns a fully independent deep copy of <paramref name="slide"/> for an in-place edit.
    /// Unlike <see cref="CloneSlide"/>, the package identity is preserved so slide-targeting
    /// references remain valid while the edited model is swapped into the presentation.
    /// </summary>
    public static Slide CloneSlidePreservingIdentity(Slide slide)
    {
        var copy = CloneSlide(slide);
        copy.Id = slide.Id;
        copy.NumericId = slide.NumericId;
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
            IsHidden       = shape.IsHidden,
            Kind           = shape.Kind,
            AutoShapeKind  = shape.AutoShapeKind,
            OffsetXEmu     = shape.OffsetXEmu,
            OffsetYEmu     = shape.OffsetYEmu,
            ExtentCxEmu    = shape.ExtentCxEmu,
            ExtentCyEmu    = shape.ExtentCyEmu,
            HasExplicitZeroExtentTransform = shape.HasExplicitZeroExtentTransform,
            RotationDeg    = shape.RotationDeg,
            FlipH          = shape.FlipH,
            FlipV          = shape.FlipV,
            Effects        = PresentationModelCloneHelper.CloneShapeEffects(shape.Effects),
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

        copy.Media = shape.Media is null ? null : CloneMedia(shape.Media);

        foreach (var pair in shape.PresetGeometryAdjustments)
            copy.PresetGeometryAdjustments[pair.Key] = pair.Value;

        foreach (var path in shape.CustomGeometry)
        {
            var pathCopy = new CustomGeometryPath
            {
                PathW = path.PathW,
                PathH = path.PathH,
                Fill = path.Fill,
                Stroke = path.Stroke,
            };
            pathCopy.Segments.AddRange(path.Segments);
            copy.CustomGeometry.Add(pathCopy);
        }

        // Theme 21: OLE package bytes belong to the editable duplicate.
        copy.OleObject = shape.OleObject is null ? null : CloneOleObject(shape.OleObject);

        // Wave 25A: preserved modern-object package bytes belong to the editable duplicate.
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

    private static MediaInfo CloneMedia(MediaInfo source)
    {
        var copy = new MediaInfo
        {
            IsVideo = source.IsVideo,
            PlaybackStartMode = source.PlaybackStartMode,
            Loop = source.Loop,
            Bytes = source.Bytes.ToArray(),
            ContentType = source.ContentType,
            SourcePackagePath = source.SourcePackagePath,
            LinkUrl = source.LinkUrl,
        };

        foreach (var track in source.CaptionTracks)
        {
            copy.CaptionTracks.Add(new MediaCaptionTrackInfo
            {
                RelationshipId = track.RelationshipId,
                Source = track.Source,
                Bytes = track.Bytes.ToArray(),
                ContentType = track.ContentType,
                Language = track.Language,
                Label = track.Label,
                IsExternal = track.IsExternal,
            });
        }

        return copy;
    }

    private static ChartShape CloneChart(ChartShape src)
    {
        var copy = new ChartShape
        {
            ChartType    = src.ChartType,
            OfPieType = src.OfPieType,
            OfPieSplitType = src.OfPieSplitType,
            OfPieSplitPosition = src.OfPieSplitPosition,
            OfPieSecondPieSizePercent = src.OfPieSecondPieSizePercent,
            OfPieCustomPointIndices = new(src.OfPieCustomPointIndices),
            OfPieSeriesLinesSpecified = src.OfPieSeriesLinesSpecified,
            SeriesLinesSpecified = src.SeriesLinesSpecified,
            SeriesLineStyle = CloneChartLineStyle(src.SeriesLineStyle),
            LeaderLinesSpecified = src.LeaderLinesSpecified,
            HasHighLowLines = src.HasHighLowLines,
            ShowDropLines = src.ShowDropLines,
            ShowUpDownBars = src.ShowUpDownBars,
            ShowWaterfallConnectorLines = src.ShowWaterfallConnectorLines,
            UpDownBarGapWidthPercent = src.UpDownBarGapWidthPercent,
            UpBarFill = src.UpBarFill,
            DownBarFill = src.DownBarFill,
            StyleId      = src.StyleId,
            Title        = src.Title,
            TitleOverlay = src.TitleOverlay,
            TitleStyle   = CloneChartTextStyle(src.TitleStyle),
            ChartAreaFill = src.ChartAreaFill,
            ChartAreaOutline = src.ChartAreaOutline,
            HasAutomaticTitle = src.HasAutomaticTitle,
            TextStyle    = CloneChartTextStyle(src.TextStyle),
            Legend       = src.Legend,
            PlotAreaManualLayout = CloneChartManualLayout(src.PlotAreaManualLayout),
            PlotAreaFill = src.PlotAreaFill,
            PlotAreaOutline = src.PlotAreaOutline,
            LegendManualLayout = CloneChartManualLayout(src.LegendManualLayout),
            LegendOverlay = src.LegendOverlay,
            LegendTextStyle = CloneChartTextStyle(src.LegendTextStyle),
            VaryColors = src.VaryColors,
            CategoryAxis = CloneChartAxis(src.CategoryAxis),
            ValueAxis    = CloneChartAxis(src.ValueAxis),
            DataTable    = src.DataTable is null ? null : CloneChartDataTableSettings(src.DataTable),
            DisplayBlanksAs = src.DisplayBlanksAs,
            PlotVisibleOnly = src.PlotVisibleOnly,
            RoundedCorners = src.RoundedCorners,
            ShowDataLabelsOverMaximum = src.ShowDataLabelsOverMaximum,
            BarGapWidthPercent = src.BarGapWidthPercent,
            BarOverlapPercent = src.BarOverlapPercent,
            BarGapDepthPercent = src.BarGapDepthPercent,
            ThreeDStyle = src.ThreeDStyle,
            View3D = src.View3D is null ? null : new Chart3DView
            {
                RotationX = src.View3D.RotationX,
                RotationY = src.View3D.RotationY,
                RightAngleAxes = src.View3D.RightAngleAxes,
                Perspective = src.View3D.Perspective,
                HeightPercent = src.View3D.HeightPercent,
                DepthPercent = src.View3D.DepthPercent,
            },
            Wireframe = src.Wireframe,
            WireframeSpecified = src.WireframeSpecified,
            DoughnutHolePercent = src.DoughnutHolePercent,
            FirstSliceAngleDegrees = src.FirstSliceAngleDegrees,
            ScatterStyle = src.ScatterStyle,
            BubbleScalePercent = src.BubbleScalePercent,
            BubbleSizeRepresents = src.BubbleSizeRepresents,
            ShowNegativeBubbles = src.ShowNegativeBubbles,
            RadarStyle = src.RadarStyle,
            DataLabels = CloneChartDataLabels(src.DataLabels),
            SecondaryValueAxis = src.SecondaryValueAxis is null ? null : CloneChartAxis(src.SecondaryValueAxis),
            RegenerateWorkbookOnSave = src.RegenerateWorkbookOnSave,
            SourcePartPath = src.SourcePartPath,
            ChartDate1904 = src.ChartDate1904,
            ChartLanguage = src.ChartLanguage,
            PreservedPivotSourceXml = src.PreservedPivotSourceXml,
            PreservedChartProtectionXml = src.PreservedChartProtectionXml,
            ChartObjectProtected = src.ChartObjectProtected,
            ChartDataProtected = src.ChartDataProtected,
            ChartFormattingProtected = src.ChartFormattingProtected,
            ChartSelectionProtected = src.ChartSelectionProtected,
            PreservedChartSpaceExtensionsXml = src.PreservedChartSpaceExtensionsXml,
        };

        foreach (var c in src.Categories)
            copy.Categories.Add(c);

        foreach (var s in src.Series)
        {
            var sc = new ChartSeries
            {
                Name              = s.Name,
                FillColor         = s.FillColor,
                Fill              = s.Fill,
                LineStyle         = CloneChartLineStyle(s.LineStyle),
                MarkerStyle       = CloneChartMarkerStyle(s.MarkerStyle),
                SmoothLine        = s.SmoothLine,
                OnSecondaryAxis   = s.OnSecondaryAxis,
                DataLabels        = CloneChartDataLabels(s.DataLabels),
                ErrorBars         = CloneChartErrorBars(s.ErrorBars),
                Trendline         = CloneChartTrendline(s.Trendline),
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
            sc.FormulaReferences.SeriesName = s.FormulaReferences.SeriesName;
            sc.FormulaReferences.Category = s.FormulaReferences.Category;
            sc.FormulaReferences.Values = s.FormulaReferences.Values;
            sc.FormulaReferences.XValues = s.FormulaReferences.XValues;
            sc.FormulaReferences.YValues = s.FormulaReferences.YValues;
            sc.FormulaReferences.BubbleSizes = s.FormulaReferences.BubbleSizes;
            copy.Series.Add(sc);
        }

        return copy;
    }

    private static ChartManualLayout? CloneChartManualLayout(ChartManualLayout? source) =>
        source is null
            ? null
            : new ChartManualLayout
            {
                LayoutTarget = source.LayoutTarget,
                XMode = source.XMode,
                YMode = source.YMode,
                WidthMode = source.WidthMode,
                HeightMode = source.HeightMode,
                RawXModeToken = source.RawXModeToken,
                RawYModeToken = source.RawYModeToken,
                RawWidthModeToken = source.RawWidthModeToken,
                RawHeightModeToken = source.RawHeightModeToken,
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height,
            };

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

    private static ChartDataLabels? CloneChartDataLabels(ChartDataLabels? labels) =>
        labels is null
            ? null
            : new ChartDataLabels
            {
                Delete = labels.Delete,
                ShowValue = labels.ShowValue,
                ShowPercent = labels.ShowPercent,
                ShowCategoryName = labels.ShowCategoryName,
                ShowSeriesName = labels.ShowSeriesName,
                ShowLegendKey = labels.ShowLegendKey,
                ShowBubbleSize = labels.ShowBubbleSize,
                ShowLeaderLines = labels.ShowLeaderLines,
                Position = labels.Position,
                NumberFormat = labels.NumberFormat,
                Separator = labels.Separator,
                TextStyle = CloneChartTextStyle(labels.TextStyle),
            };

    private static ChartErrorBars? CloneChartErrorBars(ChartErrorBars? bars) => bars is null
        ? null
        : new ChartErrorBars
        {
            Direction = bars.Direction,
            BarType = bars.BarType,
            ValueType = bars.ValueType,
            Value = bars.Value,
            NoEndCap = bars.NoEndCap,
        };

    private static ChartTrendline? CloneChartTrendline(ChartTrendline? trendline) => trendline is null
        ? null
        : new ChartTrendline
        {
            Type = trendline.Type,
            PolynomialOrder = trendline.PolynomialOrder,
            MovingAveragePeriod = trendline.MovingAveragePeriod,
            Forward = trendline.Forward,
            Backward = trendline.Backward,
            DisplayEquation = trendline.DisplayEquation,
            DisplayRSquared = trendline.DisplayRSquared,
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
                Fill          = style.Fill,
                StrokeColor   = style.StrokeColor,
                StrokeWidthPt = style.StrokeWidthPt,
                NoFill        = style.NoFill,
                NoStroke      = style.NoStroke,
            };

    private static ChartPointStyle CloneChartPointStyle(ChartPointStyle style) =>
        new()
        {
            DataLabels    = CloneChartDataLabels(style.DataLabels),
            FillColor     = style.FillColor,
            Fill          = style.Fill,
            StrokeColor   = style.StrokeColor,
            StrokeWidthPt = style.StrokeWidthPt,
            ExplosionPercent = style.ExplosionPercent,
            Marker        = CloneChartMarkerStyle(style.Marker),
        };

    private static ShapeOutline? CloneShapeOutline(ShapeOutline? outline) => outline;

    private static ChartAxis CloneChartAxis(ChartAxis a) => new()
    {
        Title             = a.Title,
        TitleStyle        = CloneChartTextStyle(a.TitleStyle),
        NumberFormatCode  = a.NumberFormatCode,
        NumberFormatSourceLinked = a.NumberFormatSourceLinked,
        DisplayUnit       = a.DisplayUnit,
        RawDisplayUnitToken = a.RawDisplayUnitToken,
        Min               = a.Min,
        Max               = a.Max,
        MajorUnit         = a.MajorUnit,
        MinorUnit         = a.MinorUnit,
        HasMajorGridlines = a.HasMajorGridlines,
        HasMinorGridlines = a.HasMinorGridlines,
        MajorTickMark     = a.MajorTickMark,
        RawMajorTickMarkToken = a.RawMajorTickMarkToken,
        MinorTickMark     = a.MinorTickMark,
        RawMinorTickMarkToken = a.RawMinorTickMarkToken,
        TickLabelPosition = a.TickLabelPosition,
        RawTickLabelPositionToken = a.RawTickLabelPositionToken,
        LabelOffsetPercent = a.LabelOffsetPercent,
        NoMultiLevelLabels = a.NoMultiLevelLabels,
        CrossBetween      = a.CrossBetween,
        RawCrossBetweenToken = a.RawCrossBetweenToken,
        AutoCrossing      = a.AutoCrossing,
        LabelAlignment    = a.LabelAlignment,
        RawLabelAlignmentToken = a.RawLabelAlignmentToken,
        Crosses           = a.Crosses,
        RawCrossesToken   = a.RawCrossesToken,
        CrossesAt         = a.CrossesAt,
        ReverseOrder      = a.ReverseOrder,
        Delete            = a.Delete,
    };

    /// <summary>Returns a fully independent deep copy of a SmartArt payload.</summary>
    public static SmartArtShape CloneSmartArt(SmartArtShape source)
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
                Bytes = kv.Value.Bytes.ToArray(),
            };
        }

        foreach (var kv in source.PartRels)
            copy.PartRels[kv.Key] = kv.Value.ToArray();

        return copy;
    }

    /// <summary>
    /// Replaces the mutable contents of <paramref name="target"/> with a deep copy of
    /// <paramref name="source"/> while preserving the target object identity. This matters to
    /// host controls that keep a reference to the selected SmartArt payload while an undoable
    /// edit is applied.
    /// </summary>
    public static void CopySmartArt(SmartArtShape target, SmartArtShape source)
    {
        var copy = CloneSmartArt(source);
        target.Data = copy.Data;
        target.QuickStyle = copy.QuickStyle;
        target.Colors = copy.Colors;
        target.DrawingPartPath = copy.DrawingPartPath;

        target.FallbackShapes.Clear();
        target.FallbackShapes.AddRange(copy.FallbackShapes);

        target.DiagramRelIds.Clear();
        foreach (var pair in copy.DiagramRelIds)
            target.DiagramRelIds[pair.Key] = pair.Value;

        target.Parts.Clear();
        foreach (var pair in copy.Parts)
            target.Parts[pair.Key] = pair.Value;

        target.PartRels.Clear();
        foreach (var pair in copy.PartRels)
            target.PartRels[pair.Key] = pair.Value;
    }

    private static SmartArtData CloneSmartArtData(SmartArtData source)
    {
        var copy = new SmartArtData
        {
            Family = source.Family,
            LayoutUniqueId = source.LayoutUniqueId,
            IsLiveLayoutSupported = source.IsLiveLayoutSupported,
            UsesGroupedListBands = source.UsesGroupedListBands,
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
            Picture = CloneImagePart(source.Picture),
        };

        foreach (var child in source.Children)
            copy.Children.Add(CloneSmartArtNode(child));

        return copy;
    }

    private static ImagePart? CloneImagePart(ImagePart? source) =>
        source is null
            ? null
            : new ImagePart
            {
                Bytes = source.Bytes.ToArray(),
                ContentType = source.ContentType,
            };

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
        foreach (var label in source.StyleLabelMetadata)
        {
            copy.StyleLabelMetadata.Add(new SmartArtQuickStyleLabelMetadata
            {
                Name = label.Name,
                LineReferenceIndex = label.LineReferenceIndex,
                FillReferenceIndex = label.FillReferenceIndex,
                EffectReferenceIndex = label.EffectReferenceIndex,
                FontReferenceIndex = label.FontReferenceIndex,
            });
        }

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
        SplitOrientation = t.SplitOrientation,
        DurationMs      = t.DurationMs,
        AdvanceOnClick  = t.AdvanceOnClick,
        AdvanceAfterMs  = t.AdvanceAfterMs,
        RawXml          = t.RawXml,
        MorphOption     = t.MorphOption,
        WheelSpokeCount = t.WheelSpokeCount,
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
            RepeatCount   = a.RepeatCount,
            RepeatIndefinitely = a.RepeatIndefinitely,
            AutoReverse   = a.AutoReverse,
            Direction     = a.Direction,
            WheelSpokeCount = a.WheelSpokeCount,
            EffectSubtype = a.EffectSubtype,
            ScaleBehavior = a.ScaleBehavior?.Clone(),
            TriggerShapeId = a.TriggerShapeId,
            RawPresetClass = a.RawPresetClass,
            RawPresetId = a.RawPresetId,
            RawPresetSubtype = a.RawPresetSubtype,
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

    // Theme 21: OLE cloner — copy the package payload for duplicate/undo isolation.
    private static OleObjectInfo CloneOleObject(OleObjectInfo src) => new()
    {
        EmbeddedBytes        = src.EmbeddedBytes.ToArray(),
        EmbeddedContentType  = src.EmbeddedContentType,
        ProgId               = src.ProgId,
        RelType              = src.RelType,
        OleObjXml            = src.OleObjXml,
        WasAlternateContent  = src.WasAlternateContent,
        EmbeddedExtension    = src.EmbeddedExtension,
    };

    // Wave 25A: PreservedObject cloner — copy all referenced package bytes.
    private static PreservedObjectInfo ClonePreservedObject(PreservedObjectInfo src)
    {
        var copy = new PreservedObjectInfo
        {
            ObjectKind          = src.ObjectKind,
            ZoomTargetSlideNumericId = src.ZoomTargetSlideNumericId,
            ZoomTargetSectionId  = src.ZoomTargetSectionId,
            ZoomProperties       = src.ZoomProperties,
            RawXml              = src.RawXml,
            AlternateContentFallbackXml = src.AlternateContentFallbackXml,
            WasAlternateContent = src.WasAlternateContent,
            McRequiresToken     = src.McRequiresToken,
            McRequiresNsUri     = src.McRequiresNsUri,
        };
        foreach (var kv in src.Parts)
            copy.Parts[kv.Key] = kv.Value.ToArray();
        foreach (var kv in src.PartContentTypes)
            copy.PartContentTypes[kv.Key] = kv.Value;
        foreach (var kv in src.PartRels)
            copy.PartRels[kv.Key] = kv.Value.ToArray();
        foreach (var kv in src.SlideRels)
            copy.SlideRels[kv.Key] = kv.Value;
        foreach (var kv in src.McRequiresNsUris)
            copy.McRequiresNsUris[kv.Key] = kv.Value;
        copy.SummaryZoomTargets.AddRange(src.SummaryZoomTargets);
        return copy;
    }
}
