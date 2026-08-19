using System.Buffers.Binary;
using Free.Shared.Drawing;
using Free.Shared.IO;
using Free.Shared.Opc;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideObjectInsertionKind
{
    TextBox,
    AutoShape,
    Connector,
    Picture,
    Media,
    Table,
    Chart,
    SmartArt
}

public sealed record SlideObjectPicturePayload(
    byte[] Bytes,
    string ContentType,
    long? WidthEmu = null,
    long? HeightEmu = null);

public sealed record SlideObjectMediaPayload(
    byte[] Bytes,
    bool IsVideo,
    string ContentType);

public sealed record SlideObjectSmartArtPicturePayload(
    IReadOnlyList<SlideObjectPicturePayload> Pictures)
{
    public bool HasPictures => Pictures.Count > 0;
}

public sealed record SlideObjectInsertionPlan(
    string CommandId,
    SlideObjectInsertionKind Kind,
    DrawingShapeKind? AutoShapeKind = null,
    int TableRows = 0,
    int TableColumns = 0,
    ChartType ChartKind = ChartType.ColumnClustered,
    SmartArtLayoutPreset SmartArtLayout = SmartArtLayoutPreset.BasicProcess,
    bool IsComboChart = false)
{
    public bool RequiresPicturePayload => Kind == SlideObjectInsertionKind.Picture;

    public bool RequiresMediaPayload => Kind == SlideObjectInsertionKind.Media;

    public bool IsPictureSmartArt =>
        Kind == SlideObjectInsertionKind.SmartArt &&
        SmartArtLayout is (SmartArtLayoutPreset.PictureAccentProcess or SmartArtLayoutPreset.PictureCaptionList or SmartArtLayoutPreset.PictureAccentList or SmartArtLayoutPreset.PictureStack or SmartArtLayoutPreset.PictureLineup or SmartArtLayoutPreset.PictureStrips or SmartArtLayoutPreset.ContinuousPictureList or SmartArtLayoutPreset.PictureGrid or SmartArtLayoutPreset.VerticalPictureList);
}

