namespace FreeW.Core.Model;

/// <summary>
/// One colour transform applied to a text-effect colour, exactly as WordprocessingML 2010 spells it
/// (<c>w14:tint</c>, <c>w14:shade</c>, <c>w14:alpha</c>, <c>w14:hueMod</c>, <c>w14:sat</c>,
/// <c>w14:satOff</c>, <c>w14:satMod</c>, <c>w14:lum</c>, <c>w14:lumOff</c>, <c>w14:lumMod</c>).
/// <see cref="Value"/> is the raw <c>w14:val</c> in thousandths of a percent.
/// </summary>
public sealed record TextEffectColorTransform(string Name, int Value);

/// <summary>A resolved 8-bit ARGB colour.</summary>
public readonly record struct TextEffectArgb(byte A, byte R, byte G, byte B)
{
    /// <summary>The colour as <c>#RRGGBB</c>, ignoring alpha.</summary>
    public string RgbHex => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Opacity in 0..1.</summary>
    public double Opacity => A / 255.0;
}

/// <summary>
/// A text-effect colour: either a literal <c>w14:srgbClr</c> or a theme <c>w14:schemeClr</c>, followed
/// by its colour transforms in document order. Note that <c>w14:alpha</c> is a TRANSPARENCY (0 is
/// opaque, 100000 fully transparent) — the opposite of DrawingML's <c>a:alpha</c>.
/// </summary>
public sealed record TextEffectColor
{
    /// <summary>The schema order of colour transforms inside a w14 colour element.</summary>
    public static readonly IReadOnlyList<string> TransformOrder =
        ["tint", "shade", "alpha", "hueMod", "sat", "satOff", "satMod", "lum", "lumOff", "lumMod"];

    /// <summary>Literal colour as six uppercase hex digits (no '#'), or null for a scheme colour.</summary>
    public string? RgbHex { get; init; }

    /// <summary>Theme slot (<c>accent1</c>, <c>tx1</c>, <c>bg1</c>, <c>dk1</c>…), or null for a literal colour.</summary>
    public string? SchemeName { get; init; }

    /// <summary>Colour transforms, in the order they apply.</summary>
    public IReadOnlyList<TextEffectColorTransform> Transforms { get; init; } = [];

    public static TextEffectColor FromRgb(string hex, int transparencyPercent = 0) =>
        new TextEffectColor { RgbHex = NormalizeHex(hex) }.WithTransparencyPercent(transparencyPercent);

    public static TextEffectColor FromScheme(string schemeName, int transparencyPercent = 0) =>
        new TextEffectColor { SchemeName = schemeName }.WithTransparencyPercent(transparencyPercent);

    /// <summary>Transparency in whole percent (0 opaque … 100 invisible), from the <c>alpha</c> transform.</summary>
    public int TransparencyPercent =>
        Transforms.LastOrDefault(transform => transform.Name == "alpha") is { } alpha
            ? Math.Clamp(alpha.Value / 1000, 0, 100)
            : 0;

    /// <summary>Returns the colour with its transparency replaced (0 removes the transform).</summary>
    public TextEffectColor WithTransparencyPercent(int percent)
    {
        var others = Transforms.Where(transform => transform.Name != "alpha").ToList();
        var clamped = Math.Clamp(percent, 0, 100);
        if (clamped > 0)
            others.Add(new TextEffectColorTransform("alpha", clamped * 1000));
        return this with { Transforms = SortTransforms(others) };
    }

    /// <summary>Returns the colour with one more transform, kept in schema order.</summary>
    public TextEffectColor With(string name, int value) =>
        this with { Transforms = SortTransforms([.. Transforms, new TextEffectColorTransform(name, value)]) };

    /// <summary>Resolves the colour against a theme palette, applying every transform.</summary>
    public TextEffectArgb Resolve(ThemeColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        var hex = RgbHex ?? SchemeHex(scheme, SchemeName);
        var (r, g, b) = ParseHex(hex);
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        var opacity = 1.0;

        foreach (var transform in Transforms)
        {
            var amount = transform.Value / 100000.0;
            switch (transform.Name)
            {
                case "tint":
                    // Word's tint value is the share of the ORIGINAL colour kept; the rest is white.
                    red = red * amount + (1 - amount);
                    green = green * amount + (1 - amount);
                    blue = blue * amount + (1 - amount);
                    break;
                case "shade":
                    red *= amount;
                    green *= amount;
                    blue *= amount;
                    break;
                case "alpha":
                    opacity = 1 - Math.Clamp(amount, 0, 1);
                    break;
                default:
                    (red, green, blue) = ApplyHslTransform(red, green, blue, transform.Name, amount);
                    break;
            }
        }

        return new TextEffectArgb(ToByte(opacity), ToByte(red), ToByte(green), ToByte(blue));
    }

