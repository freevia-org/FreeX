using System.Text;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>PowerPoint-style SmartArt color authoring presets.</summary>
public enum SmartArtColorPreset
{
    ThemeAccents,
    SingleAccent,
    Grayscale,
}

public sealed record SmartArtColorApplyResult(
    bool Applied,
    string Message,
    string? PartPath,
    int ColorCount);

/// <summary>
/// Applies SmartArt Change Colors operations to both the live model and the native diagram
/// colors part. Keeping the native part authoritative makes the edit survive save/reopen.
/// </summary>
public static class SmartArtAuthoringPlanner
{
    private static readonly XNamespace Diagram = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public const string ThemeAccentsCommandId = "freep.smartart.colors.theme-accents";
    public const string SingleAccentCommandId = "freep.smartart.colors.single-accent";
    public const string GrayscaleCommandId = "freep.smartart.colors.grayscale";

    public static SmartArtColorApplyResult ApplyColorPreset(
        SmartArtShape? smartArt,
        SmartArtColorPreset preset,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (smartArt is null)
            return NotApplied("No SmartArt graphic is available.");

        var part = smartArt.Parts.Values.FirstOrDefault(candidate =>
            candidate.ContentType.Contains("diagramColors", StringComparison.OrdinalIgnoreCase) ||
            candidate.PartPath.Contains("colors", StringComparison.OrdinalIgnoreCase));

        XDocument document;
        if (part is null)
        {
            if (!smartArt.Parts.Values.Any(candidate =>
                    candidate.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase)))
            {
                return NotApplied("The SmartArt graphic has no native data part for a new colors definition.");
            }

            part = CreateColorsPart(smartArt);
            document = CreateEmptyColorsDefinition();
        }
        else
        {
            if (part.Bytes.Length == 0)
                return NotApplied("The SmartArt colors part is empty.");

            try
            {
                document = XDocument.Parse(Encoding.UTF8.GetString(part.Bytes), LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex) when (ex is FormatException or XmlException)
            {
                return NotApplied("The native SmartArt colors part is not valid XML.");
            }
        }

        var fillLists = document
            .Descendants(Diagram + "fillClrLst")
            .ToList();
        var firstPalette = fillLists.FirstOrDefault()?
            .Elements()
            .Where(IsColorElement)
            .ToList();
        if (firstPalette is null || firstPalette.Count == 0)
            return NotApplied("The SmartArt colors part has no node fill palette.");

        var appliedColors = BuildColors(preset, firstPalette.Count, theme, effectiveClrMap);
        foreach (var fillList in fillLists)
        {
            var colors = fillList.Elements().Where(IsColorElement).ToList();
            for (var index = 0; index < colors.Count; index++)
            {
                var color = appliedColors[index % appliedColors.Count];
                colors[index].ReplaceWith(BuildColorElement(color, colors[index]));
            }
        }

        part.Bytes = Serialize(document);
        smartArt.Colors ??= new SmartArtColorMetadata();
        smartArt.Colors.Palette.Clear();
        smartArt.Colors.Palette.AddRange(appliedColors.Select(color => color.ModelColor));

        return new SmartArtColorApplyResult(
            true,
            $"SmartArt colors changed to the {preset} preset.",
            part.PartPath,
            appliedColors.Count);
    }

    private static DiagramPart CreateColorsPart(SmartArtShape smartArt)
    {
        var dataPartPath = smartArt.Parts.Values
            .FirstOrDefault(part => part.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase))
            ?.PartPath;
        if (string.IsNullOrWhiteSpace(dataPartPath))
            throw new InvalidOperationException("A SmartArt data part is required to create a colors part.");

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataPartPath)))
            .ToLowerInvariant()[..8];
        var directory = dataPartPath[..(dataPartPath.LastIndexOf('/') + 1)];
        var part = new DiagramPart
        {
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml",
            PartPath = $"{directory}colors-freep-{digest}.xml",
            Bytes = Array.Empty<byte>(),
        };

        smartArt.Parts[part.PartPath] = part;
        smartArt.DiagramRelIds["cs"] = "rIdFreePColors";
        return part;
    }

    private static XDocument CreateEmptyColorsDefinition()
    {
        var fillColors = Enumerable.Range(0, 6)
            .Select(_ => new XElement(Drawing + "schemeClr", new XAttribute("val", "accent1")));
        return new XDocument(
            new XElement(
                Diagram + "colorsDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Diagram.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", Drawing.NamespaceName),
                new XElement(
                    Diagram + "styleLbl",
                    new XAttribute("name", "node0"),
                    new XElement(Diagram + "fillClrLst", fillColors))));
    }

    private static bool IsColorElement(XElement element) =>
        element.Name.Namespace == Drawing &&
        (element.Name.LocalName is "schemeClr" or "srgbClr" or "sysClr");

    private static XElement BuildColorElement(PaletteColor color, XElement previous)
    {
        var name = color.SchemeRole is null ? "srgbClr" : "schemeClr";
        var attributes = previous.Attributes()
            .Where(attribute =>
                attribute.Name.LocalName is not "val" and not "lastClr")
            .ToList();
        attributes.Add(new XAttribute("val", color.SchemeRole ?? color.Resolved.ToString()[1..]));
        return new XElement(Drawing + name, attributes, previous.Nodes());
    }

    private static IReadOnlyList<PaletteColor> BuildColors(
        SmartArtColorPreset preset,
        int count,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        var accents = new[]
        {
            ThemeColorSlot.Accent1,
            ThemeColorSlot.Accent2,
            ThemeColorSlot.Accent3,
            ThemeColorSlot.Accent4,
            ThemeColorSlot.Accent5,
            ThemeColorSlot.Accent6,
        };

        if (preset == SmartArtColorPreset.Grayscale)
        {
            var grays = new[] { 0x404040, 0x666666, 0x808080, 0x999999, 0xB3B3B3, 0xD9D9D9 };
            return Enumerable.Range(0, count)
                .Select(index =>
                {
                    var resolved = SrgbColor.FromRgb(grays[index % grays.Length]);
                    return new PaletteColor(resolved, null, new ThemeAwareColor(resolved));
                })
                .ToArray();
        }

        var slot = accents[0];
        if (preset == SmartArtColorPreset.SingleAccent)
        {
            var single = CreateSchemeColor(slot, theme, effectiveClrMap);
            return Enumerable.Repeat(single, count).ToArray();
        }

        return Enumerable.Range(0, count)
            .Select(index => CreateSchemeColor(accents[index % accents.Length], theme, effectiveClrMap))
            .ToArray();
    }

    private static PaletteColor CreateSchemeColor(
        ThemeColorSlot slot,
        PresentationTheme theme,
        IReadOnlyDictionary<string, string>? effectiveClrMap)
    {
        var role = $"accent{(int)slot - (int)ThemeColorSlot.Accent1 + 1}";
        var reference = new SchemeColorRef { RoleName = role, Slot = slot };
        var modelColor = new ThemeAwareColor(
            ThemeColorResolver.Resolve(new ThemeAwareColor(theme.ColorScheme[slot], reference), theme, effectiveClrMap),
            reference);
        return new PaletteColor(modelColor.Resolved, role, modelColor);
    }

    private static byte[] Serialize(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false,
        }))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static SmartArtColorApplyResult NotApplied(string message) =>
        new(false, message, null, 0);

    private sealed record PaletteColor(SrgbColor Resolved, string? SchemeRole, ThemeAwareColor ModelColor);
}
