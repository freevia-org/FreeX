namespace FreeW.Core.Model;

/// <summary>Controls whether tracked-change metadata survives a document-model clone.</summary>
public enum RevisionClonePolicy
{
    Preserve,
    Strip
}

/// <summary>
/// Deep-clones document body models and their nested content without aliasing mutable payloads.
/// Callers must state whether existing revision metadata belongs in the cloned graph.
/// </summary>
public static class DocumentModelCloner
{
    public static IReadOnlyList<Block> CloneBlocks(TextDocument source, RevisionClonePolicy revisionPolicy)
    {
        ArgumentNullException.ThrowIfNull(source);
        var preserveRevisions = ShouldPreserveRevisions(revisionPolicy);
        var clones = new List<Block>(source.Blocks.Count);
        foreach (var block in source.Blocks)
            clones.Add(CloneBlockCore(block, preserveRevisions));
        return clones;
    }

    public static Block CloneBlock(Block source, RevisionClonePolicy revisionPolicy)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CloneBlockCore(source, ShouldPreserveRevisions(revisionPolicy));
    }

    public static Paragraph CloneParagraph(Paragraph source, RevisionClonePolicy revisionPolicy)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CloneParagraphCore(source, ShouldPreserveRevisions(revisionPolicy));
    }

    /// <summary>
    /// Deep-clones a paragraph text range while retaining paragraph metadata and run payloads. By default,
    /// only the normalized range is returned. Set <paramref name="preserveUnselectedText"/> to keep the
    /// complete paragraph and apply <paramref name="selectedFormatting"/> only to the selected fragments.
    /// </summary>
    public static Paragraph CloneParagraphTextRange(
        Paragraph source,
        int start,
        int end,
        RevisionClonePolicy revisionPolicy,
        Func<RunFormatting, RunFormatting>? selectedFormatting = null,
        bool preserveUnselectedText = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var preserveRevisions = ShouldPreserveRevisions(revisionPolicy);
        var clone = CloneParagraphCore(source, preserveRevisions);
        var bookmarkPositions = BookmarkBoundaryMapper.Capture(source);
        clone.Runs.Clear();

        var lo = Math.Clamp(Math.Min(start, end), 0, source.PlainText.Length);
        var hi = Math.Clamp(Math.Max(start, end), lo, source.PlainText.Length);
        var position = 0;
        foreach (var sourceRun in source.Runs)
        {
            var length = sourceRun.Text.Length;
            var runStart = position;
            var runEnd = runStart + length;
            position = runEnd;

            if (preserveUnselectedText)
            {
                if (length == 0 || runEnd <= lo || runStart >= hi)
                {
                    clone.Runs.Add(CloneRunWithTextCore(sourceRun, sourceRun.Text, preserveRevisions));
                    continue;
                }

                var selectedStart = Math.Max(lo, runStart);
                var selectedEnd = Math.Min(hi, runEnd);
                if (selectedStart > runStart)
                {
                    clone.Runs.Add(CloneRunWithTextCore(
                        sourceRun,
                        sourceRun.Text[..(selectedStart - runStart)],
                        preserveRevisions));
                }

                var selected = CloneRunWithTextCore(
                    sourceRun,
                    sourceRun.Text[(selectedStart - runStart)..(selectedEnd - runStart)],
                    preserveRevisions);
                if (selectedFormatting is not null)
                    selected.Formatting = selectedFormatting(sourceRun.Formatting);
                clone.Runs.Add(selected);

                if (selectedEnd < runEnd)
                {
                    clone.Runs.Add(CloneRunWithTextCore(
                        sourceRun,
                        sourceRun.Text[(selectedEnd - runStart)..],
                        preserveRevisions));
                }
                continue;
            }

            if (length == 0)
            {
                if (runStart >= lo && (runStart < hi || runStart == hi && hi == source.PlainText.Length))
                    clone.Runs.Add(CloneRunWithTextCore(sourceRun, sourceRun.Text, preserveRevisions));
                continue;
            }

            var overlapStart = Math.Max(lo, runStart);
            var overlapEnd = Math.Min(hi, runEnd);
            if (overlapEnd <= overlapStart)
                continue;

            var fragment = CloneRunWithTextCore(
                sourceRun,
                sourceRun.Text[(overlapStart - runStart)..(overlapEnd - runStart)],
                preserveRevisions);
            if (selectedFormatting is not null)
                fragment.Formatting = selectedFormatting(sourceRun.Formatting);
            clone.Runs.Add(fragment);
        }

        if (clone.Runs.Count == 0)
        {
            clone.Runs.Add(new Run(
                string.Empty,
                source.Runs.FirstOrDefault()?.Formatting ?? RunFormatting.Default));
        }

        clone.BookmarkBoundaries.Clear();
        BookmarkBoundaryMapper.Restore(
            clone,
            bookmarkPositions,
            preserveUnselectedText
                ? null
                : offset => Math.Clamp(offset - lo, 0, hi - lo));
        return clone;
    }

    public static Run CloneRun(Run source, RevisionClonePolicy revisionPolicy)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CloneRunCore(source, ShouldPreserveRevisions(revisionPolicy));
    }

    /// <summary>Deep-clones a footnote's content paragraphs.</summary>
    public static Footnote CloneFootnote(Footnote source, RevisionClonePolicy revisionPolicy)
    {
        ArgumentNullException.ThrowIfNull(source);
        var preserveRevisions = ShouldPreserveRevisions(revisionPolicy);
        var clone = new Footnote(source.Id) { HasAutomaticReferenceMark = source.HasAutomaticReferenceMark };
        foreach (var paragraph in source.Content)
            clone.Content.Add(CloneParagraphCore(paragraph, preserveRevisions));
        return clone;
    }

    /// <summary>Deep-clones an endnote's content paragraphs.</summary>
    public static Endnote CloneEndnote(Endnote source, RevisionClonePolicy revisionPolicy)
    {
        ArgumentNullException.ThrowIfNull(source);
        var preserveRevisions = ShouldPreserveRevisions(revisionPolicy);
        var clone = new Endnote(source.Id) { HasAutomaticReferenceMark = source.HasAutomaticReferenceMark };
        foreach (var paragraph in source.Content)
            clone.Content.Add(CloneParagraphCore(paragraph, preserveRevisions));
        return clone;
    }

    /// <summary>Deep-clones a header/footer's content paragraphs, or returns null when <paramref name="source"/> is null.</summary>
    public static HeaderFooter? CloneHeaderFooter(HeaderFooter? source, RevisionClonePolicy revisionPolicy) =>
        source is null ? null : CloneHeaderFooter(source, ShouldPreserveRevisions(revisionPolicy));

    /// <summary>Deep-clones a section's default/even/first header and footer slots.</summary>
    public static SectionHeadersFooters CloneSectionHeadersFooters(SectionHeadersFooters source, RevisionClonePolicy revisionPolicy)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CloneSectionHeadersFooters(source, ShouldPreserveRevisions(revisionPolicy));
    }

    private static bool ShouldPreserveRevisions(RevisionClonePolicy revisionPolicy) => revisionPolicy switch
    {
        RevisionClonePolicy.Preserve => true,
        RevisionClonePolicy.Strip => false,
        _ => throw new ArgumentOutOfRangeException(nameof(revisionPolicy), revisionPolicy, null)
    };

    private static Block CloneBlockCore(Block source, bool preserveRevisions) => source switch
    {
        Paragraph paragraph => CloneParagraphCore(paragraph, preserveRevisions),
        Table table => CloneTable(table, preserveRevisions),
        AltChunkBlock altChunk => new AltChunkBlock(altChunk.PreservedPartName)
        {
            BlockContentControl = altChunk.BlockContentControl,
            BlockCustomXml = altChunk.BlockCustomXml
        },
        _ => source
    };

    private static Paragraph CloneParagraphCore(Paragraph source, bool preserveRevisions)
    {
        var clone = new Paragraph
        {
            BlockContentControl = source.BlockContentControl,
            BlockCustomXml = source.BlockCustomXml,
            Formatting = source.Formatting,
            StyleId = source.StyleId,
            SpanningFieldStart = source.SpanningFieldStart,
            SpanningFieldOwner = source.SpanningFieldOwner,
            EndsSpanningField = source.EndsSpanningField,
            DropCap = source.DropCap,
            SectionBreak = source.SectionBreak is { } section
                ? CloneSection(section, preserveRevisions)
                : null,
            PreservedNumbering = source.PreservedNumbering,
            ParagraphFormatRevision = preserveRevisions ? source.ParagraphFormatRevision : null,
            MarkRevision = preserveRevisions ? source.MarkRevision : RevisionKind.None,
            MarkRevisionAuthor = preserveRevisions ? source.MarkRevisionAuthor : null,
            MarkRevisionDateXml = preserveRevisions ? source.MarkRevisionDateXml : null
        };
        clone.BookmarkNames.AddRange(source.BookmarkNames);
        clone.BookmarkBoundaries.AddRange(source.BookmarkBoundaries);
        foreach (var run in source.Runs)
            clone.Runs.Add(CloneRunCore(run, preserveRevisions));
        return clone;
    }

    private static Run CloneRunCore(Run source, bool preserveRevisions) => new(source.Text, source.Formatting)
    {
        Image = source.Image?.Clone(),
        Equation = source.Equation?.Clone(),
        Shape = source.Shape is { } shape ? CloneShape(shape, preserveRevisions) : null,
        WordArt = source.WordArt?.Clone(),
        SmartArt = source.SmartArt is { } smartArt ? SmartArtCommandCopy.Clone(smartArt) : null,
        Chart = source.Chart?.Clone(),
        EmbeddedObject = source.EmbeddedObject?.Clone(),
        Ruby = source.Ruby?.Clone(),
        PreservedDrawing = source.PreservedDrawing?.Duplicate(),
        DrawingGroup = source.DrawingGroup is { } drawingGroup
            ? CloneDrawingGroup(drawingGroup, preserveRevisions)
            : null,
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        SubDocument = source.SubDocument,
        FieldKind = source.FieldKind,
        TableFormula = source.TableFormula,
        FootnoteId = source.FootnoteId,
        EndnoteId = source.EndnoteId,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        IsPageBreak = source.IsPageBreak,
        IsColumnBreak = source.IsColumnBreak,
        Revision = preserveRevisions ? source.Revision : RevisionKind.None,
        Control = source.Control,
        Citation = source.Citation,
        CrossReference = source.CrossReference,
        ComplexField = source.ComplexField,
        StyleId = source.StyleId,
        RevisionAuthor = preserveRevisions ? source.RevisionAuthor : null,
        RevisionDateXml = preserveRevisions ? source.RevisionDateXml : null,
        MoveRevisionId = preserveRevisions ? source.MoveRevisionId : null,
        FormatRevision = preserveRevisions ? source.FormatRevision : null
    };

    private static Run CloneRunWithTextCore(Run source, string text, bool preserveRevisions)
    {
        var clone = CloneRunCore(source, preserveRevisions);
        clone.Text = text;
        if (!string.Equals(text, source.Text, StringComparison.Ordinal))
            clone.Ruby = null;
        return clone;
    }

    private static Shape CloneShape(Shape source, bool preserveRevisions)
    {
        var clone = new Shape
        {
            Kind = source.Kind,
            WidthPt = source.WidthPt,
            HeightPt = source.HeightPt,
            FillColorHex = source.FillColorHex,
            OutlineColorHex = source.OutlineColorHex,
            OutlineWidthPt = source.OutlineWidthPt,
            OutlineDash = source.OutlineDash,
            AltText = source.AltText,
            TextDirection = source.TextDirection,
            Placement = source.Placement?.Clone(),
            ExtendedFill = source.ExtendedFill is { } fill ? CloneShapeFill(fill) : null,
            Effects = source.Effects is { } effects ? CloneShapeEffects(effects) : null,
            CustomGeometry = source.CustomGeometry is { } geometry ? CloneCustomGeometry(geometry) : null,
            RotationAngle = source.RotationAngle,
            FlipH = source.FlipH,
            FlipV = source.FlipV
        };
        foreach (var paragraph in source.TextParagraphs)
            clone.TextParagraphs.Add(CloneParagraphCore(paragraph, preserveRevisions));
        return clone;
    }

    private static ShapeFill CloneShapeFill(ShapeFill source)
    {
        var clone = new ShapeFill
        {
            Kind = source.Kind,
            GradientAngle = source.GradientAngle,
            PatternPreset = source.PatternPreset,
            PatternFgColorHex = source.PatternFgColorHex,
            PatternBgColorHex = source.PatternBgColorHex
        };
        clone.GradientStops.AddRange(source.GradientStops);
        return clone;
    }

    private static ShapeEffectLst CloneShapeEffects(ShapeEffectLst source) => new()
    {
        HasShadow = source.HasShadow,
        ShadowBlurRad = source.ShadowBlurRad,
        ShadowDist = source.ShadowDist,
        ShadowDir = source.ShadowDir,
        ShadowColorHex = source.ShadowColorHex,
        ShadowAlpha = source.ShadowAlpha,
        HasGlow = source.HasGlow,
        GlowRad = source.GlowRad,
        GlowColorHex = source.GlowColorHex,
        GlowAlpha = source.GlowAlpha,
        HasSoftEdge = source.HasSoftEdge,
        SoftEdgeRad = source.SoftEdgeRad,
        HasReflection = source.HasReflection,
        ReflectionBlurRad = source.ReflectionBlurRad,
        ReflectionStartAlpha = source.ReflectionStartAlpha,
        ReflectionStartPosition = source.ReflectionStartPosition,
        ReflectionEndAlpha = source.ReflectionEndAlpha,
        ReflectionEndPosition = source.ReflectionEndPosition,
        ReflectionDir = source.ReflectionDir,
        ReflectionFadeDir = source.ReflectionFadeDir,
        ReflectionScaleX = source.ReflectionScaleX,
        ReflectionScaleY = source.ReflectionScaleY,
        ReflectionSkewX = source.ReflectionSkewX,
        ReflectionSkewY = source.ReflectionSkewY,
        ReflectionAlignment = source.ReflectionAlignment,
        ReflectionRotWithShape = source.ReflectionRotWithShape,
        ReflectionDist = source.ReflectionDist,
        HasBevel = source.HasBevel,
        BevelW = source.BevelW,
        BevelH = source.BevelH,
        BevelPresetType = source.BevelPresetType
    };

    private static CustomGeometry CloneCustomGeometry(CustomGeometry source)
    {
        var clone = new CustomGeometry { Width = source.Width, Height = source.Height };
        clone.Segments.AddRange(source.Segments);
        return clone;
    }

    private static DrawingGroup CloneDrawingGroup(DrawingGroup source, bool preserveRevisions)
    {
        var clone = new DrawingGroup
        {
            Placement = source.Placement.Clone(),
            WidthPt = source.WidthPt,
            HeightPt = source.HeightPt,
            RotationAngle = source.RotationAngle,
            FlipH = source.FlipH,
            FlipV = source.FlipV
        };
        foreach (var child in source.Children)
            clone.Children.Add(CloneDrawingGroupChild(child, preserveRevisions));
        clone.ChildOffsets.AddRange(source.ChildOffsets);
        return clone;
    }

    private static object CloneDrawingGroupChild(object source, bool preserveRevisions) => source switch
    {
        InlineImage image => image.Clone(),
        Shape shape => CloneShape(shape, preserveRevisions),
        Chart chart => chart.Clone(),
        SmartArt smartArt => SmartArtCommandCopy.Clone(smartArt),
        WordArt wordArt => wordArt.Clone(),
        DrawingGroup drawingGroup => CloneDrawingGroup(drawingGroup, preserveRevisions),
        _ => source
    };

    private static Table CloneTable(Table source, bool preserveRevisions)
    {
        var clone = new Table
        {
            BlockContentControl = source.BlockContentControl,
            BlockCustomXml = source.BlockCustomXml,
            Formatting = source.Formatting,
            TableStyleId = source.TableStyleId,
            Borders = source.Borders,
            PreferredWidthPt = source.PreferredWidthPt,
            Alignment = source.Alignment,
            IndentFromLeftPt = source.IndentFromLeftPt,
            FloatingPosition = source.FloatingPosition,
            FloatingTableAllowsOverlap = source.FloatingTableAllowsOverlap,
            DefaultCellMargins = source.DefaultCellMargins,
            CellSpacingPt = source.CellSpacingPt,
            AutoFit = source.AutoFit
        };
        clone.ColumnWidthsPt.AddRange(source.ColumnWidthsPt);
        foreach (var row in source.Rows)
        {
            var rowClone = new TableRow
            {
                HeightPt = row.HeightPt,
                HeightRule = row.HeightRule,
                AllowBreakAcrossPages = row.AllowBreakAcrossPages,
                RowRevision = preserveRevisions ? row.RowRevision : RevisionKind.None,
                RowRevisionAuthor = preserveRevisions ? row.RowRevisionAuthor : null,
                RowRevisionDateXml = preserveRevisions ? row.RowRevisionDateXml : null
            };
            foreach (var cell in row.Cells)
                rowClone.Cells.Add(CloneCell(cell, preserveRevisions));
            clone.Rows.Add(rowClone);
        }
        return clone;
    }

    private static TableCell CloneCell(TableCell source, bool preserveRevisions)
    {
        var clone = new TableCell
        {
            ShadingColorHex = source.ShadingColorHex,
            WidthPt = source.WidthPt,
            GridSpan = source.GridSpan,
            VerticalMerge = source.VerticalMerge,
            VerticalAlignment = source.VerticalAlignment,
            Margins = source.Margins,
            Borders = source.Borders,
            TextDirection = source.TextDirection,
            WrapText = source.WrapText,
            FitText = source.FitText
        };
        foreach (var paragraph in source.Paragraphs)
            clone.Paragraphs.Add(CloneParagraphCore(paragraph, preserveRevisions));
        foreach (var nestedTable in source.NestedTables)
            clone.NestedTables.Add(CloneTable(nestedTable, preserveRevisions));
        return clone;
    }

    private static Section CloneSection(Section source, bool preserveRevisions) =>
        new(source.Page.Clone(), source.BreakKind)
        {
            HeadersFooters = CloneSectionHeadersFooters(source.HeadersFooters, preserveRevisions)
        };

    private static SectionHeadersFooters CloneSectionHeadersFooters(
        SectionHeadersFooters source,
        bool preserveRevisions) => new()
    {
        Header = CloneHeaderFooter(source.Header, preserveRevisions),
        Footer = CloneHeaderFooter(source.Footer, preserveRevisions),
        EvenHeader = CloneHeaderFooter(source.EvenHeader, preserveRevisions),
        EvenFooter = CloneHeaderFooter(source.EvenFooter, preserveRevisions),
        FirstHeader = CloneHeaderFooter(source.FirstHeader, preserveRevisions),
        FirstFooter = CloneHeaderFooter(source.FirstFooter, preserveRevisions)
    };

    private static HeaderFooter? CloneHeaderFooter(HeaderFooter? source, bool preserveRevisions)
    {
        if (source is null)
            return null;

        var clone = new HeaderFooter();
        foreach (var paragraph in source.Paragraphs)
            clone.Paragraphs.Add(CloneParagraphCore(paragraph, preserveRevisions));
        return clone;
    }
}
