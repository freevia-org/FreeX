using System.Reflection;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class InsertFunctionDialogTests
{
    private static char GetAccessKey(string label)
    {
        var index = label.IndexOf('_', StringComparison.Ordinal);
        while (index >= 0 && index + 1 < label.Length && label[index + 1] == '_')
            index = label.IndexOf('_', index + 2);

        index.Should().BeGreaterThanOrEqualTo(0);
        return char.ToUpperInvariant(label[index + 1]);
    }

    private static T GetPrivateControl<T>(InsertFunctionDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(InsertFunctionDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }
}
