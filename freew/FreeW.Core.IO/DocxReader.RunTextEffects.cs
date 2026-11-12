using System.Xml.Linq;
using FreeW.Core.Model;
using static FreeW.Core.IO.Ooxml;

namespace FreeW.Core.IO;

public static partial class DocxReader
{
    /// <summary>
    /// Reads the w14 text-effect children of a <c>w:rPr</c> (body run, style, or document default)
    /// into a <see cref="RunTextEffects"/>, keeping each element's XML verbatim so it is written back
    /// exactly. Returns null when the run properties carry none, so effect-free runs stay unchanged.
    /// </summary>
    private static RunTextEffects? ReadRunTextEffects(XElement rPr)
    {
        var effects = new RunTextEffects
        {
            Glow = EffectXml(rPr, "glow"),
            Shadow = EffectXml(rPr, "shadow"),
            Reflection = EffectXml(rPr, "reflection"),
            Outline = EffectXml(rPr, "textOutline"),
            Fill = EffectXml(rPr, "textFill"),
            Scene3D = EffectXml(rPr, "scene3d"),
            Props3D = EffectXml(rPr, "props3d"),
            ContextualAlternates = EffectXml(rPr, "cntxtAlts"),
        };
        return effects.IsEmpty ? null : effects;
    }

    private static string? EffectXml(XElement rPr, string localName) =>
        rPr.Element(W14 + localName) is { } element ? RunTextEffectsXml.Canonicalize(element) : null;
}

/// <summary>
/// The one canonical string form of a w14 effect element shared by the reader and the writer: the
/// element alone, unformatted, with exactly one <c>xmlns:w14</c> declaration. A single form keeps two
/// runs with the same effect equal however their source documents declared the prefix.
/// </summary>
internal static class RunTextEffectsXml
{
    public static string Canonicalize(XElement element)
    {
        var copy = new XElement(element);
        foreach (var node in copy.DescendantsAndSelf())
            node.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
        // Effect elements carry no text content, so whitespace between them is only the source part's
        // indentation. Dropping it keeps one effect one string whether the package was pretty-printed
        // (FreeW's own output) or not (Word's).
        copy.DescendantNodesAndSelf().OfType<XText>()
            .Where(text => string.IsNullOrWhiteSpace(text.Value))
            .ToList()
            .ForEach(text => text.Remove());
        copy.SetAttributeValue(XNamespace.Xmlns + "w14", W14.NamespaceName);
        return copy.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Parses a stored effect back into an element for <paramref name="localName"/>. Anything that is
    /// not well-formed XML, or is not the expected w14 element, yields null and is not written: the
    /// model value is free text as far as the type system goes, and a package must never be corrupted by it.
    /// </summary>
    public static XElement? Parse(string? xml, string localName)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        XElement element;
        try
        {
            element = XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        if (element.Name != W14 + localName)
            return null;

        // The package root declares w14, so drop the local declaration rather than repeating it per run.
        foreach (var node in element.DescendantsAndSelf())
            node.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
        return element;
    }
}