public static class SlideObjectInsertionPlanner
{
    public const string TextBoxCommandId = "freep.text-box";
    public const string RectangleCommandId = "freep.shape-rectangle";
    public const string RoundedRectangleCommandId = "freep.shape-rounded-rectangle";
    public const string EllipseCommandId = "freep.shape-ellipse";
    public const string TriangleCommandId = "freep.shape-triangle";
    public const string DiamondCommandId = "freep.shape-diamond";
    public const string HexagonCommandId = "freep.shape-hexagon";
    public const string ParallelogramCommandId = "freep.shape-parallelogram";
    public const string TrapezoidCommandId = "freep.shape-trapezoid";
    public const string LeftArrowCommandId = "freep.shape-left-arrow";
    public const string RightArrowCommandId = "freep.shape-right-arrow";
    public const string UpArrowCommandId = "freep.shape-up-arrow";
    public const string DownArrowCommandId = "freep.shape-down-arrow";
    public const string Star5CommandId = "freep.shape-star5";
    public const string CrossCommandId = "freep.shape-cross";
    public const string PlusSignCommandId = "freep.shape-plus-sign";
    public const string PentagonCommandId = "freep.shape-pentagon";
    public const string OctagonCommandId = "freep.shape-octagon";
    public const string LeftRightArrowCommandId = "freep.shape-left-right-arrow";
    public const string UpDownArrowCommandId = "freep.shape-up-down-arrow";
    public const string Star8CommandId = "freep.shape-star8";
    public const string ChevronCommandId = "freep.shape-chevron";
    public const string HomePlateCommandId = "freep.shape-home-plate";
    public const string RightTriangleCommandId = "freep.shape-right-triangle";
    public const string MinusSignCommandId = "freep.shape-minus-sign";
    public const string MultiplySignCommandId = "freep.shape-multiply-sign";
    public const string DivideSignCommandId = "freep.shape-divide-sign";
    public const string EqualSignCommandId = "freep.shape-equal-sign";
    public const string NotEqualSignCommandId = "freep.shape-not-equal-sign";
    public const string WaveCommandId = "freep.shape-wave";
    public const string RectangularCalloutCommandId = "freep.shape-rectangular-callout";
    public const string RoundedRectangularCalloutCommandId = "freep.shape-rounded-rectangular-callout";
    public const string OvalCalloutCommandId = "freep.shape-oval-callout";
    public const string ExplosionCommandId = "freep.shape-explosion";
    public const string RibbonCommandId = "freep.shape-ribbon";
    public const string FlowchartProcessCommandId = "freep.shape-flowchart-process";
    public const string FlowchartDecisionCommandId = "freep.shape-flowchart-decision";
    public const string FlowchartDataCommandId = "freep.shape-flowchart-data";
    public const string FlowchartPredefinedProcessCommandId = "freep.shape-flowchart-predefined-process";
    public const string FlowchartDocumentCommandId = "freep.shape-flowchart-document";
    public const string FlowchartTerminatorCommandId = "freep.shape-flowchart-terminator";
    public const string LineCalloutCommandId = "freep.shape-line-callout";
    public const string CylinderCommandId = "freep.shape-cylinder";
    public const string ChordCommandId = "freep.shape-chord";
    public const string HeartCommandId = "freep.shape-heart";
    public const string ConnectorCommandId = "freep.insert-connector";
    public const string ElbowConnectorCommandId = "freep.insert-elbow-connector";
    public const string CurvedConnectorCommandId = "freep.insert-curved-connector";
    public const string PictureCommandId = "freep.picture";
    public const string VideoCommandId = "freep.video";
    public const string AudioCommandId = "freep.audio";
    public const string Table3x3CommandId = "freep.insert-table-3x3";
    public const string Table2x2CommandId = "freep.insert-table-2x2";
    public const string Table4x4CommandId = "freep.insert-table-4x4";
    public const string ChartColumnCommandId = "freep.insert-chart-column";
    public const string ChartBarCommandId = "freep.insert-chart-bar";
    public const string ChartLineCommandId = "freep.insert-chart-line";
    public const string ChartPieCommandId = "freep.insert-chart-pie";
    public const string ChartOfPieCommandId = "freep.insert-chart-of-pie";
    public const string ChartColumnStackedCommandId = "freep.insert-chart-column-stacked";
    public const string ChartColumnStacked100CommandId = "freep.insert-chart-column-stacked-100";
    public const string ChartBarStackedCommandId = "freep.insert-chart-bar-stacked";
    public const string ChartBarStacked100CommandId = "freep.insert-chart-bar-stacked-100";
    public const string ChartLineMarkersCommandId = "freep.insert-chart-line-markers";
    public const string ChartAreaCommandId = "freep.insert-chart-area";
    public const string ChartAreaStackedCommandId = "freep.insert-chart-area-stacked";
    public const string ChartScatterCommandId = "freep.insert-chart-scatter";
    public const string ChartDoughnutCommandId = "freep.insert-chart-doughnut";
    public const string ChartRadarCommandId = "freep.insert-chart-radar";
    public const string ChartBubbleCommandId = "freep.insert-chart-bubble";
    public const string ChartStockCommandId = "freep.insert-chart-stock";
    public const string ChartSurfaceCommandId = "freep.insert-chart-surface";
    public const string ChartSurface3DCommandId = "freep.insert-chart-surface-3d";
    public const string ChartFunnelCommandId = "freep.insert-chart-funnel";
    public const string ChartWaterfallCommandId = "freep.insert-chart-waterfall";
    public const string ChartComboCommandId = "freep.insert-chart-combo";
    public const string SmartArtBasicProcessCommandId = "freep.insert-smartart-basic-process";

