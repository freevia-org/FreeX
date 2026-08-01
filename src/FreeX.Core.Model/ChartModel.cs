namespace FreeX.Core.Model;

/// <summary>Lightweight chart definition stored on a Sheet.</summary>
public sealed class ChartModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; set; }

    /// <summary>
    /// R80-app-accessibility-a11y-5-1: the chart's "Alt Text" title, from the drawing anchor's
    /// &lt;xdr:cNvPr title="..."/&gt; -- distinct from <see cref="Title"/> (the chart's own visible
    /// &lt;c:title&gt; text). Mirrors PictureModel/DrawingShapeModel/TextBoxModel's existing
    /// Title/AltText pair so the Accessibility Checker can inspect a chart's real alt text instead
    /// of using the chart title as a false proxy.
    /// </summary>
    public string? AltTextTitle { get; set; }

    /// <summary>
    /// R80-app-accessibility-a11y-5-1: the chart's "Alt Text" description, from the drawing anchor's
    /// &lt;xdr:cNvPr descr="..."/&gt;. Without this, a chart's real alt text set in Excel (View Alt
    /// Text) was silently dropped on open+save round-trip through FreeX.
    /// </summary>
    public string? AltTextDescription { get; set; }

    /// <summary>
    /// R98-io-chart-hyperlink-model-field: the chart's OBJECT-level hyperlink (an
    /// <c>&lt;a:hlinkClick&gt;</c> on the chart graphicFrame's <c>xdr:cNvPr</c>), populated on load and
    /// carried through clone/paste/move (<see cref="DuplicateSheetDrawingCloner.CloneChart"/>,
    /// <c>MoveChartCommand</c>/<c>MoveChartToNewSheetCommand</c> -- which relocate this SAME
    /// <see cref="ChartModel"/> instance, so the field simply travels with it) -- mirrors
    /// <see cref="DrawingShapeModel.Hyperlink"/>/<see cref="TextBoxModel.Hyperlink"/>/
    /// <see cref="PictureModel.Hyperlink"/> (R97-model-drawing-hyperlink-2-2).
    /// <para>
    /// Before this field existed, <c>XlsxWorksheetChartWriter</c> re-attached a chart's hyperlink at
    /// SAVE time purely by re-reading the TRUE source package, keyed by (current host sheet name -&gt;
    /// chart <c>cNvPr@name</c>) -- see <c>XlsxFileAdapter.GetSourceChartHyperlinksBySheet</c>. That
    /// lookup silently DROPPED the hyperlink when a chart moved to a different sheet (the destination
    /// sheet's OWN original dictionary never contained it), or MISATTRIBUTED a different chart's
    /// hyperlink when the destination sheet happened to already have a chart with the same
    /// auto-generated name (e.g. two sheets each with their own "Chart 1"). Populating this field at
    /// load time -- per chart, from that chart's OWN graphicFrame, not a sheet-name-keyed guess -- and
    /// preferring it at save fixes both: the field is tied to the chart OBJECT's identity and simply
    /// travels wherever the object goes.
    /// </para>
    /// <para>
    /// The chart TITLE's own hyperlink (an <c>a:hlinkClick</c> on the title's first run's <c>a:rPr</c>)
    /// deliberately stays on the OLD source-package-keyed mechanism (<c>ChartHyperlinkPair.TitleHyperlink</c>)
    /// rather than getting a matching model field -- see the R41-io-hyperlink-drawing-rels-3-2 doc
    /// comment on <c>XlsxChartXmlWriter.ApplyVerbatimTitleHyperlink</c>: unlike this object-level
    /// hyperlink (which lives on a graphicFrame the writer always rebuilds structurally the same way),
    /// the title is written purely from <see cref="Title"/> as a plain string with no rich-run model at
    /// all, so there is nowhere on <see cref="ChartModel"/> to hang a title-run hyperlink without
    /// inventing new rich-text infrastructure the title pipeline doesn't otherwise have. This is a
    /// pre-existing, deliberate R41 tradeoff, not new to R98; the move/duplicate misattribution finding
    /// this field fixes remains a known residual gap for the title hyperlink specifically.
    /// </para>
    /// </summary>
    public DrawingObjectHyperlink? Hyperlink { get; set; }

    public ChartType Type { get; set; } = ChartType.Column;
    public GridRange DataRange { get; set; }
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// R111-model-drawing-object-lock-1-1: whether this chart is locked against move/resize while its
    /// sheet is protected with the "Edit objects" permission blocked -- mirrors
    /// <see cref="DrawingShapeModel.Locked"/> (matching OOXML
    /// <c>&lt;a:graphicFrameLocks noMove="1" noResize="1".../&gt;</c>). Defaults to <see langword="true"/>,
    /// matching Excel's default of a locked chart. When an author explicitly unlocks a chart (unchecks
    /// Format Chart Area &gt; Properties &gt; Locked) that one chart stays movable/resizable even while
    /// the sheet protection has "Edit objects" turned off, while other (default-locked) charts on the
    /// same protected sheet remain immovable.
    /// </summary>
    /// <remarks>
    /// Reading/writing the OOXML per-chart lock attribute (<c>a:graphicFrameLocks</c>) on load/save is
    /// deferred follow-up work, exactly like <see cref="DrawingShapeModel.Locked"/> -- this field is
    /// currently in-memory/session-only and defaults to locked, matching Excel's authored default when
    /// no lock override is present.
    /// </remarks>
    public bool Locked { get; set; } = true;

    public bool IsPivotChart { get; set; }
    public string? PivotSourceSheetName { get; set; }
    public string? PivotTableName { get; set; }
    public int? PivotSourceFormatId { get; set; }
    public int? PivotCacheId { get; set; }
    public string? PivotFormatsXml { get; set; }
    public bool ShowPivotChartFieldButtons { get; set; } = true;
    public bool ShowPivotChartReportFilterButtons { get; set; } = true;
    public bool ShowPivotChartAxisFieldButtons { get; set; } = true;
    public bool ShowPivotChartValueFieldButtons { get; set; } = true;
    public bool Uses1904DateSystem { get; set; }
    public string? Language { get; set; }
    public int? ChartStyleId { get; set; }
    public ChartColorMapOverrideModel? ColorMapOverride { get; set; }
    public ChartExternalDataModel? ExternalData { get; set; }
    public ChartUserShapesModel? UserShapes { get; set; }
    public ChartManualLayoutModel? PlotAreaLayout { get; set; }
    public ChartManualLayoutModel? LegendLayout { get; set; }
    public bool RoundedCorners { get; set; }
    public ChartBlankDisplayMode BlankDisplayMode { get; set; } = ChartBlankDisplayMode.Gap;
    public bool ShowDataLabelsOverMaximum { get; set; }
    public bool AutoTitleDeleted { get; set; }
    public bool ShowDataInHiddenRowsAndColumns { get; set; }
    public ChartProtectionModel? Protection { get; set; }
    public ChartPrintSettingsModel? PrintSettings { get; set; }
    public ChartDataTableModel? DataTable { get; set; }
    public Chart3DViewModel? ThreeDView { get; set; }
    public ChartSurfaceFormatModel? FloorFormat { get; set; }
    public ChartSurfaceFormatModel? SideWallFormat { get; set; }
    public ChartSurfaceFormatModel? BackWallFormat { get; set; }
    public int? BarGapWidth { get; set; }
    public int? BarOverlap { get; set; }
    public bool? VaryColorsByPoint { get; set; }
    public int BubbleScale { get; set; } = 100;
    public bool ShowNegativeBubbles { get; set; }
    public ChartBubbleSizeRepresents BubbleSizeRepresents { get; set; } = ChartBubbleSizeRepresents.Area;
    public StockChartSubtype StockSubtype { get; set; } = StockChartSubtype.HighLowClose;
    public bool FirstRowIsHeader { get; set; } = true;
    public bool FirstColIsCategories { get; set; } = true;

    /// <summary>
    /// Excel's "Switch Row/Column": when true, each ROW of <see cref="DataRange"/> is one data
    /// series (row-major) instead of the default one-series-per-column. The chart is interpreted
    /// as the transposed range, so <see cref="FirstRowIsHeader"/> and
    /// <see cref="FirstColIsCategories"/> keep their series-axis-relative meanings: with the flag
    /// set, series names come from the first COLUMN and category labels from the first ROW.
    /// <see cref="SeriesColumnMappings"/> is column-based and is ignored while this flag is set.
    /// </summary>
    public bool SeriesInRows { get; set; }
    public string? Title { get; set; }
    public ChartManualLayoutModel? TitleLayout { get; set; }
    public bool TitleOverlay { get; set; }
    public string? XAxisTitle { get; set; }
    public ChartManualLayoutModel? XAxisTitleLayout { get; set; }
    public string? YAxisTitle { get; set; }
    public ChartManualLayoutModel? YAxisTitleLayout { get; set; }

    /// <summary>
    /// Verbatim inner XML of the &lt;c:title&gt; element for the X axis.
    /// When set, written back exactly as-is to preserve axis title formatting
    /// that the model cannot represent (bold, italic, per-run colors, etc.).
    /// </summary>
    public string? XAxisTitleVerbatimXml { get; set; }

    /// <summary>
    /// Verbatim inner XML of the &lt;c:title&gt; element for the Y axis.
    /// When set, written back exactly as-is to preserve axis title formatting
    /// that the model cannot represent.
    /// </summary>
    public string? YAxisTitleVerbatimXml { get; set; }
    public bool HideXAxis { get; set; }
    public bool HideYAxis { get; set; }
    public ChartAxisPosition XAxisPosition { get; set; } = ChartAxisPosition.Bottom;
    public ChartAxisPosition YAxisPosition { get; set; } = ChartAxisPosition.Left;
    public CellColor? ChartDefaultTextColor { get; set; }
    public WorkbookThemeColorReference? ChartDefaultTextThemeColor { get; set; }
    public double ChartDefaultFontSize { get; set; } = 11;
    public CellColor? ChartTitleTextColor { get; set; }
    public WorkbookThemeColorReference? ChartTitleTextThemeColor { get; set; }
    public double ChartTitleFontSize { get; set; } = 16;
    public CellColor? AxisTitleTextColor { get; set; }
    public WorkbookThemeColorReference? AxisTitleTextThemeColor { get; set; }
    public double AxisTitleFontSize { get; set; } = 12;

    /// <summary>
    /// R43-io-chart-axis-title-numfmt-3-3: per-axis overrides for the X (category) axis title's
    /// font size/color/theme-color, distinct from the shared <see cref="AxisTitleFontSize"/>/
    /// <see cref="AxisTitleTextColor"/>/<see cref="AxisTitleTextThemeColor"/> fields (which a
    /// second read of the Y axis title formatting always clobbers). Null means "use the shared
    /// fields", preserving prior behavior for charts that never populate these overrides.
    /// </summary>
    public double? XAxisTitleFontSize { get; set; }
    public CellColor? XAxisTitleTextColor { get; set; }
    public WorkbookThemeColorReference? XAxisTitleTextThemeColor { get; set; }

    /// <summary>
    /// R71-io-chart-axis-4-3: the X (category) axis title's captured &lt;a:bodyPr&gt;@rot value
    /// (raw 60,000ths-of-a-degree units, not converted to degrees), read verbatim from a plain
    /// (single-run) axis title so a non-default rotation (e.g. rot="0" forcing a vertical axis's
    /// title horizontal) survives round-trip instead of always reverting to the writer's hardcoded
    /// default. Null means "no explicit rotation was captured" — the writer falls back to its
    /// existing vertical/horizontal default.
    /// </summary>
    public double? XAxisTitleRotation { get; set; }

    /// <summary>
    /// R43-io-chart-axis-title-numfmt-3-3: per-axis overrides for the Y (value) axis title's
    /// font size/color/theme-color. Also used as the fallback for the secondary value axis title,
    /// mirroring the existing "own captured value, else clone primary Y axis" pattern used for
    /// other secondary-axis fields in this model. Null means "use the shared fields".
    /// </summary>
    public double? YAxisTitleFontSize { get; set; }
    public CellColor? YAxisTitleTextColor { get; set; }
    public WorkbookThemeColorReference? YAxisTitleTextThemeColor { get; set; }

    /// <summary>See <see cref="XAxisTitleRotation"/>; same capture for the Y (value) axis title.</summary>
    public double? YAxisTitleRotation { get; set; }
    public CellColor? ChartAreaFillColor { get; set; }
    public WorkbookThemeColorReference? ChartAreaFillThemeColor { get; set; }
    public CellColor? ChartAreaBorderColor { get; set; }
    public WorkbookThemeColorReference? ChartAreaBorderThemeColor { get; set; }
    public double? ChartAreaBorderThickness { get; set; }

    /// <summary>
    /// R42-io-chart-plotarea-legend-3-1: true when the source file explicitly declared
    /// <c>&lt;a:noFill/&gt;</c> on the chart area's shape properties (the user picked "No Fill"),
    /// as distinct from simply not setting a fill at all. Takes priority over
    /// <see cref="ChartAreaFillColor"/>/<see cref="ChartAreaFillThemeColor"/> on write.
    /// </summary>
    public bool? ChartAreaNoFill { get; set; }

    /// <summary>
    /// R42-io-chart-plotarea-legend-3-1: true when the source file explicitly declared
    /// <c>&lt;a:ln&gt;&lt;a:noFill/&gt;&lt;/a:ln&gt;</c> on the chart area (the user picked "No
    /// Line"), as distinct from simply not setting a border. Takes priority over
    /// <see cref="ChartAreaBorderColor"/>/<see cref="ChartAreaBorderThemeColor"/>/
    /// <see cref="ChartAreaBorderThickness"/> on write.
    /// </summary>
    public bool? ChartAreaNoLine { get; set; }
    public CellColor? PlotAreaFillColor { get; set; }
    public WorkbookThemeColorReference? PlotAreaFillThemeColor { get; set; }
    public CellColor? PlotAreaBorderColor { get; set; }
    public WorkbookThemeColorReference? PlotAreaBorderThemeColor { get; set; }
    public double PlotAreaBorderThickness { get; set; } = 1;

    /// <summary>
    /// R42-io-chart-plotarea-legend-3-1: same as <see cref="ChartAreaNoFill"/> but for the plot
    /// area's own shape properties.
    /// </summary>
    public bool? PlotAreaNoFill { get; set; }

    /// <summary>
    /// R42-io-chart-plotarea-legend-3-1: same as <see cref="ChartAreaNoLine"/> but for the plot
    /// area's own shape properties.
    /// </summary>
    public bool? PlotAreaNoLine { get; set; }
    public CellColor? LegendTextColor { get; set; }
    public WorkbookThemeColorReference? LegendTextThemeColor { get; set; }
    public CellColor? LegendFillColor { get; set; }
    public WorkbookThemeColorReference? LegendFillThemeColor { get; set; }
    public CellColor? LegendBorderColor { get; set; }
    public WorkbookThemeColorReference? LegendBorderThemeColor { get; set; }
    public double LegendBorderThickness { get; set; }
    public double LegendFontSize { get; set; } = 12;

    /// <summary>
    /// R45-io-chart-datatable-legend-3-3: legend-wide Bold/Italic, read from the &lt;c:legend&gt;'s
    /// &lt;c:txPr&gt;&lt;a:defRPr b="1"/i="1"&gt; attributes. Null when the source file left the
    /// attribute unspecified (Excel's own default -- neither forced bold nor forced non-bold).
    /// </summary>
    public bool? LegendBold { get; set; }

    /// <summary>See <see cref="LegendBold"/>.</summary>
    public bool? LegendItalic { get; set; }
    public List<ChartLegendEntryModel> LegendEntries { get; set; } = [];
    public double DoughnutHoleSize { get; set; } = 0.55;
    public double FirstSliceAngle { get; set; }
    public int ExplodedSliceIndex { get; set; } = -1;
    public double ExplodedSliceDistance { get; set; } = 0.1;

    /// <summary>
    /// Per-data-point explosion overrides for pie/doughnut slices, read from
    /// <c>&lt;c:dPt&gt;</c> elements with an explicit <c>&lt;c:explosion&gt;</c> value greater
    /// than zero. Mirrors <see cref="PointFillColors"/> in shape. Populated for every exploded
    /// point (not just the first) so a chart where all slices are exploded round-trips without
    /// losing all but one. <see cref="ExplodedSliceIndex"/>/<see cref="ExplodedSliceDistance"/>
    /// remain the scalar single-explosion representation used by the pie-format UI commands;
    /// when this list is empty the writer falls back to that scalar pair.
    /// </summary>
    public List<ChartPointExplosion> ExplodedSlices { get; set; } = [];
    public double? XAxisMinimum { get; set; }
    public double? XAxisMaximum { get; set; }
    public double? XAxisMajorUnit { get; set; }
    public double? XAxisMinorUnit { get; set; }
    public bool XAxisLogScale { get; set; }
    public double? XAxisLogBase { get; set; }
    public bool XAxisReverseOrder { get; set; }
    public ChartDataLabelNumberFormat XAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
    public string? XAxisNumberFormatCode { get; set; }
    public bool? XAxisNumberFormatSourceLinked { get; set; }
    public bool ShowXAxisMajorGridlines { get; set; }
    public bool ShowXAxisMinorGridlines { get; set; }
    public bool XAxisIsDateAxis { get; set; }
    public CellColor? XAxisMajorGridlineColor { get; set; }
    public CellColor? XAxisMinorGridlineColor { get; set; }
    public double XAxisGridlineThickness { get; set; } = 1;
    public ChartAxisTickStyle XAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
    public ChartAxisTickStyle XAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
    public bool ShowXAxisLabels { get; set; } = true;
    public ChartAxisTickLabelPosition XAxisTickLabelPosition { get; set; } = ChartAxisTickLabelPosition.NextTo;
    public CellColor? XAxisLabelTextColor { get; set; }
    public WorkbookThemeColorReference? XAxisLabelTextThemeColor { get; set; }
    public double XAxisLabelFontSize { get; set; } = 11;
    public double XAxisLabelAngle { get; set; }
    public int XAxisLabelSkip { get; set; }
    public int XAxisTickMarkSkip { get; set; }
    public int XAxisLabelOffset { get; set; }
    public bool XAxisNoMultiLevelLabels { get; set; }
    public ChartAxisLabelAlignment XAxisLabelAlignment { get; set; } = ChartAxisLabelAlignment.Center;
    public ChartDateAxisUnit? XAxisBaseTimeUnit { get; set; }
    public ChartDateAxisUnit? XAxisMajorTimeUnit { get; set; }
    public ChartDateAxisUnit? XAxisMinorTimeUnit { get; set; }
    public CellColor? XAxisLineColor { get; set; }
    public double XAxisLineThickness { get; set; } = 1;
    public ChartAxisCrosses XAxisCrosses { get; set; } = ChartAxisCrosses.AutoZero;
    public double? XAxisCrossesAt { get; set; }
    public ChartAxisCrossBetween? XAxisCrossBetween { get; set; }
    public ChartAxisDisplayUnit? XAxisDisplayUnit { get; set; }
    public double? XAxisCustomDisplayUnit { get; set; }

    /// <summary>
    /// R36-io-chart-axis-scaling-2-3: whether Excel's "Show display units label on chart" checkbox
    /// (&lt;c:dispUnitsLbl/&gt; under &lt;c:dispUnits&gt;) was set for this axis's display unit. When
    /// true and a display unit is set, the on-chart caption (e.g. "Thousands") is round-tripped.
    /// </summary>
    public bool ShowXAxisDisplayUnitLabel { get; set; }
    public double? YAxisMinimum { get; set; }
    public double? YAxisMaximum { get; set; }
    public double? YAxisMajorUnit { get; set; }
    public double? YAxisMinorUnit { get; set; }
    public bool YAxisLogScale { get; set; }
    public double? YAxisLogBase { get; set; }
    public bool YAxisReverseOrder { get; set; }
    public ChartDataLabelNumberFormat YAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
    public string? YAxisNumberFormatCode { get; set; }
    public bool? YAxisNumberFormatSourceLinked { get; set; }
    public bool ShowYAxisMajorGridlines { get; set; }
    public bool ShowYAxisMinorGridlines { get; set; }
    public CellColor? YAxisMajorGridlineColor { get; set; }
    public CellColor? YAxisMinorGridlineColor { get; set; }
    public double YAxisGridlineThickness { get; set; } = 1;
    public ChartAxisTickStyle YAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
    public ChartAxisTickStyle YAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
    public bool ShowYAxisLabels { get; set; } = true;
    public ChartAxisTickLabelPosition YAxisTickLabelPosition { get; set; } = ChartAxisTickLabelPosition.NextTo;
    public CellColor? YAxisLabelTextColor { get; set; }
    public WorkbookThemeColorReference? YAxisLabelTextThemeColor { get; set; }
    public double YAxisLabelFontSize { get; set; } = 11;
    public double YAxisLabelAngle { get; set; }
    public CellColor? YAxisLineColor { get; set; }
    public double YAxisLineThickness { get; set; } = 1;
    public ChartAxisCrosses YAxisCrosses { get; set; } = ChartAxisCrosses.AutoZero;
    public double? YAxisCrossesAt { get; set; }
    public ChartAxisCrossBetween? YAxisCrossBetween { get; set; }
    public ChartAxisDisplayUnit? YAxisDisplayUnit { get; set; }
    public double? YAxisCustomDisplayUnit { get; set; }

    /// <summary>See <see cref="ShowXAxisDisplayUnitLabel"/>.</summary>
    public bool ShowYAxisDisplayUnitLabel { get; set; }

    /// <summary>
    /// The secondary value axis's own title/min/max/number-format (combo charts, e.g. bar-primary +
    /// line-secondary). Null/default means "not captured" — the writer then falls back to cloning the
    /// primary (Y) axis's settings, matching prior behavior for charts never round-tripped through the
    /// XLSX reader.
    /// </summary>
    public string? SecondaryAxisTitle { get; set; }
    public double? SecondaryAxisMinimum { get; set; }
    public double? SecondaryAxisMaximum { get; set; }

    /// <summary>
    /// R62-io-chart-axis-6-2: the secondary value axis's OWN major/minor unit, captured separately
    /// from the primary (Y) axis's <see cref="YAxisMajorUnit"/>/<see cref="YAxisMinorUnit"/> so the
    /// writer doesn't silently clone the primary axis's current unit onto the secondary axis. Null
    /// means "not captured" — the writer falls back to the primary (Y) axis's value, matching prior
    /// behavior for charts never round-tripped through the XLSX reader.
    /// </summary>
    public double? SecondaryAxisMajorUnit { get; set; }
    public double? SecondaryAxisMinorUnit { get; set; }
    public ChartDataLabelNumberFormat SecondaryAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
    public string? SecondaryAxisNumberFormatCode { get; set; }
    public bool? SecondaryAxisNumberFormatSourceLinked { get; set; }

    /// <summary>
    /// R36-io-chart-axis-scaling-2-2: the secondary value axis's OWN orientation (reversed/maxMin), log
    /// scale, tick style, and crossing — captured separately from the primary (Y) axis's fields above so
    /// the writer doesn't silently clone the primary axis's current settings onto the secondary axis.
    /// Null/default means "not captured" — the writer falls back to the primary (Y) axis's value,
    /// matching prior behavior for charts never round-tripped through the XLSX reader.
    /// </summary>
    public bool? SecondaryAxisReverseOrder { get; set; }
    public bool? SecondaryAxisLogScale { get; set; }
    public double? SecondaryAxisLogBase { get; set; }
    public ChartAxisTickStyle? SecondaryAxisMajorTickStyle { get; set; }
    public ChartAxisTickStyle? SecondaryAxisMinorTickStyle { get; set; }
    public ChartAxisCrosses? SecondaryAxisCrosses { get; set; }
    public double? SecondaryAxisCrossesAt { get; set; }
    public ChartAxisCrossBetween? SecondaryAxisCrossBetween { get; set; }

    /// <summary>
    /// R71-io-chart-axis-4-2: the secondary value axis's OWN &lt;c:dispUnits&gt; (e.g. "Millions"),
    /// captured separately from the primary (Y) axis's <see cref="YAxisDisplayUnit"/>/
    /// <see cref="YAxisCustomDisplayUnit"/> so the writer never clones the primary axis's display
    /// unit onto the secondary axis. Unlike the other Secondary* fields above, a null value here
    /// means "the secondary axis genuinely has no display unit" — the writer must NOT fall back to
    /// the primary axis's setting, since Excel's own secondary-axis default is "no display unit"
    /// regardless of what the primary axis has.
    /// </summary>
    public ChartAxisDisplayUnit? SecondaryAxisDisplayUnit { get; set; }
    public double? SecondaryAxisCustomDisplayUnit { get; set; }
    public bool ShowSecondaryAxisDisplayUnitLabel { get; set; }
    public ChartLegendPosition LegendPosition { get; set; } = ChartLegendPosition.Right;

    /// <summary>
    /// R45-io-chart-datatable-legend-3-2: true when the source file's &lt;c:legend&gt; actually
    /// declared an explicit position (&lt;c:legendPos val="..."/&gt; or chartEx "pos" attribute),
    /// as opposed to <see cref="LegendPosition"/> merely holding its C# default. The writer's
    /// classic-stacked-chart "legend defaults to bottom" heuristic (see
    /// <c>ToEffectiveLegendPosition</c> in XlsxChartXmlWriter.Format.cs) must never override a
    /// genuinely explicit "Right" choice loaded from a real file. Null for a chart that was never
    /// round-tripped through the XLSX reader (freshly created in FreeX), in which case the writer
    /// keeps applying its classic-Excel-default heuristic for stacked charts.
    /// </summary>
    public bool? LegendPositionExplicit { get; set; }
    public bool LegendOverlay { get; set; }
    public bool ShowLegend { get; set; } = true;
    public bool ShowDataLabels { get; set; }
    public ChartDataLabelPosition DataLabelPosition { get; set; } = ChartDataLabelPosition.BestFit;
    public bool ShowDataLabelValue { get; set; } = true;
    public bool ShowDataLabelLegendKey { get; set; }
    public bool ShowDataLabelBubbleSize { get; set; }
    public bool ShowDataLabelCategoryName { get; set; }
    public bool ShowDataLabelSeriesName { get; set; }
    public bool ShowDataLabelPercentage { get; set; }
    public ChartDataLabelSeparator DataLabelSeparator { get; set; } = ChartDataLabelSeparator.Comma;

    /// <summary>
    /// The literal separator text when <see cref="DataLabelSeparator"/> is
    /// <see cref="ChartDataLabelSeparator.Custom"/> (e.g. Excel's "Period" separator choice, whose
    /// <c>&lt;c:separator&gt;</c> value is a literal string with no dedicated enum member). Null for
    /// every other <see cref="DataLabelSeparator"/> value.
    /// </summary>
    public string? DataLabelSeparatorText { get; set; }
    public ChartDataLabelNumberFormat DataLabelNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
    public string? DataLabelNumberFormatCode { get; set; }
    public bool? DataLabelNumberFormatSourceLinked { get; set; }
    public bool ShowDataLabelCallouts { get; set; }

    /// <summary>
    /// Combo charts write one native plot-chart-type group per series subset (e.g. a bar group on
    /// the primary axis plus a line group on the secondary axis); today only the FIRST group's
    /// data-label settings are modeled by the scalar <see cref="ShowDataLabels"/>/<c>DataLabel*</c>
    /// properties above. When a LATER group (e.g. that secondary-axis line series) has its own
    /// <c>&lt;c:dLbls&gt;</c> instead, its raw XML is preserved verbatim here — keyed by the
    /// group's 0-based index in plot-area write/read order (matching
    /// <c>XlsxChartXmlWriter.CreatePlotCharts</c>' yield order) — so it survives an open/save
    /// round-trip instead of being silently dropped. This is a stop-gap round-trip fix, not a full
    /// per-group data-label model: FreeX cannot yet render or edit labels attached to a non-first
    /// combo group; that remains a follow-up.
    /// </summary>
    public List<ChartPlotGroupDataLabelsXml> AdditionalPlotGroupDataLabels { get; set; } = [];

    public CellColor? DataLabelFillColor { get; set; }
    public WorkbookThemeColorReference? DataLabelFillThemeColor { get; set; }
    public CellColor? DataLabelBorderColor { get; set; }
    public WorkbookThemeColorReference? DataLabelBorderThemeColor { get; set; }
    public CellColor? DataLabelTextColor { get; set; }
    public WorkbookThemeColorReference? DataLabelTextThemeColor { get; set; }
    public double DataLabelBorderThickness { get; set; }
    public double DataLabelFontSize { get; set; } = 11;
    public double DataLabelAngle { get; set; }
    public CellColor? DataLabelLeaderLineColor { get; set; }
    public WorkbookThemeColorReference? DataLabelLeaderLineThemeColor { get; set; }
    public double DataLabelLeaderLineThickness { get; set; } = 1;
    public ChartLineDashStyle DataLabelLeaderLineDashStyle { get; set; } = ChartLineDashStyle.Solid;
    public bool ShowLinearTrendline { get; set; }

    /// <summary>
    /// 0-based index of the series the captured trendline was read from (defaults to 0 for a
    /// programmatically-created chart that never went through the XLSX reader). Round-tripping
    /// must reattach the trendline to this same series rather than always series 0.
    /// </summary>
    public int TrendlineSeriesIndex { get; set; }
    public string? TrendlineName { get; set; }
    public ChartTrendlineType TrendlineType { get; set; } = ChartTrendlineType.Linear;
    public int TrendlinePeriod { get; set; } = 2;
    public int TrendlineOrder { get; set; } = 2;
    public double? TrendlineForward { get; set; }
    public double? TrendlineBackward { get; set; }
    public double? TrendlineIntercept { get; set; }
    public bool ShowTrendlineEquation { get; set; }
    public bool ShowTrendlineRSquared { get; set; }
    public string? TrendlineLabelNumberFormatCode { get; set; }
    public bool? TrendlineLabelNumberFormatSourceLinked { get; set; }
    public ChartManualLayoutModel? TrendlineLabelLayout { get; set; }
    public CellColor? TrendlineLabelFillColor { get; set; }
    public WorkbookThemeColorReference? TrendlineLabelFillThemeColor { get; set; }
    public CellColor? TrendlineLabelBorderColor { get; set; }
    public WorkbookThemeColorReference? TrendlineLabelBorderThemeColor { get; set; }
    public double? TrendlineLabelBorderThickness { get; set; }
    public CellColor? TrendlineLabelTextColor { get; set; }
    public WorkbookThemeColorReference? TrendlineLabelTextThemeColor { get; set; }
    public double? TrendlineLabelFontSize { get; set; }
    public double? TrendlineLabelAngle { get; set; }
    public CellColor? TrendlineColor { get; set; }
    public WorkbookThemeColorReference? TrendlineThemeColor { get; set; }
    public double TrendlineThickness { get; set; } = 1.5;
    public ChartLineDashStyle TrendlineDashStyle { get; set; } = ChartLineDashStyle.Dash;
    public bool ShowErrorBars { get; set; }

    /// <summary>
    /// 0-based index of the series the captured error bars were read from (defaults to 0 for a
    /// programmatically-created chart that never went through the XLSX reader). Round-tripping
    /// must reattach the error bars to this same series rather than always series 0.
    /// </summary>
    public int ErrorBarSeriesIndex { get; set; }
    public ChartErrorBarKind ErrorBarKind { get; set; } = ChartErrorBarKind.StandardError;
    public ChartErrorBarAxisDirection ErrorBarAxisDirection { get; set; } = ChartErrorBarAxisDirection.Y;
    public ChartErrorBarDirection ErrorBarDirection { get; set; } = ChartErrorBarDirection.Both;
    public double ErrorBarValue { get; set; } = 5;
    public string? ErrorBarPlusRangeFormula { get; set; }
    public string? ErrorBarMinusRangeFormula { get; set; }
    public bool ErrorBarEndCaps { get; set; } = true;
    public CellColor? ErrorBarColor { get; set; }
    public WorkbookThemeColorReference? ErrorBarThemeColor { get; set; }
    public string? ErrorBarPlusRangeCacheXml { get; set; }
    public string? ErrorBarMinusRangeCacheXml { get; set; }
    public double ErrorBarThickness { get; set; } = 1;
    public ChartLineDashStyle ErrorBarDashStyle { get; set; } = ChartLineDashStyle.Solid;

    /// <summary>
    /// R41-io-chart-errorbars-trendline-3-2 passthrough: Excel writes TWO sibling
    /// <c>&lt;c:errBars&gt;</c> elements on the same series when both horizontal (X) and vertical
    /// (Y) error bars are configured (common for scatter/statistical charts), but only one set is
    /// modeled by the scalar <c>ErrorBar*</c> properties above. Every <c>&lt;c:errBars&gt;</c>
    /// beyond the first one captured chart-wide — whether a second set on the SAME series or the
    /// first set on a LATER series — is preserved here verbatim, keyed by the 0-based series index
    /// it belongs to, so it survives an open/save round-trip instead of being silently dropped.
    /// This is a stop-gap round-trip fix, not a full per-series error-bar model: FreeX cannot yet
    /// render or edit these extra error bars; that remains a follow-up.
    /// </summary>
    public List<ChartSeriesRawXmlEntry> AdditionalSeriesErrorBarsXml { get; set; } = [];

    /// <summary>
    /// R41-io-chart-errorbars-trendline-3-3 passthrough: only the FIRST series (in document order)
    /// carrying a <c>&lt;c:trendline&gt;</c> is modeled by the scalar <c>Trendline*</c> properties
    /// above. Every additional <c>&lt;c:trendline&gt;</c> — a second trendline on that same series,
    /// or any trendline on a LATER series — is preserved here verbatim, keyed by the 0-based series
    /// index it belongs to, so it survives an open/save round-trip instead of being silently and
    /// permanently dropped. This is a stop-gap round-trip fix, not a full per-series trendline
    /// model: FreeX cannot yet render or edit these extra trendlines; that remains a follow-up.
    /// </summary>
    public List<ChartSeriesRawXmlEntry> AdditionalSeriesTrendlinesXml { get; set; } = [];
    public bool ShowDropLines { get; set; }
    public CellColor? DropLineColor { get; set; }
    public WorkbookThemeColorReference? DropLineThemeColor { get; set; }
    public double DropLineThickness { get; set; } = 1;
    public ChartLineDashStyle DropLineDashStyle { get; set; } = ChartLineDashStyle.Solid;
    public bool ShowHighLowLines { get; set; }
    public CellColor? HighLowLineColor { get; set; }
    public WorkbookThemeColorReference? HighLowLineThemeColor { get; set; }
    public double HighLowLineThickness { get; set; } = 1;
    public ChartLineDashStyle HighLowLineDashStyle { get; set; } = ChartLineDashStyle.Solid;
    public bool ShowSeriesLines { get; set; }
    public CellColor? SeriesLineColor { get; set; }
    public WorkbookThemeColorReference? SeriesLineThemeColor { get; set; }
    public double SeriesLineThickness { get; set; } = 1;
    public ChartLineDashStyle SeriesLineDashStyle { get; set; } = ChartLineDashStyle.Solid;

    /// <summary>Histogram bin configuration (Excel "Format Axis ▸ Bins"); null means automatic binning.</summary>
    public HistogramBinningModel? HistogramBinning { get; set; }

    /// <summary>
    /// Waterfall points (0-based) drawn as total/anchor columns (Excel "Set as Total").
    /// Null falls back to treating the last point as the total; an empty list means no totals.
    /// </summary>
    public List<int>? WaterfallTotalPointIndices { get; set; }

    /// <summary>
    /// BoxAndWhisker cx:statistics/@quartileMethod ("inclusive" or "exclusive"). Null means unset,
    /// in which case the writer defaults to Excel's "exclusive" behavior.
    /// </summary>
    public string? QuartileMethod { get; set; }

    public bool ShowUpDownBars { get; set; }
    public int? UpDownBarGapWidth { get; set; }
    public CellColor? UpBarFillColor { get; set; }
    public WorkbookThemeColorReference? UpBarFillThemeColor { get; set; }
    public CellColor? UpBarBorderColor { get; set; }
    public WorkbookThemeColorReference? UpBarBorderThemeColor { get; set; }
    public double? UpBarBorderThickness { get; set; }
    public CellColor? DownBarFillColor { get; set; }
    public WorkbookThemeColorReference? DownBarFillThemeColor { get; set; }
    public CellColor? DownBarBorderColor { get; set; }
    public WorkbookThemeColorReference? DownBarBorderThemeColor { get; set; }
    public double? DownBarBorderThickness { get; set; }
    public bool ShowSecondaryAxis { get; set; }
    public List<int> SecondaryAxisSeriesIndexes { get; set; } = [];

    /// <summary>
    /// Optional explicit series-to-value-column mapping (one entry per chart series, in
    /// declared idx order). When populated the renderer plots exactly these columns/indices
    /// instead of scanning every column in <see cref="DataRange"/>. Empty means "use the legacy
    /// positional column scan". See <see cref="ChartSeriesColumnMapping"/>.
    /// </summary>
    public List<ChartSeriesColumnMapping> SeriesColumnMappings { get; set; } = [];

    /// <summary>
    /// The chart-XML series indexes (<c>&lt;c:idx&gt;</c>) in the order the series are DECLARED in
    /// the chart XML (i.e. the order the <c>&lt;c:ser&gt;</c> elements appear across every plot
    /// group). This is the "legend position" order that OOXML <c>&lt;c:legendEntry&gt;&lt;c:idx&gt;</c>
    /// references — Excel can declare series out of idx order (e.g. a combo chart whose line series
    /// has idx 0 but is declared last). Empty means "declaration order equals positional/idx order"
    /// (the legacy single-plot-group case), in which case legend-entry deletes match the series idx
    /// directly. See <see cref="ChartLegendEntryModel"/>.
    /// </summary>
    public List<int> SeriesPlotOrder { get; set; } = [];
    public List<int> ComboLineSeriesIndexes { get; set; } = [];
    public List<int> ComboScatterSeriesIndexes { get; set; } = [];
    public List<ChartSeriesFormat> SeriesFormats { get; set; } = [];
    public List<ChartSeriesDataLabelFormat> SeriesDataLabelFormats { get; set; } = [];
    public List<ChartPointDataLabelFormat> PointDataLabelFormats { get; set; } = [];

    /// <summary>
    /// Literal per-point data-label strings from Excel's "Value From Cells" labels
    /// (<c>c15:datalabelsRange</c>). When populated the renderer draws these strings
    /// verbatim above the matching point instead of formatting the numeric value.
    /// </summary>
    public List<ChartRangeDataLabel> RangeDataLabels { get; set; } = [];

    /// <summary>
    /// Per-series "Value From Cells" data-label definitions (<c>c15:datalabelsRange</c>),
    /// preserving the source formula + cached point count/strings so the feature round-trips
    /// on XLSX and native (.fxl) save. The flat <see cref="RangeDataLabels"/> list is derived
    /// from this for rendering.
    /// </summary>
    public List<ChartSeriesRangeDataLabels> SeriesRangeDataLabels { get; set; } = [];

    /// <summary>
    /// Per-data-point fill color overrides for pie/doughnut slices, read from
    /// <c>&lt;c:dPt&gt;</c> elements with explicit <c>&lt;c:spPr&gt;</c> fills.
    /// When a slice's index matches an entry here its fill color takes precedence
    /// over the series-level fill (or palette fallback).
    /// </summary>
    public List<ChartPointFillFormat> PointFillColors { get; set; } = [];

    /// <summary>
    /// Per-series verbatim formula strings preserved from the source XML.
    /// Populated when any series formula cannot be parsed as a single rectangular range
    /// (e.g. multi-area unions like "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5").
    /// When present the writer emits these formulas verbatim instead of computing
    /// them positionally from <see cref="DataRange"/>.
    /// </summary>
    public List<ChartSeriesVerbatimFormulas>? VerbatimSeriesFormulas { get; set; }

    /// <summary>
    /// R82-io-chart-series-5-1: explicit &lt;c:order&gt; values captured for a series whose order
    /// diverges from its &lt;c:idx&gt; — Excel keeps idx as a series' stable identity but order as
    /// the actual plot/legend display sequence, and the two commonly diverge after the user
    /// reorders series (Chart Design &gt; Select Data &gt; Move Up/Down) or deletes a middle series
    /// (idx keeps a gap, order stays contiguous). Empty/no entry for a series means order == idx,
    /// the ordinary case. The writer re-emits the captured order instead of always recomputing it
    /// positionally as == idx.
    /// </summary>
    public List<ChartSeriesOrderOverride> SeriesOrderOverrides { get; set; } = [];

    /// <summary>
    /// R103-io-chart-series-tx-1: a series' &lt;c:tx&gt;&lt;c:strRef&gt;&lt;c:f&gt; formula, captured
    /// verbatim from the source XML whenever present. Excel's "Select Data &gt; Edit Series &gt;
    /// Series name" lets the user point a series' name at ANY cell — not necessarily the header cell
    /// directly above that series' data column — so it is a first-class, independently-addressable
    /// formula that cannot be recomputed positionally from <see cref="DataRange"/>/
    /// <see cref="FirstRowIsHeader"/> the way the writer's default header-cell guess is. When present
    /// for a series, the writer emits this formula verbatim instead of recomputing the strip's own
    /// header cell (and emits it even when <see cref="FirstRowIsHeader"/> is false, since the
    /// series-name reference is independent of whether the data range's own header row is used for
    /// categories). Empty/no entry for a series means the writer falls back to the positional guess,
    /// matching the pre-existing behavior.
    /// </summary>
    public List<ChartSeriesNameOverride> SeriesNameOverrides { get; set; } = [];

    /// <summary>
    /// R82-io-chart-series-5-2: verbatim &lt;c:cat&gt; XML captured for a series whose category
    /// container is a &lt;c:multiLvlStrRef&gt; (Excel's grouped/multi-level category axis, e.g. an
    /// outer "Region" level over an inner "City" level) — there is no positional-strip equivalent
    /// for this shape, so it is preserved verbatim (keyed by series index) rather than rebuilt as a
    /// flat &lt;c:strRef&gt;/&lt;c:numRef&gt;, which would silently discard the outer grouping level.
    /// </summary>
    public List<ChartSeriesRawXmlEntry> MultiLevelCategoryXml { get; set; } = [];

    /// <summary>
    /// R82-io-chart-series-5-3: per-data-point marker overrides (Format Data Point &gt; Marker
    /// Options — e.g. highlighting a single Line/Scatter point with its own symbol/size/fill/border
    /// while the rest of the series uses the series-level marker), read from a &lt;c:dPt&gt;'s
    /// &lt;c:marker&gt; child. Distinct from <see cref="PointFillColors"/>, which only models a
    /// dPt's &lt;c:spPr&gt; fill — a point whose ONLY override is its marker has no entry there.
    /// </summary>
    public List<ChartPointMarkerFormat> PointMarkerFormats { get; set; } = [];

    /// <summary>
    /// Embedded series data extracted from <c>&lt;c:numCache&gt;</c> / <c>&lt;c:strCache&gt;</c>
    /// elements in the chart XML. Populated when the series data range formula is an
    /// unresolvable named range (e.g. <c>Sheet1!rngMyData</c>). When non-null and non-empty,
    /// the renderer uses these values directly instead of looking up cells by <see cref="DataRange"/>.
    /// </summary>
    public List<ChartEmbeddedSeriesData>? EmbeddedSeriesData { get; set; }
    public bool UseComboLineForSecondarySeries { get; set; }
    public double Left   { get; set; } = 50;
    public double Top    { get; set; } = 50;
    public double Width  { get; set; } = 400;
    public double Height { get; set; } = 300;
    public ChartDrawingAnchorKind DrawingAnchorKind { get; set; } = ChartDrawingAnchorKind.Absolute;

    public CellColor? ResolveChartDefaultTextColor(WorkbookTheme theme) =>
        ChartDefaultTextThemeColor?.Resolve(theme) ?? ChartDefaultTextColor;

    public CellColor? ResolveChartAreaFillColor(WorkbookTheme theme) =>
        ChartAreaFillThemeColor?.Resolve(theme) ?? ChartAreaFillColor;

    public CellColor? ResolveChartAreaBorderColor(WorkbookTheme theme) =>
        ChartAreaBorderThemeColor?.Resolve(theme) ?? ChartAreaBorderColor;

    public CellColor? ResolvePlotAreaFillColor(WorkbookTheme theme) =>
        PlotAreaFillThemeColor?.Resolve(theme) ?? PlotAreaFillColor;

    public CellColor? ResolvePlotAreaBorderColor(WorkbookTheme theme) =>
        PlotAreaBorderThemeColor?.Resolve(theme) ?? PlotAreaBorderColor;

    /// <summary>
    /// R44-meta-1: true when the chart area was explicitly set to "No Fill" (<c>&lt;a:noFill/&gt;</c>),
    /// as distinct from simply having no fill configured. <see cref="ResolveChartAreaFillColor"/>
    /// alone cannot distinguish the two cases (both resolve to <c>null</c>) -- renderers that want to
    /// paint nothing (transparent) rather than falling back to a default background must check this
    /// first. See <see cref="ChartAreaNoFill"/>.
    /// </summary>
    public bool IsChartAreaFillSuppressed => ChartAreaNoFill == true;

    /// <summary>
    /// R44-meta-1: true when the chart area border was explicitly set to "No Line". Renderers that
    /// want to paint no border must check this before falling back to a default border. See
    /// <see cref="ChartAreaNoLine"/>.
    /// </summary>
    public bool IsChartAreaLineSuppressed => ChartAreaNoLine == true;

    /// <summary>
    /// R44-meta-1: true when the plot area was explicitly set to "No Fill". See
    /// <see cref="IsChartAreaFillSuppressed"/>.
    /// </summary>
    public bool IsPlotAreaFillSuppressed => PlotAreaNoFill == true;

    /// <summary>
    /// R44-meta-1: true when the plot area border was explicitly set to "No Line". See
    /// <see cref="IsChartAreaLineSuppressed"/>.
    /// </summary>
    public bool IsPlotAreaLineSuppressed => PlotAreaNoLine == true;

    public CellColor? ResolveChartTitleTextColor(WorkbookTheme theme) =>
        ChartTitleTextThemeColor?.Resolve(theme) ?? ChartTitleTextColor;

    public CellColor? ResolveAxisTitleTextColor(WorkbookTheme theme) =>
        AxisTitleTextThemeColor?.Resolve(theme) ?? AxisTitleTextColor;

    public CellColor? ResolveLegendTextColor(WorkbookTheme theme) =>
        LegendTextThemeColor?.Resolve(theme) ?? LegendTextColor;

    public CellColor? ResolveLegendFillColor(WorkbookTheme theme) =>
        LegendFillThemeColor?.Resolve(theme) ?? LegendFillColor;

    public CellColor? ResolveLegendBorderColor(WorkbookTheme theme) =>
        LegendBorderThemeColor?.Resolve(theme) ?? LegendBorderColor;

    public CellColor? ResolveDataLabelFillColor(WorkbookTheme theme) =>
        DataLabelFillThemeColor?.Resolve(theme) ?? DataLabelFillColor;

    public CellColor? ResolveDataLabelBorderColor(WorkbookTheme theme) =>
        DataLabelBorderThemeColor?.Resolve(theme) ?? DataLabelBorderColor;

    public CellColor? ResolveDataLabelTextColor(WorkbookTheme theme) =>
        DataLabelTextThemeColor?.Resolve(theme) ?? DataLabelTextColor;

    // R88-render-chart-labels-legend-5-3: leader-line color/theme-color round-trip from XLSX
    // (XlsxChartDataLabelReader.ApplyDataLabelLeaderLineProperties) but were never resolved for
    // rendering; expose the same Color-vs-ThemeColor precedence as the sibling Resolve* members
    // so the renderer can actually consume the parsed leader-line formatting.
    public CellColor? ResolveDataLabelLeaderLineColor(WorkbookTheme theme) =>
        DataLabelLeaderLineThemeColor?.Resolve(theme) ?? DataLabelLeaderLineColor;

    public CellColor? ResolveXAxisLabelTextColor(WorkbookTheme theme) =>
        XAxisLabelTextThemeColor?.Resolve(theme) ?? XAxisLabelTextColor;

    public CellColor? ResolveYAxisLabelTextColor(WorkbookTheme theme) =>
        YAxisLabelTextThemeColor?.Resolve(theme) ?? YAxisLabelTextColor;

    public CellColor? ResolveTrendlineColor(WorkbookTheme theme) =>
        TrendlineThemeColor?.Resolve(theme) ?? TrendlineColor;
}

