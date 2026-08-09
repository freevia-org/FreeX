using Free.Shared.Drawing;
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
    string ContentType);

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
                : editor.InsertPicture(picturePayload.Bytes, picturePayload.ContentType),
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
        return new SlideObjectPicturePayload(
            imageBytes,
            InferPictureContentType(fileNameOrExtension));
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

    public static string InferPictureContentType(string? fileNameOrExtension)
    {
        var extension = Path.GetExtension(fileNameOrExtension);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = fileNameOrExtension ?? string.Empty;
        }

        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "svg" => "image/svg+xml",
            _ => "image/png",
        };
    }

    public static string InferMediaContentType(string? fileNameOrExtension, bool isVideo)
    {
        var extension = Path.GetExtension(fileNameOrExtension ?? string.Empty).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
            extension = (fileNameOrExtension ?? string.Empty).TrimStart('.').ToLowerInvariant();

        return extension switch
        {
            "mp4" => "video/mp4",
            "mov" => "video/quicktime",
            "avi" => "video/x-msvideo",
            "wmv" => "video/x-ms-wmv",
            "m4v" => "video/x-m4v",
            "mp3" => "audio/mpeg",
            "m4a" => "audio/mp4",
            "wav" => "audio/wav",
            "wma" => "audio/x-ms-wma",
            _ => isVideo ? "video/mp4" : "audio/mpeg",
        };
    }

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
