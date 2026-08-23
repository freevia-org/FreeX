using System.Xml.Linq;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

internal static class DrawingMlGradientFillWriter
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    internal static XElement Build(
        ShapeFill.Gradient gradient,
        Func<ThemeAwareColor, XElement> buildColorElement)
    {
        var stops = gradient.Stops.OrderBy(stop => stop.Position).ToList();
        if (stops.Count == 0)
        {
            stops =
            [
                new GradientStop(0.0, ThemeAwareColor.White),
                new GradientStop(1.0, ThemeAwareColor.Black),
            ];
        }
        else if (stops.Count == 1)
        {
            var color = stops[0].Color;
            stops =
            [
                new GradientStop(0.0, color),
                new GradientStop(1.0, color),
            ];
        }

        var stopListElement = new XElement(A + "gsLst");
        foreach (var stop in stops)
        {
            stopListElement.Add(new XElement(
                A + "gs",
                new XAttribute("pos", (int)Math.Round(stop.Position * 100000)),
                buildColorElement(stop.Color)));
        }

        XElement kindElement = gradient.Kind == GradientKind.Radial
            ? new XElement(
                A + "path",
                new XAttribute("path", "circle"),
                new XElement(
                    A + "fillToRect",
                    new XAttribute("l", "50000"),
                    new XAttribute("t", "50000"),
                    new XAttribute("r", "50000"),
                    new XAttribute("b", "50000")))
            : new XElement(
                A + "lin",
                new XAttribute("ang", (long)Math.Round(gradient.AngleDegrees * 60000)),
                new XAttribute("scaled", "0"));

        return new XElement(A + "gradFill", stopListElement, kindElement);
    }
}