    public bool Equals(TextEffectColor? other) =>
        other is not null
        && RgbHex == other.RgbHex
        && SchemeName == other.SchemeName
        && Transforms.SequenceEqual(other.Transforms);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RgbHex);
        hash.Add(SchemeName);
        foreach (var transform in Transforms)
            hash.Add(transform);
        return hash.ToHashCode();
    }

    internal static string NormalizeHex(string hex)
    {
        var trimmed = (hex ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
        return trimmed.Length == 6 && trimmed.All(Uri.IsHexDigit) ? trimmed : "000000";
    }

    private static IReadOnlyList<TextEffectColorTransform> SortTransforms(
        IEnumerable<TextEffectColorTransform> transforms) =>
        transforms
            .Select((transform, index) => (transform, index))
            .OrderBy(pair => OrderOf(pair.transform.Name))
            .ThenBy(pair => pair.index)
            .Select(pair => pair.transform)
            .ToArray();

    private static int OrderOf(string name)
    {
        for (var index = 0; index < TransformOrder.Count; index++)
        {
            if (TransformOrder[index] == name)
                return index;
        }

        return TransformOrder.Count;
    }

    private static string SchemeHex(ThemeColorScheme scheme, string? name) => name switch
    {
        "bg1" or "lt1" => scheme.Light1,
        "tx1" or "dk1" => scheme.Dark1,
        "bg2" or "lt2" => scheme.Light2,
        "tx2" or "dk2" => scheme.Dark2,
        "accent1" => scheme.Accent1,
        "accent2" => scheme.Accent2,
        "accent3" => scheme.Accent3,
        "accent4" => scheme.Accent4,
        "accent5" => scheme.Accent5,
        "accent6" => scheme.Accent6,
        "hlink" => scheme.Hyperlink,
        "folHlink" => scheme.FollowedHyperlink,
        _ => scheme.Dark1,
    };

    private static (byte R, byte G, byte B) ParseHex(string hex)
    {
        var normalized = NormalizeHex(hex);
        return (
            Convert.ToByte(normalized[..2], 16),
            Convert.ToByte(normalized.Substring(2, 2), 16),
            Convert.ToByte(normalized.Substring(4, 2), 16));
    }

    private static byte ToByte(double unit) => (byte)Math.Round(Math.Clamp(unit, 0, 1) * 255.0);

    private static (double R, double G, double B) ApplyHslTransform(
        double red, double green, double blue, string name, double amount)
    {
        var (hue, saturation, luminance) = ToHsl(red, green, blue);
        switch (name)
        {
            case "hueMod": hue = (hue * amount) % 1.0; break;
            case "sat": saturation = amount; break;
            case "satOff": saturation += amount; break;
            case "satMod": saturation *= amount; break;
            case "lum": luminance = amount; break;
            case "lumOff": luminance += amount; break;
            case "lumMod": luminance *= amount; break;
            default: return (red, green, blue);
        }

        return FromHsl(hue, Math.Clamp(saturation, 0, 1), Math.Clamp(luminance, 0, 1));
    }

    private static (double H, double S, double L) ToHsl(double red, double green, double blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var luminance = (max + min) / 2;
        if (max - min < 1e-9)
            return (0, 0, luminance);

        var delta = max - min;
        var saturation = luminance > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == red)
            hue = (green - blue) / delta + (green < blue ? 6 : 0);
        else if (max == green)
            hue = (blue - red) / delta + 2;
        else
            hue = (red - green) / delta + 4;
        return (hue / 6, saturation, luminance);
    }

    private static (double R, double G, double B) FromHsl(double hue, double saturation, double luminance)
    {
        if (saturation < 1e-9)
            return (luminance, luminance, luminance);

        var q = luminance < 0.5 ? luminance * (1 + saturation) : luminance + saturation - luminance * saturation;
        var p = 2 * luminance - q;
        return (Channel(p, q, hue + 1.0 / 3), Channel(p, q, hue), Channel(p, q, hue - 1.0 / 3));

        static double Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}

