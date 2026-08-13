using FreeP.App.Avalonia;

namespace FreeP.VisualEvidence.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (AvaloniaWholeWindowVisualEvidenceCapture.TryParse(
                args,
                out var outputRoot,
                out var scenarioId,
                out var error))
        {
            return Run(error, mainWindow =>
                AvaloniaWholeWindowVisualEvidenceCapture.Start(mainWindow, outputRoot!, scenarioId));
        }

        if (AvaloniaDialogPaneVisualEvidenceCapture.TryParse(
                args,
                out outputRoot,
                out scenarioId,
                out error))
        {
            return Run(error, mainWindow =>
                AvaloniaDialogPaneVisualEvidenceCapture.Start(mainWindow, outputRoot!, scenarioId));
        }

        Console.Error.WriteLine(
            "Expected --dialog-pane-visual-evidence-output or --whole-window-visual-evidence-output.");
        return 2;
    }

    private static int Run(string? error, Action<MainWindow> coordinator)
    {
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        return FreeP.App.Avalonia.Program.RunToolHost(coordinator);
    }
}
