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
            "AllowEditRangeDialog.cs");

    private static Rect BoundsRelativeTo(FrameworkElement root, FrameworkElement element) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static void AssertInside(FrameworkElement root, FrameworkElement element)
    {
        var bounds = BoundsRelativeTo(root, element);

        bounds.Left.Should().BeGreaterThanOrEqualTo(-0.5);
        bounds.Top.Should().BeGreaterThanOrEqualTo(-0.5);
        bounds.Right.Should().BeLessThanOrEqualTo(root.ActualWidth + 0.5);
        bounds.Bottom.Should().BeLessThanOrEqualTo(root.ActualHeight + 0.5);
    }
}
