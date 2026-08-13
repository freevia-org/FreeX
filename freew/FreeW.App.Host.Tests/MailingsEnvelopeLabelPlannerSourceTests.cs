using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailingsEnvelopeLabelPlannerSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesEnvelopeAndLabelPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("MailingsEnvelopeLabelPlanner.CreateEnvelopeDialogPlan()");
        source.Should().Contain("MailingsEnvelopeLabelPlanner.PlanEnvelope(");
        source.Should().Contain("MailingsEnvelopeLabelPlanner.CreateLabelDialogPlan()");
        source.Should().Contain("MailingsEnvelopeLabelPlanner.PlanLabel(");
        source.Should().NotContain("MailingsEnvelopeLabelPlanner.GetEnvelopeSizes()");
        source.Should().NotContain("MailingsEnvelopeLabelPlanner.GetLabelPresets()");
        source.Should().NotContain("MailingsEnvelopeLabelPlanner.CustomLabelPresetIndex");
        source.Should().NotContain("private readonly record struct EnvelopeSize");
        source.Should().NotContain("private readonly record struct LabelPreset");
        source.Should().NotContain("private static readonly EnvelopeSize[]");
        source.Should().NotContain("private static readonly LabelPreset[]");
        source.Should().NotContain("110 * 72 / 25.4");
        source.Should().NotContain("Avery 5160");
    }

}
