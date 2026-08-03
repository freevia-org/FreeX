using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.Shell;

namespace FreeW.App.Host.Tests;

public sealed class DialogButtonRowFactoryLocalizationTests : IDisposable
{
    private readonly IShellStrings _originalShellStrings = ShellStrings.Current;

    public void Dispose() => ShellStrings.Current = _originalShellStrings;

    [StaFact]
    public void Shared_display_labels_resolve_through_localized_shell_strings_and_accelerators()
    {
        ShellStrings.Current = new StaticShellStrings(ok: "_Daccord", cancel: "_Annuler");

        var row = DialogButtonRowFactory.Create(
            () => { },
            buttonWidth: 80,
            acceptContent: "OK",
            cancelContent: "Cancel");
        var buttons = row.Children.OfType<Button>().ToArray();

        buttons.Select(button => button.Content?.ToString())
            .Should().Equal("_Daccord", "_Annuler");
        AutomationProperties.GetAcceleratorKey(buttons[0]).Should().Be("Alt+D");
        AutomationProperties.GetAcceleratorKey(buttons[1]).Should().Be("Alt+A");
    }
}
