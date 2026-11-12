using FreeW.App.Localization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>
/// One entry of Home &gt; Text Effects and Typography: a command id, its menu label and the pure
/// run-formatting edit it applies to the selection.
/// </summary>
public sealed record TextEffectChoice(
    string CommandId,
    string LabelKey,
    Func<RunFormatting, RunFormatting> Apply,
    bool StartsNewGroup = false,
    object?[]? LabelArguments = null)
{
    /// <summary>The label in the CURRENT UI culture (resolved on every read, never cached).</summary>
    public string Label => LabelArguments is { Length: > 0 }
        ? Loc.Format(
            LabelKey,
            LabelArguments.Select(argument => argument is TextEffectLabelKey key ? Loc.Get(key.Key) : argument).ToArray())
        : Loc.Get(LabelKey);
}

/// <summary>A label argument that is itself a resource key, resolved when the label is read.</summary>
public sealed record TextEffectLabelKey(string Key);

/// <summary>
/// Canonical content of Word's Home &gt; Text Effects and Typography gallery: the fifteen preset
/// tiles, the Outline / Shadow / Reflection / Glow submenus, the OpenType Number Styles / Ligatures /
/// Stylistic Sets entries and Clear Text Effects. Both renderers build their menu from this catalog
/// and register one command per entry, so ids, labels and the applied formatting cannot drift apart.
/// <para>
/// Every edit goes through the typed text-effect layer, so what is applied is valid w14 XML that real
/// Word reads. The two bevel presets also carry Word's 3-D scene/bevel elements: FreeW does not draw
/// 3-D text, but the document then shows the bevel in Word.
/// </para>
/// </summary>
public static class TextEffectsRibbonCatalog
{
    public const string RootCommandId = "freew.text-effects";
    public const string ClearCommandId = "freew.text-effects.clear";

    private const string Scene3DXml =
        "<w14:scene3d xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><w14:camera w14:prst=\"orthographicFront\"/><w14:lightRig w14:rig=\"threePt\" w14:dir=\"t\"><w14:rot w14:lat=\"0\" w14:lon=\"0\" w14:rev=\"0\"/></w14:lightRig></w14:scene3d>";

    private const string SoftBevelXml =
        "<w14:props3d w14:extrusionH=\"57150\" w14:contourW=\"0\" w14:prstMaterial=\"softEdge\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><w14:bevelT w14:w=\"25400\" w14:h=\"38100\"/></w14:props3d>";

    private const string SharpBevelXml =
        "<w14:props3d w14:extrusionH=\"57150\" w14:contourW=\"0\" w14:prstMaterial=\"matte\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"><w14:bevelT w14:w=\"38100\" w14:h=\"38100\" w14:prst=\"angle\"/></w14:props3d>";

    private static readonly (string Token, string Scheme)[] Accents =
    [
        ("accent1", "accent1"), ("accent2", "accent2"), ("accent3", "accent3"),
        ("accent4", "accent4"), ("accent5", "accent5"), ("accent6", "accent6"),
    ];

    private static readonly int[] GlowSizesPt = [5, 8, 11, 18];

    /// <summary>The fifteen preset tiles, in Word's gallery order (three rows of five).</summary>
    public static IReadOnlyList<TextEffectChoice> Presets { get; } = BuildPresets();

    public static IReadOnlyList<TextEffectChoice> Outline { get; } = BuildOutline();

    public static IReadOnlyList<TextEffectChoice> Shadow { get; } = BuildShadow();

    public static IReadOnlyList<TextEffectChoice> Reflection { get; } = BuildReflection();

    public static IReadOnlyList<TextEffectChoice> Glow { get; } = BuildGlow();

    public static IReadOnlyList<TextEffectChoice> NumberStyles { get; } =
    [
        Typography("number.default", "TextEffects_Number_Default", f => f with { NumberForm = NumberForm.Default, NumberSpacing = NumberSpacing.Default }),
        Typography("number.proportional-lining", "TextEffects_Number_ProportionalLining", f => f with { NumberForm = NumberForm.Lining, NumberSpacing = NumberSpacing.Proportional }),
        Typography("number.tabular-lining", "TextEffects_Number_TabularLining", f => f with { NumberForm = NumberForm.Lining, NumberSpacing = NumberSpacing.Tabular }),
        Typography("number.proportional-oldstyle", "TextEffects_Number_ProportionalOldStyle", f => f with { NumberForm = NumberForm.OldStyle, NumberSpacing = NumberSpacing.Proportional }),
        Typography("number.tabular-oldstyle", "TextEffects_Number_TabularOldStyle", f => f with { NumberForm = NumberForm.OldStyle, NumberSpacing = NumberSpacing.Tabular }),
    ];