    public static IReadOnlyList<SmartArtLayoutPreset> InsertableSmartArtLayouts { get; } =
        Enum.GetValues<SmartArtLayoutPreset>().ToArray();

    public static string SmartArtLayoutCommandId(SmartArtLayoutPreset preset) =>
        $"freep.insert-smartart-{ToKebabCase(preset.ToString())}";

    public static string SmartArtLayoutDisplayName(SmartArtLayoutPreset preset) =>
        string.Join(' ', ToWords(preset.ToString()));

    private static readonly SlideObjectInsertionPlan[] BasePlans =
    [
        new(TextBoxCommandId, SlideObjectInsertionKind.TextBox),
        new(RectangleCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Rectangle),
        new(RoundedRectangleCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.RoundedRectangle),
        new(EllipseCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Ellipse),
        new(TriangleCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Triangle),
        new(DiamondCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Diamond),
        new(HexagonCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Hexagon),
        new(ParallelogramCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Parallelogram),
        new(TrapezoidCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Trapezoid),
        new(LeftArrowCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.LeftArrow),
        new(RightArrowCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.RightArrow),
        new(UpArrowCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.UpArrow),
        new(DownArrowCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.DownArrow),
        new(Star5CommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Star5),
        new(CrossCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Cross),
        new(PlusSignCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.PlusSign),
        new(PentagonCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Pentagon),
        new(OctagonCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Octagon),
        new(LeftRightArrowCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.LeftRightArrow),
        new(UpDownArrowCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.UpDownArrow),
        new(Star8CommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Star8),
        new(ChevronCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Chevron),
        new(HomePlateCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.HomePlate),
        new(RightTriangleCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.RightTriangle),
        new(MinusSignCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.MinusSign),
        new(MultiplySignCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.MultiplySign),
        new(DivideSignCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.DivideSign),
        new(EqualSignCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.EqualSign),
        new(NotEqualSignCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.NotEqualSign),
        new(WaveCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Wave),
        new(RectangularCalloutCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.RectangularCallout),
        new(RoundedRectangularCalloutCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.RoundedRectangularCallout),
        new(OvalCalloutCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.OvalCallout),
        new(ExplosionCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Explosion),
        new(RibbonCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Ribbon),
        new(FlowchartProcessCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.FlowchartProcess),
        new(FlowchartDecisionCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.FlowchartDecision),
        new(FlowchartDataCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.FlowchartData),
        new(FlowchartPredefinedProcessCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.FlowchartPredefinedProcess),
        new(FlowchartDocumentCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.FlowchartDocument),
        new(FlowchartTerminatorCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.FlowchartTerminator),
        new(LineCalloutCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.LineCallout),
        new(CylinderCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Cylinder),
        new(ChordCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Chord),
        new(HeartCommandId, SlideObjectInsertionKind.AutoShape, AutoShapeKind: DrawingShapeKind.Heart),
        new(ConnectorCommandId, SlideObjectInsertionKind.Connector, AutoShapeKind: DrawingShapeKind.Line),
        new(ElbowConnectorCommandId, SlideObjectInsertionKind.Connector, AutoShapeKind: DrawingShapeKind.ElbowConnector),
        new(CurvedConnectorCommandId, SlideObjectInsertionKind.Connector, AutoShapeKind: DrawingShapeKind.CurvedConnector),
        new(PictureCommandId, SlideObjectInsertionKind.Picture),
        new(VideoCommandId, SlideObjectInsertionKind.Media),
        new(AudioCommandId, SlideObjectInsertionKind.Media),
        new(Table3x3CommandId, SlideObjectInsertionKind.Table, TableRows: 3, TableColumns: 3),
        new(Table2x2CommandId, SlideObjectInsertionKind.Table, TableRows: 2, TableColumns: 2),
        new(Table4x4CommandId, SlideObjectInsertionKind.Table, TableRows: 4, TableColumns: 4),
        new(ChartColumnCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.ColumnClustered),
        new(ChartBarCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.BarClustered),
        new(ChartLineCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Line),
        new(ChartPieCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Pie),
        new(ChartOfPieCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.OfPie),
        new(ChartColumnStackedCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.ColumnStacked),
        new(ChartColumnStacked100CommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.ColumnStacked100),
        new(ChartBarStackedCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.BarStacked),
        new(ChartBarStacked100CommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.BarStacked100),
        new(ChartLineMarkersCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.LineMarkers),
        new(ChartAreaCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Area),
        new(ChartAreaStackedCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.AreaStacked),
        new(ChartScatterCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Scatter),
        new(ChartDoughnutCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Doughnut),
        new(ChartRadarCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Radar),
        new(ChartBubbleCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Bubble),
        new(ChartStockCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Stock),
        new(ChartSurfaceCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Surface),
        new(ChartSurface3DCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Surface3D),
        new(ChartFunnelCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Funnel),
        new(ChartWaterfallCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.Waterfall),
        new(ChartComboCommandId, SlideObjectInsertionKind.Chart, ChartKind: ChartType.ColumnClustered, IsComboChart: true),
        new(SmartArtBasicProcessCommandId, SlideObjectInsertionKind.SmartArt,
            SmartArtLayout: SmartArtLayoutPreset.BasicProcess),
    ];

