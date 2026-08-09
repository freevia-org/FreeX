using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.Shell;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R124: IconPickerDialog (WPF) hand-rolled unlocalized OK/Cancel buttons, bypassing the shared
/// Free.Shared.Shell.DialogButtonRowFactory/ShellStrings pipeline every other FreeW WPF dialog
/// uses (see DialogSharedHelperDedupTests). A French-locale FreeW build showed "OK"/"Cancel" on
/// the Insert Icon dialog instead of the localized "Annuler" every other WPF dialog's Cancel
/// button gets, and neither button had the Alt+ accelerator DialogButtonRowFactory.SetAcceleratorKey
/// wires for every other dialog. (ManualHyphenationDialog's matching Cancel-button bug is covered
/// by ManualHyphenationDialogParityTests.Cancel_button_uses_WPF_cancel_semantics.)
///
/// This test deliberately does NOT swap the ambient <see cref="ShellStrings.Current"/> (unlike
/// DialogButtonRowFactoryLocalizationTests) -- xUnit runs test classes in parallel by default, and
/// swapping process-wide static state here was proven to race with unrelated dialog tests (e.g.
/// FootnoteEndnoteOptionsDialogParityTests, OptionsDialogParityTests) that assert button content
/// against the untouched default. Instead it reads whatever ShellStrings.Current already is and
/// asserts the dialog's buttons resolve through it dynamically -- this still distinguishes "routes
/// through the shared pipeline" from "hardcoded English literal", because DefaultShellStrings
/// resolves "_OK"/"_Cancel" (with the WPF access-key underscore), which a literal "OK"/"Cancel"
/// never equals.
/// </summary>
public sealed class R124_DialogButtonLocalizationTests
{
    [StaFact]
    public void R124_IconPickerDialog_ButtonsResolveThroughSharedShellStrings_NotHardcodedLiteral()
    {
        var expectedOk = ShellStrings.Current.Ok;
        var expectedCancel = ShellStrings.Current.Cancel;
        var expectedOkName = ShellStrings.Current.CreateAutomationName(expectedOk);
        var expectedCancelName = ShellStrings.Current.CreateAutomationName(expectedCancel);
        var expectedOkAccelerator = ShellStringText.CreateAcceleratorKey(expectedOk);
        var expectedCancelAccelerator = ShellStringText.CreateAcceleratorKey(expectedCancel);

        var type = typeof(MainWindow).Assembly.GetType(
            "FreeW.App.Host.IconPickerDialog", throwOnError: true)!;
        var constructor = type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 1);
        var dialog = (Window)constructor.Invoke([null]);

        try
        {
            var root = (DockPanel)dialog.Content!;
            var bottomRow = root.Children.OfType<DockPanel>().Single();
            var buttons = bottomRow.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();

            buttons.Should().HaveCount(2);
            var ok = buttons.Single(b => b.IsDefault);
            var cancel = buttons.Single(b => b.IsCancel);

            // Fail-before: the unfixed dialog hardcoded literal "OK"/"Cancel" (no underscore
            // access-key marker), which never equals the DefaultShellStrings "_OK"/"_Cancel".
            ok.Content?.ToString().Should().Be(expectedOk);
            cancel.Content?.ToString().Should().Be(expectedCancel);

            AutomationProperties.GetName(ok).Should().Be(expectedOkName);
            AutomationProperties.GetName(cancel).Should().Be(expectedCancelName);

            if (!string.IsNullOrEmpty(expectedOkAccelerator))
                AutomationProperties.GetAcceleratorKey(ok).Should().Be(expectedOkAccelerator);
            if (!string.IsNullOrEmpty(expectedCancelAccelerator))
                AutomationProperties.GetAcceleratorKey(cancel).Should().Be(expectedCancelAccelerator);
        }
        finally
        {
            dialog.Close();
        }
    }

    // No-regression sibling: the refactor to DialogButtonRowFactory.Create must preserve the
    // WPF default/cancel button wiring (Enter activates OK, Esc/Cancel closes) that IconPickerDialog
    // relied on before the conversion.
    [StaFact]
    public void R124_IconPickerDialog_OkIsDefaultButton_CancelIsCancelButton_NoRegression()
    {
        var type = typeof(MainWindow).Assembly.GetType(
            "FreeW.App.Host.IconPickerDialog", throwOnError: true)!;
        var constructor = type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 1);
        var dialog = (Window)constructor.Invoke([null]);

        try
        {
            var root = (DockPanel)dialog.Content!;
            var bottomRow = root.Children.OfType<DockPanel>().Single();
            var buttons = bottomRow.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();

            buttons.Should().HaveCount(2);
            buttons.Should().ContainSingle(b => b.IsDefault && !b.IsCancel);
            buttons.Should().ContainSingle(b => b.IsCancel && !b.IsDefault);
        }
        finally
        {
            dialog.Close();
        }
    }
}
