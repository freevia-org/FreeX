using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    private static string ReadTextToColumnsDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.FixedWidth.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.ColumnFormats.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.Delimiters.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.Wizard.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsWizardPlanner.cs")));
}
