namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal Func<Task>? PasteSpecialWorkflowOverrideForTest { get; set; }

    partial void ResolvePasteSpecialWorkflowOverride(ref Func<Task>? handler) =>
        handler = PasteSpecialWorkflowOverrideForTest;
}
