using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeW.App.Host.Tests;

public sealed class Wave21DialogSemanticParityTests
{
    [StaFact]
    public void Password_prompt_exposes_the_focus_target_automation_id_used_by_both_hosts()
    {
        var constructor = typeof(PasswordPromptDialog).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Window), typeof(string), typeof(string)],
            modifiers: null);
        var dialog = (PasswordPromptDialog)constructor!.Invoke([null, "Unprotect Document", "Enter the password:"]);
        var passwordBox = ((StackPanel)dialog.Content!).Children.OfType<PasswordBox>().Single();

        AutomationProperties.GetAutomationId(passwordBox).Should().Be("PasswordPromptPasswordBox");
        AutomationProperties.GetName(passwordBox).Should().Be("Enter the password:");
    }
}
