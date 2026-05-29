using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.App.Host;
using Xunit;

namespace FreeX.App.Host.Tests;

public sealed class DialogFocusTests
{
    [Fact]
    public void FocusAndSelect_SelectsAllText()
    {
        StaTestRunner.Run(() =>
        {
            var textBox = new TextBox { Text = "Sheet1!A1:C10" };

            DialogFocus.FocusAndSelect(textBox);

            textBox.SelectionStart.Should().Be(0);
            textBox.SelectionLength.Should().Be(textBox.Text.Length);
        });
    }

    [Fact]
    public void FocusAndSelect_PreservesKeyboardFocusCall()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DialogFocus.cs"));

        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void FocusDefaultButton_SkipsDisabledDefaultButtons()
    {
        StaTestRunner.Run(() =>
        {
            var disabledDefault = new Button { Content = "Disabled", IsDefault = true, IsEnabled = false };
            var enabledDefault = new Button { Content = "Enabled", IsDefault = true };
            var window = new Window
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        disabledDefault,
                        new StackPanel { Children = { enabledDefault } }
                    }
                },
                Width = 240,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };

            try
            {
                window.Show();
                PumpDispatcher();

                StatusDialogKeyboardFocus.FocusDefaultButton(window);
                PumpDispatcher();

                Keyboard.FocusedElement.Should().BeSameAs(enabledDefault);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
