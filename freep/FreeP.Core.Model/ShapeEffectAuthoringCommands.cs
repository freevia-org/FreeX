namespace FreeP.Core.Model;

/// <summary>Authoritative outer-shadow values used by the shape-effects authoring command.</summary>
public readonly record struct ShapeShadowValues(
    bool Enabled,
    SrgbColor Color,
    byte Alpha,
    long BlurRadEmu,
    long DistEmu,
    double DirDeg)
{
    public static ShapeShadowValues None { get; } = new(
        Enabled: false,
        Color: SrgbColor.Black,
        Alpha: 0,
        BlurRadEmu: 0,
        DistEmu: 0,
        DirDeg: 0);
}

/// <summary>Authoritative glow values used by the shape-effects authoring command.</summary>
public readonly record struct ShapeGlowValues(
    bool Enabled,
    SrgbColor Color,
    byte Alpha,
    long RadiusEmu)
{
    public static ShapeGlowValues None { get; } = new(
        Enabled: false,
        Color: SrgbColor.Black,
        Alpha: 0,
        RadiusEmu: 0);
}

/// <summary>Authoritative soft-edge values used by the shape-effects authoring command.</summary>
public readonly record struct ShapeSoftEdgeValues(
    bool Enabled,
    long RadiusEmu)
{
    public static ShapeSoftEdgeValues None { get; } = new(
        Enabled: false,
        RadiusEmu: 0);
}

/// <summary>Authoritative top-and-bottom bevel values used by the shape-effects authoring command.</summary>
public readonly record struct ShapeBevelValues(
    bool Enabled,
    string PresetName,
    long WidthEmu,
    long HeightEmu)
{
    public static ShapeBevelValues None { get; } = new(
        Enabled: false,
        PresetName: string.Empty,
        WidthEmu: 0,
        HeightEmu: 0);
}

/// <summary>Authoritative scene/extrusion values used by the shape 3-D authoring command.</summary>
public readonly record struct Shape3dValues(
    bool Enabled,
    string CameraPreset,
    string LightRig,
    string LightRigDir,
    long ExtrusionHeightEmu,
    string PrstMaterial)
{
    public static Shape3dValues None { get; } = new(
        Enabled: false,
        CameraPreset: string.Empty,
        LightRig: string.Empty,
        LightRigDir: string.Empty,
        ExtrusionHeightEmu: 0,
        PrstMaterial: string.Empty);
}

/// <summary>Changes only a shape's outer shadow, preserving every other effect layer.</summary>
public sealed class SetShapeShadowCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ShapeShadowValues _values;
    private bool _captured;
    private ShapeEffects? _oldEffects;

    public SetShapeShadowCommand(int slideIndex, uint shapeId, ShapeShadowValues values)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _values = values;
    }

    public string Label => "Shape Shadow";

    public bool HasEffect(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
            return false;

        return ReadValues(shape.Effects) != _values;
    }

    public void Apply(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
            return;

        if (!_captured)
        {
            _captured = true;
            _oldEffects = PresentationModelCloneHelper.CloneShapeEffects(shape.Effects);
        }

        if (!_values.Enabled && shape.Effects is null)
            return;

        if (shape.Effects is null)
            shape.Effects = new ShapeEffects();

        shape.Effects.HasOuterShadow = _values.Enabled;
        shape.Effects.OuterShadowColor = _values.Color;
        shape.Effects.OuterShadowAlpha = _values.Alpha;
        shape.Effects.OuterShadowBlurRadEmu = _values.BlurRadEmu;
        shape.Effects.OuterShadowDistEmu = _values.DistEmu;
        shape.Effects.OuterShadowDirDeg = _values.DirDeg;

        if (!HasAnyEffects(shape.Effects))
            shape.Effects = null;
    }

    public void Revert(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null || !_captured)
            return;

        shape.Effects = PresentationModelCloneHelper.CloneShapeEffects(_oldEffects);
    }

    private static ShapeShadowValues ReadValues(ShapeEffects? effects) => effects is null
        ? ShapeShadowValues.None
        : new(
            effects.HasOuterShadow,
            effects.OuterShadowColor,
            effects.OuterShadowAlpha,
            effects.OuterShadowBlurRadEmu,
            effects.OuterShadowDistEmu,
            effects.OuterShadowDirDeg);

    private SlideShape? FindShape(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;

        return ShapeHelper.Find(presentation, _slideIndex, _shapeId);
    }

    private static bool HasAnyEffects(ShapeEffects effects) =>
        effects.HasOuterShadow ||
        effects.HasInnerShadow ||
        effects.HasGlow ||
        effects.HasSoftEdge ||
        effects.BevelTop is not null ||
        effects.BevelBottom is not null ||
        effects.ExtrusionHeightEmu != 0 ||
        effects.ContourWidthEmu != 0 ||
        effects.Scene3d is not null ||
        !string.IsNullOrWhiteSpace(effects.PrstMaterial) ||
        effects.ExtrusionColor is not null ||
        effects.ContourColor is not null;
}