/// <summary><c>w14:glow</c>: a coloured halo of <see cref="RadiusEmu"/> around the glyphs.</summary>
public sealed record TextGlowEffect(long RadiusEmu, TextEffectColor Color)
{
    public double RadiusPt => RadiusEmu / TextEffectUnits.EmuPerPoint;
}

/// <summary><c>w14:shadow</c>: an outer shadow.</summary>
public sealed record TextShadowEffect
{
    public long BlurRadiusEmu { get; init; }
    public long DistanceEmu { get; init; }

    /// <summary>Direction the shadow is cast, in 60000ths of a degree clockwise from the positive x axis.</summary>
    public int DirectionAngle { get; init; }

    /// <summary>Horizontal / vertical scale in thousandths of a percent (100000 = 100%).</summary>
    public int ScaleX { get; init; } = 100000;
    public int ScaleY { get; init; } = 100000;

    /// <summary>Horizontal / vertical skew in 60000ths of a degree.</summary>
    public int SkewX { get; init; }
    public int SkewY { get; init; }

    /// <summary>Alignment token (<c>tl</c>, <c>t</c>, <c>tr</c>, <c>l</c>, <c>ctr</c>, <c>r</c>, <c>bl</c>, <c>b</c>, <c>br</c>).</summary>
    public string Alignment { get; init; } = "tl";

    public TextEffectColor Color { get; init; } = TextEffectColor.FromScheme("tx1");

    public double BlurRadiusPt => BlurRadiusEmu / TextEffectUnits.EmuPerPoint;
    public double DistancePt => DistanceEmu / TextEffectUnits.EmuPerPoint;
    public double DirectionDegrees => DirectionAngle / TextEffectUnits.AngleUnitsPerDegree;
}

/// <summary><c>w14:reflection</c>: a mirrored copy below the text that fades out.</summary>
public sealed record TextReflectionEffect
{
    public long BlurRadiusEmu { get; init; }

    /// <summary>Opacity at the start / end of the fade, in thousandths of a percent.</summary>
    public int StartOpacity { get; init; } = 100000;
    public int StartPosition { get; init; }
    public int EndOpacity { get; init; }
    public int EndPosition { get; init; } = 100000;

    public long DistanceEmu { get; init; }
    public int DirectionAngle { get; init; } = 5400000;
    public int FadeDirectionAngle { get; init; } = 5400000;
    public int ScaleX { get; init; } = 100000;
    public int ScaleY { get; init; } = -100000;
    public int SkewX { get; init; }
    public int SkewY { get; init; }
    public string Alignment { get; init; } = "bl";

    public double BlurRadiusPt => BlurRadiusEmu / TextEffectUnits.EmuPerPoint;
    public double DistancePt => DistanceEmu / TextEffectUnits.EmuPerPoint;
}

public enum TextFillKind
{
    /// <summary><c>w14:noFill</c>.</summary>
    None,

    /// <summary><c>w14:solidFill</c>.</summary>
    Solid,

    /// <summary><c>w14:gradFill</c>.</summary>
    Gradient,
}

public enum TextGradientKind
{
    /// <summary><c>w14:lin</c>: a linear gradient at <see cref="TextFillEffect.LinearAngle"/>.</summary>
    Linear,

    /// <summary><c>w14:path w14:path="circle"</c>.</summary>
    Circle,

    /// <summary><c>w14:path w14:path="rect"</c>.</summary>
    Rectangle,

    /// <summary><c>w14:path w14:path="shape"</c>.</summary>
    Shape,
}

/// <summary>One <c>w14:gs</c>: a colour at a position in thousandths of a percent (0..100000).</summary>
public sealed record TextGradientStop(int Position, TextEffectColor Color);

/// <summary>
/// A glyph or outline fill: <c>w14:noFill</c>, <c>w14:solidFill</c> or <c>w14:gradFill</c>. Used for
/// <c>w14:textFill</c> and as the fill of a <c>w14:textOutline</c>.
/// </summary>
public sealed record TextFillEffect
{
    public TextFillKind Kind { get; init; } = TextFillKind.Solid;

    /// <summary>The solid colour (<see cref="TextFillKind.Solid"/> only).</summary>
    public TextEffectColor? Color { get; init; }