/// <summary>
/// A per-data-point explosion override for a pie/doughnut slice (<c>&lt;c:dPt&gt;/&lt;c:explosion&gt;</c>).
/// <see cref="Distance"/> is normalized 0-0.5 (matching <see cref="ChartModel.ExplodedSliceDistance"/>),
/// i.e. an OOXML <c>explosion val="25"</c> (25%) becomes 0.25.
/// </summary>
public sealed record ChartPointExplosion(int SeriesIndex, int PointIndex, double Distance);

/// <summary>
/// Verbatim <c>&lt;c:dLbls&gt;</c> XML preserved for a non-first combo-chart plot group.
/// See <see cref="ChartModel.AdditionalPlotGroupDataLabels"/> for the round-trip rationale.
/// </summary>
public sealed record ChartPlotGroupDataLabelsXml(int GroupIndex, string RawXml);

/// <summary>
/// Verbatim per-series chart XML (e.g. an extra <c>&lt;c:errBars&gt;</c> or <c>&lt;c:trendline&gt;</c>)
/// preserved for a series beyond the one the scalar chart-wide properties model. See
/// <see cref="ChartModel.AdditionalSeriesErrorBarsXml"/> and
/// <see cref="ChartModel.AdditionalSeriesTrendlinesXml"/> for the round-trip rationale.
/// </summary>
public sealed record ChartSeriesRawXmlEntry(int SeriesIndex, string RawXml);