/// <summary>Changes only a shape's glow, preserving every other effect layer.</summary>
public sealed class SetShapeGlowCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ShapeGlowValues _values;
    private bool _captured;
    private ShapeEffects? _oldEffects;

    public SetShapeGlowCommand(int slideIndex, uint shapeId, ShapeGlowValues values)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _values = values;
    }

    public string Label => "Shape Glow";

    public bool HasEffect(Presentation presentation)
    {
        var shape = FindShape(presentation);
        return shape is not null && ReadValues(shape.Effects) != _values;
    }

    public void Apply(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
            return;

        if (!_captured)
        {
            _captured = true;
            _oldEffects = PresentationModelCloneHelper.CloneShapeEffects(shape.Effects);
        }

        if (!_values.Enabled && shape.Effects is null)
            return;

        if (shape.Effects is null)
            shape.Effects = new ShapeEffects();

        shape.Effects.HasGlow = _values.Enabled;
        shape.Effects.GlowColor = _values.Color;
        shape.Effects.GlowAlpha = _values.Alpha;
        shape.Effects.GlowRadiusEmu = _values.RadiusEmu;

        if (!HasAnyEffects(shape.Effects))
            shape.Effects = null;
    }

    public void Revert(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null || !_captured)
            return;

        shape.Effects = PresentationModelCloneHelper.CloneShapeEffects(_oldEffects);
    }

    private static ShapeGlowValues ReadValues(ShapeEffects? effects) => effects is null
        ? ShapeGlowValues.None
        : new(
            effects.HasGlow,
            effects.GlowColor,
            effects.GlowAlpha,
            effects.GlowRadiusEmu);

    private SlideShape? FindShape(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;

        return ShapeHelper.Find(presentation, _slideIndex, _shapeId);
    }

    private static bool HasAnyEffects(ShapeEffects effects) =>
        effects.HasOuterShadow ||
        effects.HasInnerShadow ||
        effects.HasGlow ||
        effects.HasSoftEdge ||
        effects.BevelTop is not null ||
        effects.BevelBottom is not null ||
        effects.ExtrusionHeightEmu != 0 ||
        effects.ContourWidthEmu != 0 ||
        effects.Scene3d is not null ||
        !string.IsNullOrWhiteSpace(effects.PrstMaterial) ||
        effects.ExtrusionColor is not null ||
        effects.ContourColor is not null;
}