    private static readonly SlideObjectInsertionPlan[] SmartArtLayoutPlans =
        InsertableSmartArtLayouts
            .Where(preset => preset != SmartArtLayoutPreset.BasicProcess)
            .Select(preset => new SlideObjectInsertionPlan(
                SmartArtLayoutCommandId(preset),
                SlideObjectInsertionKind.SmartArt,
                SmartArtLayout: preset))
            .ToArray();

    private static readonly SlideObjectInsertionPlan[] Plans =
        BasePlans.Concat(SmartArtLayoutPlans).ToArray();

    public static IReadOnlyList<SlideObjectInsertionPlan> BuiltInPlans { get; } =
        Array.AsReadOnly(Plans);

    public static IReadOnlyList<string> BuiltInCommandIds { get; } =
        Array.AsReadOnly(Plans.Select(plan => plan.CommandId).ToArray());

    private static readonly IReadOnlyDictionary<string, SlideObjectInsertionPlan> PlansByCommandId =
        Plans.ToDictionary(plan => plan.CommandId, StringComparer.Ordinal);

    public static bool TryCreatePlan(string commandId, out SlideObjectInsertionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        return PlansByCommandId.TryGetValue(commandId, out plan!);
    }

    public static SlideShape? ApplyCommand(
        EditingSession editor,
        string commandId,
        SlideObjectPicturePayload? picturePayload = null,
        SlideObjectMediaPayload? mediaPayload = null,
        SlideObjectSmartArtPicturePayload? smartArtPicturePayload = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(commandId);

        return TryCreatePlan(commandId, out var plan)
            ? Apply(editor, plan, picturePayload, mediaPayload, smartArtPicturePayload)
            : null;
    }

    public static SlideShape? Apply(
        EditingSession editor,
        SlideObjectInsertionPlan plan,
        SlideObjectPicturePayload? picturePayload = null,
        SlideObjectMediaPayload? mediaPayload = null,
        SlideObjectSmartArtPicturePayload? smartArtPicturePayload = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Kind switch
        {
            SlideObjectInsertionKind.TextBox => editor.InsertDefaultTextBox(),
            SlideObjectInsertionKind.AutoShape => ApplyAutoShape(editor, plan.AutoShapeKind),
            SlideObjectInsertionKind.Connector => ApplyConnector(editor, plan.AutoShapeKind),
            SlideObjectInsertionKind.Picture => picturePayload is null
                ? null
                : editor.InsertPicture(
                    picturePayload.Bytes,
                    picturePayload.ContentType,
                    picturePayload.WidthEmu,
                    picturePayload.HeightEmu),
            SlideObjectInsertionKind.Media => mediaPayload is null
                ? null
                : editor.InsertMedia(
                    mediaPayload.Bytes,
                    mediaPayload.IsVideo,
                    mediaPayload.ContentType),
            SlideObjectInsertionKind.Table => editor.InsertTable(plan.TableRows, plan.TableColumns),
            SlideObjectInsertionKind.Chart => plan.IsComboChart
                ? editor.InsertComboChart()
                : editor.InsertChart(plan.ChartKind),
            SlideObjectInsertionKind.SmartArt => editor.InsertSmartArt(
                plan.SmartArtLayout,
                smartArtPicturePayload?.Pictures),
            _ => null,
        };
    }

