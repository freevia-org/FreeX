using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Converts Word's run baseline position into the compositor's downward-positive coordinate space.
/// Positive <c>w:position</c> values raise glyphs, while negative values lower them.
/// </summary>
public static class RunBaselinePositionPlanner
{
    public static double ResolveOffsetDip(RunFormatting formatting, double dipPerPoint = 4.0 / 3.0)
    {
        ArgumentNullException.ThrowIfNull(formatting);
        return -formatting.PositionPt * Math.Max(0, dipPerPoint);
    }
}
