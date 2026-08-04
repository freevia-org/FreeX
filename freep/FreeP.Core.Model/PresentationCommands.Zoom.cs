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
    private readonly string? _newSectionId;
    private readonly string _newAlternativeText;
    private uint? _oldSlideNumericId;
    private string? _oldSectionId;
    private string? _oldRawXml;
    private string? _oldAlternativeText;

    public SetZoomTargetCommand(
        int slideIndex,
        uint shapeId,
        ZoomTargetKind targetKind,
        uint? slideNumericId,
        string? sectionId,
        string alternativeText)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _targetKind = targetKind;
        _newSlideNumericId = targetKind == ZoomTargetKind.Slide
            ? slideNumericId ?? throw new ArgumentNullException(nameof(slideNumericId))
            : null;
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
        _oldSectionId = info.ZoomTargetSectionId;
        _oldRawXml = info.RawXml;
        _oldAlternativeText = shape.AlternativeText;
        if (!TryPatchRawXml(info.RawXml, _targetKind, _newSlideNumericId, _newSectionId, out var rawXml))
            return;

        info.ZoomTargetSlideNumericId = _targetKind == ZoomTargetKind.Slide ? _newSlideNumericId : null;
        info.ZoomTargetSectionId = _targetKind == ZoomTargetKind.Section ? _newSectionId : null;
        info.RawXml = rawXml;
        shape.AlternativeText = _newAlternativeText;
    }

    public void Revert(Presentation presentation)
    {
        if (!TryGetZoom(presentation, out var shape) || shape.PreservedObject is not { } info)
            return;

        info.ZoomTargetSlideNumericId = _oldSlideNumericId;
        info.ZoomTargetSectionId = _oldSectionId;
        if (_oldRawXml is not null)
            info.RawXml = _oldRawXml;
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
