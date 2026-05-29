using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SheetNameDialogTests
{
    [Fact]
    public void SheetNameInput_ExposesStableAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SheetNameDialog("Sheet1");
            try
            {
                var nameBox = GetPrivateField<TextBox>(dialog, "_nameBox");

                AutomationProperties.GetName(nameBox).Should().Be("Sheet name");
                AutomationProperties.GetAutomationId(nameBox).Should().Be("SheetNameBox");
                AutomationProperties.GetHelpText(nameBox).Should().Be("Enter a worksheet name up to 31 characters.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static T GetPrivateField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }
}
