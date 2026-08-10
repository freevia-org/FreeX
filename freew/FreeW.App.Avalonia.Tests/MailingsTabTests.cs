using System.Collections.Generic;
using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-MAIL: tests for the Mailings tab — the in-scope mail-merge subset over the portable
/// <see cref="MailMerge"/> engine: Select Recipients (load CSV), Insert Merge Field, Address Block /
/// Greeting Line insertion, Preview Results (record-1 values + Next/Previous stepping), Finish &amp;
/// Merge (merge to a new in-memory document), and Send E-mail Messages planning. Pure-model — no
/// headless Avalonia backend required.
/// </summary>
public sealed class MailingsTabTests
{
    private const string SampleCsv = "FirstName,LastName,City\nAda,Lovelace,London\nGrace,Hopper,New York";

    // Callbacks that supply the two optional dialog hooks from canned values so the engine's dialog-driven
    // commands run end-to-end without a real UI.
    private static RibbonHostCallbacks Callbacks(
        string? recipientCsv = null,
        string? mergeFieldName = null,
        List<string>? infoSink = null,
        MailMergeRuleIfDialogResult? ruleIf = null,
        MailMergeRuleConditionDialogResult? ruleCondition = null,
        string? rulePrompt = null,
        MailMergeRuleNameValueDialogResult? ruleNameValue = null,
        List<string>? mailDraftSink = null) =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
            SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
            OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
            ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { }, OpenWordCountDialog: () => { },
            InsertPicture: () => { }, ApplyZoom: (_, _) => { },
            AskRecipientCsv: _ => recipientCsv,
            AskMergeFieldName: _ => mergeFieldName,
            ShowMailMergeInfo: m => infoSink?.Add(m),
            AskMergeRuleIf: _ => ruleIf,
            AskMergeRuleCondition: (_, _) => ruleCondition,
            AskMergeRulePrompt: (_, _) => rulePrompt,
            AskMergeRuleNameValue: (_, _) => ruleNameValue,
            OpenMailDraft: target =>
            {
                mailDraftSink?.Add(target);
                return true;
            });

    private static DocumentView ViewWith(params Block[] blocks)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        if (blocks.Length == 0)
            doc.Blocks.Add(new Paragraph("Body"));
        else
            doc.Blocks.AddRange(blocks);
        var view = new DocumentView();
        view.LoadDocument(doc);
        return view;
    }

    private static string PlainText(TextDocument doc) =>
        string.Concat(doc.Blocks.OfType<Paragraph>().Select(p => p.PlainText + "\n"));

    private static List<Run> ComplexFields(DocumentView view) => view.Document.Blocks
        .OfType<Paragraph>()
        .SelectMany(paragraph => paragraph.Runs)
        .Where(run => run.ComplexField is not null)
        .ToList();

    // ── Select Recipients ───────────────────────────────────────────────────────────

    [Fact]
    public void LoadRecipientsCsv_populates_field_names_and_records()
    {
        var engine = new MailMergeEngine(ViewWith(), Callbacks());

        var data = engine.LoadRecipientsCsv(SampleCsv);

        data.Count.Should().Be(2, "two recipient rows");
        engine.AvailableFieldNames.Should().Equal("FirstName", "LastName", "City");
        engine.Session.Mapping.Should().NotBeNull("Select Recipients auto-matches field roles");
    }

    [Fact]
    public void SelectRecipients_via_callback_loads_data()
    {
        var engine = new MailMergeEngine(ViewWith(), Callbacks(recipientCsv: SampleCsv));

        engine.SelectRecipients();

        engine.Session.Data.Should().NotBeNull();
        engine.Session.Data!.Count.Should().Be(2);
    }

    [Fact]
    public void SelectRecipients_is_noop_when_no_callback_supplied()
    {
        var engine = new MailMergeEngine(ViewWith(), Callbacks(recipientCsv: null));

        engine.SelectRecipients(); // AskRecipientCsv returns null (cancel)

        engine.Session.Data.Should().BeNull("a cancelled / unsupplied dialog loads nothing");
    }

    // ── Insert Merge Field ────────────────────────────────────────────────────────────

    [Fact]
    public void InsertMergeFieldNamed_inserts_native_field_at_caret_and_is_undoable()
    {
        var view = ViewWith(new Paragraph("Hi "));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.InsertMergeFieldNamed("FirstName");

        var text = ((Paragraph)view.Document.Blocks[0]).PlainText;
        text.Should().Contain($"{MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}",
            "the native field retains Word's familiar cached label");
        ComplexFields(view).Should().ContainSingle();
        var field = ComplexFields(view).Single();
        field.ComplexField!.Instruction.Should().Be(" MERGEFIELD FirstName \\* MERGEFORMAT ");

        view.Undo();
        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("Hi ",
            "undo removes the inserted merge field");
    }

    [Fact]
    public void InsertMergeField_via_callback_inserts_chosen_field()
    {
        var view = ViewWith(new Paragraph(""));
        var engine = new MailMergeEngine(view, Callbacks(mergeFieldName: "City"));
        engine.LoadRecipientsCsv(SampleCsv);

        engine.InsertMergeField();

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Contain("«City»");
        ComplexFields(view).Should().ContainSingle()
            .Which.ComplexField!.Keyword.Should().Be("MERGEFIELD");
    }

    [Fact]
    public void InsertMergeFieldNamed_strips_existing_guillemets()
    {
        var view = ViewWith(new Paragraph(""));
        var engine = new MailMergeEngine(view, Callbacks());

        engine.InsertMergeFieldNamed("«LastName»");

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("«LastName»",
            "the name is normalised, not double-wrapped");
        ComplexFields(view).Single().ComplexField!.Instruction
            .Should().Be(" MERGEFIELD LastName \\* MERGEFORMAT ");
    }

    // ── Address Block / Greeting Line ───────────────────────────────────────────────────

    [Fact]
    public void InsertAddressBlock_inserts_composite_placeholder_when_recipients_loaded()
    {
        var view = ViewWith(new Paragraph(""));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.InsertAddressBlock();

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Contain("«AddressBlock»");
        ComplexFields(view).Single().ComplexField!.Instruction
            .Should().Be(" ADDRESSBLOCK \\* MERGEFORMAT ");
    }

    [Fact]
    public void InsertGreetingLine_inserts_composite_placeholder_when_recipients_loaded()
    {
        var view = ViewWith(new Paragraph(""));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.InsertGreetingLine();

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Contain("«GreetingLine»");
        ComplexFields(view).Single().ComplexField!.Instruction
            .Should().Be(" GREETINGLINE \\f \"<<_BEFORE_ Dear >><<_TITLE0_ >><<_LAST0_>><<_AFTER_ ,>>\" \\e \"Dear Sir or Madam,\" \\l 1033 \\* MERGEFORMAT ");
    }

    [Fact]
    public void CompositeRowAugmentation_PreservesExplicitSourceValues()
    {
        var view = ViewWith(new Paragraph(""));
        var engine = new MailMergeEngine(view, Callbacks());
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = "Ignored",
            ["AddressBlock"] = "Explicit address",
            ["GreetingLine"] = "Explicit greeting"
        };

        var augmented = engine.Session.AugmentRow(row);

        augmented["AddressBlock"].Should().Be("Explicit address");
        augmented["GreetingLine"].Should().Be("Explicit greeting");
    }

    [Fact]
    public void InsertAddressBlock_without_recipients_is_noop_and_emits_info()
    {
        var info = new List<string>();
        var view = ViewWith(new Paragraph(""));
        var engine = new MailMergeEngine(view, Callbacks(infoSink: info));

        engine.InsertAddressBlock();

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().BeEmpty("nothing is inserted without recipients");
        info.Should().ContainSingle().Which.Should().Contain("Select recipients first");
    }

    // ── Preview Results ────────────────────────────────────────────────────────────────

    [Fact]
    public void TogglePreview_shows_record_one_merged_values()
    {
        var view = ViewWith(new Paragraph("Hello «FirstName» «LastName» of «City»."));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.TogglePreview();

        engine.Session.IsPreviewing.Should().BeTrue("entering preview stashes the template");
        PlainText(view.Document).Should().Contain("Hello Ada Lovelace of London.",
            "record 1's values are substituted into the preview");
        PlainText(view.Document).Should().NotContain("«FirstName»", "placeholders are resolved in preview");
    }

    [Fact]
    public void TogglePreview_resolves_native_merge_field_and_restores_template()
    {
        var view = ViewWith(new Paragraph
        {
            Runs =
            {
                new Run("Hello "),
                Run.ComplexFieldRun(" MERGEFIELD FirstName \\* MERGEFORMAT ", "«FirstName»")
            }
        });
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.TogglePreview();
        PlainText(view.Document).Should().Contain("Hello Ada");
        ComplexFields(view).Should().BeEmpty("preview materializes the current recipient value");

        engine.TogglePreview();
        PlainText(view.Document).Should().Contain("Hello «FirstName»");
        ComplexFields(view).Should().ContainSingle()
            .Which.ComplexField!.Keyword.Should().Be("MERGEFIELD");
    }

    [Fact]
    public void LoadRecipientsWhilePreviewing_RestoresNativeTemplateBeforeReset()
    {
        var view = ViewWith(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" MERGEFIELD FirstName ", "«FirstName»") }
        });
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);
        engine.TogglePreview();
        PlainText(view.Document).Should().Contain("Ada");

        engine.LoadRecipientsCsv("FirstName\nMargaret");

        engine.Session.IsPreviewing.Should().BeFalse();
        PlainText(view.Document).Should().Contain("«FirstName»");
        ComplexFields(view).Should().ContainSingle()
            .Which.ComplexField!.Keyword.Should().Be("MERGEFIELD");

        engine.TogglePreview();
        PlainText(view.Document).Should().Contain("Margaret");
    }

    [Fact]
    public void NextRecord_then_PreviousRecord_steps_preview_records()
    {
        var view = ViewWith(new Paragraph("«FirstName» «LastName»"));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.TogglePreview();          // record 0 → Ada Lovelace
        engine.NextRecord();             // record 1 → Grace Hopper
        engine.Session.CurrentIndex.Should().Be(1);
        PlainText(view.Document).Should().Contain("Grace Hopper");

        engine.NextRecord();             // clamp at last record
        engine.Session.CurrentIndex.Should().Be(1, "Next clamps at the last record");

        engine.PreviousRecord();         // back to record 0 → Ada Lovelace
        engine.Session.CurrentIndex.Should().Be(0);
        PlainText(view.Document).Should().Contain("Ada Lovelace");
    }

    [Fact]
    public void TogglePreview_twice_restores_editable_template()
    {
        var view = ViewWith(new Paragraph("Hello «FirstName»"));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.TogglePreview();   // enter preview
        engine.TogglePreview();   // leave preview

        engine.Session.IsPreviewing.Should().BeFalse();
        PlainText(view.Document).Should().Contain("Hello «FirstName»",
            "leaving preview restores the un-merged template");
    }

    [Fact]
    public void EnsurePreviewingForNavigation_enters_preview_before_navigation_dialog()
    {
        var view = ViewWith(new Paragraph($"Hello {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}"));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.EnsurePreviewingForNavigation().Should().BeTrue();

        engine.Session.IsPreviewing.Should().BeTrue();
        PlainText(view.Document).Should().Contain("Hello Ada");
    }

    [Fact]
    public void ApplyFieldMapping_while_previewing_restores_template_and_exits_preview()
    {
        var field = $"{MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}";
        var view = ViewWith(new Paragraph($"Hello {field}"));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);
        engine.TogglePreview();

        engine.ApplyFieldMapping(new FieldMapping());

        engine.Session.IsPreviewing.Should().BeFalse();
        PlainText(view.Document).Should().Contain($"Hello {field}");
        engine.Session.CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void AddressBlock_and_GreetingLine_resolve_in_preview()
    {
        var view = ViewWith(new Paragraph("«GreetingLine»"), new Paragraph("«AddressBlock»"));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.TogglePreview();

        var text = PlainText(view.Document);
        text.Should().Contain("Ada", "the greeting / address block compose from record 1");
        text.Should().NotContain("«GreetingLine»");
        text.Should().NotContain("«AddressBlock»");
    }

    // ── Finish & Merge ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FinishMerge_produces_merged_document_with_all_records()
    {
        var view = ViewWith(new Paragraph("Dear «FirstName» «LastName»,"));
        view.Document.Header = new HeaderFooter("Recipient «FirstName»");
        var info = new List<string>();
        var engine = new MailMergeEngine(view, Callbacks(infoSink: info));
        engine.LoadRecipientsCsv(SampleCsv);

        var merged = engine.FinishMerge();

        merged.Should().NotBeNull();
        var text = PlainText(merged!);
        text.Should().Contain("Dear Ada Lovelace,", "record 1 is merged");
        text.Should().Contain("Dear Grace Hopper,", "record 2 is merged");
        text.Should().NotContain("«FirstName»", "all placeholders are substituted");
        merged.Sections.Should().HaveCount(2);
        merged.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("Recipient Ada");
        merged.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("Recipient Grace");
        info.Should().ContainSingle().Which.Should().Contain("Merged 2 record(s)");
    }

    [Fact]
    public void FinishMerge_resolves_composite_address_block_per_record()
    {
        var view = ViewWith(new Paragraph("«AddressBlock»"));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        var merged = engine.FinishMerge();

        var text = PlainText(merged!);
        text.Should().Contain("Ada Lovelace", "address block composes the name line for record 1");
        text.Should().Contain("Grace Hopper", "and for record 2");
    }

    [Theory]
    [InlineData(MailMergeOutputMode.Letters, 2)]
    [InlineData(MailMergeOutputMode.Directory, 1)]
    public void BuildFinishedMerge_selects_scope_applies_rules_and_preserves_template_session(
        MailMergeOutputMode mode,
        int expectedSectionCount)
    {
        var skip = MergeRuleEvaluator.BuildSkipRecordIfInstruction(
            "City", MergeConditionOperator.Equal, "Arlington");
        var template = ViewWith(new Paragraph(
            $"{Wrap(skip)}{MailMerge.FieldOpen}GreetingLine{MailMerge.FieldClose} | " +
            $"{MailMerge.FieldOpen}AddressBlock{MailMerge.FieldClose}"));
        var engine = new MailMergeEngine(template, Callbacks());
        engine.LoadRecipientsCsv(
            "FirstName,LastName,City\n" +
            "Ada,Lovelace,London\n" +
            "Grace,Hopper,New York\n" +
            "Katherine,Johnson,Arlington\n" +
            "Dorothy,Vaughan,Hampton");
        engine.Session.Mode = mode;
        engine.TogglePreview();
        engine.NextRecord();

        var visiblePreview = template.Document;
        var stashedTemplate = engine.Session.Template;
        var recipients = engine.Session.Data;
        var mapping = engine.Session.Mapping;
        var plan = MailMergeFinishPlanner.Plan(
            MailMergeFinishDestination.Printer,
            MailMergeRecipientScope.FromTo,
            recordCount: 4,
            currentIndex: 1,
            fromRecordText: "2",
            toRecordText: "4");

        var result = engine.BuildFinishedMerge(plan);

        result.Should().NotBeNull();
        result!.MergedRecordCount.Should().Be(2);
        result.SkippedRecordCount.Should().Be(1);
        var text = PlainText(result.Document);
        text.Should().Contain("Grace Hopper");
        text.Should().Contain("Dorothy Vaughan");
        text.Should().NotContain("Ada Lovelace", "record 1 is outside the selected range");
        text.Should().NotContain("Katherine Johnson", "the selected record is skipped by its merge rule");
        result.Document.Sections.Should().HaveCount(expectedSectionCount);

        template.Document.Should().BeSameAs(visiblePreview);
        engine.Session.Template.Should().BeSameAs(stashedTemplate);
        engine.Session.Data.Should().BeSameAs(recipients);
        engine.Session.Mapping.Should().BeSameAs(mapping);
        engine.Session.CurrentIndex.Should().Be(1);
        engine.Session.Mode.Should().Be(mode);
    }

    [Fact]
    public void BuildFinishedMerge_UsesHostCollectedFillInAndAskAnswersForEveryRecord()
    {
        var fillIn = MergeRuleEvaluator.BuildFillInInstruction("Department");
        var ask = MergeRuleEvaluator.BuildAskInstruction("Manager", "Who is the manager?");
        var view = ViewWith(new Paragraph(
            $"{Wrap(fillIn)} | {Wrap(ask)} | {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}"));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);
        var state = new MergeState();
        state.FillInAnswers["Department"] = "Engineering";
        state.AskAnswers["Manager"] = "Margaret";
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(2);

        var result = engine.BuildFinishedMerge(plan, state);

        result.Should().NotBeNull();
        PlainText(result!.Document).Should().Contain("Engineering | Margaret | Ada");
        PlainText(result.Document).Should().Contain("Engineering | Margaret | Grace");
        state.Bookmarks["Manager"].Should().Be("Margaret");
        engine.GetInteractiveFinishPrompts().Should().Equal(
            new MailMergeInteractivePrompt(MailMergeInteractivePromptKind.FillIn, "Department", "Department"),
            new MailMergeInteractivePrompt(MailMergeInteractivePromptKind.Ask, "Manager", "Who is the manager?"));
    }

    [Fact]
    public void BuildFinishedMerge_UsesDistinctNativePromptAnswersPerRecord()
    {
        var view = ViewWith(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(" FILLIN \"Department\" ", "cached"),
                new Run($" | {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}")
            }
        });
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);
        var state = new MergeState
        {
            RecordPromptResolver = (_, recordIndex) => $"Department {recordIndex}"
        };
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(2);

        var result = engine.BuildFinishedMerge(plan, state);

        result.Should().NotBeNull();
        PlainText(result!.Document).Should().Contain("Department 1 | Ada");
        PlainText(result.Document).Should().Contain("Department 2 | Grace");
    }

    [Fact]
    public void BuildFinishedMerge_CancelledNativePromptReturnsNoPartialDocument()
    {
        var view = ViewWith(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" FILLIN \"Department\" ", "cached") }
        });
        var engine = new MailMergeEngine(view, Callbacks());
        var template = view.Document;
        engine.LoadRecipientsCsv(SampleCsv);
        var state = new MergeState
        {
            RecordPromptResolver = (_, recordIndex) => recordIndex == 2 ? null : "Engineering"
        };
        var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(2);

        var result = engine.BuildFinishedMerge(plan, state);

        result.Should().BeNull();
        state.CancelRequested.Should().BeTrue();
        view.Document.Should().BeSameAs(template);
        PlainText(view.Document).Should().Be("cached\n");
    }

    [Fact]
    public void FinishMerge_without_recipients_is_noop_and_emits_info()
    {
        var info = new List<string>();
        var view = ViewWith(new Paragraph("Dear «FirstName»"));
        var engine = new MailMergeEngine(view, Callbacks(infoSink: info));

        var merged = engine.FinishMerge();

        merged.Should().BeNull("nothing to merge without recipients");
        info.Should().ContainSingle().Which.Should().Contain("Select recipients first");
        PlainText(view.Document).Should().Contain("«FirstName»", "the document is unchanged");
    }

    [Fact]
    public void CheckForErrors_PauseModeCompletesAfterReportingMissingFieldsAndCleanMerge()
    {
        var missingView = ViewWith(new Paragraph(
            $"Dear {MailMerge.FieldOpen}Missing{MailMerge.FieldClose}"));
        var missingEngine = new MailMergeEngine(missingView, Callbacks());
        missingEngine.LoadRecipientsCsv(SampleCsv);

        var paused = missingEngine.CheckForErrors(MailMergeCheckForErrorsMode.CompleteAndPause);

        paused!.HasErrors.Should().BeTrue();
        paused.ShouldCompleteMerge.Should().BeTrue();
        paused.ShouldPauseForErrors.Should().BeTrue();
        PlainText(missingView.Document).Should().NotContain("Missing");

        var cleanView = ViewWith(new Paragraph(
            $"Dear {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}"));
        var cleanEngine = new MailMergeEngine(cleanView, Callbacks());
        cleanEngine.LoadRecipientsCsv(SampleCsv);

        var completed = cleanEngine.CheckForErrors(MailMergeCheckForErrorsMode.CompleteAndPause);

        completed!.HasErrors.Should().BeFalse();
        completed.ShouldCompleteMerge.Should().BeTrue();
        PlainText(cleanView.Document).Should().Contain("Dear Ada");
        PlainText(cleanView.Document).Should().Contain("Dear Grace");
    }

    [Fact]
    public void PlanEmailMerge_opens_merged_default_client_draft_without_sending_or_mutating_document()
    {
        var info = new List<string>();
        var drafts = new List<string>();
        var view = ViewWith(new Paragraph("Dear «FirstName»"));
        var engine = new MailMergeEngine(view, Callbacks(infoSink: info, mailDraftSink: drafts));
        engine.LoadRecipientsCsv("FirstName,Email\nAda,ada@example.test\nGrace,");
        var before = PlainText(view.Document);
        var intent = new MailMergeEmailDeliveryIntent(
            "Email",
            "Newsletter",
            MailMergeEmailOutputFormat.MessageBody,
            MailMergeEmailBodyFormat.Html,
            MailMergeEmailRecordScope.AllRecords);

        var plan = engine.PlanEmailMerge(intent);

        plan.Should().NotBeNull();
        plan!.DeliverableRecordIndexes.Should().Equal(0);
        plan.Warnings.Should().Contain(message => message.Contains("Record 2"));
        engine.LastEmailPlan.Should().BeSameAs(plan);
        engine.LastEmailDraftPlan!.Drafts.Should().ContainSingle();
        drafts.Should().ContainSingle().Which.Should().Contain("mailto:ada@example.test");
        drafts[0].Should().Contain("body=Dear%20Ada");
        PlainText(view.Document).Should().Be(before, "opening an e-mail draft does not alter the document");
        info.Should().ContainSingle().Which.Should().Contain("Opened 1 of 1");
        info[0].Should().Contain("no messages were sent");
    }

    // ── Registry wiring ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyLabels_inserts_requested_grid_and_applies_page_setup_without_recipients()
    {
        var view = ViewWith();
        var engine = new MailMergeEngine(view, Callbacks());
        var setup = new LabelSetupResult(2, 3, 612, 792, 18, Landscape: false);

        engine.ApplyLabels(setup);

        view.Document.Page.WidthPt.Should().Be(612);
        view.Document.Page.HeightPt.Should().Be(792);
        view.Document.Page.MarginLeftPt.Should().Be(18);
        var table = view.Document.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        table.Rows.Should().HaveCount(2);
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 3);
        table.Rows.SelectMany(row => row.Cells).Should().OnlyContain(cell => cell.PlainText == string.Empty);
    }

    [Fact]
    public void ApplyLabels_populates_recipients_in_order_and_preserves_rich_runs()
    {
        var template = new Paragraph();
        template.Runs.Add(new Run("Dear ", RunFormatting.Default));
        template.Runs.Add(new Run(
            $"{MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}",
            RunFormatting.Default with { Bold = true }));
        var view = ViewWith(template);
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.ApplyLabels(new LabelSetupResult(2, 2, 612, 792, 18, Landscape: false));

        var table = view.Document.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        table.Rows[0].Cells[0].PlainText.Should().Be("Dear Ada");
        table.Rows[0].Cells[1].PlainText.Should().Be("Dear Grace");
        table.Rows[1].Cells[0].PlainText.Should().BeEmpty();
        table.Rows[0].Cells[0].Paragraphs[0].Runs[1].Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void ApplyLabels_skipped_recipient_does_not_consume_a_cell()
    {
        var skip = MergeRuleEvaluator.BuildSkipRecordIfInstruction(
            "City", MergeConditionOperator.Equal, "London");
        var view = ViewWith(new Paragraph($"{Wrap(skip)}{Wrap("FirstName")}"));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.ApplyLabels(new LabelSetupResult(1, 2, 612, 792, 18, Landscape: false));

        var table = view.Document.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        table.Rows[0].Cells[0].PlainText.Should().Be("Grace");
        table.Rows[0].Cells[1].PlainText.Should().BeEmpty();
    }

    [Fact]
    public void Registry_resolves_all_mailings_tab_commands()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), Callbacks());

        var expected = new[]
        {
            "freew.merge-envelopes",
            "freew.merge-labels",
            "freew.start-mail-merge",
            "freew.start-mail-merge-letters",
            "freew.start-mail-merge-directory",
            "freew.start-mail-merge-normal",
            "freew.merge-data",
            "freew.merge-edit-recipients",
            "freew.merge-filter-sort",
            "freew.merge-address-block",
            "freew.merge-greeting-line",
            "freew.merge-field",
            "freew.merge-match-fields",
            "freew.merge-rules",
            "freew.merge-rule-if",
            "freew.merge-rule-skip-record-if",
            "freew.merge-rule-next-record-if",
            "freew.merge-next-record",
            "freew.merge-record-number",
            "freew.merge-sequence-number",
            "freew.merge-rule-fill-in",
            "freew.merge-rule-ask",
            "freew.merge-rule-set",
            "freew.merge-rule-ref",
            "freew.merge-preview",
            "freew.merge-preview-first",
            "freew.merge-preview-previous",
            "freew.merge-preview-next",
            "freew.merge-preview-last",
            "freew.merge-find-recipient",
            "freew.merge-check-errors",
            "freew.merge-finish",
            "freew.merge-email",
            "freew.select-recipients",
            "freew.address-block",
            "freew.greeting-line",
            "freew.preview-results",
            "freew.next-record",
            "freew.prev-record",
            "freew.finish-merge",
        };

        foreach (var id in expected)
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Mailings-tab command '{id}' must be registered");
    }

    [Fact]
    public void Registry_preserves_legacy_mailings_aliases_for_canonical_commands()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), Callbacks());
        var aliases = new[]
        {
            ("freew.merge-data", "freew.select-recipients"),
            ("freew.merge-address-block", "freew.address-block"),
            ("freew.merge-greeting-line", "freew.greeting-line"),
            ("freew.merge-preview", "freew.preview-results"),
            ("freew.merge-preview-previous", "freew.prev-record"),
            ("freew.merge-preview-next", "freew.next-record"),
            ("freew.merge-finish", "freew.finish-merge"),
        };

        foreach (var (canonicalId, aliasId) in aliases)
        {
            registry.TryGet(new RibbonCommandId(canonicalId), out var canonical).Should().BeTrue();
            registry.TryGet(new RibbonCommandId(aliasId), out var alias).Should().BeTrue();
            alias.Should().BeSameAs(canonical, $"{aliasId} remains a compatibility alias for {canonicalId}");
        }
    }

    [Fact]
    public void Mailings_tab_definition_exposes_groups_and_email_merge_plan_command()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var mailings = definition.FindTab("mailings");
        mailings.Should().NotBeNull();

        mailings!.Groups.Select(g => g.Header).Should()
            .Contain(new[] { "Create", "Start Mail Merge", "Write & Insert Fields", "Preview Results", "Finish" });

        var ids = mailings.Groups.SelectMany(g => g.Controls).Select(c => c.CommandId.Value).ToList();
        ids.Should().Contain("freew.merge-email",
            "Send E-mail Messages is a plan-only mail-merge exposure command");
    }

    [Fact]
    public void Mailings_tab_definition_uses_canonical_shared_command_ids()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var mailings = definition.FindTab("mailings");
        mailings.Should().NotBeNull();
        var commandIds = mailings!.Groups
            .SelectMany(group => group.Controls)
            .SelectMany(CommandIds)
            .ToHashSet(StringComparer.Ordinal);

        commandIds.Should().Contain(new[]
        {
            "freew.merge-envelopes",
            "freew.merge-labels",
            "freew.start-mail-merge",
            "freew.start-mail-merge-letters",
            "freew.start-mail-merge-directory",
            "freew.start-mail-merge-normal",
            "freew.merge-data",
            "freew.merge-edit-recipients",
            "freew.merge-filter-sort",
            "freew.merge-address-block",
            "freew.merge-greeting-line",
            "freew.merge-field",
            "freew.merge-match-fields",
            "freew.merge-rules",
            "freew.merge-rule-if",
            "freew.merge-rule-skip-record-if",
            "freew.merge-rule-next-record-if",
            "freew.merge-next-record",
            "freew.merge-record-number",
            "freew.merge-sequence-number",
            "freew.merge-rule-fill-in",
            "freew.merge-rule-ask",
            "freew.merge-rule-set",
            "freew.merge-rule-ref",
            "freew.merge-preview",
            "freew.merge-preview-first",
            "freew.merge-preview-previous",
            "freew.merge-preview-next",
            "freew.merge-preview-last",
            "freew.merge-finish",
            "freew.merge-email",
        });

        commandIds.Should().NotContain(new[]
        {
            "freew.select-recipients",
            "freew.address-block",
            "freew.greeting-line",
            "freew.preview-results",
            "freew.prev-record",
            "freew.next-record",
            "freew.finish-merge",
        });
    }

    [Fact]
    public void Mailings_tab_definition_exposes_start_merge_and_rules_dropdown_depth()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var mailings = definition.FindTab("mailings");
        mailings.Should().NotBeNull();

        mailings!.Groups.Select(group => group.Id).Should()
            .Equal("create", "merge-data", "merge-write", "merge-preview", "merge-finish");

        var startMailMerge = mailings.Groups.Single(g => g.Id == "merge-data").Controls
            .OfType<RibbonDropdown>()
            .Single(c => c.CommandId.Value == "freew.start-mail-merge");
        startMailMerge.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value, item.Header))
            .Should()
            .Equal(
                ("freew.start-mail-merge-letters", "Letters"),
                ("freew.start-mail-merge-directory", "Directory"),
                ("freew.start-mail-merge-normal", "Normal Word Document"));

        var rules = mailings.Groups.Single(g => g.Id == "merge-write").Controls
            .OfType<RibbonDropdown>()
            .Single(c => c.CommandId.Value == "freew.merge-rules");
        rules.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .Should()
            .Equal(
                "freew.merge-rule-if",
                "freew.merge-rule-skip-record-if",
                "freew.merge-rule-next-record-if",
                "freew.merge-next-record",
                "freew.merge-record-number",
                "freew.merge-sequence-number",
                "freew.merge-rule-fill-in",
                "freew.merge-rule-ask",
                "freew.merge-rule-set",
                "freew.merge-rule-ref");
    }

    [Fact]
    public void Start_mail_merge_commands_set_output_mode_and_clear_session()
    {
        var view = ViewWith(new Paragraph("Dear Â«FirstNameÂ»"));
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks(), out var engine);
        engine.LoadRecipientsCsv(SampleCsv);

        Execute(registry, "freew.start-mail-merge-directory");
        engine.Session.Mode.Should().Be(MailMergeOutputMode.Directory);

        Execute(registry, "freew.start-mail-merge");
        engine.Session.Mode.Should().Be(MailMergeOutputMode.Letters);

        Execute(registry, "freew.start-mail-merge-normal");
        engine.Session.Data.Should().BeNull();
        engine.Session.Mode.Should().Be(MailMergeOutputMode.Letters);
        engine.Session.IsPreviewing.Should().BeFalse();
    }

    [Fact]
    public void Rules_commands_insert_shared_rule_instructions_via_registry()
    {
        var ifResult = MailMergeRuleDialogPlanner.CreateIfResult(
            "City",
            selectedOperatorIndex: 0,
            value: "London",
            trueText: "Local",
            falseText: "Remote");
        var condition = MailMergeRuleDialogPlanner.CreateConditionResult(
            "City",
            selectedOperatorIndex: 0,
            value: "New York");
        var nameValue = MailMergeRuleDialogPlanner.CreateNameValueResult("CustomerCode", "Enter code");

        var view = ViewWith(new Paragraph(""));
        var registry = FreeWRibbon.BuildRegistry(
            view,
            Callbacks(
                ruleIf: ifResult,
                ruleCondition: condition,
                rulePrompt: "CustomerCode",
                ruleNameValue: nameValue),
            out _);

        Execute(registry, "freew.merge-rule-if");
        Execute(registry, "freew.merge-rule-skip-record-if");
        Execute(registry, "freew.merge-rule-next-record-if");
        Execute(registry, "freew.merge-rule-fill-in");
        Execute(registry, "freew.merge-rule-ask");
        Execute(registry, "freew.merge-rule-set");
        Execute(registry, "freew.merge-rule-ref");
        Execute(registry, "freew.merge-next-record");
        Execute(registry, "freew.merge-record-number");
        Execute(registry, "freew.merge-sequence-number");

        NativeFields().Select(run => run.ComplexField!.Keyword).Should().BeEquivalentTo(
            new[]
            {
                "IF",
                "SKIPIF",
                "NEXTIF",
                "FILLIN",
                "ASK",
                "SET",
                "REF",
                MailMerge.NextRecordInstruction,
                MailMerge.MergeRecordNumberInstruction,
                MailMerge.MergeSequenceNumberInstruction
            },
            "every Rules command must insert a native Word field");

        var text = PlainText(view.Document);
        text.Should().Contain(Wrap(MergeRuleEvaluator.BuildIfInstruction(
            ifResult.FieldName,
            ifResult.Operator,
            ifResult.Value,
            ifResult.TrueText,
            ifResult.FalseText)));
        text.Should().Contain(Wrap(MergeRuleEvaluator.BuildSkipRecordIfInstruction(
            condition.FieldName,
            condition.Operator,
            condition.Value)));
        text.Should().Contain(Wrap(MergeRuleEvaluator.BuildNextRecordIfInstruction(
            condition.FieldName,
            condition.Operator,
            condition.Value)));
        var nativeFields = NativeFields();
        nativeFields.ToDictionary(run => run.ComplexField!.Keyword, run => run.Text).Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                ["IF"] = Wrap(MergeRuleEvaluator.BuildIfInstruction(
                    ifResult.FieldName,
                    ifResult.Operator,
                    ifResult.Value,
                    ifResult.TrueText,
                    ifResult.FalseText)),
                ["SKIPIF"] = Wrap(MergeRuleEvaluator.BuildSkipRecordIfInstruction(
                    condition.FieldName,
                    condition.Operator,
                    condition.Value)),
                ["NEXTIF"] = Wrap(MergeRuleEvaluator.BuildNextRecordIfInstruction(
                    condition.FieldName,
                    condition.Operator,
                    condition.Value)),
                ["FILLIN"] = Wrap(MergeRuleEvaluator.BuildFillInInstruction("CustomerCode")),
                ["ASK"] = Wrap(MergeRuleEvaluator.BuildAskInstruction("CustomerCode", "Enter code")),
                ["SET"] = Wrap(MergeRuleEvaluator.BuildSetInstruction("CustomerCode", "Enter code")),
                ["REF"] = Wrap(MergeRuleEvaluator.BuildRefInstruction("CustomerCode")),
                [MailMerge.NextRecordInstruction] = Wrap(MailMerge.NextRecordField),
                [MailMerge.MergeRecordNumberInstruction] = Wrap(MailMerge.MergeRecordNumberField),
                [MailMerge.MergeSequenceNumberInstruction] = Wrap(MailMerge.MergeSequenceNumberField)
            });
        nativeFields.Where(run => run.ComplexField!.Keyword is "IF" or "SKIPIF" or "NEXTIF")
            .Select(run => run.ComplexField!.NestedFields!.Single().Field.Keyword)
            .Should().OnlyContain(keyword => keyword == "MERGEFIELD");
        text.Should().Contain(Wrap(MergeRuleEvaluator.BuildFillInInstruction("CustomerCode")));
        text.Should().Contain(Wrap(MergeRuleEvaluator.BuildAskInstruction("CustomerCode", "Enter code")));
        text.Should().Contain(Wrap(MergeRuleEvaluator.BuildSetInstruction("CustomerCode", "Enter code")));
        text.Should().Contain(Wrap(MergeRuleEvaluator.BuildRefInstruction("CustomerCode")));

        List<Run> NativeFields() => view.Document.Blocks
            .OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToList();
    }

    [Fact]
    public void Canonical_mailings_commands_execute_via_registry()
    {
        var view = ViewWith(new Paragraph("«FirstName»"));
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks(), out var engine);
        engine.LoadRecipientsCsv(SampleCsv);

        Execute(registry, "freew.merge-preview");
        PlainText(view.Document).Should().Contain("Ada", "executing the command previews record 1");

        Execute(registry, "freew.merge-preview-next");
        engine.Session.CurrentIndex.Should().Be(1);
        PlainText(view.Document).Should().Contain("Grace", "executing Next Record previews record 2");

        Execute(registry, "freew.merge-preview-first");
        engine.Session.CurrentIndex.Should().Be(0);
        PlainText(view.Document).Should().Contain("Ada", "executing First Record returns to record 1");

        Execute(registry, "freew.merge-preview-last");
        engine.Session.CurrentIndex.Should().Be(1);
        PlainText(view.Document).Should().Contain("Grace", "executing Last Record previews the final record");

        Execute(registry, "freew.merge-preview-previous");
        engine.Session.CurrentIndex.Should().Be(0);
        PlainText(view.Document).Should().Contain("Ada", "executing Previous Record returns to record 1");

        Execute(registry, "freew.merge-finish");
        PlainText(view.Document).Should().Contain("Ada").And.Contain("Grace");
        engine.Session.IsPreviewing.Should().BeFalse("finish leaves preview mode");
    }

    [Fact]
    public void MergeData_command_executes_via_registry_callback()
    {
        var view = ViewWith();
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks(recipientCsv: SampleCsv), out var engine);

        Execute(registry, "freew.merge-data");

        engine.Session.Data.Should().NotBeNull();
        engine.Session.Data!.Count.Should().Be(2);
    }

    private static void Execute(RibbonCommandRegistry registry, string commandId)
    {
        registry.TryGet(new RibbonCommandId(commandId), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static string Wrap(string instruction) =>
        $"{MailMerge.FieldOpen}{instruction}{MailMerge.FieldClose}";

    private static IEnumerable<string> CommandIds(RibbonControl control)
    {
        yield return control.CommandId.Value;

        var menu = control switch
        {
            RibbonDropdown dropdown => dropdown.Menu,
            RibbonSplitButton splitButton => splitButton.Menu,
            _ => null,
        };

        if (menu is null)
            yield break;

        foreach (var item in menu.Items)
        {
            if (item.CommandId is { } commandId)
                yield return commandId.Value;
        }
    }
}
