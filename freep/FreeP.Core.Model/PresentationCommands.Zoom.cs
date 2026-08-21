using System.Xml.Linq;
using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>The target family carried by a native single-target Zoom.</summary>
public enum ZoomTargetKind
{
    Slide,
    Section,
}

/// <summary>Changes an existing Slide Zoom or Section Zoom target as one undoable operation.</summary>
public sealed class SetZoomTargetCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ZoomTargetKind _targetKind;
    private readonly uint? _newSlideNumericId;
    private readonly string? _newSlideId;
    private readonly string? _newSectionId;
    private readonly string _newAlternativeText;
    private uint? _oldSlideNumericId;
    private string? _oldSlideId;
    private string? _oldSectionId;
    private string? _oldRawXml;
    private string? _oldAlternativeText;
    private ZoomObjectProperties? _oldProperties;
    private ImagePart? _oldPicture;
    private Dictionary<string, byte[]>? _oldParts;
    private Dictionary<string, string>? _oldPartContentTypes;
    private Dictionary<string, (string RelType, string TargetPath)>? _oldSlideRels;

    public SetZoomTargetCommand(
        int slideIndex,
        uint shapeId,
        ZoomTargetKind targetKind,
        uint? slideNumericId,
        string? sectionId,
        string alternativeText,
        string? newSlideId = null)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _targetKind = targetKind;
        _newSlideNumericId = targetKind == ZoomTargetKind.Slide
            ? slideNumericId ?? throw new ArgumentNullException(nameof(slideNumericId))
            : null;
        // The stable Slide.Id backing _newSlideNumericId, when the caller has it (e.g. from
        // SlideZoomInsertionPlanner's plan). Lets PptxPackageWriter re-resolve the numeric id
        // against the target slide's actual save-time NumericId if slides are inserted or
        // duplicated before it after this retarget. Optional/nullable for back-compat callers
        // that only have the numeric id — those keep the prior, non-re-resolved behavior.
        _newSlideId = targetKind == ZoomTargetKind.Slide ? newSlideId : null;
        _newSectionId = targetKind == ZoomTargetKind.Section
            ? string.IsNullOrWhiteSpace(sectionId)
                ? throw new ArgumentException("A Section Zoom target is required.", nameof(sectionId))
                : sectionId.Trim()
            : null;
        _newAlternativeText = string.IsNullOrWhiteSpace(alternativeText)
            ? throw new ArgumentException("Zoom alternative text is required.", nameof(alternativeText))
            : alternativeText.Trim();
    }

    public string Label => "Change Zoom Target";

    public int EstimatedBytes => PresentationCommandSizeEstimator.Combine(new[]
    {
        _oldPicture is null ? 0 : _oldPicture.Bytes.Length,
        PresentationCommandSizeEstimator.EstimateBytes(_oldParts),
    });

    public bool HasEffect(Presentation presentation) =>
        TryGetZoom(presentation, out var shape)
        && shape.PreservedObject is { } info
        && (_targetKind == ZoomTargetKind.Slide
            ? info.ZoomTargetSlideNumericId != _newSlideNumericId
            : !string.Equals(info.ZoomTargetSectionId, _newSectionId, StringComparison.Ordinal))
        && TryPatchRawXml(
            info.RawXml,
            _targetKind,
            _newSlideNumericId,
            _newSectionId,
            out _);

    public void Apply(Presentation presentation)
    {
        if (!TryGetZoom(presentation, out var shape) || shape.PreservedObject is not { } info)
            return;

        _oldSlideNumericId = info.ZoomTargetSlideNumericId;
        _oldSlideId = info.ZoomTargetSlideId;
        _oldSectionId = info.ZoomTargetSectionId;
        _oldRawXml = info.RawXml;
        _oldAlternativeText = shape.AlternativeText;
        _oldProperties = info.ZoomProperties;
        _oldPicture = shape.Picture is null
            ? null
            : new ImagePart
            {
                Bytes = shape.Picture.Bytes.ToArray(),
                ContentType = shape.Picture.ContentType,
            };
        _oldParts = CloneBytes(info.Parts);
        _oldPartContentTypes = new Dictionary<string, string>(info.PartContentTypes, StringComparer.OrdinalIgnoreCase);
        _oldSlideRels = new Dictionary<string, (string RelType, string TargetPath)>(info.SlideRels, StringComparer.Ordinal);
        if (!TryPatchRawXml(info.RawXml, _targetKind, _newSlideNumericId, _newSectionId, out var rawXml))
            return;

        info.ZoomTargetSlideNumericId = _targetKind == ZoomTargetKind.Slide ? _newSlideNumericId : null;
        info.ZoomTargetSlideId = _targetKind == ZoomTargetKind.Slide ? _newSlideId : null;
        info.ZoomTargetSectionId = _targetKind == ZoomTargetKind.Section ? _newSectionId : null;
        if (TryClearAutoPreview(rawXml, info, out var previewRelId, out var previewPath, out var clearedXml))
        {
            rawXml = clearedXml;
            info.SlideRels.Remove(previewRelId!);
            if (previewPath is not null)
                RemoveUnreferencedPart(info, previewPath);
            info.ZoomProperties = (info.ZoomProperties ?? new ZoomObjectProperties()) with
            {
                ImageType = "preview",
            };
            shape.Picture = null;
        }
        info.RawXml = rawXml;
        shape.AlternativeText = _newAlternativeText;
    }

    public void Revert(Presentation presentation)
    {
        if (!TryGetZoom(presentation, out var shape) || shape.PreservedObject is not { } info)
            return;

        info.ZoomTargetSlideNumericId = _oldSlideNumericId;
        info.ZoomTargetSlideId = _oldSlideId;
        info.ZoomTargetSectionId = _oldSectionId;
        if (_oldRawXml is not null)
            info.RawXml = _oldRawXml;
        info.ZoomProperties = _oldProperties;
        shape.Picture = _oldPicture is null
            ? null
            : new ImagePart
            {
                Bytes = _oldPicture.Bytes.ToArray(),
                ContentType = _oldPicture.ContentType,
            };
        if (_oldParts is not null)
            Restore(info.Parts, _oldParts);
        if (_oldPartContentTypes is not null)
            Restore(info.PartContentTypes, _oldPartContentTypes);
        if (_oldSlideRels is not null)
            Restore(info.SlideRels, _oldSlideRels);
        if (_oldAlternativeText is not null)
            shape.AlternativeText = _oldAlternativeText;
    }

    private bool TryGetZoom(Presentation presentation, out SlideShape shape)
    {
        shape = null!;
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return false;

        shape = FindShape(presentation.Slides[_slideIndex].Shapes, _shapeId)!;
        if (shape is not { Kind: SlideShapeKind.Zoom, PreservedObject.ObjectKind: PreservedObjectKind.Zoom }
            || shape.PreservedObject.SummaryZoomTargets.Count != 0)
            return false;

        return _targetKind == ZoomTargetKind.Slide
            ? shape.PreservedObject.ZoomTargetSlideNumericId is not null
            : !string.IsNullOrWhiteSpace(shape.PreservedObject.ZoomTargetSectionId);
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    private static bool TryPatchRawXml(
        string rawXml,
        ZoomTargetKind targetKind,
        uint? slideNumericId,
        string? sectionId,
        out string patchedXml)
    {
        patchedXml = rawXml;
        if (string.IsNullOrWhiteSpace(rawXml))
            return false;

        XElement root;
        try { root = XElement.Parse(rawXml, LoadOptions.PreserveWhitespace); }
        catch { return false; }

        var targets = targetKind == ZoomTargetKind.Slide
            ? root.Descendants().Where(element => element.Name.LocalName == "sldZmObj").ToArray()
            : root.Descendants().Where(element => element.Name.LocalName == "sectionZmObj").ToArray();
        if (targets.Length == 0)
            return false;

        foreach (var target in targets)
        {
            if (targetKind == ZoomTargetKind.Slide)
                target.SetAttributeValue("sldId", slideNumericId!.Value);
            else
                target.SetAttributeValue("sectionId", sectionId);
        }

        patchedXml = root.ToString(SaveOptions.DisableFormatting);
        return true;
    }

    private static bool TryClearAutoPreview(
        string rawXml,
        PreservedObjectInfo info,
        out string? relationshipId,
        out string? targetPath,
        out string clearedXml)
    {
        relationshipId = null;
        targetPath = null;
        clearedXml = rawXml;
        XElement root;
        try { root = XElement.Parse(rawXml, LoadOptions.PreserveWhitespace); }
        catch { return false; }

        var properties = root.Descendants().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "zmPr", StringComparison.OrdinalIgnoreCase));
        if (properties is null
            || string.Equals(properties.Attribute("imageType")?.Value, "cover", StringComparison.OrdinalIgnoreCase))
            return false;

        var blip = properties.Descendants().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "blip", StringComparison.OrdinalIgnoreCase));
        var embed = blip?.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, "embed", StringComparison.OrdinalIgnoreCase));
        if (embed is null || string.IsNullOrWhiteSpace(embed.Value)
            || !info.SlideRels.TryGetValue(embed.Value, out var relation))
            return false;

        relationshipId = embed.Value;
        targetPath = relation.TargetPath;
        embed.Remove();
        clearedXml = root.ToString(SaveOptions.DisableFormatting);
        return true;
    }

    private static void RemoveUnreferencedPart(PreservedObjectInfo info, string targetPath)
    {
        if (info.SlideRels.Values.Any(relation =>
                string.Equals(relation.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase)))
            return;

        info.Parts.Remove(targetPath);
        info.PartContentTypes.Remove(targetPath);
    }

    private static Dictionary<string, byte[]> CloneBytes(IReadOnlyDictionary<string, byte[]> source) =>
        source.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

    private static void Restore<T>(IDictionary<string, T> destination, IReadOnlyDictionary<string, T> source)
    {
        destination.Clear();
        foreach (var pair in source)
            destination[pair.Key] = pair.Value;
    }
}

