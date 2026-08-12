using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Behavior tests for the W23 Insert depth additions:
/// document-property fields, date-time field vs static, text-box gallery,
/// drop-cap clear/options, and parity of all new command ids.
///
/// STA tests use the real <see cref="DocumentView"/> (WPF); pure model
/// assertions do not.
/// </summary>
public sealed class InsertDepthCommandTests
{
    private static DocumentView EmptyView()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        return view;
    }

    // ── Document-property field insertion ──────────────────────────────────────────────────────

    [StaFact]
    public void InsertField_Title_ProducesRunFieldKindTitle()
    {
        var view = EmptyView();
        view.InsertField(RunFieldKind.Title);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.FieldKind == RunFieldKind.Title);

        run.Should().NotBeNull("a Title field run should have been inserted");
    }

    [StaFact]
    public void InsertField_Title_RendersDocumentPropertyValue()
    {
        var view = EmptyView();
        view.Model.Properties.Title = "My Great Document";
        view.InsertField(RunFieldKind.Title);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.FieldKind == RunFieldKind.Title);

        // When the Title property is set, InsertField resolves it to the live value at insert time.
        run.Should().NotBeNull();
        run!.Text.Should().Be("My Great Document");
    }

    [StaFact]
    public void InsertField_UsesOwningDocumentContextInsideAWrapperStory()
    {
        var owner = TextDocument.CreateEmpty();
        owner.Properties.Title = "Owning title";
        var view = EmptyView();
        view.FieldEvaluationDocument = owner;
        view.FieldEvaluationFileName = "Owning.docx";

        view.InsertField(RunFieldKind.Title);
        view.InsertField(RunFieldKind.FileName);
        view.CommitToModel();

        var runs = view.Model.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs).ToArray();
        runs.Single(run => run.FieldKind == RunFieldKind.Title).Text.Should().Be("Owning title");
        runs.Single(run => run.FieldKind == RunFieldKind.FileName).Text.Should().Be("Owning.docx");
    }

    [StaFact]
    public void InsertField_Subject_ProducesSubjectKind()
    {
        var view = EmptyView();
        view.Model.Properties.Subject = "Test Subject";
        view.InsertField(RunFieldKind.Subject);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.FieldKind == RunFieldKind.Subject);

        run.Should().NotBeNull();
        run!.Text.Should().Be("Test Subject");
    }

    [StaFact]
    public void InsertField_Keywords_ProducesKeywordsKind()
    {
        var view = EmptyView();
        view.Model.Properties.Keywords = "word; field; test";
        view.InsertField(RunFieldKind.Keywords);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.FieldKind == RunFieldKind.Keywords);

        run.Should().NotBeNull();
        run!.Text.Should().Be("word; field; test");
    }

    [StaFact]
    public void InsertField_DocComments_ProducesCommentsKind()
    {
        var view = EmptyView();
        view.Model.Properties.Comments = "A test document";
        view.InsertField(RunFieldKind.DocComments);
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.FieldKind == RunFieldKind.DocComments);

        run.Should().NotBeNull();
        run!.Text.Should().Be("A test document");
    }

    // ── Date & Time field vs static ───────────────────────────────────────────────────────────

    [StaFact]
    public void InsertComplexField_DateInstruction_ProducesComplexFieldRun()
    {
        var view = EmptyView();
        view.InsertComplexField(@" DATE \@ ""M/d/yyyy"" ");
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.ComplexField is not null);

        run.Should().NotBeNull("InsertComplexField should produce a complex-field run");
        run!.ComplexField!.Keyword.Should().Be("DATE");
    }

    [StaFact]
    public void InsertComplexField_TimeInstruction_ProducesTimeComplexField()
    {
        var view = EmptyView();
        view.InsertComplexField(@" TIME \@ ""h:mm am/pm"" ");
        view.CommitToModel();

        var run = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.ComplexField?.Keyword == "TIME");

        run.Should().NotBeNull();
    }

    // ── Text Box gallery presets ──────────────────────────────────────────────────────────────

    [StaFact]
    public void TextboxSimple_InsertsShapeWithText()
    {
        var view = EmptyView();
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        registry.TryGet("freew.textbox-simple", out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);
        view.CommitToModel();

        var shapeRun = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.Shape is not null);

        shapeRun.Should().NotBeNull("Simple text box should be inserted as a shape run");
        shapeRun!.Shape!.Kind.Should().Be(ShapeKind.TextBox);
        shapeRun.Shape.HasText.Should().BeTrue();
    }

    [StaFact]
    public void TextboxSidebar_InsertsShapeWithDarkFill()
    {
        var view = EmptyView();
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        registry.TryGet("freew.textbox-sidebar", out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);
        view.CommitToModel();

        var shapeRun = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.Shape?.Kind == ShapeKind.TextBox);

        shapeRun.Should().NotBeNull();
        shapeRun!.Shape!.FillColorHex.Should().Be("#243F60", "Sidebar uses the dark blue fill");
        shapeRun.Shape.HasText.Should().BeTrue();
    }

    [StaFact]
    public void TextboxQuote_InsertsShapeWithItalicRun()
    {
        var view = EmptyView();
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        registry.TryGet("freew.textbox-quote", out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);
        view.CommitToModel();

        var shapeRun = view.Model.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.Shape?.Kind == ShapeKind.TextBox);

        shapeRun.Should().NotBeNull();
        var innerRun = shapeRun!.Shape!.TextParagraphs.SelectMany(p => p.Runs).FirstOrDefault();
        innerRun.Should().NotBeNull();
        innerRun!.Formatting.Italic.Should().BeTrue("Quote preset uses italic text");
    }

    // ── Drop Cap: clear ──────────────────────────────────────────────────────────────────────

    [StaFact]
    public void ClearDropCap_RemovesCapFormattingFromParagraph()
    {
        var view = EmptyView();
        view.Model.Blocks.Clear();
        var para = new Paragraph("Hello world");
        view.Model.Blocks.Add(para);
        view.LoadModel(view.Model);

        // First apply, then remove.
        view.ApplyDropCap();
        view.ClearDropCap();

        // After clearing, every run should be at default formatting (no oversized cap left).
        var firstRun = para.Runs[0];
        firstRun.Formatting.FontSizePt.Should().BeNull("drop cap size should be cleared");
        firstRun.Formatting.Bold.Should().BeFalse("drop cap bold should be cleared");
        para.DropCap.Should().BeNull("None removes the shared layout intent too");
    }

    [StaFact]
    public void DropCapCommands_StampDistinctSharedLayoutIntent()
    {
        var view = EmptyView();
        view.Model.Blocks.Clear();
        view.Model.Blocks.Add(new Paragraph("Hello world"));
        view.LoadModel(view.Model);
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        registry.TryGet("freew.drop-cap-in-margin", out var inMargin).Should().BeTrue();
        inMargin!.Execute(RibbonCommandContext.Empty);

        var paragraph = view.Model.Blocks.OfType<Paragraph>().Single();
        paragraph.DropCap.Should().NotBeNull();
        paragraph.DropCap!.Position.Should().Be(DropCapPosition.InMargin);

        registry.TryGet("freew.drop-cap-dropped", out var dropped).Should().BeTrue();
        dropped!.Execute(RibbonCommandContext.Empty);

        paragraph = view.Model.Blocks.OfType<Paragraph>().Single();
        paragraph.DropCap.Should().NotBeNull();
        paragraph.DropCap!.Position.Should().Be(DropCapPosition.Dropped);
    }

    [StaFact]
    public void DropCapFloaterCommitReadback_PreservesLeadingCharacterAndIntent()
    {
        var view = EmptyView();
        view.Model.Blocks.Clear();
        view.Model.Blocks.Add(new Paragraph("Hello world"));
        view.LoadModel(view.Model);

        view.ApplyDropCap(DropCapPosition.InMargin, sizePt: 48, lineSpan: 4, distanceFromTextPt: 9);
        view.CommitToModel();

        var paragraph = view.Model.Blocks.OfType<Paragraph>().Single();
        paragraph.PlainText.Should().Be("Hello world");
        paragraph.Runs[0].Text.Should().Be("H");
        paragraph.Runs[0].Formatting.FontSizePt.Should().Be(48);
        paragraph.Runs[1].Text.Should().Be("ello world");
        paragraph.DropCap.Should().Be(new DropCapLayoutIntent(DropCapPosition.InMargin, 4, 48, 9));
    }

    // ── Parity: new command ids are registered ───────────────────────────────────────────────

    [StaFact]
    public void NewInsertCommandIds_AreAllRegistered()
    {
        var view = EmptyView();
        var registry = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        var expectedIds = new[]
        {
            // Document property fields
            "freew.docprop-title",
            "freew.docprop-subject",
            "freew.docprop-author",
            "freew.docprop-keywords",
            "freew.docprop-comments",
            // Text box gallery
            "freew.textbox-simple",
            "freew.textbox-sidebar",
            "freew.textbox-quote",
            // Drop cap options
            "freew.drop-cap-dropped",
            "freew.drop-cap-in-margin",
            "freew.drop-cap-none",
            "freew.drop-cap-options",
            "freew.drop-cap.dropped",
            "freew.drop-cap.in-margin",
            "freew.drop-cap.none",
        };

        foreach (var id in expectedIds)
            registry.TryGet(id, out _).Should().BeTrue($"{id} must be registered");
    }

    [StaFact]
    public void InsertTab_TextGroup_ExposesNewTextboxAndDropCapMenuItems()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var textGroup = definition.FindTab("insert")!.FindGroup("text");

        textGroup.Should().NotBeNull();

        // freew.shape-textbox should now carry a gallery menu.
        var textboxControl = textGroup!.Controls.FirstOrDefault(c => c.CommandId.Value == "freew.shape-textbox");
        textboxControl.Should().NotBeNull();

        // freew.drop-cap should now carry a menu with an options item.
        var dropCapControl = textGroup.Controls.FirstOrDefault(c => c.CommandId.Value == "freew.drop-cap");
        dropCapControl.Should().NotBeNull();
    }
}
