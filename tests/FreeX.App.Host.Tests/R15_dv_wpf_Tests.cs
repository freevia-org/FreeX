using System.Windows;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R15-data-validation-ui-3: Cancel on an Information-style AskToContinue data-validation alert
/// must discard the invalid entry and restore the previously committed value, just like Cancel
/// on a Warning-style alert does. Only "No" (Warning) leaves the invalid entry for editing, and
/// Stop-style alerts never reach AskToContinue at all.
/// </summary>
public sealed class R15_dv_wpf_Tests
{
    [Fact]
    public void Information_Cancel_ShouldRestore()
    {
        MainWindow.ShouldRestoreOnCancel(DvAlertStyle.Information, MessageBoxResult.Cancel)
            .Should().BeTrue();
    }

    [Fact]
    public void Warning_Cancel_ShouldRestore()
    {
        MainWindow.ShouldRestoreOnCancel(DvAlertStyle.Warning, MessageBoxResult.Cancel)
            .Should().BeTrue();
    }

    [Fact]
    public void Warning_No_ShouldNotRestore()
    {
        MainWindow.ShouldRestoreOnCancel(DvAlertStyle.Warning, MessageBoxResult.No)
            .Should().BeFalse();
    }

    [Fact]
    public void Stop_AnyResult_ShouldNotRestore()
    {
        MainWindow.ShouldRestoreOnCancel(DvAlertStyle.Stop, MessageBoxResult.Cancel)
            .Should().BeFalse();
        MainWindow.ShouldRestoreOnCancel(DvAlertStyle.Stop, MessageBoxResult.No)
            .Should().BeFalse();
    }

    [Fact]
    public void Information_OK_ShouldNotRestore()
    {
        MainWindow.ShouldRestoreOnCancel(DvAlertStyle.Information, MessageBoxResult.OK)
            .Should().BeFalse();
    }
}
