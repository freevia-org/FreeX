using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Free.Shared.Ribbon;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class BackstagePaneComposerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActionPane_ContainsAndReportsCallbackFailure(bool useLinkActionRows)
    {
        RunSta(() =>
        {
            var failure = new InvalidOperationException("export failed");
            (Exception Exception, string CommandId)? reported = null;
            var previousHandler = RibbonCommandFaultReporter.Handler;
            RibbonCommandFaultReporter.Handler = (exception, commandId) =>
                reported = (exception, commandId);
            try
            {
                var composer = new BackstagePaneComposer(
                    new BackstageVisualKit(System.Windows.Media.Colors.Blue, 150, 190),
                    BackstagePaneComposerProfile.Default with { UseLinkActionRows = useLinkActionRows });
                var pane = composer.BuildActionPane(new BackstageActionPaneSpec(
                    "Export",
                    "Create a copy.",
                    [new BackstageActionGroup(
                        "PDF",
                        [new BackstageActionRow("Export PDF", "Publish.", () => throw failure)
                        {
                            AutomationId = "ExportPdfAction",
                        }])]));
                var button = Descendants(pane).OfType<Button>().Single();

                var click = () => button.RaiseEvent(
                    new RoutedEventArgs(ButtonBase.ClickEvent, button));

                click.Should().NotThrow("an action fault must not escape the WPF click boundary");
                reported.Should().NotBeNull();
                reported!.Value.Exception.Should().BeSameAs(failure);
                reported.Value.CommandId.Should().Be("ExportPdfAction");
            }
            finally
            {
                RibbonCommandFaultReporter.Handler = previousHandler;
            }
        });
    }

    private static IEnumerable<object> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            yield return child;
            if (child is DependencyObject dependencyObject)
            {
                foreach (var descendant in Descendants(dependencyObject))
                    yield return descendant;
            }
        }
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
