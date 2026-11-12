using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

public static partial class DocxWriter
{
    /// <summary>
    /// Appends the run's w14 text effects to <paramref name="rPr"/>. Call after every core <c>w:</c>
    /// run property and BEFORE the OpenType elements (w14:ligatures…): the w14 schema orders the
    /// effects group — glow, shadow, reflection, textOutline, textFill, scene3d, props3d — ahead of
    /// the OpenType group.
    /// </summary>
    private static void AddRunTextEffects(XElement rPr, RunFormatting formatting)
    {
        if (formatting.TextEffects is not { } effects)
            return;

        AddEffect(rPr, effects.Glow, "glow");
        AddEffect(rPr, effects.Shadow, "shadow");
        AddEffect(rPr, effects.Reflection, "reflection");
        AddEffect(rPr, effects.Outline, "textOutline");
        AddEffect(rPr, effects.Fill, "textFill");
        AddEffect(rPr, effects.Scene3D, "scene3d");
        AddEffect(rPr, effects.Props3D, "props3d");
    }

    /// <summary>
    /// Appends <c>w14:cntxtAlts</c>, the last element of the w14 OpenType group. Call after
    /// w14:stylisticSets.
    /// </summary>
    private static void AddRunContextualAlternates(XElement rPr, RunFormatting formatting) =>
        AddEffect(rPr, formatting.TextEffects?.ContextualAlternates, "cntxtAlts");

    private static void AddEffect(XElement rPr, string? xml, string localName)
    {
        if (RunTextEffectsXml.Parse(xml, localName) is { } element)
            rPr.Add(element);
    }
}
