using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ConditionalFormatConvertersSourceTests
{
    [Fact]
    public void OneWayConverters_ReturnBindingDoNothingFromConvertBack()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ManageConditionalFormatsDialog.Helpers.cs");

        source.Should().Contain("Binding.DoNothing");
        source.Should().NotContain("throw new NotSupportedException()");
    }
}