/// <summary>Replaces the ordered target list of a native Summary Zoom as one undoable edit.</summary>
public sealed class SetSummaryZoomTargetsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly IReadOnlyList<SummaryZoomTarget> _newTargets;
    private readonly string _newRawXml;
    private IReadOnlyList<SummaryZoomTarget>? _oldTargets;
    private string? _oldRawXml;

    public SetSummaryZoomTargetsCommand(
        int slideIndex,
        uint shapeId,
        IReadOnlyList<SummaryZoomTarget> targets,
        string rawXml)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newTargets = targets?.ToArray()
            ?? throw new ArgumentNullException(nameof(targets));
        if (_newTargets.Count < 2)
            throw new ArgumentException("A Summary Zoom requires at least two targets.", nameof(targets));
        _newRawXml = string.IsNullOrWhiteSpace(rawXml)
            ? throw new ArgumentException("Summary Zoom XML is required.", nameof(rawXml))
            : rawXml;
    }

    public string Label => "Edit Summary Zoom Targets";

    public bool HasEffect(Presentation presentation) =>
        TryGetSummaryZoom(presentation, out var info)
        && (!info.SummaryZoomTargets.SequenceEqual(_newTargets)
            || !string.Equals(info.RawXml, _newRawXml, StringComparison.Ordinal));

    public void Apply(Presentation presentation)
    {
        if (!TryGetSummaryZoom(presentation, out var info))
            return;

        _oldTargets ??= info.SummaryZoomTargets.ToArray();
        _oldRawXml ??= info.RawXml;
        info.SummaryZoomTargets.Clear();
        info.SummaryZoomTargets.AddRange(_newTargets);
        info.RawXml = _newRawXml;
    }

    public void Revert(Presentation presentation)
    {
        if (_oldTargets is null || _oldRawXml is null || !TryGetSummaryZoom(presentation, out var info))
            return;

        info.SummaryZoomTargets.Clear();
        info.SummaryZoomTargets.AddRange(_oldTargets);
        info.RawXml = _oldRawXml;
    }

    private bool TryGetSummaryZoom(Presentation presentation, out PreservedObjectInfo info)
    {
        info = null!;
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return false;

        var shape = FindShape(presentation.Slides[_slideIndex].Shapes, _shapeId);
        if (shape is not { Kind: SlideShapeKind.Zoom, PreservedObject.ObjectKind: PreservedObjectKind.Zoom }
            || shape.PreservedObject is not { } preserved
            || preserved.SummaryZoomTargets.Count < 2)
            return false;

        info = preserved;
        return true;
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }
}
