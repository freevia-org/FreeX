using System.Globalization;

namespace FreeP.Core.Model;

/// <summary>
/// The authored PresentationML scale behavior for an animation.
/// Values are retained as XML text because PowerPoint permits fixed-percentage
/// spellings and unknown/custom values must survive a read/write cycle.
/// </summary>
public sealed class AnimationScaleBehavior
{
    public string? FromX { get; set; }
    public string? FromY { get; set; }
    public string? ToX { get; set; }
    public string? ToY { get; set; }
    public string? ByX { get; set; }
    public string? ByY { get; set; }
    public bool? ZoomContents { get; set; }

    public AnimationScaleBehavior Clone() => new()
    {
        FromX = FromX,
        FromY = FromY,
        ToX = ToX,
        ToY = ToY,
        ByX = ByX,
        ByY = ByY,
        ZoomContents = ZoomContents,
    };

    public static AnimationScaleBehavior FromTo(double scale) => new()
    {
        FromX = "100000",
        FromY = "100000",
        ToX = Format(scale),
        ToY = Format(scale),
    };

    public static string Format(double scale) =>
        (scale * 100000d).ToString("0.############", CultureInfo.InvariantCulture);
}