/// <summary>Changes only a shape's soft edge, preserving every other effect layer.</summary>
public sealed class SetShapeSoftEdgeCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ShapeSoftEdgeValues _values;
    private bool _captured;
    private ShapeEffects? _oldEffects;

    public SetShapeSoftEdgeCommand(int slideIndex, uint shapeId, ShapeSoftEdgeValues values)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _values = values;
    }

    public string Label => "Shape Soft Edge";

    public bool HasEffect(Presentation presentation)
    {
        var shape = FindShape(presentation);
        return shape is not null && ReadValues(shape.Effects) != _values;
    }

    public void Apply(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
            return;

        if (!_captured)
        {
            _captured = true;
            _oldEffects = PresentationModelCloneHelper.CloneShapeEffects(shape.Effects);
        }

        if (!_values.Enabled && shape.Effects is null)
            return;

        if (shape.Effects is null)
            shape.Effects = new ShapeEffects();

        shape.Effects.HasSoftEdge = _values.Enabled;
        shape.Effects.SoftEdgeRadEmu = _values.RadiusEmu;

        if (!HasAnyEffects(shape.Effects))
            shape.Effects = null;
    }

    public void Revert(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null || !_captured)
            return;

        shape.Effects = PresentationModelCloneHelper.CloneShapeEffects(_oldEffects);
    }

    private static ShapeSoftEdgeValues ReadValues(ShapeEffects? effects) => effects is null
        ? ShapeSoftEdgeValues.None
        : new(effects.HasSoftEdge, effects.SoftEdgeRadEmu);

    private SlideShape? FindShape(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;

        return ShapeHelper.Find(presentation, _slideIndex, _shapeId);
    }

    private static bool HasAnyEffects(ShapeEffects effects) =>
        effects.HasOuterShadow ||
        effects.HasInnerShadow ||
        effects.HasGlow ||
        effects.HasSoftEdge ||
        effects.BevelTop is not null ||
        effects.BevelBottom is not null ||
        effects.ExtrusionHeightEmu != 0 ||
        effects.ContourWidthEmu != 0 ||
        effects.Scene3d is not null ||
        !string.IsNullOrWhiteSpace(effects.PrstMaterial) ||
        effects.ExtrusionColor is not null ||
        effects.ContourColor is not null;
}

/// <summary>Changes both faces of a shape's bevel, preserving every other effect layer.</summary>
public sealed class SetShapeBevelCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ShapeBevelValues _values;
    private bool _captured;
    private ShapeEffects? _oldEffects;

    public SetShapeBevelCommand(int slideIndex, uint shapeId, ShapeBevelValues values)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _values = values;
    }

    public string Label => "Shape Bevel";

    public bool HasEffect(Presentation presentation)
    {
        var shape = FindShape(presentation);
        return shape is not null && !Matches(shape.Effects);
    }

    public void Apply(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
            return;

        if (!_captured)
        {
            _captured = true;
            _oldEffects = PresentationModelCloneHelper.CloneShapeEffects(shape.Effects);
        }

        if (!_values.Enabled && shape.Effects is null)
            return;

        if (shape.Effects is null)
            shape.Effects = new ShapeEffects();

        shape.Effects.BevelTop = _values.Enabled
            ? new BevelInfo
            {
                PresetName = _values.PresetName,
                WidthEmu = _values.WidthEmu,
                HeightEmu = _values.HeightEmu,
            }
            : null;
        shape.Effects.BevelBottom = _values.Enabled
            ? new BevelInfo
            {
                PresetName = _values.PresetName,
                WidthEmu = _values.WidthEmu,
                HeightEmu = _values.HeightEmu,
            }
            : null;

        if (!HasAnyEffects(shape.Effects))
            shape.Effects = null;
    }

    public void Revert(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null || !_captured)
            return;

        shape.Effects = PresentationModelCloneHelper.CloneShapeEffects(_oldEffects);
    }

    private bool Matches(ShapeEffects? effects)
    {
        if (!_values.Enabled)
            return effects?.BevelTop is null && effects?.BevelBottom is null;

        return Matches(effects?.BevelTop) && Matches(effects?.BevelBottom);

        bool Matches(BevelInfo? bevel) =>
            bevel is not null &&
            bevel.PresetName == _values.PresetName &&
            bevel.WidthEmu == _values.WidthEmu &&
            bevel.HeightEmu == _values.HeightEmu;
    }

    private SlideShape? FindShape(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;

        return ShapeHelper.Find(presentation, _slideIndex, _shapeId);
    }

    private static bool HasAnyEffects(ShapeEffects effects) =>
        effects.HasOuterShadow ||
        effects.HasInnerShadow ||
        effects.HasGlow ||
        effects.HasSoftEdge ||
        effects.BevelTop is not null ||
        effects.BevelBottom is not null ||
        effects.ExtrusionHeightEmu != 0 ||
        effects.ContourWidthEmu != 0 ||
        effects.Scene3d is not null ||
        !string.IsNullOrWhiteSpace(effects.PrstMaterial) ||
        effects.ExtrusionColor is not null ||
        effects.ContourColor is not null;
}

