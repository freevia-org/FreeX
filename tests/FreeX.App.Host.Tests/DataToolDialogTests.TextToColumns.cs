using System.Reflection;
using System.Windows;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    private static string ReadTextToColumnsDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "TextToColumnsDialog.cs",
            "TextToColumnsDialog.FixedWidth.cs",
            "TextToColumnsDialog.ColumnFormats.cs",
            "TextToColumnsDialog.Delimiters.cs",
            "TextToColumnsDialog.Wizard.cs");

    private static T GetTextToColumnsField<T>(TextToColumnsDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(TextToColumnsDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeAssignableTo<T>().Subject;
    }

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
