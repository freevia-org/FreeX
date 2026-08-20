using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeSessionWorkflowTests
{
    [Fact]
    public void LoadRecipients_EndsPreviewAndReturnsEditableTemplateForNativeLoading()
    {
        var template = DocumentWith($"Hello {MailMerge.FieldOpen}Name{MailMerge.FieldClose}");
        var session = new MailMergeSession
        {
            Template = template,
            CurrentIndex = 2,
        };
        var workflow = new MailMergeSessionWorkflow(session);
        var data = MergeData.FromCsv("Name\nAda");

        var transition = workflow.LoadRecipients(data);

        transition.DocumentToLoad.Should().BeSameAs(template);
        transition.Message.Should().Be("Loaded 1 record(s) with 1 field(s).");
        session.Data.Should().BeSameAs(data);
        session.IsPreviewing.Should().BeFalse();
        session.CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void PreviewNavigation_EntersOnceAndAlwaysRendersFromEditableTemplate()
    {
        var template = DocumentWith($"Hello {MailMerge.FieldOpen}Name{MailMerge.FieldClose}");
        var workflow = WorkflowWith("Name\nAda\nGrace");

        var next = workflow.NavigatePreview(template, MailMergePreviewNavigationAction.Next);
        var previous = workflow.NavigatePreview(
            next.DocumentToLoad!,
            MailMergePreviewNavigationAction.Previous);

        next.Success.Should().BeTrue();
        next.CurrentIndex.Should().Be(1);
        next.DocumentToLoad!.PlainText.Should().Contain("Grace");
        previous.CurrentIndex.Should().Be(0);
        previous.DocumentToLoad!.PlainText.Should().Contain("Ada");
        workflow.Session.Template.Should().BeSameAs(template);
    }

    [Fact]
    public void PreviewNavigation_MergeRecordAndSequenceReflectTheNavigatedRecord()
    {
        // MERGEREC/MERGESEQ must report the record actually being previewed, not always record 1/
        // sequence 0 -- otherwise what the user proofs in Preview Results for those fields never
        // matches what Finish & Merge prints for the same record.
        var template = DocumentWith(
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}" +
            "/" +
            $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose}");
        var workflow = WorkflowWith("Name\nAda\nGrace\nAlan");

        var next = workflow.NavigatePreview(template, MailMergePreviewNavigationAction.Next);
        var next2 = workflow.NavigatePreview(next.DocumentToLoad!, MailMergePreviewNavigationAction.Next);

        next.CurrentIndex.Should().Be(1);
        next.DocumentToLoad!.PlainText.Should().Be("2/2");
        next2.CurrentIndex.Should().Be(2);
        next2.DocumentToLoad!.PlainText.Should().Be("3/3");
    }

    [Fact]
    public void FindRecipient_RefreshesVisiblePreviewWhenARecordIsFound()
    {
        var template = DocumentWith($"Hello {MailMerge.FieldOpen}Name{MailMerge.FieldClose}");
        var workflow = WorkflowWith("Name,City\nAda,London\nGrace,Arlington");
        workflow.EnsurePreviewing(template);

        var result = workflow.FindRecipient("Arlington");

        result.Success.Should().BeTrue();
        result.Result!.Value.Index.Should().Be(1);
        result.DocumentToLoad!.PlainText.Should().Contain("Grace");
        workflow.Session.CurrentIndex.Should().Be(1);
    }

    [Fact]
    public void MovePreviewTo_ClampsDialogTargetAndRendersFromTemplate()
    {
        var template = DocumentWith($"Hello {MailMerge.FieldOpen}Name{MailMerge.FieldClose}");
        var workflow = WorkflowWith("Name\nAda\nGrace");

        var result = workflow.MovePreviewTo(template, 99);

        result.CurrentIndex.Should().Be(1);
        result.DocumentToLoad!.PlainText.Should().Contain("Grace");
        workflow.Session.Template.Should().BeSameAs(template);
    }

    [Fact]
    public void ApplyRecipientFilter_RestoresTemplateAndPreservesFieldMapping()
    {
        var template = DocumentWith($"Hello {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}");
        var workflow = WorkflowWith("FirstName\nAda\nGrace");
        var mapping = workflow.Session.Mapping;
        workflow.EnsurePreviewing(template);
        var filtered = MergeData.FromCsv("FirstName\nGrace");

        var transition = workflow.ApplyRecipientFilter(filtered);

        transition.DocumentToLoad.Should().BeSameAs(template);
        workflow.Session.Data.Should().BeSameAs(filtered);
        workflow.Session.Mapping.Should().BeSameAs(mapping);
        workflow.Session.IsPreviewing.Should().BeFalse();
    }

    [Fact]
    public void ApplyRecipientFilter_FinishMergeReportsOriginalRecipientListRowNotFilteredPosition()
    {
        // Word's Merge Record # (MERGEREC) must report each record's row number in the ORIGINAL,
        // unfiltered recipient list. Ada/Grace/Alan with Grace excluded must still print Alan=3 (his
        // true original row), not Alan=2 (his position within the filtered two-row list).
        var template = DocumentWith(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}=" +
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose};");
        var workflow = WorkflowWith("Name\nAda\nGrace\nAlan");
        workflow.SetMode(MailMergeOutputMode.Directory);
        var filtered = MergeData.FromCsv("Name\nAda\nAlan");

        workflow.ApplyRecipientFilter(filtered);
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(filtered.Count);
        var result = workflow.ExecuteFinish(template, plan);

        result.Success.Should().BeTrue();
        result.Document!.PlainText.Should().Contain("Ada=1");
        result.Document.PlainText.Should().Contain("Alan=3");
        result.Document.PlainText.Should().NotContain("Alan=2");
    }

    [Fact]
    public void ApplyRecipientFilter_FinishMergeReportsOriginalRowAcrossFreshWorkflowsPerRibbonClick()
    {
        // FreeWRibbonCommands.cs never keeps one MailMergeSessionWorkflow around: every ribbon command's
        // Execute constructs `new MailMergeSessionWorkflow(session)` fresh (13 sites), sharing only the
        // persistent MailMergeSession across clicks. Reproduce that exact pattern here -- a fresh workflow
        // per step, wrapping one shared session -- instead of driving a single long-lived workflow the way
        // the other tests in this file do, so this test cannot pass on lineage state that only survives
        // because it happens to live on the workflow instance a single test method reuses.
        var template = DocumentWith(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}=" +
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose};");
        var session = new MailMergeSession();

        new MailMergeSessionWorkflow(session).LoadRecipients(MergeData.FromCsv("Name\nAda\nGrace\nAlan"));
        new MailMergeSessionWorkflow(session).SetMode(MailMergeOutputMode.Directory);
        var filtered = MergeData.FromCsv("Name\nAda\nAlan");
        new MailMergeSessionWorkflow(session).ApplyRecipientFilter(filtered);
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(filtered.Count);
        var result = new MailMergeSessionWorkflow(session).ExecuteFinish(template, plan);

        result.Success.Should().BeTrue();
        result.Document!.PlainText.Should().Contain("Ada=1");
        result.Document.PlainText.Should().Contain("Alan=3");
        result.Document.PlainText.Should().NotContain("Alan=2");
    }

    [Fact]
    public void ExecuteFinish_UnfilteredRecipientsReportPositionAcrossFreshWorkflowsPerRibbonClick()
    {
        // Sibling/no-regression case for the fresh-workflow-per-click pattern above: with no Edit
        // Recipient List filter or sort ever applied, MERGEREC must still report each record's plain
        // 1-based position even when every step goes through its own freshly constructed workflow.
        var template = DocumentWith(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}=" +
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose};");
        var session = new MailMergeSession();

        new MailMergeSessionWorkflow(session).LoadRecipients(MergeData.FromCsv("Name\nAda\nGrace\nAlan"));
        new MailMergeSessionWorkflow(session).SetMode(MailMergeOutputMode.Directory);
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(3);
        var result = new MailMergeSessionWorkflow(session).ExecuteFinish(template, plan);

        result.Success.Should().BeTrue();
        result.Document!.PlainText.Should().Contain("Ada=1");
        result.Document.PlainText.Should().Contain("Grace=2");
        result.Document.PlainText.Should().Contain("Alan=3");
    }

    [Fact]
    public void ApplyRecipientFilter_PreviewReportsOriginalRecipientListRowNotFilteredPosition()
    {
        // Preview Results must match what Finish & Merge will print for the same record -- so it must
        // report Alan's true original row (3), not his position in the filtered two-row list (2).
        var template = DocumentWith(
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}");
        var workflow = WorkflowWith("Name\nAda\nGrace\nAlan");
        var filtered = MergeData.FromCsv("Name\nAda\nAlan");

        workflow.ApplyRecipientFilter(filtered);
        var preview = workflow.MovePreviewTo(template, 1); // Alan is at filtered-list position 1

        preview.DocumentToLoad!.PlainText.Should().Be("3");
    }

    [Fact]
    public void ApplyRecipientFilter_SortedListStillReportsOriginalRecipientListRowNumbers()
    {
        // Sorting is the other Edit Recipient List gesture that rebuilds Session.Data from scratch;
        // Merge Record # must survive a reorder the same way it survives an exclusion.
        var template = DocumentWith(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}=" +
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose};");
        var workflow = WorkflowWith("Name\nAda\nGrace\nAlan");
        workflow.SetMode(MailMergeOutputMode.Directory);
        // Descending alphabetical sort of the full list: Grace, Alan, Ada.
        var sorted = MailMergeRecipientFilterSortPlanner.Apply(
            workflow.Session.Data!,
            [0, 1, 2],
            "Name",
            ascending: false);

        workflow.ApplyRecipientFilter(sorted);
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(sorted.Count);
        var result = workflow.ExecuteFinish(template, plan);

        result.Success.Should().BeTrue();
        result.Document!.PlainText.Should().Contain("Grace=2");
        result.Document.PlainText.Should().Contain("Alan=3");
        result.Document.PlainText.Should().Contain("Ada=1");
    }

    [Fact]
    public void ExecuteFinish_UnfilteredRecipientsStillReportPositionAsRecordNumber()
    {
        // Sibling/no-regression case: with no Edit Recipient List filter or sort ever applied,
        // MERGEREC must keep reporting each record's plain 1-based position, unchanged from before.
        var template = DocumentWith(
            $"{MailMerge.FieldOpen}Name{MailMerge.FieldClose}=" +
            $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose};");
        var workflow = WorkflowWith("Name\nAda\nGrace\nAlan");
        workflow.SetMode(MailMergeOutputMode.Directory);
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(3);

        var result = workflow.ExecuteFinish(template, plan);

        result.Success.Should().BeTrue();
        result.Document!.PlainText.Should().Contain("Ada=1");
        result.Document.PlainText.Should().Contain("Grace=2");
        result.Document.PlainText.Should().Contain("Alan=3");
    }

    [Fact]
    public void ExecuteFinish_ShapesCompositeFieldsRunsRulesAndEndsDocumentPreview()
    {
        var skip = MergeRuleEvaluator.BuildSkipRecordIfInstruction(
            "Skip",
            MergeConditionOperator.Equal,
            "Yes");
        var template = DocumentWith(
            $"{MailMerge.FieldOpen}{skip}{MailMerge.FieldClose}" +
            $"{MailMerge.FieldOpen}GreetingLine{MailMerge.FieldClose} " +
            $"{MailMerge.FieldOpen}AddressBlock{MailMerge.FieldClose}");
        var workflow = WorkflowWith(
            "FirstName,LastName,Address1,City,Skip\n" +
            "Ada,Lovelace,12 St James Square,London,No\n" +
            "Grace,Hopper,1 Navy Way,Arlington,Yes");
        workflow.EnsurePreviewing(template);
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(2);

        var result = workflow.ExecuteFinish(template, plan);

        result.Success.Should().BeTrue();
        result.MergedRecordCount.Should().Be(1);
        result.SkippedRecordCount.Should().Be(1);
        result.Document!.PlainText.Should().Contain("Dear Ada Lovelace,");
        result.Document.PlainText.Should().Contain("12 St James Square");
        result.Document.PlainText.Should().NotContain("Grace");
        workflow.Session.IsPreviewing.Should().BeFalse();
    }

    [Fact]
    public void ExecuteFinish_PrinterPlanPreservesPreviewForReuse()
    {
        var template = DocumentWith($"Hello {MailMerge.FieldOpen}Name{MailMerge.FieldClose}");
        var workflow = WorkflowWith("Name\nAda");
        workflow.EnsurePreviewing(template);
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Printer,
            MailMergeRecipientScope.All,
            recordCount: 1,
            currentIndex: 0,
            fromRecordText: null,
            toRecordText: null);

        var result = workflow.ExecuteFinish(template, plan);

        result.Success.Should().BeTrue();
        workflow.Session.IsPreviewing.Should().BeTrue();
        workflow.Session.Template.Should().BeSameAs(template);
    }

    [Fact]
    public void BuildFinish_NewDocumentRemainsReusableUntilRendererCompletesIt()
    {
        var template = DocumentWith($"Hello {MailMerge.FieldOpen}Name{MailMerge.FieldClose}");
        var workflow = WorkflowWith("Name\nAda");
        workflow.EnsurePreviewing(template);
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(1);

        var result = workflow.BuildFinish(template, plan);

        workflow.Session.IsPreviewing.Should().BeTrue();
        workflow.CompleteFinish(result);
        workflow.Session.IsPreviewing.Should().BeFalse();
    }

    [Fact]
    public void BuildFinish_CancelledMergeDiscardsPartialDocument()
    {
        var template = TextDocument.CreateEmpty();
        template.Blocks.Clear();
        template.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" FILLIN \"Department\" ", "cached") }
        });
        var workflow = WorkflowWith("Name\nAda");
        var state = new MergeState { RecordPromptResolver = (_, _) => null };

        var result = workflow.BuildFinish(
            template,
            MailMergeFinishPlanner.PlanNewDocumentAllRecords(1),
            state);

        result.Success.Should().BeFalse();
        result.Document.Should().BeNull();
        result.Message.Should().Contain("cancelled");
    }

    [Fact]
    public void RouteFinishOwnsDestinationCapabilitiesAndEmailSelection()
    {
        var workflow = WorkflowWith("Name,Email\nAda,ada@example.test\nGrace,grace@example.test");
        var emailPlan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Email,
            MailMergeRecipientScope.FromTo,
            recordCount: 2,
            currentIndex: 0,
            fromRecordText: "2",
            toRecordText: "2");

        var unavailable = workflow.RouteFinish(
            emailPlan,
            printingAvailable: true,
            emailAvailable: false);
        var available = workflow.RouteFinish(
            emailPlan,
            printingAvailable: true,
            emailAvailable: true);

        unavailable.Success.Should().BeFalse();
        unavailable.Message.Should().Contain("not available");
        available.Should().Be(new MailMergeFinishRoutingPlan(
            true,
            MailMergeFinishRoute.Email,
            emailPlan.RowIndexes,
            string.Empty));
        available.EmailRecordIndexes.Should().Equal(1);
    }

    [Fact]
    public void ExecuteEmailDraftsOwnsLaunchCountingAndStatus()
    {
        var template = DocumentWith($"Hello {MailMerge.FieldOpen}Name{MailMerge.FieldClose}");
        var workflow = WorkflowWith(
            "Name,Email\nAda,ada@example.test\nGrace,grace@example.test");
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "Hello",
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.PlainText,
            MailMergeEmailRecordScope.AllRecords);
        var targets = new List<string>();

        var result = workflow.ExecuteEmailDrafts(
            template,
            intent,
            target =>
            {
                targets.Add(target);
                return targets.Count == 1;
            });

        result.Success.Should().BeTrue();
        result.LaunchedDraftCount.Should().Be(1);
        result.Execution.DraftPlan!.Drafts.Should().HaveCount(2);
        targets.Should().HaveCount(2);
        result.Message.Should().Contain("Opened 1 of 2");
        result.Message.Should().Contain("1 draft(s) could not be opened");
    }

    [Fact]
    public void PlanEmail_UsesSharedValidationAndStatusPlan()
    {
        var empty = new MailMergeSessionWorkflow();
        var workflow = WorkflowWith("Name,Email\nAda,ada@example.test");

        var missing = empty.PlanEmail();
        var result = workflow.PlanEmail();

        missing.Success.Should().BeFalse();
        missing.Message.Should().Contain("Select recipients first");
        result.Success.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Message.Should().Be(MailMergeEmailDeliveryPlanner.FormatStatus(result.Plan!));
    }

    [Fact]
    public void CheckForErrors_ProducesRendererReadyMessagesReportAndCompletionIntent()
    {
        var template = DocumentWith($"Hello {MailMerge.FieldOpen}Missing{MailMerge.FieldClose}");
        var workflow = WorkflowWith("Name\nAda");

        var report = workflow.CheckForErrors(
            template,
            MailMergeCheckForErrorsMode.SimulateAndReport);
        var pause = workflow.CheckForErrors(
            template,
            MailMergeCheckForErrorsMode.CompleteAndPause);

        report.Success.Should().BeTrue();
        report.Messages.Should().BeEmpty();
        report.ReportDocument!.Properties.Title.Should().Be("Mail Merge Error Report");
        pause.Result!.ShouldCompleteMerge.Should().BeTrue();
        pause.Messages.Should().ContainSingle().Which.Should().Contain("Missing");
    }

    [Fact]
    public void PromptPlanner_DeduplicatesRequestsAndPopulatesMergeState()
    {
        var fill = MailMergeRuleAuthoringPlanner.CreateFillInPlan("Customer code").CachedLabel;
        var ask = MailMergeRuleAuthoringPlanner.CreateAskPlan("Region", "Enter region")!.CachedLabel;
        var template = DocumentWith(fill + ask + fill);

        var requests = MailMergeInteractivePromptPlanner.Plan(template);
        var state = new MergeState();
        MailMergeInteractivePromptPlanner.ApplyResponse(state, requests[0], "A-17");
        MailMergeInteractivePromptPlanner.ApplyResponse(state, requests[1], "EMEA");

        requests.Should().HaveCount(2);
        requests[0].Should().Be(new MailMergeInteractivePrompt(
            MailMergeInteractivePromptKind.FillIn,
            "Customer code",
            "Customer code"));
        requests[1].Kind.Should().Be(MailMergeInteractivePromptKind.Ask);
        state.FillInAnswers["Customer code"].Should().Be("A-17");
        state.AskAnswers["Region"].Should().Be("EMEA");
    }

    [Fact]
    public void RuleAuthoringPlanner_ProducesNativeFieldsAndPortablePlaceholders()
    {
        var result = new MailMergeRuleIfDialogResult(
            "Balance",
            MergeConditionOperator.GreaterThan,
            "100",
            "Due",
            "Clear");

        var plan = MailMergeRuleAuthoringPlanner.CreateIfPlan(result);

        plan.Field.Instruction.Should().Be(
            MergeRuleEvaluator.BuildNativeIfField(
                result.FieldName,
                result.Operator,
                result.Value,
                result.TrueText,
                result.FalseText).Instruction);
        plan.CachedLabel.Should().Be(
            $"{MailMerge.FieldOpen}" +
            MergeRuleEvaluator.BuildIfInstruction(
                result.FieldName,
                result.Operator,
                result.Value,
                result.TrueText,
            result.FalseText) +
            $"{MailMerge.FieldClose}");
        MailMergeRuleAuthoringPlanner.CreateAskPlan(" ", "ignored").Should().BeNull();
        MailMergeRuleAuthoringPlanner.CreateSetPlan(" ", "ignored").Should().BeNull();
        MailMergeRuleAuthoringPlanner.CreateRefPlan(" ").Should().BeNull();
    }

    [Fact]
    public void RuleAuthoringPlanner_UsesOneTypedPlanForEveryNativeRule()
    {
        var condition = new MailMergeRuleConditionDialogResult(
            "Region",
            MergeConditionOperator.Equal,
            "EU");
        var plans = new[]
        {
            MailMergeRuleAuthoringPlanner.CreateIfPlan(new MailMergeRuleIfDialogResult(
                "Status",
                MergeConditionOperator.Equal,
                "Active",
                "Approved",
                "Review")),
            MailMergeRuleAuthoringPlanner.CreateConditionPlan(condition, skipRecord: true),
            MailMergeRuleAuthoringPlanner.CreateConditionPlan(condition, skipRecord: false),
            MailMergeRuleAuthoringPlanner.CreateFillInPlan("Customer code"),
            MailMergeRuleAuthoringPlanner.CreateAskPlan("CustomerCode", "Enter code")!,
            MailMergeRuleAuthoringPlanner.CreateSetPlan("CustomerCode", "A-17")!,
            MailMergeRuleAuthoringPlanner.CreateRefPlan("CustomerCode")!,
        };

        plans.Select(plan => plan.Field.Keyword).Should().Equal(
            "IF",
            "SKIPIF",
            "NEXTIF",
            "FILLIN",
            "ASK",
            "SET",
            "REF");
        plans.Should().AllSatisfy(plan =>
        {
            plan.Should().BeOfType<MailMergeFieldInsertionPlan>();
            plan.CachedLabel.Should().StartWith(MailMerge.FieldOpen.ToString());
            plan.CachedLabel.Should().EndWith(MailMerge.FieldClose.ToString());
        });
    }

    [Theory]
    [InlineData(MailMergeOperation.InsertAddressBlock, "insert an Address Block")]
    [InlineData(MailMergeOperation.PreviewRecord, "preview a record")]
    [InlineData(MailMergeOperation.FinishMerge, "Finish & Merge")]
    public void ValidationPlanner_ReturnsReusableOperationMessage(
        MailMergeOperation operation,
        string expectedText)
    {
        var validation = MailMergeValidationPlanner.Validate(null, operation);

        validation.IsValid.Should().BeFalse();
        validation.Message.Should().Contain(expectedText);
    }

    private static MailMergeSessionWorkflow WorkflowWith(string csv)
    {
        var workflow = new MailMergeSessionWorkflow();
        workflow.LoadRecipients(MergeData.FromCsv(csv));
        return workflow;
    }

    private static TextDocument DocumentWith(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        return document;
    }
}
