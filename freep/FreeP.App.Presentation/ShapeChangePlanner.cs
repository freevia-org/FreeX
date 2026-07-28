using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

/// <summary>Common Change Shape command ids and the bounded preset menu shared by both hosts.</summary>
public static class ShapeChangePlanner
{
    public const string MenuCommandId = "freep.arrange.change-shape";
    public const string RectangleCommandId = "freep.arrange.change-shape.rectangle";
    public const string RoundedRectangleCommandId = "freep.arrange.change-shape.rounded-rectangle";
    public const string EllipseCommandId = "freep.arrange.change-shape.ellipse";
    public const string TriangleCommandId = "freep.arrange.change-shape.triangle";
    public const string DiamondCommandId = "freep.arrange.change-shape.diamond";
    public const string RightArrowCommandId = "freep.arrange.change-shape.right-arrow";
    public const string HexagonCommandId = "freep.arrange.change-shape.hexagon";
    public const string ParallelogramCommandId = "freep.arrange.change-shape.parallelogram";
    public const string TrapezoidCommandId = "freep.arrange.change-shape.trapezoid";
    public const string LeftArrowCommandId = "freep.arrange.change-shape.left-arrow";
    public const string Star5CommandId = "freep.arrange.change-shape.star5";
    public const string UpArrowCommandId = "freep.arrange.change-shape.up-arrow";
    public const string DownArrowCommandId = "freep.arrange.change-shape.down-arrow";
    public const string CrossCommandId = "freep.arrange.change-shape.cross";
    public const string PlusSignCommandId = "freep.arrange.change-shape.plus-sign";
    public const string PentagonCommandId = "freep.arrange.change-shape.pentagon";
    public const string OctagonCommandId = "freep.arrange.change-shape.octagon";
    public const string LeftRightArrowCommandId = "freep.arrange.change-shape.left-right-arrow";
    public const string UpDownArrowCommandId = "freep.arrange.change-shape.up-down-arrow";
    public const string Star8CommandId = "freep.arrange.change-shape.star8";
    public const string ChevronCommandId = "freep.arrange.change-shape.chevron";
    public const string HomePlateCommandId = "freep.arrange.change-shape.home-plate";
    public const string RightTriangleCommandId = "freep.arrange.change-shape.right-triangle";
    public const string MinusSignCommandId = "freep.arrange.change-shape.minus-sign";
    public const string MultiplySignCommandId = "freep.arrange.change-shape.multiply-sign";
    public const string DivideSignCommandId = "freep.arrange.change-shape.divide-sign";
    public const string EqualSignCommandId = "freep.arrange.change-shape.equal-sign";
    public const string NotEqualSignCommandId = "freep.arrange.change-shape.not-equal-sign";
    public const string WaveCommandId = "freep.arrange.change-shape.wave";
    public const string RectangularCalloutCommandId = "freep.arrange.change-shape.rectangular-callout";
    public const string RoundedRectangularCalloutCommandId = "freep.arrange.change-shape.rounded-rectangular-callout";
    public const string OvalCalloutCommandId = "freep.arrange.change-shape.oval-callout";
    public const string ExplosionCommandId = "freep.arrange.change-shape.explosion";
    public const string RibbonCommandId = "freep.arrange.change-shape.ribbon";
    public const string FlowchartProcessCommandId = "freep.arrange.change-shape.flowchart-process";
    public const string FlowchartDecisionCommandId = "freep.arrange.change-shape.flowchart-decision";
    public const string FlowchartDataCommandId = "freep.arrange.change-shape.flowchart-data";
    public const string FlowchartPredefinedProcessCommandId = "freep.arrange.change-shape.flowchart-predefined-process";
    public const string FlowchartDocumentCommandId = "freep.arrange.change-shape.flowchart-document";
    public const string FlowchartTerminatorCommandId = "freep.arrange.change-shape.flowchart-terminator";
    public const string LineCalloutCommandId = "freep.arrange.change-shape.line-callout";
    public const string CylinderCommandId = "freep.arrange.change-shape.cylinder";
    public const string ChordCommandId = "freep.arrange.change-shape.chord";
    public const string HeartCommandId = "freep.arrange.change-shape.heart";

    public static IReadOnlyList<(string CommandId, DrawingShapeKind Kind)> Presets =>
    [
        (RectangleCommandId, DrawingShapeKind.Rectangle),
        (RoundedRectangleCommandId, DrawingShapeKind.RoundedRectangle),
        (EllipseCommandId, DrawingShapeKind.Ellipse),
        (TriangleCommandId, DrawingShapeKind.Triangle),
        (DiamondCommandId, DrawingShapeKind.Diamond),
        (RightArrowCommandId, DrawingShapeKind.RightArrow),
        (HexagonCommandId, DrawingShapeKind.Hexagon),
        (ParallelogramCommandId, DrawingShapeKind.Parallelogram),
        (TrapezoidCommandId, DrawingShapeKind.Trapezoid),
        (LeftArrowCommandId, DrawingShapeKind.LeftArrow),
        (Star5CommandId, DrawingShapeKind.Star5),
        (UpArrowCommandId, DrawingShapeKind.UpArrow),
        (DownArrowCommandId, DrawingShapeKind.DownArrow),
        (CrossCommandId, DrawingShapeKind.Cross),
        (PlusSignCommandId, DrawingShapeKind.PlusSign),
        (PentagonCommandId, DrawingShapeKind.Pentagon),
        (OctagonCommandId, DrawingShapeKind.Octagon),
        (LeftRightArrowCommandId, DrawingShapeKind.LeftRightArrow),
        (UpDownArrowCommandId, DrawingShapeKind.UpDownArrow),
        (Star8CommandId, DrawingShapeKind.Star8),
        (ChevronCommandId, DrawingShapeKind.Chevron),
        (HomePlateCommandId, DrawingShapeKind.HomePlate),
        (RightTriangleCommandId, DrawingShapeKind.RightTriangle),
        (MinusSignCommandId, DrawingShapeKind.MinusSign),
        (MultiplySignCommandId, DrawingShapeKind.MultiplySign),
        (DivideSignCommandId, DrawingShapeKind.DivideSign),
        (EqualSignCommandId, DrawingShapeKind.EqualSign),
        (NotEqualSignCommandId, DrawingShapeKind.NotEqualSign),
        (WaveCommandId, DrawingShapeKind.Wave),
        (RectangularCalloutCommandId, DrawingShapeKind.RectangularCallout),
        (RoundedRectangularCalloutCommandId, DrawingShapeKind.RoundedRectangularCallout),
        (OvalCalloutCommandId, DrawingShapeKind.OvalCallout),
        (ExplosionCommandId, DrawingShapeKind.Explosion),
        (RibbonCommandId, DrawingShapeKind.Ribbon),
        (FlowchartProcessCommandId, DrawingShapeKind.FlowchartProcess),
        (FlowchartDecisionCommandId, DrawingShapeKind.FlowchartDecision),
        (FlowchartDataCommandId, DrawingShapeKind.FlowchartData),
        (FlowchartPredefinedProcessCommandId, DrawingShapeKind.FlowchartPredefinedProcess),
        (FlowchartDocumentCommandId, DrawingShapeKind.FlowchartDocument),
        (FlowchartTerminatorCommandId, DrawingShapeKind.FlowchartTerminator),
        (LineCalloutCommandId, DrawingShapeKind.LineCallout),
        (CylinderCommandId, DrawingShapeKind.Cylinder),
        (ChordCommandId, DrawingShapeKind.Chord),
        (HeartCommandId, DrawingShapeKind.Heart),
    ];
}
