namespace FreeX.App.Presentation.TextToColumns;

/// <summary>
/// Canonical sample data for the cross-platform Text to Columns visual-evidence route.
/// Production dialogs continue to derive their preview from the user's selected cells.
/// </summary>
public static class TextToColumnsParityFixture
{
    public const int PreviewRowLimit = 3;
    public const double WindowWidth = 560;
    public const double WindowHeight = 560;
    public const double MinimumWindowWidth = 520;
    public const double MinimumWindowHeight = 500;

    public static IReadOnlyList<string> SampleRows { get; } = Array.AsReadOnly(
    [
        "North,Widget,120",
        "South,Gadget,85",
        "East,Sprocket,200",
        "West,Gizmo,64",
    ]);
}