    public static IReadOnlyList<TextEffectChoice> Ligatures { get; } =
    [
        Typography("ligatures.none", "TextEffects_Ligatures_None", f => f with { Ligatures = LigatureMode.None }),
        Typography("ligatures.standard", "TextEffects_Ligatures_Standard", f => f with { Ligatures = LigatureMode.Standard }),
        Typography("ligatures.standard-contextual", "TextEffects_Ligatures_StandardContextual", f => f with { Ligatures = LigatureMode.StandardContextual }),
        Typography("ligatures.historical-discretionary", "TextEffects_Ligatures_HistoricalDiscretionary", f => f with { Ligatures = LigatureMode.HistoricalDiscretional }),
        Typography("ligatures.all", "TextEffects_Ligatures_All", f => f with { Ligatures = LigatureMode.All }),
    ];

    public static IReadOnlyList<TextEffectChoice> StylisticSets { get; } = BuildStylisticSets();

    public static TextEffectChoice Clear { get; } = new(
        ClearCommandId,
        "TextEffects_Clear",
        formatting => formatting with { TextEffects = formatting.TextEffects.ClearVisualEffects() });

    /// <summary>Every entry, in menu order.</summary>
    public static IEnumerable<TextEffectChoice> All =>
        Presets
            .Concat(Outline)
            .Concat(Shadow)
            .Concat(Reflection)
            .Concat(Glow)
            .Concat(NumberStyles)
            .Concat(Ligatures)
            .Concat(StylisticSets)
            .Append(Clear);

    // ------------------------------------------------------------------ presets

    private static IReadOnlyList<TextEffectChoice> BuildPresets()
    {
        var softShadow = Shadow45(blurPt: 3, distancePt: 1.5, Scheme("dk1", 60));
        return
        [
            Preset(1, Fill("tx1"), outline: null, softShadow),
            Preset(2, Fill("accent1"), outline: null, softShadow),
            Preset(3, Fill("accent2"), OutlineOf("accent2", 0.75)),
            Preset(4, Fill("bg1"), OutlineOf("accent1", 0.75), Shadow45(3, 1.5, Scheme("accent1", 60))),
            Preset(5, Fill("accent4"), outline: null, softShadow, props3D: SoftBevelXml),
            Preset(6, Gradient("bg1", shadeEnd: 50000), OutlineOf("tx1", 0.5, transparency: 50)),
            Preset(7, Gradient("accent5", shadeEnd: 60000), outline: null, reflection: ReflectionOf(35000, 0, 50000)),
            Preset(8, Gradient("accent4", shadeEnd: 70000), OutlineOf("accent4", 0.75)),
            Preset(9, Fill("bg1"), OutlineOf("accent5", 0.75), glow: new TextGlowEffect(TextEffectUnits.PointsToEmu(5), Scheme("accent5", 60))),
            Preset(10, Fill("accent3"), outline: null, softShadow, props3D: SharpBevelXml),
            Preset(11, Fill("tx1"), OutlineOf("bg1", 0.75), HardShadow("bg1")),
            Preset(12, Fill("tx1"), OutlineOf("bg1", 0.75), HardShadow("accent5")),
            Preset(13, Fill("accent1"), OutlineOf("bg1", 0.75), HardShadow("accent1")),
            Preset(14, Fill("bg1"), OutlineOf("accent2", 1), HardShadow("accent2")),
            Preset(15, Fill("accent3"), OutlineOf("accent3", 0.5), Shadow45(4, 2, Scheme("tx1", 50))),
        ];
    }

    private static TextEffectChoice Preset(
        int number,
        TextFillEffect fill,
        TextOutlineEffect? outline,
        TextShadowEffect? shadow = null,
        TextGlowEffect? glow = null,
        TextReflectionEffect? reflection = null,
        string? props3D = null)
    {
        // A preset REPLACES every visual effect (Word's tiles are whole looks, not additions) but keeps
        // the run's OpenType contextual-alternates setting.
        return new TextEffectChoice(
            $"{RootCommandId}.preset-{number}",
            $"TextEffects_Preset_{number}",
            formatting =>
            {
                var effects = formatting.TextEffects.ClearVisualEffects()
                    .WithFill(fill)
                    .WithOutline(outline)
                    .WithShadow(shadow)
                    .WithGlow(glow)
                    .WithReflection(reflection);
                if (props3D is not null)
                {
                    effects = (effects ?? new RunTextEffects()) with
                    {
                        Scene3D = RunTextEffectsCodec.Canonicalize(Scene3DXml),
                        Props3D = RunTextEffectsCodec.Canonicalize(props3D),
                    };
                }

                return formatting with { TextEffects = effects };
            });
    }

    // ------------------------------------------------------------------ outline

