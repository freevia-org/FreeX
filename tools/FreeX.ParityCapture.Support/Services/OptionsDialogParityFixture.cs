namespace FreeX.App.Services;

/// <summary>
/// Deterministic options state used only by the WPF/Avalonia parity-capture routes.
/// Keeping the fixture in shared services prevents paired screenshots from inheriting
/// different user-local options stores while leaving normal launches store-backed.
/// </summary>
public static class OptionsDialogParityFixture
{
    public static AppOptions Create() => new()
    {
        // Keep the capture semantically aligned with the production default.
        ShowFormulaBar = true,
        FormulaBarExpanded = false,
    };
}
