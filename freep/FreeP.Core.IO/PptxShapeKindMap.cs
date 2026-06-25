using Free.Shared.Drawing;

namespace FreeP.Core.IO;

/// <summary>
/// Maps between OOXML <c>a:prstGeom prst="..."</c> strings and <see cref="DrawingShapeKind"/>.
/// Unknown presets fall back to <see cref="DrawingShapeKind.Rectangle"/>.
/// </summary>
internal static class PptxShapeKindMap
{
    /// <summary>Maps a PresentationML preset geometry name to a DrawingShapeKind.</summary>
    public static DrawingShapeKind FromPreset(string? prst) =>
        prst?.ToLowerInvariant() switch
        {
            "rect" => DrawingShapeKind.Rectangle,
            "roundrect" => DrawingShapeKind.RoundedRectangle,
            "ellipse" => DrawingShapeKind.Ellipse,
            "line" => DrawingShapeKind.Line,
            "triangle" => DrawingShapeKind.Triangle,
            "rttriangle" => DrawingShapeKind.RightTriangle,
            "diamond" => DrawingShapeKind.Diamond,
            "parallelogram" => DrawingShapeKind.Parallelogram,
            "trapezoid" => DrawingShapeKind.Trapezoid,
            "pentagon" => DrawingShapeKind.Pentagon,
            "hexagon" => DrawingShapeKind.Hexagon,
            "octagon" => DrawingShapeKind.Octagon,
            "cross" => DrawingShapeKind.Cross,
            "rightarrow" => DrawingShapeKind.RightArrow,
            "leftarrow" => DrawingShapeKind.LeftArrow,
            "uparrow" => DrawingShapeKind.UpArrow,
            "downarrow" => DrawingShapeKind.DownArrow,
            "leftrightarrow" => DrawingShapeKind.LeftRightArrow,
            "updownarrow" => DrawingShapeKind.UpDownArrow,
            "mathplus" => DrawingShapeKind.PlusSign,
            "mathminus" => DrawingShapeKind.MinusSign,
            "mathmultiply" => DrawingShapeKind.MultiplySign,
            "mathdivide" => DrawingShapeKind.DivideSign,
            "mathequal" => DrawingShapeKind.EqualSign,
            "mathnotequal" => DrawingShapeKind.NotEqualSign,
            "flowchartprocess" => DrawingShapeKind.FlowchartProcess,
            "flowchartdecision" => DrawingShapeKind.FlowchartDecision,
            "flowchartinputoutput" => DrawingShapeKind.FlowchartData,
            "flowchartpredefinedprocess" => DrawingShapeKind.FlowchartPredefinedProcess,
            "flowchartdocument" => DrawingShapeKind.FlowchartDocument,
            "flowchartterminator" => DrawingShapeKind.FlowchartTerminator,
            "star5" => DrawingShapeKind.Star5,
            "star8" => DrawingShapeKind.Star8,
            "irregularseal1" or "irregularseal2" => DrawingShapeKind.Explosion,
            "ribbon" or "ribbon2" => DrawingShapeKind.Ribbon,
            "wave" => DrawingShapeKind.Wave,
            "wedgerectcallout" => DrawingShapeKind.RectangularCallout,
            "wedgeroundrectcallout" => DrawingShapeKind.RoundedRectangularCallout,
            "wedgeellipsecallout" => DrawingShapeKind.OvalCallout,
            "linecallout1" or "linecallout2" or "linecallout3" or "linecallout4" => DrawingShapeKind.LineCallout,
            "chevron" => DrawingShapeKind.Chevron,
            "homeplate" => DrawingShapeKind.HomePlate,
            // connector presets
            "bentconnector2" or "bentconnector3" or "bentconnector4" or "bentconnector5"
                => DrawingShapeKind.ElbowConnector,
            "curvedconnector2" or "curvedconnector3" or "curvedconnector4" or "curvedconnector5"
                => DrawingShapeKind.CurvedConnector,
            // everything else → rectangle fallback
            _ => DrawingShapeKind.Rectangle
        };

    /// <summary>Maps a DrawingShapeKind back to a canonical OOXML preset name.</summary>
    public static string ToPreset(DrawingShapeKind kind) =>
        kind switch
        {
            DrawingShapeKind.Rectangle => "rect",
            DrawingShapeKind.RoundedRectangle => "roundRect",
            DrawingShapeKind.Ellipse => "ellipse",
            DrawingShapeKind.Line => "line",
            DrawingShapeKind.Triangle => "triangle",
            DrawingShapeKind.RightTriangle => "rtTriangle",
            DrawingShapeKind.Diamond => "diamond",
            DrawingShapeKind.Parallelogram => "parallelogram",
            DrawingShapeKind.Trapezoid => "trapezoid",
            DrawingShapeKind.Pentagon => "pentagon",
            DrawingShapeKind.Hexagon => "hexagon",
            DrawingShapeKind.Octagon => "octagon",
            DrawingShapeKind.Cross => "cross",
            DrawingShapeKind.RightArrow => "rightArrow",
            DrawingShapeKind.LeftArrow => "leftArrow",
            DrawingShapeKind.UpArrow => "upArrow",
            DrawingShapeKind.DownArrow => "downArrow",
            DrawingShapeKind.LeftRightArrow => "leftRightArrow",
            DrawingShapeKind.UpDownArrow => "upDownArrow",
            DrawingShapeKind.PlusSign => "mathPlus",
            DrawingShapeKind.MinusSign => "mathMinus",
            DrawingShapeKind.MultiplySign => "mathMultiply",
            DrawingShapeKind.DivideSign => "mathDivide",
            DrawingShapeKind.EqualSign => "mathEqual",
            DrawingShapeKind.NotEqualSign => "mathNotEqual",
            DrawingShapeKind.FlowchartProcess => "flowChartProcess",
            DrawingShapeKind.FlowchartDecision => "flowChartDecision",
            DrawingShapeKind.FlowchartData => "flowChartInputOutput",
            DrawingShapeKind.FlowchartPredefinedProcess => "flowChartPredefinedProcess",
            DrawingShapeKind.FlowchartDocument => "flowChartDocument",
            DrawingShapeKind.FlowchartTerminator => "flowChartTerminator",
            DrawingShapeKind.Star5 => "star5",
            DrawingShapeKind.Star8 => "star8",
            DrawingShapeKind.Explosion => "irregularSeal1",
            DrawingShapeKind.Ribbon => "ribbon",
            DrawingShapeKind.Wave => "wave",
            DrawingShapeKind.RectangularCallout => "wedgeRectCallout",
            DrawingShapeKind.RoundedRectangularCallout => "wedgeRoundRectCallout",
            DrawingShapeKind.OvalCallout => "wedgeEllipseCallout",
            DrawingShapeKind.LineCallout => "lineCallout1",
            DrawingShapeKind.Chevron => "chevron",
            DrawingShapeKind.HomePlate => "homePlate",
            DrawingShapeKind.ElbowConnector => "bentConnector2",
            DrawingShapeKind.CurvedConnector => "curvedConnector2",
            _ => "rect"
        };
}