    private static IReadOnlyList<TextEffectChoice> BuildOutline()
    {
        var choices = new List<TextEffectChoice>
        {
            Effect("outline.none", "TextEffects_Outline_None", effects => effects.WithOutline(null)),
            Effect("outline.automatic", "TextEffects_Outline_Automatic", effects => effects.WithOutline(
                OutlineBase(effects) with { Fill = TextFillEffect.Solid(Scheme("tx1")) })),
        };

        foreach (var (token, scheme) in Accents)
        {
            choices.Add(Effect(
                $"outline.{token}",
                $"TextEffects_Color_{Pascal(token)}",
                effects => effects.WithOutline(OutlineBase(effects) with { Fill = TextFillEffect.Solid(Scheme(scheme)) }),
                startsNewGroup: token == "accent1"));
        }

        var weights = new (string Token, double Points)[]
        {
            ("025", 0.25), ("05", 0.5), ("075", 0.75), ("1", 1), ("15", 1.5), ("225", 2.25), ("3", 3),
        };
        foreach (var (token, points) in weights)
        {
            choices.Add(Effect(
                $"outline.weight-{token}",
                "TextEffects_Outline_Weight_Format",
                effects => effects.WithOutline(OutlineBase(effects) with { WidthEmu = TextEffectUnits.PointsToEmu(points) }),
                startsNewGroup: token == "025",
                labelArguments: [points]));
        }

        var dashes = new (string Token, string Dash)[]
        {
            ("solid", "solid"), ("round-dot", "sysDot"), ("square-dot", "sysDash"),
            ("dash", "dash"), ("dash-dot", "dashDot"), ("long-dash", "lgDash"),
        };
        foreach (var (token, dash) in dashes)
        {
            choices.Add(Effect(
                $"outline.dash-{token}",
                $"TextEffects_Outline_Dash_{Pascal(token)}",
                effects => effects.WithOutline(OutlineBase(effects) with { Dash = dash }),
                startsNewGroup: token == "solid"));
        }

        return choices;
    }

    /// <summary>
    /// The outline a weight/dash/colour pick modifies: the run's current outline when it has a visible
    /// one, otherwise Word's default hairline in the text colour (so choosing only a weight still shows).
    /// </summary>
    private static TextOutlineEffect OutlineBase(RunTextEffects? effects) =>
        effects.GetOutline() is { IsInvisible: false, Fill: not null } existing
            ? existing
            : new TextOutlineEffect { Fill = TextFillEffect.Solid(Scheme("tx1")) };

    // ------------------------------------------------------------------ shadow

    private static IReadOnlyList<TextEffectChoice> BuildShadow()
    {
        var choices = new List<TextEffectChoice>
        {
            Effect("shadow.none", "TextEffects_Shadow_None", effects => effects.WithShadow(null)),
        };

        // Word's nine "Outer" offsets, in gallery order (the direction the shadow falls).
        var outer = new (string Token, double Degrees, double DistancePt, string Alignment)[]
        {
            ("bottom-right", 45, 3, "tl"), ("bottom", 90, 3, "t"), ("bottom-left", 135, 3, "tr"),
            ("right", 0, 3, "l"), ("center", 0, 0, "ctr"), ("left", 180, 3, "r"),
            ("top-right", 315, 3, "bl"), ("top", 270, 3, "b"), ("top-left", 225, 3, "br"),
        };
        foreach (var (token, degrees, distance, alignment) in outer)
        {
            var shadow = new TextShadowEffect
            {
                BlurRadiusEmu = TextEffectUnits.PointsToEmu(4),
                DistanceEmu = TextEffectUnits.PointsToEmu(distance),
                DirectionAngle = TextEffectUnits.DegreesToAngle(degrees),
                Alignment = alignment,
                // The centred shadow has no offset to reveal it, so Word grows it slightly instead.
                ScaleX = distance == 0 ? 102000 : 100000,
                ScaleY = distance == 0 ? 102000 : 100000,
                Color = Scheme("tx1", 60),
            };
            choices.Add(Effect(
                $"shadow.{token}",
                $"TextEffects_Shadow_{Pascal(token)}",
                effects => effects.WithShadow(shadow),
                startsNewGroup: token == "bottom-right"));
        }

        return choices;
    }

    // ------------------------------------------------------------------ reflection

    private static IReadOnlyList<TextEffectChoice> BuildReflection()
    {
        var choices = new List<TextEffectChoice>
        {
            Effect("reflection.none", "TextEffects_Reflection_None", effects => effects.WithReflection(null)),
        };

        var sizes = new (string Token, int EndPosition, int StartOpacity)[]
        {
            ("tight", 35000, 50000), ("half", 55000, 50000), ("full", 90000, 50000),
        };
        var offsets = new (string Token, double DistancePt)[] { ("touching", 0), ("4pt", 4), ("8pt", 8) };
        foreach (var (offsetToken, distance) in offsets)
        {
            foreach (var (sizeToken, endPosition, startOpacity) in sizes)
            {
                var reflection = ReflectionOf(endPosition, distance, startOpacity);
                choices.Add(Effect(
                    $"reflection.{sizeToken}-{offsetToken}",
                    $"TextEffects_Reflection_{Pascal(sizeToken)}_{Pascal(offsetToken)}",
                    effects => effects.WithReflection(reflection),
                    startsNewGroup: offsetToken == "touching" && sizeToken == "tight"));
            }
        }

        return choices;
    }

