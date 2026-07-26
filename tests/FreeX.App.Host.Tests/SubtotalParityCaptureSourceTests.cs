using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SubtotalParityCaptureSourceTests
{
    [Fact]
    public void WpfCapture_ConsumesTheSharedSubtotalFixtureState()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("var subtotalWorkbook = ParityDemoWorkbookFactory.Create();");
        source.Should().Contain("SubtotalParityFixture.ApplySheetState(subtotalSheet);");
        source.Should().Contain("var subtotalFixture = SubtotalParityFixture.CreateState(subtotalSheet);");
        source.Should().Contain("subtotalFixture.Columns");
        source.Should().Contain("subtotalFixture.SummaryBelowData");
        source.Should().Contain("subtotalFixture.CreatePlan()");
        source.Should().NotContain("CreateSubtotalChoices");
    }
}
