using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
                var nameBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_nameBox");

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

    [Fact]
    public void SheetNameDialog_EscapeCancelsModalRenameAndPreservesCurrentName()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SheetNameDialog("Sheet1");
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
            {
                var source = PresentationSource.FromVisual(dialog)
                    ?? throw new InvalidOperationException("Sheet name dialog has no presentation source.");
                dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
            });

            dialog.ShowDialog().Should().BeFalse();
            dialog.IsVisible.Should().BeFalse();
            dialog.Result.Should().Be(new FreeX.App.Presentation.SheetUI.SheetNameDialogResult("Sheet1"));
        });
    }

}