    // ------------------------------------------------------------------ glow

    private static IReadOnlyList<TextEffectChoice> BuildGlow()
    {
        var choices = new List<TextEffectChoice>
        {
            Effect("glow.none", "TextEffects_Glow_None", effects => effects.WithGlow(null)),
        };

        foreach (var size in GlowSizesPt)
        {
            foreach (var (token, scheme) in Accents)
            {
                var glow = new TextGlowEffect(TextEffectUnits.PointsToEmu(size), Scheme(scheme, 60));
                choices.Add(Effect(
                    $"glow.{token}-{size}pt",
                    "TextEffects_Glow_Format",
                    effects => effects.WithGlow(glow),
                    startsNewGroup: token == "accent1",
                    labelArguments: [new TextEffectLabelKey($"TextEffects_Color_{Pascal(token)}"), size]));
            }
        }

        return choices;
    }

    // ------------------------------------------------------------------ typography

    private static IReadOnlyList<TextEffectChoice> BuildStylisticSets()
    {
        var choices = new List<TextEffectChoice>
        {
            Typography("stylistic-set.default", "TextEffects_StylisticSet_Default", f => f with { StylisticSet = null }),
        };
        for (var set = 1; set <= 10; set++)
        {
            var captured = set;
            choices.Add(new TextEffectChoice(
                $"{RootCommandId}.stylistic-set.{set}",
                "TextEffects_StylisticSet_Format",
                formatting => formatting with { StylisticSet = captured },
                StartsNewGroup: set == 1,
                LabelArguments: [set]));
        }

        return choices;
    }

    // ------------------------------------------------------------------ helpers

    private static TextEffectChoice Typography(
        string token, string labelKey, Func<RunFormatting, RunFormatting> apply) =>
        new($"{RootCommandId}.{token}", labelKey, apply);

    private static TextEffectChoice Effect(
        string token,
        string labelKey,
        Func<RunTextEffects?, RunTextEffects?> edit,
        bool startsNewGroup = false,
        object?[]? labelArguments = null) =>
        new(
            $"{RootCommandId}.{token}",
            labelKey,
            formatting => formatting with { TextEffects = edit(formatting.TextEffects) },
            startsNewGroup,
            labelArguments);

    private static TextEffectColor Scheme(string name, int transparencyPercent = 0) =>
        TextEffectColor.FromScheme(name, transparencyPercent);

    private static TextFillEffect Fill(string scheme) => TextFillEffect.Solid(Scheme(scheme));

    private static TextFillEffect Gradient(string scheme, int shadeEnd) =>
        TextFillEffect.LinearGradient(
            90,
            new TextGradientStop(0, Scheme(scheme)),
            new TextGradientStop(100000, Scheme(scheme).With("shade", shadeEnd)));

    private static TextOutlineEffect OutlineOf(string scheme, double widthPt, int transparency = 0) => new()
    {
        WidthEmu = TextEffectUnits.PointsToEmu(widthPt),
        Fill = TextFillEffect.Solid(Scheme(scheme, transparency)),
    };

    private static TextShadowEffect Shadow45(double blurPt, double distancePt, TextEffectColor color) => new()
    {
        BlurRadiusEmu = TextEffectUnits.PointsToEmu(blurPt),
        DistanceEmu = TextEffectUnits.PointsToEmu(distancePt),
        DirectionAngle = TextEffectUnits.DegreesToAngle(45),
        Color = color,
    };

    private static TextShadowEffect HardShadow(string scheme) => new()
    {
        BlurRadiusEmu = 0,
        DistanceEmu = TextEffectUnits.PointsToEmu(3),
        DirectionAngle = TextEffectUnits.DegreesToAngle(45),
        Color = Scheme(scheme),
    };

    private static TextReflectionEffect ReflectionOf(int endPosition, double distancePt, int startOpacity) => new()
    {
        BlurRadiusEmu = 6350,
        StartOpacity = startOpacity,
        EndOpacity = 300,
        EndPosition = endPosition,
        DistanceEmu = TextEffectUnits.PointsToEmu(distancePt),
    };

    /// <summary>"bottom-right" → "BottomRight", "accent1" → "Accent1", "4pt" → "4pt".</summary>
    private static string Pascal(string token) =>
        string.Concat(token.Split('-').Select(part =>
            part.Length == 0 || char.IsDigit(part[0]) ? part : char.ToUpperInvariant(part[0]) + part[1..]));
}
