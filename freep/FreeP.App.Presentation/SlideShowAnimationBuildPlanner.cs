using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Resolves PowerPoint paragraph-build metadata and creates text-only paragraph
/// overlays for slideshow playback. Hosts keep the authored shape background in
/// a separate overlay so the existing effect implementations can be reused.
/// </summary>
public static class SlideShowAnimationBuildPlanner
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";

    public static bool IsParagraphBuild(Slide slide, uint shapeId)
    {
        ArgumentNullException.ThrowIfNull(slide);
        if (string.IsNullOrWhiteSpace(slide.AnimationBuildListXml))
            return false;

        try
        {
            var root = XElement.Parse(slide.AnimationBuildListXml, LoadOptions.PreserveWhitespace);
            return root.Name == P + "bldLst" && root
                .Elements(P + "bldP")
                .Any(build =>
                    uint.TryParse(build.Attribute("spid")?.Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var spid)
                    && spid == shapeId
                    && string.Equals(build.Attribute("build")?.Value, "p",
                        StringComparison.OrdinalIgnoreCase));
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds or removes the paragraph-build entry for one animated text shape while
    /// preserving the other timing entries in the slide's raw build list.
    /// </summary>
    public static bool TrySetParagraphBuild(
        Slide slide,
        uint shapeId,
        bool enabled,
        out string? updatedXml)
    {
        ArgumentNullException.ThrowIfNull(slide);
        updatedXml = slide.AnimationBuildListXml;
        if (shapeId == 0)
            return false;

        try
        {
            var root = string.IsNullOrWhiteSpace(slide.AnimationBuildListXml)
                ? new XElement(P + "bldLst")
                : XElement.Parse(slide.AnimationBuildListXml, LoadOptions.PreserveWhitespace);
            if (root.Name != P + "bldLst")
                return false;

            var entries = root.Elements(P + "bldP")
                .Where(build => uint.TryParse(
                    build.Attribute("spid")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var spid) && spid == shapeId)
                .ToArray();

            if (enabled)
            {
                var entry = entries.FirstOrDefault();
                if (entry is null)
                {
                    root.Add(new XElement(
                        P + "bldP",
                        new XAttribute("spid", shapeId.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("grpId", "0"),
                        new XAttribute("build", "p")));
                }
                else
                {
                    entry.SetAttributeValue("build", "p");
                }
            }
            else
            {
                foreach (var entry in entries)
                    entry.Remove();
            }

            updatedXml = root.Elements().Any()
                ? root.ToString(SaveOptions.DisableFormatting)
                : null;
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates one renderable copy per authored paragraph. Each copy retains the
    /// shape geometry and text formatting but contributes no shape-owned paint,
    /// allowing the host to compose it above a single background overlay.
    /// </summary>
    public static IReadOnlyList<SlideShape> CreateParagraphShapes(SlideShape source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.TextBody is null || source.TextBody.Paragraphs.Count == 0)
            return Array.Empty<SlideShape>();

        var result = new List<SlideShape>(source.TextBody.Paragraphs.Count);
        for (var index = 0; index < source.TextBody.Paragraphs.Count; index++)
        {
            var copy = SlideCloner.CloneShape(source);
            var body = TextBodyModelCloner.CloneTextBody(source.TextBody)!;
            body.Paragraphs.Clear();
            body.Paragraphs.Add(TextBodyModelCloner.CloneParagraph(source.TextBody.Paragraphs[index]));
            copy.TextBody = body;
            copy.Fill = null;
            copy.Outline = null;
            copy.Effects = null;
            result.Add(copy);
        }

        return result;
    }
}
