using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ConditionalFormatConvertersSourceTests
{
    [Fact]
    public void OneWayConverters_ReturnBindingDoNothingFromConvertBack()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src",
            "Freexcel.App.Host",
            "ManageConditionalFormatsDialog.Helpers.cs"));

        source.Should().Contain("Binding.DoNothing");
        source.Should().NotContain("throw new NotSupportedException()");
    }
}