    public static SlideObjectPicturePayload CreatePicturePayload(
        byte[] imageBytes,
        string? fileNameOrExtension)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        long? widthEmu = null;
        long? heightEmu = null;
        if (TryDecodeNativePixelSize(imageBytes, out var pixelWidth, out var pixelHeight) &&
            TryComputeAspectFitEmuSize(pixelWidth, pixelHeight, out var fitCx, out var fitCy))
        {
            widthEmu = fitCx;
            heightEmu = fitCy;
        }

        return new SlideObjectPicturePayload(
            imageBytes,
            InferPictureContentType(fileNameOrExtension),
            widthEmu,
            heightEmu);
    }

    /// <summary>
    /// Computes an EMU width/height that preserves the picture's native pixel aspect ratio while
    /// fitting within the same footprint <see cref="EditingSession"/>'s default shape box uses
    /// (~3in x ~2in) -- so an inserted picture letterboxes into that box instead of being
    /// stretched/squashed to a fixed 1.5:1 rectangle regardless of its real proportions.
    /// </summary>
    private static bool TryComputeAspectFitEmuSize(int pixelWidth, int pixelHeight, out long widthEmu, out long heightEmu)
    {
        widthEmu = 0;
        heightEmu = 0;
        if (pixelWidth <= 0 || pixelHeight <= 0)
            return false;

        const long boxCx = DrawingMlCoordinateUnits.EmuPerInch * 3;
        const long boxCy = DrawingMlCoordinateUnits.EmuPerInch * 2;
        var aspect = (double)pixelWidth / pixelHeight;
        var boxAspect = (double)boxCx / boxCy;

        if (aspect >= boxAspect)
        {
            widthEmu = boxCx;
            heightEmu = Math.Max(1, (long)Math.Round(boxCx / aspect));
        }
        else
        {
            heightEmu = boxCy;
            widthEmu = Math.Max(1, (long)Math.Round(boxCy * aspect));
        }

        return true;
    }

    /// <summary>
    /// Best-effort native pixel size sniff from raw image bytes (PNG/GIF/BMP/JPEG headers).
    /// Returns false (leaving the caller to fall back to the fixed default box) for formats this
    /// does not recognize, such as SVG, or for malformed/truncated data.
    /// </summary>
    internal static bool TryDecodeNativePixelSize(byte[]? imageBytes, out int pixelWidth, out int pixelHeight)
    {
        pixelWidth = 0;
        pixelHeight = 0;
        if (imageBytes is not { Length: > 8 })
            return false;

        return TryDecodePngSize(imageBytes, out pixelWidth, out pixelHeight) ||
               TryDecodeGifSize(imageBytes, out pixelWidth, out pixelHeight) ||
               TryDecodeBmpSize(imageBytes, out pixelWidth, out pixelHeight) ||
               TryDecodeJpegSize(imageBytes, out pixelWidth, out pixelHeight);
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static bool TryDecodePngSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature))
            return false;

        // IHDR is always the first chunk: length(4) "IHDR"(4) width(4, BE) height(4, BE) ...
        if (bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
            return false;

        width = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return width > 0 && height > 0;
    }

    private static bool TryDecodeGifSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 10 ||
            bytes[0] != (byte)'G' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F' ||
            bytes[3] != (byte)'8' || (bytes[4] != (byte)'7' && bytes[4] != (byte)'9') || bytes[5] != (byte)'a')
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
        height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
        return width > 0 && height > 0;
    }

    private static bool TryDecodeBmpSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 26 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
            return false;

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(14, 4));
        if (headerSize == 12)
        {
            // BITMAPCOREHEADER: 16-bit width/height.
            width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(18, 2));
            height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20, 2));
        }
        else if (headerSize >= 40)
        {
            // BITMAPINFOHEADER (and newer variants): 32-bit signed width/height; a negative
            // height denotes a top-down bitmap and carries no aspect-ratio meaning here.
            width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4));
            height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4)));
        }
        else
        {
            return false;
        }

        return width > 0 && height > 0;
    }

    private static bool TryDecodeJpegSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return false;

        var offset = 2;
        while (offset + 3 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = bytes[offset + 1];
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            // Markers with no payload: TEM and the restart markers RST0-RST7.
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                offset += 2;
                continue;
            }

            if (marker == 0xD8)
            {
                offset += 2;
                continue;
            }

            if (marker == 0xD9 || offset + 3 >= bytes.Length)
                break;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 2, 2));
            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isStartOfFrame)
            {
                if (offset + 9 > bytes.Length)
                    return false;

                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 7, 2));
                return width > 0 && height > 0;
            }

            if (segmentLength < 2)
                return false;

            offset += 2 + segmentLength;
        }

        return false;
    }

    public static SlideObjectMediaPayload CreateMediaPayload(
        byte[] mediaBytes,
        string? fileNameOrExtension,
        bool isVideo)
    {
        ArgumentNullException.ThrowIfNull(mediaBytes);
        return new SlideObjectMediaPayload(
            mediaBytes,
            isVideo,
            InferMediaContentType(fileNameOrExtension, isVideo));
    }

    public static SlideObjectSmartArtPicturePayload CreateSmartArtPicturePayload(
        IEnumerable<SlideObjectPicturePayload> pictures)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        var materialized = pictures.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("At least one picture is required for a picture-based SmartArt layout.", nameof(pictures));

        return new SlideObjectSmartArtPicturePayload(materialized);
    }

    public static string InferPictureContentType(string? fileNameOrExtension) =>
        OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            fileNameOrExtension,
            OpcMediaContentTypeProfile.PresentationPictureInsertion);

    public static string InferMediaContentType(string? fileNameOrExtension, bool isVideo) =>
        OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            fileNameOrExtension,
            isVideo
                ? OpcMediaContentTypeProfile.PresentationVideoInsertion
                : OpcMediaContentTypeProfile.PresentationAudioInsertion);

    private static SlideShape? ApplyAutoShape(
        EditingSession editor,
        DrawingShapeKind? shapeKind)
    {
        if (shapeKind is not { } kind ||
            !DrawingShapeKindSupport.IsRenderable(kind) ||
            DrawingShapeKindSupport.IsLineLike(kind))
        {
            return null;
        }

        return editor.InsertDefaultAutoShape(kind);
    }

    private static SlideShape? ApplyConnector(
        EditingSession editor,
        DrawingShapeKind? shapeKind)
    {
        return shapeKind is { } kind && DrawingShapeKindSupport.IsLineLike(kind)
            ? editor.InsertDefaultConnector(kind)
            : null;
    }

    private static string ToKebabCase(string value) =>
        string.Join('-', ToWords(value).Select(word => word.ToLowerInvariant()));

    private static IEnumerable<string> ToWords(string value)
    {
        var start = 0;
        for (var index = 1; index < value.Length; index++)
        {
            if (!char.IsUpper(value[index]))
                continue;

            yield return value[start..index];
            start = index;
        }

        if (start < value.Length)
            yield return value[start..];
    }
}
