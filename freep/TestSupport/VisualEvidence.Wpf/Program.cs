namespace FreeP.VisualEvidence.Wpf;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (WpfWholeWindowVisualEvidenceCapture.TryRun(args, out var exitCode))
            return exitCode;
        if (WpfDialogPaneVisualEvidenceCapture.TryRun(args, out exitCode))
            return exitCode;

        Console.Error.WriteLine(
            "Expected --dialog-pane-visual-evidence-output or --whole-window-visual-evidence-output.");
        return 2;
    }
}