/// <summary>Changes only a shape's scene/extrusion 3-D layer, preserving other effects.</summary>
public sealed class SetShape3dCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly Shape3dValues _values;
    private bool _captured;
    private ShapeEffects? _oldEffects;

    public SetShape3dCommand(int slideIndex, uint shapeId, Shape3dValues values)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _values = values;
    }

    public string Label => "Shape 3-D";

    public bool HasEffect(Presentation presentation)
    {
        var shape = FindShape(presentation);
        return shape is not null && !Matches(shape.Effects);
    }

    public void Apply(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null)
            return;

        if (!_captured)
        {
            _captured = true;
            _oldEffects = PresentationModelCloneHelper.CloneShapeEffects(shape.Effects);
        }

        if (!_values.Enabled && shape.Effects is null)
            return;

        if (shape.Effects is null)
            shape.Effects = new ShapeEffects();

        shape.Effects.Scene3d = _values.Enabled
            ? new Scene3dInfo
            {
                CameraPreset = _values.CameraPreset,
                LightRig = _values.LightRig,
                LightRigDir = _values.LightRigDir,
            }
            : null;
        shape.Effects.ExtrusionHeightEmu = _values.Enabled ? _values.ExtrusionHeightEmu : 0;
        shape.Effects.PrstMaterial = _values.Enabled ? _values.PrstMaterial : string.Empty;
        if (!_values.Enabled)
        {
            shape.Effects.ContourWidthEmu = 0;
            shape.Effects.ExtrusionColor = null;
            shape.Effects.ContourColor = null;
        }

        if (!HasAnyEffects(shape.Effects))
            shape.Effects = null;
    }

    public void Revert(Presentation presentation)
    {
        var shape = FindShape(presentation);
        if (shape is null || !_captured)
            return;

        shape.Effects = PresentationModelCloneHelper.CloneShapeEffects(_oldEffects);
    }

    private bool Matches(ShapeEffects? effects)
    {
        if (!_values.Enabled)
        {
            return effects is null ||
                (effects.Scene3d is null
                && effects.ExtrusionHeightEmu == 0
                && effects.ContourWidthEmu == 0
                && string.IsNullOrWhiteSpace(effects.PrstMaterial)
                && effects.ExtrusionColor is null
                && effects.ContourColor is null);
        }

        var scene = effects?.Scene3d;
        return effects is not null
            && scene is not null
            && scene.CameraPreset == _values.CameraPreset
            && scene.LightRig == _values.LightRig
            && scene.LightRigDir == _values.LightRigDir
            && effects.ExtrusionHeightEmu == _values.ExtrusionHeightEmu
            && effects.PrstMaterial == _values.PrstMaterial;
    }

    private SlideShape? FindShape(Presentation presentation)
    {
        if (_slideIndex < 0 || _slideIndex >= presentation.Slides.Count)
            return null;

        return ShapeHelper.Find(presentation, _slideIndex, _shapeId);
    }

    private static bool HasAnyEffects(ShapeEffects effects) =>
        effects.HasOuterShadow ||
        effects.HasInnerShadow ||
        effects.HasGlow ||
        effects.HasSoftEdge ||
        effects.BevelTop is not null ||
        effects.BevelBottom is not null ||
        effects.ExtrusionHeightEmu != 0 ||
        effects.ContourWidthEmu != 0 ||
        effects.Scene3d is not null ||
        !string.IsNullOrWhiteSpace(effects.PrstMaterial) ||
        effects.ExtrusionColor is not null ||
        effects.ContourColor is not null;
}
