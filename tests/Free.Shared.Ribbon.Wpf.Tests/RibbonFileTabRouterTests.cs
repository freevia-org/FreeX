using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

namespace Free.Shared.Ribbon.Wpf.Tests;

[Trait("Category", "RibbonUiLane")]
[Collection("RibbonCommandFaultReporter")]
public sealed class RibbonFileTabRouterTests
{
    [Fact]
    public void FileTab_ContainsAndReportsBackstageCallbackFailure()
    {
        RunSta(() =>
        {
            var failure = new InvalidOperationException("backstage failed");
            (Exception Exception, string CommandId)? reported = null;
            var previousHandler = RibbonCommandFaultReporter.Handler;
            RibbonCommandFaultReporter.Handler = (exception, commandId) =>
                reported = (exception, commandId);
            try
            {
                var tabs = new TabControl();
                var fileTab = new TabItem { Header = "File" };
                var homeTab = new TabItem { Header = "Home" };
                tabs.Items.Add(fileTab);
                tabs.Items.Add(homeTab);
                tabs.SelectedIndex = 1;
                using var router = RibbonFileTabRouter.Attach(
                    tabs,
                    fileTab,
                    () => throw failure);

                var selectFile = () => tabs.SelectedIndex = 0;

                selectFile.Should().NotThrow("a backstage fault must not escape SelectionChanged");
                tabs.SelectedItem.Should().BeSameAs(homeTab);
                reported.Should().NotBeNull();
                reported!.Value.Exception.Should().BeSameAs(failure);
                reported.Value.CommandId.Should().Be("FileTab");
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
