using FreeP.App.Localization;

namespace FreeP.App.Compositor;

public enum ZoomObjectPropertiesDialogChromeAction
{
    Accept,
    Cancel,
}

public sealed record PresentationDialogChromePlan(
    string Title,
    string AcceptLabel,
    string CancelLabel,
    double Width)
{
    public string AccessibleName => $"{Title} dialog";

    public string AutomationId => "FreeP.ZoomFormat.Window";

    public PresentationDialogActionPlan<ZoomObjectPropertiesDialogChromeAction> Action(
        ZoomObjectPropertiesDialogChromeAction action) => action switch
        {
            ZoomObjectPropertiesDialogChromeAction.Accept => new(
                action,
                AcceptLabel,
                "Apply Zoom formatting",
                "FreeP.ZoomFormat.Accept",
                IsDefault: true),
            ZoomObjectPropertiesDialogChromeAction.Cancel => new(
                action,
                CancelLabel,
                "Cancel Zoom formatting",
                "FreeP.ZoomFormat.Cancel",
                IsCancel: true),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
}

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
    string ReflectionBlurLabel,
    string ReflectionDistanceLabel,
    string ReflectionDirectionLabel,
    string ReflectionScaleLabel,
    string ReflectionEndPositionLabel,
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
    IReadOnlyList<string> ImageTypeOptions,
    IReadOnlyList<ZoomObjectPropertiesDialogControlPlan> FieldCatalog);

