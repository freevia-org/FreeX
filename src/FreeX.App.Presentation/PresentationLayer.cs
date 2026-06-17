namespace FreeX.App.Presentation;

/// <summary>
/// Marker for the portable (net10.0) presentation layer: cross-platform view-model + layout-model
/// code shared by the desktop hosts (the Windows and macOS shells). No UI-framework types live here
/// — only domain models and pure layout/evaluation math — so any renderer, platform, or app can
/// consume it. See docs/planning/macos-parity-roadmap.md (M2). Concrete models are added per vertical.
/// </summary>
public static class PresentationLayer
{
    /// <summary>Stable identifier for diagnostics/tests; confirms the portable layer is wired in.</summary>
    public const string Name = "FreeX.App.Presentation";
}
