using System.Collections.Generic;
using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-MAIL: tests for the Mailings tab — the in-scope mail-merge subset over the portable
/// <see cref="MailMerge"/> engine: Select Recipients (load CSV), Insert Merge Field, Address Block /
/// Greeting Line insertion, Preview Results (record-1 values + Next/Previous stepping), and Finish &amp;
/// Merge (merge to a new in-memory document). Mail-SEND is out of scope and intentionally not tested /
/// wired. Pure-model — no headless Avalonia backend required.
/// </summary>
public sealed class MailingsTabTests
{
    private const string SampleCsv = "FirstName,LastName,City\nAda,Lovelace,London\nGrace,Hopper,New York";

    // Callbacks that supply the two optional dialog hooks from canned values so the engine's dialog-driven
    // commands run end-to-end without a real UI.
    private static RibbonHostCallbacks Callbacks(
        string? recipientCsv = null,
        string? mergeFieldName = null,
        List<string>? infoSink = null) =>
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
            ShowMailMergeInfo: m => infoSink?.Add(m));

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
    public void InsertMergeFieldNamed_inserts_guillemet_placeholder_at_caret_and_is_undoable()
    {
        var view = ViewWith(new Paragraph("Hi "));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.InsertMergeFieldNamed("FirstName");

        var text = ((Paragraph)view.Document.Blocks[0]).PlainText;
        text.Should().Contain($"{MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}",
            "a «FirstName» merge-field run is inserted at the caret");

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
    }

    [Fact]
    public void InsertMergeFieldNamed_strips_existing_guillemets()
    {
        var view = ViewWith(new Paragraph(""));
        var engine = new MailMergeEngine(view, Callbacks());

        engine.InsertMergeFieldNamed("«LastName»");

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("«LastName»",
            "the name is normalised, not double-wrapped");
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
    }

    [Fact]
    public void InsertGreetingLine_inserts_composite_placeholder_when_recipients_loaded()
    {
        var view = ViewWith(new Paragraph(""));
        var engine = new MailMergeEngine(view, Callbacks());
        engine.LoadRecipientsCsv(SampleCsv);

        engine.InsertGreetingLine();

        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Contain("«GreetingLine»");
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
        var info = new List<string>();
        var engine = new MailMergeEngine(view, Callbacks(infoSink: info));
        engine.LoadRecipientsCsv(SampleCsv);

        var merged = engine.FinishMerge();

        merged.Should().NotBeNull();
        var text = PlainText(merged!);
        text.Should().Contain("Dear Ada Lovelace,", "record 1 is merged");
        text.Should().Contain("Dear Grace Hopper,", "record 2 is merged");
        text.Should().NotContain("«FirstName»", "all placeholders are substituted");
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

    // ── Registry wiring ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_resolves_all_mailings_tab_commands()
    {
        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), Callbacks());

        var expected = new[]
        {
            "freew.select-recipients",
            "freew.merge-field",
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
    public void Mailings_tab_definition_exposes_groups_and_no_mail_send()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var mailings = definition.FindTab("mailings");
        mailings.Should().NotBeNull();

        mailings!.Groups.Select(g => g.Header).Should()
            .Contain(new[] { "Start Mail Merge", "Write & Insert Fields", "Preview Results", "Finish" });

        // Mail-SEND is OUT OF SCOPE: no send/e-mail command may appear in the Mailings tab.
        var ids = mailings.Groups.SelectMany(g => g.Controls).Select(c => c.CommandId.Value).ToList();
        ids.Should().NotContain(id => id.Contains("send") || id.Contains("email") || id.Contains("e-mail"),
            "mail-send (e-mail merge) is out of scope and must not be wired");
    }

    [Fact]
    public void PreviewResults_command_executes_via_registry()
    {
        var view = ViewWith(new Paragraph("«FirstName»"));
        var registry = FreeWRibbon.BuildRegistry(view, Callbacks(), out var engine);
        engine.LoadRecipientsCsv(SampleCsv);

        registry.TryGet(new RibbonCommandId("freew.preview-results"), out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);

        PlainText(view.Document).Should().Contain("Ada", "executing the command previews record 1");
    }
}
