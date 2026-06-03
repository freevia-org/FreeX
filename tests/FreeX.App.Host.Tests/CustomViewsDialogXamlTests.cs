using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class CustomViewsDialogXamlTests
{
    private static string ReadCustomViewsDialogSource()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CustomViewsDialog.xaml.cs"));
        var start = source.IndexOf("public sealed partial class CustomViewsDialog", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        return source[start..];
    }
}