    /// <summary>Gradient stops in ascending position (<see cref="TextFillKind.Gradient"/> only).</summary>
    public IReadOnlyList<TextGradientStop> Stops { get; init; } = [];

    public TextGradientKind GradientKind { get; init; } = TextGradientKind.Linear;

    /// <summary>Linear gradient angle in 60000ths of a degree, clockwise from left-to-right.</summary>
    public int LinearAngle { get; init; }

    /// <summary><c>w14:lin/@w14:scaled</c>.</summary>
    public bool LinearScaled { get; init; }

    /// <summary>Path-gradient focus rectangle insets (<c>w14:fillToRect</c>), thousandths of a percent.</summary>
    public int FillToRectLeft { get; init; }
    public int FillToRectTop { get; init; }
    public int FillToRectRight { get; init; }
    public int FillToRectBottom { get; init; }

    public double LinearAngleDegrees => LinearAngle / TextEffectUnits.AngleUnitsPerDegree;

    public static TextFillEffect NoFill { get; } = new() { Kind = TextFillKind.None };

    public static TextFillEffect Solid(TextEffectColor color) => new() { Kind = TextFillKind.Solid, Color = color };

    public static TextFillEffect LinearGradient(double angleDegrees, params TextGradientStop[] stops) => new()
    {
        Kind = TextFillKind.Gradient,
        GradientKind = TextGradientKind.Linear,
        LinearAngle = (int)Math.Round(angleDegrees * TextEffectUnits.AngleUnitsPerDegree),
        Stops = stops,
    };

    public bool Equals(TextFillEffect? other) =>
        other is not null
        && Kind == other.Kind
        && Equals(Color, other.Color)
        && Stops.SequenceEqual(other.Stops)
        && GradientKind == other.GradientKind
        && LinearAngle == other.LinearAngle
        && LinearScaled == other.LinearScaled
        && FillToRectLeft == other.FillToRectLeft
        && FillToRectTop == other.FillToRectTop
        && FillToRectRight == other.FillToRectRight
        && FillToRectBottom == other.FillToRectBottom;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Color);
        foreach (var stop in Stops)
            hash.Add(stop);
        hash.Add(GradientKind);
        hash.Add(LinearAngle);
        return hash.ToHashCode();
    }
}

public enum TextOutlineJoin
{
    /// <summary>No join element is written.</summary>
    Unspecified,
    Round,
    Bevel,
    Miter,
}

/// <summary><c>w14:textOutline</c>: the glyph outline.</summary>
public sealed record TextOutlineEffect
{
    /// <summary>Line width in EMU (12700 per point).</summary>
    public long WidthEmu { get; init; } = 9525;

    /// <summary>Line cap token (<c>rnd</c>, <c>sq</c>, <c>flat</c>), or null when absent.</summary>
    public string? Cap { get; init; } = "rnd";

    /// <summary>Compound-line token (<c>sng</c>, <c>dbl</c>, …), or null when absent.</summary>
    public string? Compound { get; init; } = "sng";

    /// <summary>Pen alignment token (<c>ctr</c>, <c>in</c>), or null when absent.</summary>
    public string? Alignment { get; init; } = "ctr";

    /// <summary>The line fill, or null when the outline carries no fill element.</summary>
    public TextFillEffect? Fill { get; init; }

    /// <summary>Preset dash token (<c>solid</c>, <c>sysDot</c>, <c>dash</c>, …), or null when absent.</summary>
    public string? Dash { get; init; } = "solid";

    public TextOutlineJoin Join { get; init; } = TextOutlineJoin.Round;

    /// <summary>Miter limit in thousandths of a percent (<see cref="TextOutlineJoin.Miter"/> only).</summary>
    public int? MiterLimit { get; init; }

    public double WidthPt => WidthEmu / TextEffectUnits.EmuPerPoint;

    /// <summary>True when the outline draws nothing (<c>w14:noFill</c>).</summary>
    public bool IsInvisible => Fill is { Kind: TextFillKind.None } || WidthEmu <= 0;
}

/// <summary>Unit constants shared by the text-effect values.</summary>
public static class TextEffectUnits
{
    public const double EmuPerPoint = 12700.0;
    public const double AngleUnitsPerDegree = 60000.0;

    public static long PointsToEmu(double points) => (long)Math.Round(points * EmuPerPoint);

    public static int DegreesToAngle(double degrees) => (int)Math.Round(degrees * AngleUnitsPerDegree);
}
