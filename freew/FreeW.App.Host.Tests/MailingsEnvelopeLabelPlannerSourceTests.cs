using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailingsEnvelopeLabelPlannerSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesEnvelopeAndLabelPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("MailingsEnvelopeLabelPlanner.GetEnvelopeSizes()");
        source.Should().Contain("MailingsEnvelopeLabelPlanner.PlanEnvelope(");
        source.Should().Contain("MailingsEnvelopeLabelPlanner.GetLabelPresets()");
        source.Should().Contain("MailingsEnvelopeLabelPlanner.PlanLabel(");
        source.Should().Contain("MailingsEnvelopeLabelPlanner.CustomLabelPresetIndex");
        source.Should().NotContain("private readonly record struct EnvelopeSize");
        source.Should().NotContain("private readonly record struct LabelPreset");
        source.Should().NotContain("private static readonly EnvelopeSize[]");
        source.Should().NotContain("private static readonly LabelPreset[]");
        source.Should().NotContain("110 * 72 / 25.4");
        source.Should().NotContain("Avery 5160");
    }

}
