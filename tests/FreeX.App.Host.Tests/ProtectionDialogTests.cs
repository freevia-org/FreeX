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
    private static void InvokePrivate(AllowEditRangeDialog dialog, string methodName)
        => DialogSourceTestSupport.InvokePrivateHandler(dialog, methodName);

    private static string ReadProtectionDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "ProtectionDialogs.cs",
            "AllowEditRangeDialog.cs",
            "AllowEditRangeDialogPlanner.cs",
            "ProtectionDialogPlanner.cs");
}
