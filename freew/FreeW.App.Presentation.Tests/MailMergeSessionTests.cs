using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeSessionTests
{
    [Fact]
    public void Load_AutoMatchesFieldsAndResetsPreviewState()
    {
        var session = new MailMergeSession
        {
            Template = TextDocument.CreateEmpty(),
            CurrentIndex = 4,
        };
        var data = MergeData.FromCsv("FirstName,LastName,Address1,City\nAda,Lovelace,12 St James Square,London");

        var loaded = session.Load(data);

        loaded.Should().BeSameAs(data);
        session.Data.Should().BeSameAs(data);
        session.Template.Should().BeNull();
        session.CurrentIndex.Should().Be(0);
        session.Mapping![FieldRole.FirstName].Should().Be("FirstName");
        session.Mapping[FieldRole.LastName].Should().Be("LastName");
    }

    [Fact]
    public void EndPreview_ReturnsEditableTemplateAndResetsCursor()
    {
        var template = TextDocument.CreateEmpty();
        var session = new MailMergeSession
        {
            Template = template,
            CurrentIndex = 4,
        };

        session.EndPreview().Should().BeSameAs(template);
        session.Template.Should().BeNull();
        session.CurrentIndex.Should().Be(0);
        session.EndPreview().Should().BeNull();
    }

    [Theory]
    [InlineData("FirstName", " MERGEFIELD FirstName \\* MERGEFORMAT ", "«FirstName»")]
    [InlineData("«Postal Code»", " MERGEFIELD \"Postal Code\" \\* MERGEFORMAT ", "«Postal Code»")]
    public void FieldAuthoringPlan_ProducesNativeInstructionAndCachedLabel(
        string input,
        string instruction,
        string cachedLabel)
    {
        var plan = MailMergeFieldAuthoringPlanner.CreateMergeFieldPlan(input);

        plan.Should().NotBeNull();
        plan!.Field.Instruction.Should().Be(instruction);
        plan.CachedLabel.Should().Be(cachedLabel);
    }

    [Theory]
    [InlineData(MailMerge.NextRecordField, MailMerge.NextRecordInstruction)]
    [InlineData(MailMerge.MergeRecordNumberField, MailMerge.MergeRecordNumberInstruction)]
    [InlineData(MailMerge.MergeSequenceNumberField, MailMerge.MergeSequenceNumberInstruction)]
    public void SpecialFieldAuthoringPlan_ProducesNativeFieldAndCachedLabel(
        string label,
        string instruction)
    {
        var plan = MailMergeFieldAuthoringPlanner.CreateSpecialFieldPlan(label);

        plan.Should().NotBeNull();
        plan!.Field.Instruction.Should().Be(instruction);
        plan.CachedLabel.Should().Be($"{MailMerge.FieldOpen}{label}{MailMerge.FieldClose}");
    }

    [Fact]
    public void FieldAuthoringPlan_RejectsBlankAndUnsupportedLabels()
    {
        MailMergeFieldAuthoringPlanner.CreateMergeFieldPlan(" ").Should().BeNull();
        MailMergeFieldAuthoringPlanner.CreateSpecialFieldPlan("unsupported").Should().BeNull();
    }

    [Fact]
    public void CompositeFieldAuthoringPlans_UseTheSameTypedContract()
    {
        var address = MailMergeFieldAuthoringPlanner.CreateAddressBlockPlan();
        var greeting = MailMergeFieldAuthoringPlanner.CreateGreetingLinePlan();

        address.Field.Keyword.Should().Be("ADDRESSBLOCK");
        address.CachedLabel.Should().Be(
            $"{MailMerge.FieldOpen}AddressBlock{MailMerge.FieldClose}");
        greeting.Field.Keyword.Should().Be("GREETINGLINE");
        greeting.CachedLabel.Should().Be(
            $"{MailMerge.FieldOpen}GreetingLine{MailMerge.FieldClose}");
    }

    [Fact]
    public void BuildAugmentedData_PreservesSyntheticCompositeColumns()
    {
        var session = new MailMergeSession();
        session.Load(MergeData.FromCsv(
            "FirstName,LastName,Address1,City\nAda,Lovelace,12 St James Square,London\nGrace,Hopper,1 Navy Way,Arlington"));

        var augmented = session.BuildAugmentedData([1]);

        augmented.Header.Should().ContainInOrder(
            "FirstName",
            "LastName",
            "Address1",
            "City",
            "AddressBlock",
            "GreetingLine");
        augmented.Rows.Should().ContainSingle();
        augmented.Rows[0]["AddressBlock"].Should().Contain("1 Navy Way").And.Contain("Arlington");
        augmented.Rows[0]["GreetingLine"].Should().Be("Dear Grace Hopper,");
    }

    [Fact]
    public void BuildLabelCellContents_SkippedRecipientDoesNotConsumeCell()
    {
        var skip = MergeRuleEvaluator.BuildSkipRecordIfInstruction(
            "Skip",
            MergeConditionOperator.Equal,
            "Yes");
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph(
            $"{MailMerge.FieldOpen}{skip}{MailMerge.FieldClose}" +
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}"));
        var session = new MailMergeSession();
        session.Load(MergeData.FromCsv("Name,Skip\nAda,Yes\nGrace,No"));

        var contents = session.BuildLabelCellContents(template, capacity: 2);

        contents.Should().ContainSingle();
        contents[0].Should().ContainSingle();
        contents[0][0].PlainText.Should().Be("Grace");
    }

    [Fact]
    public void PlatformHosts_ConsumeSharedSessionWithoutRendererOwnedCopies()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var presentation = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "MailMergeSession.cs"));
        var workflow = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "MailMergeSessionWorkflow.cs"));
        var wpf = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "MailMergeEngine.cs"));

        presentation.Should().Contain("public sealed class MailMergeSession");
        presentation.Should().Contain("BuildAugmentedData");
        presentation.Should().Contain("BuildLabelCellContents");
        presentation.Should().Contain("public TextDocument? EndPreview()");
        wpf.Should().NotContain("class MailMergeSession");
        avalonia.Should().NotContain("class MailMergeSession");
        workflow.Should().Contain("Session.BuildAugmentedData(finishPlan.RowIndexes)");
        workflow.Should().Contain("MailMerge.MergeAllWithRules(template, augmentedData, state, recordNumbers)");
        wpf.Should().Contain("workflow.BuildFinish(template, finishPlan, mergeState)");
        avalonia.Should().Contain("_workflow.BuildFinish(_editor.Document, finishPlan, mergeState)");
        wpf.Should().NotContain("MailMerge.MergeAllWithRules(");
        avalonia.Should().NotContain("MailMerge.MergeAllWithRules(");
        wpf.Should().Contain("session.BuildLabelCellContents(template, rows * columns)");
        avalonia.Should().Contain("Session.BuildLabelCellContents(template, rows * columns)");
        wpf.Should().Contain("MailMergeFieldAuthoringPlanner.CreateMergeFieldPlan(name)");
        avalonia.Should().Contain("MailMergeFieldAuthoringPlanner.CreateMergeFieldPlan(name)");
        wpf.Should().NotContain("MailMerge.BuildMergeFieldInstruction(trimmed)");
        avalonia.Should().NotContain("MailMerge.BuildMergeFieldInstruction(trimmed)");
    }
}