/// <summary>
/// Renderer-neutral chrome, localized text, and stable metrics for the Zoom format dialog.
/// Hosts retain native controls, layout mechanics, focus/event wiring, and warning surfaces.
/// </summary>
public static class ZoomObjectPropertiesDialogSurfacePlanner
{
    public static ZoomObjectPropertiesDialogSurfacePlan BuildSurfacePlan()
    {
        var text = new ZoomObjectPropertiesDialogText(
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
            Loc.Get("Dialog_ZoomFormat_ReflectionBlur"),
            Loc.Get("Dialog_ZoomFormat_ReflectionDistance"),
            Loc.Get("Dialog_ZoomFormat_ReflectionDirection"),
            Loc.Get("Dialog_ZoomFormat_ReflectionScale"),
            Loc.Get("Dialog_ZoomFormat_ReflectionEndPosition"),
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
            Loc.Get("Dialog_ZoomFormat_TileScale"));
        IReadOnlyList<string> imageTypeOptions = ["preview", "cover"];

        return new ZoomObjectPropertiesDialogSurfacePlan(
            new PresentationDialogChromePlan(
                Loc.Get("Dialog_ZoomFormat_Title"),
                Loc.Get("Dialog_ZoomFormat_Accept"),
                Loc.Get("Dialog_ZoomFormat_Cancel"),
                Width: 440),
            new ZoomObjectPropertiesDialogLayoutPlan(
                ContentMargin: 14,
                LabelWidth: 160,
                InputMinWidth: 180),
            text,
            imageTypeOptions,
            [
                Choice(ZoomObjectPropertiesDialogField.ImageType, text.ImageSourceLabel,
                    imageTypeOptions.Cast<object>().ToArray()),
                Toggle(ZoomObjectPropertiesDialogField.TransitionEnabled, text.UseZoomTransitionLabel),
                Input(ZoomObjectPropertiesDialogField.TransitionDuration, text.TransitionDurationLabel),
                Toggle(ZoomObjectPropertiesDialogField.FrameBorderEnabled, text.UseZoomBorderLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderColor, text.BorderColorLabel,
                    "six-digit RGB value", "six-digit RGB value; for example 4472C4"),
                Toggle(ZoomObjectPropertiesDialogField.FrameBorderThemeEnabled,
                    text.UseThemeBorderColorLabel),
                Choice(ZoomObjectPropertiesDialogField.FrameBorderThemeColor, text.ThemeColorLabel,
                    ZoomObjectPropertiesPlanner.FrameBorderThemeColorOptions.Cast<object>().ToArray()),
                Toggle(ZoomObjectPropertiesDialogField.FrameBorderShadowEnabled,
                    text.UseOuterBorderShadowLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderShadowColor, text.ShadowColorLabel,
                    "six-digit RGB value", "six-digit RGB value; for example 404040"),
                Input(ZoomObjectPropertiesDialogField.FrameBorderShadowAlpha, text.ShadowAlphaLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderShadowBlur, text.ShadowBlurLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderShadowDistance, text.ShadowDistanceLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderShadowDirection, text.ShadowDirectionLabel),
                Toggle(ZoomObjectPropertiesDialogField.FrameBorderGlowEnabled,
                    text.UseBorderGlowLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderGlowColor, text.GlowColorLabel,
                    "six-digit RGB value", "six-digit RGB value; for example 4472C4"),
                Input(ZoomObjectPropertiesDialogField.FrameBorderGlowAlpha, text.GlowAlphaLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderGlowRadius, text.GlowRadiusLabel),
                Toggle(ZoomObjectPropertiesDialogField.FrameBorderSoftEdgeEnabled,
                    text.UseBorderSoftEdgeLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderSoftEdgeRadius, text.SoftEdgeRadiusLabel),
                Toggle(ZoomObjectPropertiesDialogField.FrameBorderReflectionEnabled,
                    text.UseBorderReflectionLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderReflectionAlpha, text.ReflectionAlphaLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderReflectionBlur, text.ReflectionBlurLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderReflectionDistance, text.ReflectionDistanceLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderReflectionDirection, text.ReflectionDirectionLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderReflectionScale, text.ReflectionScaleLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderReflectionEndPosition, text.ReflectionEndPositionLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderWidth, text.BorderWidthLabel,
                    "positive width in points", "positive width in points; for example 1.5"),
                Choice(ZoomObjectPropertiesDialogField.FrameBorderDash, text.BorderDashLabel,
                    ZoomObjectPropertiesPlanner.FrameBorderDashOptions.Cast<object>().ToArray()),
                Toggle(ZoomObjectPropertiesDialogField.FrameBorderGradientEnabled,
                    text.UseGradientBorderLabel),
                Input(ZoomObjectPropertiesDialogField.FrameBorderGradientStart, text.GradientStartLabel,
                    "start RGB value", "six-digit RGB value; for example 4472C4"),
                Input(ZoomObjectPropertiesDialogField.FrameBorderGradientEnd, text.GradientEndLabel,
                    "end RGB value", "six-digit RGB value; for example FFFFFF"),
                Input(ZoomObjectPropertiesDialogField.FrameBorderGradientAngle, text.GradientAngleLabel,
                    "angle 0-360 degrees", "linear angle in degrees from 0 to 360"),
                Toggle(ZoomObjectPropertiesDialogField.FrameBorderPatternEnabled,
                    text.UsePatternBorderLabel),
                Choice(ZoomObjectPropertiesDialogField.FrameBorderPatternPreset, text.PatternPresetLabel,
                    ZoomObjectPropertiesPlanner.FrameBorderPatternOptions.Cast<object>().ToArray()),
                Input(ZoomObjectPropertiesDialogField.FrameBorderPatternForeground,
                    text.PatternForegroundLabel, "foreground RGB value",
                    "six-digit RGB value; for example 4472C4"),
                Input(ZoomObjectPropertiesDialogField.FrameBorderPatternBackground,
                    text.PatternBackgroundLabel, "background RGB value",
                    "six-digit RGB value; for example FFFFFF"),
                Toggle(ZoomObjectPropertiesDialogField.FrameBorderNoFillEnabled,
                    text.UseNoFillBorderLabel),
                Choice(ZoomObjectPropertiesDialogField.FrameGeometry, text.FrameShapeLabel,
                    ZoomObjectPropertiesPlanner.FrameGeometryOptions.Cast<object>().ToArray()),
                Input(ZoomObjectPropertiesDialogField.CropEdges, text.PreviewCropLabel,
                    "left, top, right, bottom",
                    "left, top, right, bottom as percentages; for example 0, 5, 0, 5"),
                Choice(ZoomObjectPropertiesDialogField.SummaryTile, text.SummaryTileLabel,
                    Array.Empty<object>(), summaryOnly: true),
                Input(ZoomObjectPropertiesDialogField.SummaryOffset, text.TilePositionLabel,
                    summaryOnly: true),
                Input(ZoomObjectPropertiesDialogField.SummaryScale, text.TileScaleLabel,
                    summaryOnly: true),
                Toggle(ZoomObjectPropertiesDialogField.ApplySummaryPropertiesToAllTiles,
                    text.ApplyToAllSummaryTilesLabel, summaryOnly: true),
                Toggle(ZoomObjectPropertiesDialogField.ReturnToParent, text.ReturnToParentLabel),
                Toggle(ZoomObjectPropertiesDialogField.ShowBackground, text.ShowBackgroundLabel),
            ]);
    }

    private static ZoomObjectPropertiesDialogControlPlan Toggle(
        ZoomObjectPropertiesDialogField field,
        string label,
        bool summaryOnly = false) =>
        new(
            field,
            ZoomObjectPropertiesDialogControlKind.Toggle,
            label,
            Array.Empty<object>(),
            SummaryOnly: summaryOnly);

    private static ZoomObjectPropertiesDialogControlPlan Input(
        ZoomObjectPropertiesDialogField field,
        string label,
        string? placeholderText = null,
        string? toolTipText = null,
        bool summaryOnly = false) =>
        new(
            field,
            ZoomObjectPropertiesDialogControlKind.Text,
            label,
            Array.Empty<object>(),
            placeholderText,
            toolTipText,
            summaryOnly);

    private static ZoomObjectPropertiesDialogControlPlan Choice(
        ZoomObjectPropertiesDialogField field,
        string label,
        IReadOnlyList<object> options,
        bool summaryOnly = false) =>
        new(
            field,
            ZoomObjectPropertiesDialogControlKind.Choice,
            label,
            options,
            SummaryOnly: summaryOnly);
}
