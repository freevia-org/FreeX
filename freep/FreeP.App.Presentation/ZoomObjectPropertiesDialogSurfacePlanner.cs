using FreeP.App.Localization;

namespace FreeP.App.Compositor;

public sealed record PresentationDialogChromePlan(
    string Title,
    string AcceptLabel,
    string CancelLabel,
    double Width);

public sealed record ZoomObjectPropertiesDialogLayoutPlan(
    double ContentMargin,
    double LabelWidth,
    double InputMinWidth);

public sealed record ZoomObjectPropertiesDialogText(
    string ReturnToParentLabel,
    string ShowBackgroundLabel,
    string UseZoomTransitionLabel,
    string UseZoomBorderLabel,
    string UseGradientBorderLabel,
    string UsePatternBorderLabel,
    string UseNoFillBorderLabel,
    string UseThemeBorderColorLabel,
    string UseOuterBorderShadowLabel,
    string UseBorderGlowLabel,
    string UseBorderSoftEdgeLabel,
    string UseBorderReflectionLabel,
    string ApplyToAllSummaryTilesLabel,
    string ImageSourceLabel,
    string TransitionDurationLabel,
    string BorderColorLabel,
    string ThemeColorLabel,
    string ShadowColorLabel,
    string ShadowAlphaLabel,
    string ShadowBlurLabel,
    string ShadowDistanceLabel,
    string ShadowDirectionLabel,
    string GlowColorLabel,
    string GlowAlphaLabel,
    string GlowRadiusLabel,
    string SoftEdgeRadiusLabel,
    string ReflectionAlphaLabel,
    string ReflectionDistanceLabel,
    string ReflectionDirectionLabel,
    string ReflectionScaleLabel,
    string BorderWidthLabel,
    string BorderDashLabel,
    string GradientStartLabel,
    string GradientEndLabel,
    string GradientAngleLabel,
    string PatternPresetLabel,
    string PatternForegroundLabel,
    string PatternBackgroundLabel,
    string FrameShapeLabel,
    string PreviewCropLabel,
    string SummaryTileLabel,
    string TilePositionLabel,
    string TileScaleLabel);

public sealed record ZoomObjectPropertiesDialogSurfacePlan(
    PresentationDialogChromePlan Chrome,
    ZoomObjectPropertiesDialogLayoutPlan Layout,
    ZoomObjectPropertiesDialogText Text,
    IReadOnlyList<string> ImageTypeOptions);

/// <summary>
/// Renderer-neutral chrome, localized text, and stable metrics for the Zoom format dialog.
/// Hosts retain native controls, layout mechanics, focus/event wiring, and warning surfaces.
/// </summary>
public static class ZoomObjectPropertiesDialogSurfacePlanner
{
    public static ZoomObjectPropertiesDialogSurfacePlan BuildSurfacePlan() =>
        new(
            new PresentationDialogChromePlan(
                Loc.Get("Dialog_ZoomFormat_Title"),
                Loc.Get("Dialog_ZoomFormat_Accept"),
                Loc.Get("Dialog_ZoomFormat_Cancel"),
                Width: 440),
            new ZoomObjectPropertiesDialogLayoutPlan(
                ContentMargin: 14,
                LabelWidth: 160,
                InputMinWidth: 180),
            new ZoomObjectPropertiesDialogText(
                Loc.Get("Dialog_ZoomFormat_ReturnToParent"),
                Loc.Get("Dialog_ZoomFormat_ShowBackground"),
                Loc.Get("Dialog_ZoomFormat_UseTransition"),
                Loc.Get("Dialog_ZoomFormat_UseBorder"),
                Loc.Get("Dialog_ZoomFormat_UseGradientBorder"),
                Loc.Get("Dialog_ZoomFormat_UsePatternBorder"),
                Loc.Get("Dialog_ZoomFormat_UseNoFillBorder"),
                Loc.Get("Dialog_ZoomFormat_UseThemeBorderColor"),
                Loc.Get("Dialog_ZoomFormat_UseOuterBorderShadow"),
                Loc.Get("Dialog_ZoomFormat_UseBorderGlow"),
                Loc.Get("Dialog_ZoomFormat_UseBorderSoftEdge"),
                Loc.Get("Dialog_ZoomFormat_UseBorderReflection"),
                Loc.Get("Dialog_ZoomFormat_ApplyToAllSummaryTiles"),
                Loc.Get("Dialog_ZoomFormat_ImageSource"),
                Loc.Get("Dialog_ZoomFormat_TransitionDuration"),
                Loc.Get("Dialog_ZoomFormat_BorderColor"),
                Loc.Get("Dialog_ZoomFormat_ThemeColor"),
                Loc.Get("Dialog_ZoomFormat_ShadowColor"),
                Loc.Get("Dialog_ZoomFormat_ShadowAlpha"),
                Loc.Get("Dialog_ZoomFormat_ShadowBlur"),
                Loc.Get("Dialog_ZoomFormat_ShadowDistance"),
                Loc.Get("Dialog_ZoomFormat_ShadowDirection"),
                Loc.Get("Dialog_ZoomFormat_GlowColor"),
                Loc.Get("Dialog_ZoomFormat_GlowAlpha"),
                Loc.Get("Dialog_ZoomFormat_GlowRadius"),
                Loc.Get("Dialog_ZoomFormat_SoftEdgeRadius"),
                Loc.Get("Dialog_ZoomFormat_ReflectionAlpha"),
                Loc.Get("Dialog_ZoomFormat_ReflectionDistance"),
                Loc.Get("Dialog_ZoomFormat_ReflectionDirection"),
                Loc.Get("Dialog_ZoomFormat_ReflectionScale"),
                Loc.Get("Dialog_ZoomFormat_BorderWidth"),
                Loc.Get("Dialog_ZoomFormat_BorderDash"),
                Loc.Get("Dialog_ZoomFormat_GradientStart"),
                Loc.Get("Dialog_ZoomFormat_GradientEnd"),
                Loc.Get("Dialog_ZoomFormat_GradientAngle"),
                Loc.Get("Dialog_ZoomFormat_PatternPreset"),
                Loc.Get("Dialog_ZoomFormat_PatternForeground"),
                Loc.Get("Dialog_ZoomFormat_PatternBackground"),
                Loc.Get("Dialog_ZoomFormat_FrameShape"),
                Loc.Get("Dialog_ZoomFormat_PreviewCrop"),
                Loc.Get("Dialog_ZoomFormat_SummaryTile"),
                Loc.Get("Dialog_ZoomFormat_TilePosition"),
                Loc.Get("Dialog_ZoomFormat_TileScale")),
            ["preview", "cover"]);
}
