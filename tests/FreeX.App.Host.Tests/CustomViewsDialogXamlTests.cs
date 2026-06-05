using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class CustomViewsDialogXamlTests
{
    private static string ReadCustomViewsDialogSource()
    {
        var source = DialogSourceTestSupport.ReadHostSources("CustomViewsDialog.xaml.cs");
        var start = source.IndexOf("public sealed partial class CustomViewsDialog", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        return source[start..];
    }
}
