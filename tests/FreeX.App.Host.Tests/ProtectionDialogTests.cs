using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ProtectionDialogTests
{
    private static T GetPrivateField<T>(object instance, string name)
        where T : class =>
        DialogSourceTestSupport.GetPrivateField<T>(instance, name);

    private static void InvokePrivate(AllowEditRangeDialog dialog, string methodName)
    {
        var method = typeof(AllowEditRangeDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
    }

    private static string ReadProtectionDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "ProtectionDialogs.cs",
            "AllowEditRangeDialog.cs",
            "AllowEditRangeDialogPlanner.cs",
            "ProtectionDialogPlanner.cs");
}
