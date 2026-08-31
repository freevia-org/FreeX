using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace Free.Shared.Ribbon.Wpf.Tests;

[Trait("Category", "RibbonUiLane")]
[Collection("RibbonCommandFaultReporter")]
public sealed class QuickAccessToolbarTests
{
    [Fact]
    public void BuildButton_ContainsAndReportsClickCallbackFailure()
    {
        RunSta(() =>
        {
            var failure = new InvalidOperationException("command failed");
            (Exception Exception, string CommandId)? reported = null;
            var previousHandler = RibbonCommandFaultReporter.Handler;
            RibbonCommandFaultReporter.Handler = (exception, commandId) =>
                reported = (exception, commandId);
            try
            {
                var button = QuickAccessToolbarRenderer.BuildButton(
                    new Grid(),
                    new QuickAccessToolbarItem("save", "Save", RibbonCommandIconKind.Save),
                    _ => throw failure);

                var click = () => button.RaiseEvent(
                    new RoutedEventArgs(ButtonBase.ClickEvent, button));

                click.Should().NotThrow("a QAT command fault must not escape the WPF click boundary");
                reported.Should().NotBeNull();
                reported!.Value.Exception.Should().BeSameAs(failure);
                reported.Value.CommandId.Should().Be("save");
            }
            finally
            {
                RibbonCommandFaultReporter.Handler = previousHandler;
            }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
