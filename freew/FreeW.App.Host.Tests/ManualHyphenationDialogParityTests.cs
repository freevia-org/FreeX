using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host.Tests;

public sealed class ManualHyphenationDialogParityTests
{
    [StaFact]
    public void Cancel_button_uses_WPF_cancel_semantics()
    {
        var type = typeof(FreeW.App.Host.MainWindow).Assembly.GetType(
            "FreeW.App.Host.ManualHyphenationDialog", throwOnError: true)!;
        var constructor = type.GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 2);
        var dialog = (Window)constructor.Invoke([null, Candidate()]);

        try
        {
            var content = (StackPanel)dialog.Content!;
            var buttons = content.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
            buttons.Should().ContainSingle(button => button.IsDefault && button.Content != null && button.Content.ToString() == "_Yes");
            // Cancel is routed through the shared Free.Shared.Shell.ShellStrings pipeline (same
            // ambient source DialogButtonRowFactory reads) rather than a hardcoded English literal,
            // so a French-locale build shows "_Annuler" here instead of "Cancel". See R124_*
            // in ManualHyphenationDialogLocalizationTests.cs for the localized-content proof.
            buttons.Should().ContainSingle(button => button.IsCancel && button.Content != null
                && button.Content.ToString() == Free.Shared.Shell.ShellStrings.Current.Cancel);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static ManualHyphenationCandidate Candidate() =>
        new(1, "characterization", [new ManualHyphenationOption(5, "char-acterization")]);

}
