namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    private static string ReadTextToColumnsDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "TextToColumnsDialog.cs",
            "TextToColumnsDialog.FixedWidth.cs",
            "TextToColumnsDialog.ColumnFormats.cs",
            "TextToColumnsDialog.Delimiters.cs",
            "TextToColumnsDialog.Wizard.cs",
            "TextToColumnsWizardPlanner.cs");
}
