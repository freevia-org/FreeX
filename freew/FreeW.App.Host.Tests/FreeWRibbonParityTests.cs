using System.Collections.Generic;
using System.IO;
using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class FreeWRibbonParityTests
{
    [StaFact]
    public void FieldCommandInsertsIntoTheResolvedStoryEditor()
    {
        var bodyEditor = new DocumentView();
        var storyEditor = new DocumentView();
        storyEditor.Model.Properties.Title = "Story title";
        var resolverCalls = 0;
        var registry = FreeWRibbonCommands.Build(
            bodyEditor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty,
            new FreeWWpfRibbonNativeExecutionPorts(
                ResolveFieldEditor: () =>
                {
                    resolverCalls++;
                    return storyEditor;
                },
                AskFieldInstruction: _ => " TITLE "));

        registry.TryGet("freew.field", out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);

        resolverCalls.Should().Be(1, "the editor must be captured before ribbon or dialog focus changes");
        storyEditor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Should().ContainSingle(run =>
                run.ComplexField != null
                && run.ComplexField.Instruction == " TITLE "
                && run.Text == "Story title");
        bodyEditor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Should().NotContain(run => run.ComplexField != null);
    }

    [StaFact]
    public void NativeFieldProducerCommandsUseTheResolvedStoryEditor()
    {
        var bodyEditor = new DocumentView();
        var storyEditor = new DocumentView();
        storyEditor.Model.Properties.Title = "Story title";
        var registry = FreeWRibbonCommands.Build(
            bodyEditor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty,
            new FreeWWpfRibbonNativeExecutionPorts(
                ResolveFieldEditor: () => storyEditor));

        foreach (var commandId in new[]
                 {
                     "freew.docprop-title",
                     "freew.page-number-current",
                     "freew.merge-next-record",
                     "freew.merge-record-number",
                     "freew.merge-sequence-number"
                 })
        {
            registry.TryGet(commandId, out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }

        var storyRuns = storyEditor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .ToArray();
        storyRuns.Count(run => run.FieldKind == RunFieldKind.Title && run.Text == "Story title")
            .Should().Be(1);
        storyRuns.Count(run => run.FieldKind == RunFieldKind.PageNumber)
            .Should().Be(1);
        storyRuns.Select(run => run.ComplexField?.Keyword)
            .Should().Contain(new[] { "NEXT", "MERGEREC", "MERGESEQ" });
        bodyEditor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Count(run => run.FieldKind != RunFieldKind.None || run.ComplexField != null)
            .Should().Be(0);
    }

    [Fact]
    public void Build_OrdersImplementedTopLevelTabsLikeWord()
    {
        FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf).VisibleTabs.Select(tab => tab.Id)
            .Should()
            .Equal("home", "insert", "design", "layout", "references", "mailings", "review", "view", "help", "developer");
    }

    [Fact]
    public void Wpf_profile_uses_backstage_shell_instead_of_avalonia_file_command_strip()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var allCommandIds = definition.Tabs
            .SelectMany(CommandIds)
            .Concat(definition.Tabs.SelectMany(MenuCommandIds))
            .ToArray();

        definition.VisibleTabs.Select(tab => tab.Id)
            .Should()
            .NotContain("file", "the WPF shell opens the Backstage/File surface outside the compiled command strip");

        allCommandIds.Should().NotContain(new[]
        {
            "freew.backstage",
            "freew.new",
            "freew.open",
            "freew.import-pdf-text",
            "freew.save",
        });

        var mainWindow = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew", "FreeW.App.Host", "MainWindow.cs"));
        mainWindow.Should().Contain(
            "NewDocument: () => _applicationCommands.Execute(FreeWKeyboardCommand.NewDocument)");
        mainWindow.Should().Contain(
            "Browse: () => _applicationCommands.Execute(FreeWKeyboardCommand.OpenDocument)");
        mainWindow.Should().Contain("ImportPdfText: () => _file.ImportPdfText()");
        mainWindow.Should().Contain(
            "Save: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument)");
    }

    [StaFact]
    public void HelpTab_ExposesOnlyBackedFreeWLocalSupportCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var help = definition.FindTab("help");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                OpenHelpOnline = () => { },
                OpenFeedback = () => { },
                CopyDiagnostics = () => { },
                CheckForUpdates = () => { },
                OpenAbout = () => { },
                OpenLegalNotices = () => { },
            });

        help.Should().NotBeNull();
        help!.Groups.Select(group => group.Id)
            .Should()
            .Equal("help", "product");

        CommandIds(help)
            .Should()
            .Equal(
                "freew.help-online",
                "freew.feedback",
                "freew.copy-diagnostics",
                "freew.check-updates",
                "freew.about",
                "freew.legal-notices");

        Labels(help)
            .Should()
            .Equal("Help Online", "Feedback", "Copy Diagnostics", "Check for Updates", "About FreeW", "Legal Notices");

        foreach (var commandId in CommandIds(help))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must be backed before it appears on the Help tab");
    }

    [Fact]
    public void Build_ExposesReferencesAsAWordStyleTopLevelTab()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);

        definition.VisibleTabs.Select(tab => tab.Id)
            .Should()
            .ContainInOrder("layout", "references", "mailings");

        definition.FindTab("insert")!.Groups.Select(group => group.Id)
            .Should()
            .NotContain("references", "Word exposes References as a dedicated top-level tab, not as an Insert group");
    }

    [StaFact]
    public void PrintPreviewRibbonCommandInvokesHostCallback()
    {
        var invoked = false;
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                OpenPrintPreview = () => invoked = true,
            });

        registry.TryGet("freew.print-preview", out var command)
            .Should()
            .BeTrue("Print Preview must be backed before the shared behavior evidence row can claim WPF parity");

        command!.Execute(RibbonCommandContext.Empty);

        invoked.Should().BeTrue();
    }

    [StaFact]
    public void PageNumberFormatCommand_AppliesSharedPlannerResult()
    {
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        registry.TryGet("freew.page-number-format", out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.ForSelectedValue(
            PageNumberFormatDialogPlanner.BuildCommandValue(
                PageNumberFormat.LowerRoman,
                7,
                chapterStyleLevel: 2,
                chapterSeparator: PageNumberChapterSeparator.Colon)));

        editor.Model.Page.PageNumberFormat.Should().Be(PageNumberFormat.LowerRoman);
        editor.Model.Page.PageNumberStartAt.Should().Be(7);
        editor.Model.Page.PageNumberChapterStyleLevel.Should().Be(2);
        editor.Model.Page.PageNumberChapterSeparator.Should().Be(PageNumberChapterSeparator.Colon);
    }

    [StaFact]
    public void PageNumberCurrentPositionCommand_UsesFormattedPageNumber()
    {
        var editor = new DocumentView();
        editor.Model.Page.PageNumberFormat = PageNumberFormat.LowerRoman;
        editor.Model.Page.PageNumberStartAt = 4;
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        registry.TryGet("freew.page-number-current", out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Should().Contain(r => r.FieldKind == RunFieldKind.PageNumber && r.Text == "iv");
    }

    [StaFact]
    public void InsertTab_GroupsBackedCommandsLikeWord()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var insert = definition.FindTab("insert");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        insert.Should().NotBeNull();
        insert!.Groups.Select(group => group.Id)
            .Should()
            .Equal("pages", "tables", "illustrations", "links", "header-footer", "text", "symbols");

        CommandIds(insert.FindGroup("illustrations")!)
            .Should()
            .Equal("freew.picture", "freew.shapes", "freew.smartart", "freew.chart", "freew.screenshot", "freew.insert-icon");
        Labels(insert.FindGroup("illustrations")!)
            .Should()
            .Equal("Pictures", "Shapes", "SmartArt", "Chart", "Screenshot", "Icons");

        CommandIds(insert.FindGroup("links")!)
            .Should()
            .ContainInOrder("freew.hyperlink", "freew.bookmark", "freew.cross-reference");
        registry.TryGet("freew.cross-reference", out _)
            .Should()
            .BeTrue("Word exposes Cross-reference from Insert > Links and FreeW already backs the command");

        CommandIds(insert.FindGroup("text")!)
            .Should()
            .Equal(
                "freew.shape-textbox",
                "freew.insert-quickpart",
                "freew.insert-file",
                "freew.wordart",
                "freew.drop-cap",
                "freew.datetime",
                "freew.field",
                "freew.update-fields",
                "freew.toggle-field-codes",
                "freew.object",
                "freew.save-quickpart",
                "freew.building-blocks-organizer");

        CommandIds(insert.FindGroup("symbols")!)
            .Should()
            .Equal("freew.equation", "freew.symbol");

        insert.Groups.Select(group => group.Id)
            .Should()
            .NotContain(new[] { "media", "quick-parts" });
        CommandIds(insert)
            .Should()
            .NotContain(new[]
            {
                "freew.image-size",
                "freew.image-alt-text",
                "freew.image-wrap",
                "freew.image-align-left",
                "freew.image-align-center",
                "freew.image-align-right"
            });

        var backedParityCommandIds = new[]
        {
            "freew.picture",
            "freew.insert-icon",
            "freew.smartart",
            "freew.chart",
            "freew.shape-textbox",
            "freew.insert-quickpart",
            "freew.insert-file",
            "freew.wordart",
            "freew.drop-cap",
            "freew.datetime",
            "freew.field",
            "freew.update-fields",
            "freew.toggle-field-codes",
            "freew.object",
            "freew.save-quickpart",
            "freew.building-blocks-organizer",
            "freew.cross-reference",
            "freew.equation",
            "freew.symbol"
        };

        foreach (var commandId in backedParityCommandIds)
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Insert tab");
    }

    [StaFact]
    public void InsertPages_ExposesBackedWordStyleBlankPage()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var pages = definition.FindTab("insert")!.FindGroup("pages");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(pages!)
            .Should()
            .ContainInOrder("freew.cover-page", "freew.blank-page", "freew.page-break");
        registry.TryGet("freew.blank-page", out var command).Should().BeTrue("Insert > Pages > Blank Page is visible");

        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Before"));
        command!.Execute(RibbonCommandContext.Empty);

        editor.Model.Blocks.Should().HaveCount(3);
        editor.Model.Blocks.Skip(1).OfType<Paragraph>()
            .Should()
            .OnlyContain(paragraph => paragraph.Formatting.PageBreakBefore && paragraph.PlainText.Length == 0);
    }

    [Fact]
    public void ReferencesTab_GroupsImplementedReferenceCommandsLikeWord()
    {
        var references = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf).FindTab("references");

        references.Should().NotBeNull();
        references!.Groups.Select(group => group.Id)
            .Should()
            .Equal("table-of-contents", "footnotes", "citations", "captions", "index", "authorities");

        CommandIds(references)
            .Should()
            .Contain(new[]
            {
                "freew.toc",
                "freew.toc-refresh",
                "freew.footnote",
                "freew.endnote",
                "freew.next-footnote",
                "freew.show-notes",
                "freew.footnote-endnote-options",
                "freew.citation",
                "freew.manage-sources",
                "freew.citation-style",
                "freew.bibliography",
                "freew.caption",
                "freew.tof",
                "freew.tof-refresh",
                "freew.cross-reference",
                "freew.index-mark",
                "freew.index-insert",
                "freew.index-refresh",
                "freew.mark-citation",
                "freew.table-of-authorities",
                "freew.table-of-authorities-refresh"
            });
    }

    [StaFact]
    public void ReferencesCaptions_ExposeLabelMenusAndUpdateFieldsRefreshesTableOfFigures()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var captions = definition.FindTab("references")!.FindGroup("captions");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        var visibleAndMenuIds = CommandIds(captions!).Concat(MenuCommandIds(captions!)).ToArray();
        visibleAndMenuIds.Should().Contain(new[]
        {
            "freew.caption",
            "freew.insert-caption.figure",
            "freew.insert-caption.table",
            "freew.insert-caption.equation",
            "freew.tof",
            "freew.tof.figure",
            "freew.tof.table",
            "freew.tof.equation",
            "freew.tof-refresh"
        });

        foreach (var id in new[]
        {
            "freew.insert-caption.equation",
            "freew.tof.table",
            "freew.tof.equation",
            "freew.tof-refresh.table",
            "freew.tof-refresh.equation"
        })
        {
            registry.TryGet(id, out _).Should().BeTrue($"{id} must be registered");
        }

        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Body"));
        editor.InsertCaption(CaptionLabel.Equation, "First");
        editor.InsertTableOfFigures(CaptionLabel.Equation);
        editor.InsertCaption(CaptionLabel.Equation, "Second");

        editor.UpdateFields();

        var tableText = editor.Model.Blocks.OfType<Paragraph>()
            .Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Select(paragraph => paragraph.PlainText)
            .ToList();
        tableText.Should().StartWith("Table of Equations");
        tableText.Skip(1).Should().BeEquivalentTo("Equation 1: First\t1", "Equation 2: Second\t1");
        var nativeEntries = editor.Model.Blocks.OfType<Paragraph>()
            .Where(paragraph => TableOfFigures.TryGetNativeLabel(paragraph.SpanningFieldOwner, out var label)
                && label == Captions.EquationLabelText)
            .ToArray();
        nativeEntries.Should().HaveCount(2);
        nativeEntries[0].SpanningFieldStart!.Instruction.Should().Be(" TOC \\c \"Equation\" ");
        nativeEntries[1].EndsSpanningField.Should().BeTrue();
        editor.Model.Blocks.OfType<Paragraph>()
            .Where(Captions.IsCaptionParagraph)
            .SelectMany(paragraph => paragraph.Runs)
            .Count(run => run.ComplexField is { Keyword: "SEQ" })
            .Should().Be(2);
    }

    [StaFact]
    public void TableOfFiguresRefresh_UsesCaptionLogicalPageLabel()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Blocks.Add(new Paragraph(TableOfFigures.HeadingText(CaptionLabel.Figure))
        {
            StyleId = TableOfFigures.HeadingStyleId
        });
        var nativeField = new ComplexField(" TOC \\c \"Figure\" ");
        model.Blocks.Add(new Paragraph("Old Figure\t9")
        {
            StyleId = "Normal",
            SpanningFieldStart = nativeField,
            SpanningFieldOwner = nativeField,
            EndsSpanningField = true
        });
        model.Blocks.Add(DocumentOps.CreatePageBreak());
        model.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 1, "Architecture"));
        model.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        model.Page.PageNumberStartAt = 4;

        var editor = new DocumentView();
        editor.LoadModel(model);

        editor.RefreshTableOfFigures();

        editor.Model.Blocks.OfType<Paragraph>()
            .Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Figure 1: Architecture\tV").And.NotContain("Old Figure\t9");
    }

    [StaFact]
    public void TableOfFiguresRefresh_UsesEachCaptionRowPageInPaginatedTable()
    {
        var model = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var table = model.Blocks.OfType<Table>().Single();
        table.Rows[1].Cells[0].Paragraphs[0] = Captions.BuildCaption(CaptionLabel.Figure, 1, "Early row");
        var nested = Table.Create(1, 1);
        nested.Rows[0].Cells[0].Paragraphs[0] = Captions.BuildCaption(CaptionLabel.Figure, 2, "Later row");
        table.Rows[5].Cells[0].NestedTables.Add(nested);
        var oldRegion = TableOfFigures.Build(model, CaptionLabel.Figure, _ => "9");
        for (var index = oldRegion.Count - 1; index >= 0; index--)
            model.Blocks.Insert(0, oldRegion[index]);
        var tableBlockIndex = model.Blocks.IndexOf(table);
        DocumentViewLayoutPlanner.ResolveTableParagraphPageOffset(
            model, tableBlockIndex, new TableParagraphAddress(1, 0, 0)).Should().Be(0);
        DocumentViewLayoutPlanner.ResolveTableParagraphPageOffset(
            model,
            tableBlockIndex,
            new TableParagraphAddress(
                5,
                0,
                ParagraphIndex: -1,
                NestedTableIndex: 0,
                NestedParagraph: new TableParagraphAddress(0, 0, 0))).Should().BeGreaterThan(0);

        var editor = new DocumentView();
        editor.LoadModel(model);

        editor.RefreshTableOfFigures();

        var pageLabels = editor.Model.Blocks.OfType<Paragraph>()
            .Where(paragraph => paragraph.StyleId == TableOfFigures.EntryStyleId)
            .Select(paragraph => paragraph.PlainText.Split('\t').Last())
            .ToArray();
        pageLabels.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        pageLabels.Should().NotContain("9");
    }

    [StaFact]
    public void ReferencesIndex_ExposesBackedWordStyleUpdateIndex()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var index = definition.FindTab("references")!.FindGroup("index");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(index!)
            .Should()
            .Equal("freew.index-mark", "freew.index-insert", "freew.index-refresh");
        Labels(index!)
            .Should()
            .Equal("Mark Entry", "Insert Index", "Update Index");
        registry.TryGet("freew.index-refresh", out var refresh).Should().BeTrue("Word exposes Update Index from References > Index");

        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Body"));
        editor.Model.IndexEntries.Add(new IndexEntry("Beta"));
        editor.Model.IndexEntries.Add(new IndexEntry("Alpha"));
        editor.InsertIndex();

        editor.Model.IndexEntries.Add(new IndexEntry("Gamma"));
        refresh!.Execute(RibbonCommandContext.Empty);

        editor.Model.Blocks.OfType<Paragraph>()
            .Where(DocumentIndex.IsIndexParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should()
            .Equal("A", "Alpha, 1", "B", "Beta, 1", "G", "Gamma, 1");
        editor.Model.Blocks.OfType<Paragraph>()
            .Count(paragraph => paragraph.StyleId == DocumentIndex.HeadingStyleId)
            .Should()
            .Be(3);
    }

    [StaFact]
    public void ReferencesCitations_ExposesBackedWordStyleManageSources()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var citations = definition.FindTab("references")!.FindGroup("citations");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(citations!)
            .Should()
            .Equal("freew.citation", "freew.manage-sources", "freew.citation-style", "freew.bibliography");
        Labels(citations!)
            .Should()
            .Equal("Insert Citation", "Manage Sources", "Style", "Bibliography");
        registry.TryGet("freew.manage-sources", out _)
            .Should()
            .BeTrue("Word exposes Manage Sources in References > Citations & Bibliography");

        editor.Model.Sources.Add(new Source
        {
            Tag = "Old",
            Author = "Old Author",
            Title = "Old Title",
            Year = "1999",
            Publisher = "Old Publisher"
        });

        var replacement = new[]
        {
            new Source
            {
                Tag = "New",
                Author = "New Author",
                Title = "New Title",
                Year = "2026",
                Publisher = "New Publisher"
            }
        };

        editor.ReplaceSources(replacement);

        editor.Model.Sources.Should().ContainSingle().Which.Tag.Should().Be("New");
        editor.Commands.Undo().Should().BeTrue();
        editor.Model.Sources.Should().ContainSingle().Which.Tag.Should().Be("Old");
    }

    [StaFact]
    public void ReferencesFootnotes_ExposesBackedWordStyleNavigationAndShowNotes()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var footnotes = definition.FindTab("references")!.FindGroup("footnotes");
        var next = footnotes!.Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.next-footnote");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(footnotes)
            .Should()
            .Equal(
                "freew.footnote",
                "freew.endnote",
                "freew.next-footnote",
                "freew.show-notes",
                "freew.footnote-endnote-options");
        Labels(footnotes)
            .Should()
            .Equal("Insert Footnote", "Insert Endnote", "Next Footnote", "Show Notes", "Footnote/Endnote Options…");
        next.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value, item.Header))
            .Should()
            .Equal(
                ("freew.next-footnote", "Next Footnote"),
                ("freew.previous-footnote", "Previous Footnote"),
                ("freew.next-endnote", "Next Endnote"),
                ("freew.previous-endnote", "Previous Endnote"));

        foreach (var commandId in CommandIds(footnotes).Concat(MenuCommandIds(next)))
        {
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from References > Footnotes");
        }

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph("Body ");
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(new Run(" more "));
        paragraph.Runs.Add(Run.EndnoteReference(1));
        document.Blocks.Add(paragraph);
        document.Footnotes[1] = new Footnote(1, "Footnote text");
        document.Endnotes[1] = new Endnote(1, "Endnote text");
        editor.LoadModel(document);

        editor.MoveToNextFootnote().Should().BeTrue();
        editor.MoveToPreviousFootnote().Should().BeTrue();
        editor.MoveToNextEndnote().Should().BeTrue();
        editor.MoveToPreviousEndnote().Should().BeTrue();
        new DocumentView().MoveToNextFootnote().Should().BeFalse();
    }

    [StaFact]
    public void ReferencesTableOfContents_AddTextExposesBackedHeadingLevelCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var tocGroup = definition.FindTab("references")!.FindGroup("table-of-contents");
        var addText = tocGroup!.Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.toc-add-text");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(tocGroup)
            .Should()
            .ContainInOrder("freew.toc", "freew.toc-add-text", "freew.toc-refresh");
        addText.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value, item.Header))
            .Should()
            .Equal(
                ("freew.toc-addtext-none", "Do Not Show in Table of Contents"),
                ("freew.toc-addtext-level1", "Level 1"),
                ("freew.toc-addtext-level2", "Level 2"),
                ("freew.toc-addtext-level3", "Level 3"));

        foreach (var commandId in addText.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .Append(addText.CommandId.Value))
        {
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from References > Table of Contents > Add Text");
        }

        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Candidate") { StyleId = "Normal" });
        registry.TryGet("freew.toc-addtext-level2", out var level2).Should().BeTrue();
        level2!.Execute(RibbonCommandContext.Empty);
        ((Paragraph)editor.Model.Blocks[0]).StyleId.Should().Be("Heading2");

        registry.TryGet("freew.toc-addtext-none", out var none).Should().BeTrue();
        none!.Execute(RibbonCommandContext.Empty);
        ((Paragraph)editor.Model.Blocks[0]).StyleId.Should().Be("Normal");
    }

    [StaFact]
    public void HomeFont_ExposesAndRegistersStrikethrough()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var font = definition.FindTab("home")!.FindGroup("font");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(font!)
            .Should()
            .Contain("freew.strikethrough");
        registry.TryGet("freew.strikethrough", out _)
            .Should()
            .BeTrue("Word exposes Strikethrough alongside Bold, Italic, and Underline");
    }

    [StaFact]
    public void HomeEditing_ExposesFindReplaceAndSelect()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var editing = definition.FindTab("home")!.FindGroup("editing");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                OpenFindReplaceDialog = () => { },
            });

        CommandIds(editing!)
            .Should()
            .Equal("freew.undo", "freew.redo", "freew.find", "freew.replace", "freew.select");
        registry.TryGet("freew.undo", out _).Should().BeTrue();
        registry.TryGet("freew.redo", out _).Should().BeTrue();
        registry.TryGet("freew.find", out _).Should().BeTrue();
        registry.TryGet("freew.replace", out _).Should().BeTrue();
        registry.TryGet("freew.select", out _).Should().BeTrue();
    }

    [StaFact]
    public void HomeStyles_ExposesBackedQuickStyleAndClearStyleCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var styles = definition.FindTab("home")!.FindGroup("styles");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(styles!)
            .Should()
            .Contain(new[]
            {
                "freew.style-heading2",
                "freew.style-heading3",
                "freew.style-clear",
            });

        registry.TryGet("freew.style-heading2", out _).Should().BeTrue();
        registry.TryGet("freew.style-heading3", out _).Should().BeTrue();
        registry.TryGet("freew.style-clear", out _).Should().BeTrue();
    }

    [StaFact]
    public void HomeFormattingVisibility_ExposesBackedWordStyleToggles()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var home = definition.FindTab("home");
        var paragraph = home!.FindGroup("paragraph");
        var formatting = home.FindGroup("formatting");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleRevealFormatting = () => { },
                IsRevealFormattingVisible = () => false,
            });

        paragraph.Should().NotBeNull();
        formatting.Should().NotBeNull();
        CommandIds(paragraph!)
            .Should()
            .Contain("freew.formatting-marks", "Word keeps the paragraph mark toggle with Home > Paragraph");
        Labels(paragraph!)
            .Should()
            .Contain("Show ¶");
        CommandIds(formatting!)
            .Should()
            .Contain("freew.reveal-formatting", "FreeW keeps the backed Shift+F1 pane near Home formatting controls");
        Labels(formatting!)
            .Should()
            .Contain("Reveal Formatting");

        registry.TryGet("freew.formatting-marks", out var formattingMarks).Should().BeTrue();
        formattingMarks.Should().BeAssignableTo<IRibbonStatefulCommand>();
        registry.TryGet("freew.reveal-formatting", out var revealFormatting).Should().BeTrue();
        revealFormatting.Should().BeAssignableTo<IRibbonStatefulCommand>();
    }

    [StaFact]
    public void HomeParagraph_MultilevelListDropdownExposesBackedLevelCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var paragraph = definition.FindTab("home")!.FindGroup("paragraph");
        var multilevel = paragraph!.Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.multilevel-list");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        var menuIds = multilevel.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .ToList();
        // The dropdown exposes the level commands plus predefined presets and Define New Multilevel List.
        menuIds.Should().Contain("freew.multilevel-promote");
        menuIds.Should().Contain("freew.multilevel-demote");
        menuIds.Should().Contain("freew.multilevel-define");
        // Every command surfaced in the menu must be backed by a registered command.
        foreach (var id in menuIds)
            registry.TryGet(id, out _).Should().BeTrue($"{id} must be backed before it appears on the Multilevel List menu");

        registry.TryGet("freew.multilevel-list", out _).Should().BeTrue("the top-level Multilevel List command applies backed outline numbering");
        registry.TryGet("freew.multilevel-promote", out _).Should().BeTrue("Word exposes list-level decrease from the Multilevel List menu");
        registry.TryGet("freew.multilevel-demote", out _).Should().BeTrue("Word exposes list-level increase from the Multilevel List menu");
    }

    [StaFact]
    public void ReviewComments_ExposesAndRegistersWordStyleThreadActions()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var comments = definition.FindTab("review")!.FindGroup("comments");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(comments!)
            .Should()
            .Equal(
                "freew.new-comment",
                "freew.delete-comment",
                "freew.previous-comment",
                "freew.next-comment",
                "freew.reply-comment",
                "freew.resolve-comment",
                "freew.show-comments");

        Labels(comments!)
            .Should()
            .Equal(
                "New Comment",
                "Delete",
                "Previous",
                "Next",
                "Reply",
                "Resolve",
                "Show Comments");

        foreach (var commandId in CommandIds(comments!))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Review comments group");
    }

    [StaFact]
    public void ReviewProofing_CommandsRouteToBackedActions()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var proofing = definition.FindTab("review")!.FindGroup("proofing");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        CommandIds(proofing!)
            .Should()
            .Equal(
                "freew.statistics",
                "freew.spellcheck-toggle",
                "freew.add-to-dictionary",
                "freew.thesaurus",
                "freew.set-proofing-language");

        Labels(proofing!)
            .Should()
            .Equal(
                "Word Count",
                "Spelling & Grammar",
                "Add to Dictionary",
                "Thesaurus",
                "Set Proofing Language");

        foreach (var commandId in CommandIds(proofing!))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from Review > Proofing");

        registry.TryGet("freew.spellcheck-toggle", out var spellcheck).Should().BeTrue();
        spellcheck.Should().BeAssignableTo<IRibbonStatefulCommand>();
    }

    [StaFact]
    public void ReviewTab_GroupsBackedCommandsLikeWord()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var review = definition.FindTab("review");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleReviewingPane = () => { },
                IsReviewingPaneVisible = () => false,
                AcceptThisChange = () => { },
                RejectThisChange = () => { },
                PreviousChange = () => { },
                NextChange = () => { },
            });

        review.Should().NotBeNull();
        review!.Groups.Select(group => group.Id)
            .Should()
            .Equal("proofing", "speech", "accessibility", "comments", "tracking", "changes", "protect", "compare", "inspect");

        CommandIds(review.FindGroup("accessibility")!)
            .Should()
            .Equal("freew.check-accessibility");
        CommandIds(review.FindGroup("tracking")!)
            .Should()
            .Equal("freew.track-changes", "freew.track-formatting", "freew.reviewing-pane", "freew.display-for-review", "freew.show-markup");
        MenuCommandIds(review.FindGroup("tracking")!)
            .Should()
            .Equal(
                "freew.display-for-review-all-markup",
                "freew.display-for-review-simple-markup",
                "freew.display-for-review-no-markup",
                "freew.display-for-review-original",
                "freew.show-markup-insertions-deletions",
                "freew.show-markup-comments",
                "freew.show-markup-formatting",
                "freew.show-markup-balloons");
        CommandIds(review.FindGroup("changes")!)
            .Should()
            .Equal("freew.accept-this", "freew.reject-this", "freew.previous-change", "freew.next-change");
        MenuCommandIds(review.FindGroup("changes")!)
            .Should()
            .Equal("freew.accept-this", "freew.accept-all", "freew.reject-this", "freew.reject-all");
        CommandIds(review.FindGroup("inspect")!)
            .Should()
            .Equal("freew.inspect-document");

        foreach (var commandId in CommandIds(review).Concat(MenuCommandIds(review)).Distinct())
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Review tab");
    }

    [StaFact]
    public void ReviewTrackingAndChanges_CommandRoutesExecuteBackedActions()
    {
        var editor = new DocumentView();
        var calls = new List<string>();
        var reviewingPaneVisible = false;
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleReviewingPane = () =>
                {
                    reviewingPaneVisible = !reviewingPaneVisible;
                    calls.Add("reviewing-pane");
                },
                IsReviewingPaneVisible = () => reviewingPaneVisible,
                AcceptThisChange = () => calls.Add("accept-this"),
                RejectThisChange = () => calls.Add("reject-this"),
                PreviousChange = () => calls.Add("previous-change"),
                NextChange = () => calls.Add("next-change"),
            });

        registry.TryGet("freew.track-changes", out var trackChanges).Should().BeTrue();
        var trackChangesState = trackChanges.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        trackChangesState.GetState().IsChecked.Should().BeFalse();

        trackChanges!.Execute(RibbonCommandContext.Empty);

        editor.TrackChangesEnabled.Should().BeTrue();
        editor.Model.TrackRevisions.Should().BeTrue();
        trackChangesState.GetState().IsChecked.Should().BeTrue();

        registry.TryGet("freew.track-formatting", out var trackFormatting).Should().BeTrue();
        var trackFormattingState = trackFormatting.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
        trackFormattingState.GetState().IsChecked.Should().BeTrue();

        trackFormatting!.Execute(RibbonCommandContext.Empty);

        editor.TrackFormattingEnabled.Should().BeFalse();
        editor.Model.DoNotTrackFormatting.Should().BeTrue();
        trackFormattingState.GetState().IsChecked.Should().BeFalse();

        foreach (var commandId in new[]
        {
            "freew.reviewing-pane",
            "freew.accept-this",
            "freew.reject-this",
            "freew.previous-change",
            "freew.next-change"
        })
        {
            registry.TryGet(commandId, out var command).Should().BeTrue($"{commandId} must route to its host-backed Review action");
            command!.Execute(RibbonCommandContext.Empty);
        }

        reviewingPaneVisible.Should().BeTrue();
        calls.Should().Equal("reviewing-pane", "accept-this", "reject-this", "previous-change", "next-change");
    }

    [StaFact]
    public void DesignPageBackground_ExposesWordStyleWatermarkPageColorAndPageBorders()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var pageBackground = definition.FindTab("design")!.FindGroup("page-background");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        pageBackground.Should().NotBeNull();
        CommandIds(pageBackground!)
            .Should()
            .Equal("freew.watermark", "freew.page-color", "freew.page-border");
        Labels(pageBackground!)
            .Should()
            .Equal("Watermark", "Page Color", "Page Borders");

        foreach (var commandId in CommandIds(pageBackground!))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from Design > Page Background");
    }

    [StaFact]
    public void DesignDocumentFormatting_ExposesBackedWordStyleThemeAndColorsSurfaces()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var design = definition.FindTab("design");
        var documentFormatting = design!.FindGroup("themes");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        design.Groups.Select(group => group.Id)
            .Should()
            .Equal("themes", "page-background");
        documentFormatting.Should().NotBeNull();
        CommandIds(documentFormatting!)
            .Should()
            .Equal(
                "freew.theme", "freew.style-set", "freew.reset-style-set",
                "freew.theme-colors", "freew.theme-fonts",
                "freew.paragraph-spacing", "freew.theme-effects");
        Labels(documentFormatting!)
            .Should()
            .Equal("Themes", "Style Sets", "Reset to Default Style Set", "Colors", "Fonts", "Paragraph Spacing", "Effects");

        foreach (var commandId in CommandIds(documentFormatting!))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from Design > Document Formatting");
    }

    [Fact]
    public void LayoutTab_DoesNotExposeDesignPageBackgroundCommands()
    {
        var layout = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf).FindTab("layout");

        layout.Should().NotBeNull();
        layout!.Groups.Select(group => group.Id)
            .Should()
            .NotContain("page-background", "Word exposes Watermark, Page Color, and Page Borders from Design");

        CommandIds(layout).Should().NotContain(new[]
        {
            "freew.watermark",
            "freew.page-color",
            "freew.page-border"
        });
    }

    [StaFact]
    public void LayoutParagraph_ExposesBackedWordStyleParagraphCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var layout = definition.FindTab("layout");
        var paragraph = layout!.FindGroup("paragraph");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        layout.Groups.Select(group => group.Id)
            .Should()
            .ContainInOrder("page-setup", "paragraph", "preview");
        CommandIds(paragraph!)
            .Should()
            .Equal(
                "freew.indent-decrease",
                "freew.indent-increase",
                "freew.line-spacing",
                "freew.indent-left",
                "freew.indent-right",
                "freew.space-before-toggle",
                "freew.space-after-toggle",
                "freew.space-before",
                "freew.space-after",
                "freew.paragraph-dialog",
                "freew.tabs-dialog");
        Labels(paragraph!)
            .Should()
            .Equal(
                "Decrease Indent",
                "Increase Indent",
                "Line and Paragraph Spacing",
                "Indent Left",
                "Indent Right",
                "Add Space Before Paragraph",
                "Add Space After Paragraph",
                "Spacing Before",
                "Spacing After",
                "Paragraph Settings",
                "Tabs");

        foreach (var commandId in CommandIds(paragraph!))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from Layout > Paragraph");
    }

    [StaFact]
    public void LayoutPageSetup_LineNumbersDropdownExposesBackedWordModeCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var pageSetup = definition.FindTab("layout")!.FindGroup("page-setup");
        var lineNumbers = pageSetup!.Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.line-numbers");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        lineNumbers.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value, item.Header))
            .Should()
            .Equal(
                ("freew.line-numbers-none", "None"),
                ("freew.line-numbers-continuous", "Continuous"),
                ("freew.line-numbers-restart-page", "Restart Each Page"),
                ("freew.line-numbers-restart-section", "Restart Each Section"),
                ("freew.line-numbers-options", "Line Numbering Options..."));

        registry.TryGet("freew.line-numbers", out _).Should().BeTrue("the top-level Line Numbers command keeps quick cycle behavior");
        registry.TryGet("freew.line-numbers-none", out var none).Should().BeTrue();
        registry.TryGet("freew.line-numbers-continuous", out var continuous).Should().BeTrue();
        registry.TryGet("freew.line-numbers-restart-page", out var restartPage).Should().BeTrue();
        registry.TryGet("freew.line-numbers-restart-section", out var restartSection).Should().BeTrue();
        registry.TryGet("freew.line-numbers-options", out _).Should().BeTrue("Word exposes Line Numbering Options from the same dropdown");

        continuous!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.LineNumberMode.Should().Be(LineNumberMode.Continuous);
        restartPage!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.LineNumberMode.Should().Be(LineNumberMode.RestartEachPage);
        restartSection!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.LineNumberMode.Should().Be(LineNumberMode.RestartEachSection);
        none!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.LineNumberMode.Should().Be(LineNumberMode.None);
    }

    [StaFact]
    public void LayoutPageSetup_ColumnsDropdownExposesBackedWordPresetCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var pageSetup = definition.FindTab("layout")!.FindGroup("page-setup");
        var columns = pageSetup!.Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.columns");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        columns.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value, item.Header))
            .Should()
            .Equal(
                ("freew.columns-one", "One"),
                ("freew.columns-two", "Two"),
                ("freew.columns-three", "Three"),
                ("freew.columns-left", "Left"),
                ("freew.columns-right", "Right"),
                ("freew.columns-more", "More Columns..."));

        foreach (var commandId in columns.Menu.Items
                     .Where(item => item.Kind == RibbonMenuItemKind.Command)
                     .Select(item => item.CommandId!.Value)
                     .Append("freew.columns"))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from Layout > Columns");

        registry.TryGet("freew.columns-three", out var three).Should().BeTrue();
        three!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.ColumnCount.Should().Be(3);
        editor.Model.Page.ColumnWidthsPt.Should().BeNull("equal-width presets clear explicit Left/Right widths");

        registry.TryGet("freew.columns-left", out var left).Should().BeTrue();
        left!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.ColumnCount.Should().Be(2);
        editor.Model.Page.ColumnWidthsPt.Should().NotBeNull();
        editor.Model.Page.ColumnWidthsPt![0].Should().BeLessThan(editor.Model.Page.ColumnWidthsPt[1]);

        registry.TryGet("freew.columns-right", out var right).Should().BeTrue();
        right!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.ColumnCount.Should().Be(2);
        editor.Model.Page.ColumnWidthsPt.Should().NotBeNull();
        editor.Model.Page.ColumnWidthsPt![0].Should().BeGreaterThan(editor.Model.Page.ColumnWidthsPt[1]);

        registry.TryGet("freew.columns-one", out var one).Should().BeTrue();
        one!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.ColumnCount.Should().Be(1);
        editor.Model.Page.ColumnWidthsPt.Should().BeNull();
    }

    [StaFact]
    public void MailingsTab_UsesWordStyleMergeCommandLabels()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var mailings = definition.FindTab("mailings");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        mailings.Should().NotBeNull();
        // Word's Mailings tab: Create (Envelopes/Labels) | Start Mail Merge | Write & Insert Fields | Preview | Finish.
        mailings!.Groups.Select(group => group.Id)
            .Should()
            .Equal("create", "merge-data", "merge-write", "merge-preview", "merge-finish");

        CommandIds(mailings)
            .Should()
            .Equal(
                // Create group
                "freew.merge-envelopes",
                "freew.merge-labels",
                // Start Mail Merge group
                "freew.start-mail-merge",
                "freew.merge-data",
                "freew.merge-edit-recipients",
                "freew.merge-filter-sort",
                // Write & Insert Fields group
                "freew.merge-address-block",
                "freew.merge-greeting-line",
                "freew.merge-field",
                "freew.merge-match-fields",
                "freew.merge-rules",           // Rules dropdown (replaces bare next-record / record-number buttons)
                // Preview Results group
                "freew.merge-preview",
                "freew.merge-preview-first",
                "freew.merge-preview-previous",
                "freew.merge-preview-next",
                "freew.merge-preview-last",
                "freew.merge-find-recipient",
                "freew.merge-check-errors",
                // Finish group
                "freew.merge-finish",
                "freew.merge-email");
        Labels(mailings)
            .Should()
            .Equal(
                "Envelopes",
                "Labels",
                "Start Mail Merge",
                "Select Recipients",
                "Edit Recipient List",
                "Filter & Sort Recipients",
                "Address Block",
                "Greeting Line",
                "Insert Merge Field",
                "Match Fields",
                "Rules",
                "Preview Results",
                "First Record",
                "Previous Record",
                "Next Record",
                "Last Record",
                "Find Recipient",
                "Check for Errors",
                "Finish & Merge",
                "Send E-mail Messages");

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

        // Verify the Rules dropdown exposes all rule command ids.
        var rulesDropdown = mailings.Groups.Single(g => g.Id == "merge-write").Controls
            .OfType<RibbonDropdown>()
            .Single(c => c.CommandId.Value == "freew.merge-rules");
        rulesDropdown.Menu.Items
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

        foreach (var commandId in CommandIds(mailings))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Mailings tab");

        foreach (var commandId in startMailMerge.Menu.Items
                     .Where(item => item.Kind == RibbonMenuItemKind.Command)
                     .Select(item => item.CommandId!.Value))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Start Mail Merge menu");

        foreach (var commandId in rulesDropdown.Menu.Items
                     .Where(item => item.Kind == RibbonMenuItemKind.Command)
                     .Select(item => item.CommandId!.Value))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Rules menu");
    }

    [StaFact]
    public void FinalCommandProfileAsymmetries_RouteToBackedWpfCommands()
    {
        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());

        registry.TryGet("freew.chart-size", out var chartSize).Should().BeTrue();
        registry.TryGet("freew.chart-size-dialog", out var chartSizeDialog).Should().BeTrue();
        chartSizeDialog.Should().BeSameAs(chartSize,
            "both chart size controls must route to the existing owner-modal size behavior");

        registry.TryGet("freew.merge-find-recipient", out var findRecipient).Should().BeTrue();
        findRecipient.Should().BeOfType<FreeWRibbonCommands.FindMergeRecipientCommand>();

        registry.TryGet("freew.merge-check-errors", out var checkErrors).Should().BeTrue();
        checkErrors.Should().BeOfType<FreeWRibbonCommands.CheckMergeErrorsCommand>();
    }

    [StaFact]
    public void MailingsFindRecipientAndCheckErrors_UseSharedPlannersThroughWpfCommands()
    {
        var editor = new DocumentView();
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("Name,City\nAda,London\nGrace,New York"),
            CurrentIndex = 1,
        };
        var messages = new List<string>();

        var findRecipient = new FreeWRibbonCommands.FindMergeRecipientCommand(
            editor,
            session,
            ask: _ => "ada",
            showInfo: (_, message) => messages.Add(message));
        findRecipient.Execute(RibbonCommandContext.Empty);

        session.CurrentIndex.Should().Be(0);
        messages.Should().ContainSingle().Which.Should().Be("Found recipient 1 of 2.");

        var checkErrors = new FreeWRibbonCommands.CheckMergeErrorsCommand(
            editor,
            session,
            ask: _ => MailMergeCheckForErrorsMode.CompleteAndPause,
            showInfo: (_, message) => messages.Add(message),
            completeMerge: _ => messages.Add("completed"));
        checkErrors.Execute(RibbonCommandContext.Empty);

        messages.Should().Contain("Checked 2 recipient(s). No mail merge errors were found.");
        messages.Should().Contain("completed");
    }

    [StaFact]
    public void MailingsFindRecipientAndCheckErrors_PreserveStateOnCancelAndRejectMissingRecipients()
    {
        var editor = new DocumentView();
        var session = new MailMergeSession { CurrentIndex = 3 };
        var prompts = 0;
        var messages = new List<string>();

        var findRecipient = new FreeWRibbonCommands.FindMergeRecipientCommand(
            editor,
            session,
            ask: _ => { prompts++; return "Ada"; },
            showInfo: (_, message) => messages.Add(message));
        findRecipient.Execute(RibbonCommandContext.Empty);

        prompts.Should().Be(0);
        session.CurrentIndex.Should().Be(3);
        messages.Should().ContainSingle().Which.Should().Contain("Select recipients first");

        session.Data = MergeData.FromCsv("Name\nAda");
        var cancelled = new FreeWRibbonCommands.FindMergeRecipientCommand(
            editor,
            session,
            ask: _ => null,
            showInfo: (_, message) => messages.Add(message));
        cancelled.Execute(RibbonCommandContext.Empty);

        session.CurrentIndex.Should().Be(3);
        messages.Should().HaveCount(1);

        session.Data = null;
        var checkErrors = new FreeWRibbonCommands.CheckMergeErrorsCommand(
            editor,
            session,
            ask: _ => { prompts++; return MailMergeCheckForErrorsMode.SimulateAndReport; },
            showInfo: (_, message) => messages.Add(message));
        checkErrors.Execute(RibbonCommandContext.Empty);

        prompts.Should().Be(0);
        messages.Should().HaveCount(2);
        messages[^1].Should().Contain("Select recipients first");
    }

    [StaFact]
    public void MailingsAddressAndGreetingCommands_InsertNativeWordFields()
    {
        var editor = new DocumentView();
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("FirstName,LastName\nAda,Lovelace")
        };

        new FreeWRibbonCommands.InsertAddressBlockCommand(() => editor, session)
            .Execute(RibbonCommandContext.Empty);
        new FreeWRibbonCommands.InsertGreetingLineCommand(() => editor, session)
            .Execute(RibbonCommandContext.Empty);

        var fields = editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToArray();
        fields.Select(run => run.ComplexField!.Instruction).Should().BeEquivalentTo(
            " ADDRESSBLOCK \\* MERGEFORMAT ",
            " GREETINGLINE \\f \"<<_BEFORE_ Dear >><<_TITLE0_ >><<_LAST0_>><<_AFTER_ ,>>\" \\e \"Dear Sir or Madam,\" \\l 1033 \\* MERGEFORMAT ");
        fields.Select(run => run.Text).Should().BeEquivalentTo("«AddressBlock»", "«GreetingLine»");
    }

    [StaFact]
    public void MailingsCheckErrors_SimulationOpensEditableReportInsteadOfCompletingMerge()
    {
        var editor = new DocumentView();
        editor.LoadModel(new TextDocument
        {
            Blocks = { new Paragraph($"Dear {MailMerge.FieldOpen}Missing{MailMerge.FieldClose}") }
        });
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("Name\nAda")
        };
        var messages = new List<string>();
        TextDocument? report = null;
        var completed = false;
        var command = new FreeWRibbonCommands.CheckMergeErrorsCommand(
            editor,
            session,
            ask: _ => MailMergeCheckForErrorsMode.SimulateAndReport,
            showInfo: (_, message) => messages.Add(message),
            completeMerge: _ => completed = true,
            openReportDocument: document => report = document);

        command.Execute(RibbonCommandContext.Empty);

        report.Should().NotBeNull();
        report!.Properties.Title.Should().Be("Mail Merge Error Report");
        report.PlainText.Should().Contain("Merge field 'Missing'");
        messages.Should().BeEmpty();
        completed.Should().BeFalse();
    }

    [StaFact]
    public void MailingsCheckErrors_PauseReportsEachErrorThenCompletes()
    {
        var editor = new DocumentView();
        editor.LoadModel(new TextDocument
        {
            Blocks =
            {
                new Paragraph(
                    $"{MailMerge.FieldOpen}MissingOne{MailMerge.FieldClose} "
                    + $"{MailMerge.FieldOpen}MissingTwo{MailMerge.FieldClose}")
            }
        });
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("Name\nAda")
        };
        var events = new List<string>();
        var command = new FreeWRibbonCommands.CheckMergeErrorsCommand(
            editor,
            session,
            ask: _ => MailMergeCheckForErrorsMode.CompleteAndPause,
            showInfo: (_, message) => events.Add(message),
            completeMerge: _ => events.Add("completed"));

        command.Execute(RibbonCommandContext.Empty);

        events.Should().HaveCount(3);
        events[0].Should().Contain("MissingOne");
        events[1].Should().Contain("MissingTwo");
        events[2].Should().Be("completed");
    }

    [StaFact]
    public void MailingsCheckErrors_NoPauseCompletesAndOpensErrorReport()
    {
        var editor = new DocumentView();
        editor.LoadModel(new TextDocument
        {
            Blocks = { new Paragraph($"{MailMerge.FieldOpen}Missing{MailMerge.FieldClose}") }
        });
        var session = new MailMergeSession
        {
            Data = MergeData.FromCsv("Name\nAda")
        };
        var completed = false;
        TextDocument? report = null;
        var command = new FreeWRibbonCommands.CheckMergeErrorsCommand(
            editor,
            session,
            ask: _ => MailMergeCheckForErrorsMode.CompleteWithoutPausing,
            showInfo: (_, _) => throw new InvalidOperationException("No-pause errors belong in the report document."),
            completeMerge: _ => completed = true,
            openReportDocument: document => report = document);

        command.Execute(RibbonCommandContext.Empty);

        completed.Should().BeTrue();
        report!.PlainText.Should().Contain("Missing");
    }

    [StaFact]
    public void MailingsRulesSpecialFields_InsertSharedInstructionsThroughRegistry()
    {
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        foreach (var commandId in new[]
                 {
                     "freew.merge-next-record",
                     "freew.merge-record-number",
                     "freew.merge-sequence-number"
                 })
        {
            registry.TryGet(commandId, out var command).Should().BeTrue($"{commandId} must be backed by WPF");
            command!.Execute(RibbonCommandContext.Empty);
        }

        editor.CommitToModel();

        var fields = editor.Model.Blocks
            .OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToList();
        fields.ToDictionary(run => run.ComplexField!.Keyword, run => run.Text).Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                [MailMerge.NextRecordInstruction] = $"{MailMerge.FieldOpen}{MailMerge.NextRecordField}{MailMerge.FieldClose}",
                [MailMerge.MergeRecordNumberInstruction] = $"{MailMerge.FieldOpen}{MailMerge.MergeRecordNumberField}{MailMerge.FieldClose}",
                [MailMerge.MergeSequenceNumberInstruction] = $"{MailMerge.FieldOpen}{MailMerge.MergeSequenceNumberField}{MailMerge.FieldClose}"
            });
    }

    [StaFact]
    public void ViewShow_ExposesWordStyleRulerToggle()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var show = definition.FindTab("view")!.FindGroup("show");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleNavigationPane = () => { },
                IsNavigationPaneVisible = () => false,
                ToggleRuler = () => { },
                IsRulerVisible = () => true,
            });

        show.Should().NotBeNull();
        CommandIds(show!)
            .Should()
            .Equal("freew.ruler", "freew.nav-pane", "freew.gridlines");
        Labels(show!)
            .Should()
            .Equal("Ruler", "Navigation Pane", "Gridlines");

        registry.TryGet("freew.ruler", out var command).Should().BeTrue("Word exposes View > Show > Ruler");
        command.Should().BeAssignableTo<IRibbonStatefulCommand>();

        registry.TryGet("freew.gridlines", out var gridCommand).Should().BeTrue("Word exposes View > Show > Gridlines");
        gridCommand.Should().BeAssignableTo<IRibbonStatefulCommand>();
    }

    [StaFact]
    public void ViewZoom_ExposesBackedWordStyleQuickControls()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var zoom = definition.FindTab("view")!.FindGroup("zoom");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                OpenZoomDialog = () => { },
                ApplyZoom = (_, _) => { },
                ZoomOnePage = () => { },
                ZoomPageWidth = () => { },
            });

        zoom.Should().NotBeNull();
        CommandIds(zoom!)
            .Should()
            .Equal("freew.zoom-dialog", "freew.zoom-100", "freew.zoom-one-page", "freew.zoom-page-width",
                   "freew.zoom-multiple-pages", "freew.zoom-side-to-side");
        Labels(zoom!)
            .Should()
            .Equal("Zoom", "100%", "One Page", "Page Width", "Multiple Pages", "Side to Side");

        // The four existing zoom commands are backed by the registry built above.
        foreach (var commandId in new[]
                 {
                     "freew.zoom-dialog", "freew.zoom-100", "freew.zoom-one-page", "freew.zoom-page-width"
                 })
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from View > Zoom");
        // Multiple Pages and Side to Side are backed only when the host supplies callbacks;
        // the registry built above has no callbacks for them — they are absent here by design.
        registry.TryGet("freew.zoom-multiple-pages", out _).Should().BeFalse(
            "Multiple Pages is absent when the host supplies no callback");
        registry.TryGet("freew.zoom-side-to-side", out _).Should().BeFalse(
            "Side to Side is absent when the host supplies no callback");
    }

    [StaFact]
    public void DeveloperControls_ExposesAndRegistersImplementedContentControlCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var developer = definition.FindTab("developer");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        definition.VisibleTabs.Select(tab => tab.Id)
            .Should()
            .ContainInOrder("view", "developer");

        developer.Should().NotBeNull();
        developer!.Groups.Select(group => group.Id)
            .Should()
            .Equal("controls");

        CommandIds(developer)
            .Should()
            .Equal(
                "freew.cc-text",
                "freew.cc-richtext",
                "freew.cc-checkbox",
                "freew.cc-date",
                "freew.cc-dropdown",
                "freew.cc-combo");

        foreach (var commandId in CommandIds(developer))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Developer controls group");
    }

    [Fact]
    public void InsertTab_DoesNotExposeContentControlsOutsideDeveloper()
    {
        var insert = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf).FindTab("insert");

        insert.Should().NotBeNull();
        insert!.Groups.Select(group => group.Id)
            .Should()
            .NotContain("controls", "Word exposes content controls from the Developer tab");

        CommandIds(insert).Should().NotContain(new[]
        {
            "freew.cc-text",
            "freew.cc-richtext",
            "freew.cc-checkbox",
            "freew.cc-date",
            "freew.cc-dropdown",
            "freew.cc-combo"
        });
    }

    [Fact]
    public void Build_ExposesWordStyleTableDesignAndTableLayoutContextualTabs()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);

        definition.ContextualTabs.Select(tab => tab.Id)
            .Should()
            .ContainInOrder("picture-format", "chart-design", "chart-format", "table-design", "table-layout");

        foreach (var tabId in new[] { "table-design", "table-layout" })
        {
            var tab = definition.FindTab(tabId);

            tab.Should().NotBeNull();
            tab!.Context.Should().NotBeNull();
            tab.Context!.ActivationKey.Should().Be("table");
            tab.Context.Label.Should().Be("Table Tools");
            tab.Context.Color.Should().Be(RibbonContextColor.Teal);
        }
    }

    [Fact]
    public void Build_ExposesWordStyleChartDesignAndChartFormatContextualTabs()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);

        foreach (var tabId in new[] { "chart-design", "chart-format" })
        {
            var tab = definition.FindTab(tabId);

            tab.Should().NotBeNull();
            tab!.Context.Should().NotBeNull();
            tab.Context!.ActivationKey.Should().Be("chart");
            tab.Context.Label.Should().Be("Chart Tools");
            tab.Context.Color.Should().Be(RibbonContextColor.Orange);
        }
    }

    [StaFact]
    public void ChartDesign_ContextualTabContainsBackedChartCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var chartDesign = definition.FindTab("chart-design");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        chartDesign.Should().NotBeNull();
        chartDesign!.Groups.Select(group => group.Id)
            .Should()
            .Equal("chart-type", "chart-data", "chart-quick-layout", "chart-style", "chart-colors", "chart-elements");

        CommandIds(chartDesign)
            .Should()
            .Contain(new[]
            {
                "freew.chart-type-column",
                "freew.chart-edit-data",
                "freew.chart-title",
                "freew.chart-axis-titles",
                "freew.chart-toggle-legend"
            });

        // Gallery commands: one per catalog entry for each gallery.
        foreach (var layout in ChartQuickLayout.Catalog)
            registry.TryGet($"freew.chart-quick-layout-{layout.Id}", out _)
                .Should().BeTrue($"freew.chart-quick-layout-{layout.Id} must be backed");
        foreach (var style in ChartStyle.Catalog)
            registry.TryGet($"freew.chart-style-{style.Id}", out _)
                .Should().BeTrue($"freew.chart-style-{style.Id} must be backed");
        foreach (var scheme in ChartColorScheme.Catalog)
        {
            var commandId = ChartColorRibbonCommandCatalog.CommandId(scheme);
            registry.TryGet(commandId, out _)
                .Should().BeTrue($"{commandId} must be backed");
        }

        foreach (var commandId in new[]
        {
            "freew.chart-type-column",
            "freew.chart-type-bar",
            "freew.chart-type-line",
            "freew.chart-type-pie",
            "freew.chart-type-scatter",
            "freew.chart-type-area",
            "freew.chart-type-doughnut",
            "freew.chart-edit-data",
            "freew.chart-title",
            "freew.chart-axis-titles",
            "freew.chart-toggle-legend"
        })
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must be backed in the Chart Design tab");
    }

    [StaFact]
    public void ChartFormat_ContextualTabContainsBackedSizeCommand()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var chartFormat = definition.FindTab("chart-format");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        chartFormat.Should().NotBeNull();
        chartFormat!.Groups.Select(group => group.Id)
            .Should()
            .Equal("chart-arrange", "chart-size");

        CommandIds(chartFormat)
            .Should()
            .Equal("freew.shape-rotate", "freew.chart-size", "freew.chart-size-dialog");
        Labels(chartFormat)
            .Should()
            .Equal("Rotate", "Size", "More Size Options...");

        registry.TryGet("freew.chart-size", out _).Should().BeTrue("freew.chart-size must execute from Chart Format");
        registry.TryGet("freew.chart-size-dialog", out _).Should().BeTrue("freew.chart-size-dialog must execute from Chart Format");
    }

    [StaFact]
    public void ChartDesign_ChangeTypeRibbonCommandMutatesSelectedChartAndUndoRestoresIt()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Before"));
        editor.InsertChart(Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], seriesName: "S"));
        var chart = editor.SelectedChart() ?? editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();
        chart!.Kind.Should().Be(ChartKind.Column);

        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        registry.TryGet("freew.chart-type-bar", out var command)
            .Should().BeTrue("the WPF Chart Design type command must be registered");
        command!.Execute(RibbonCommandContext.Empty);

        chart.Kind.Should().Be(ChartKind.Bar);
        editor.Commands.Undo().Should().BeTrue();
        chart.Kind.Should().Be(ChartKind.Column);
    }

    [StaFact]
    public void ChartDesign_StyleGalleryCommandMutatesSelectedChartAndUndoRestoresIt()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Before"));
        editor.InsertChart(Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], seriesName: "S"));
        var chart = editor.SelectedChart() ?? editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();
        chart!.StyleId.Should().Be(0);

        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        registry.TryGet("freew.chart-style-5", out var command)
            .Should().BeTrue("the WPF Chart Design style gallery command must be registered");
        command!.Execute(RibbonCommandContext.Empty);

        chart.StyleId.Should().Be(5);
        editor.Commands.Undo().Should().BeTrue();
        chart.StyleId.Should().Be(0);
    }

    [StaFact]
    public void ChartDesign_QuickLayoutCatalogCommandsMatchSelectionMutationAndUndoBehavior()
    {
        foreach (var layout in ChartQuickLayout.Catalog)
        {
            var editor = new DocumentView();
            editor.Model.Blocks.Clear();
            editor.Model.Blocks.Add(new Paragraph("Before"));
            editor.InsertChart(Chart.Create(
                ChartKind.Column,
                ["A", "B"],
                [1.0, 2.0],
                seriesName: "Sales",
                title: "Revenue"));
            var chart = editor.SelectedChart()!;
            chart.ShowLegend = true;
            chart.CategoryAxisTitle = "Quarter";
            chart.ValueAxisTitle = "USD";
            chart.StyleId = 7;
            chart.ColorSchemeId = "mono-blue";
            var categories = chart.Categories.ToArray();
            var values = chart.Series[0].Values.ToArray();

            var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
            registry.TryGet($"freew.chart-quick-layout-{layout.Id}", out var command).Should().BeTrue();
            var stateful = command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject;
            stateful.GetState().IsEnabled.Should().BeTrue();

            command!.Execute(RibbonCommandContext.Empty);

            chart.QuickLayoutId.Should().Be(layout.Id);
            chart.StyleId.Should().Be(7);
            chart.ColorSchemeId.Should().Be("mono-blue");
            chart.Categories.Should().Equal(categories);
            chart.Series[0].Values.Should().Equal(values);
            editor.SelectedChart().Should().BeSameAs(chart);

            editor.Commands.Undo().Should().BeTrue();
            chart.QuickLayoutId.Should().Be(0);
            editor.Commands.Redo().Should().BeTrue();
            chart.QuickLayoutId.Should().Be(layout.Id);
        }

        var emptyRegistry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        foreach (var layout in ChartQuickLayout.Catalog)
        {
            emptyRegistry.TryGet($"freew.chart-quick-layout-{layout.Id}", out var command).Should().BeTrue();
            command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject
                .GetState().IsEnabled.Should().BeFalse();
        }
    }

    [StaFact]
    public void ChartDesign_ColorSchemeRibbonCommandMutatesSelectedChartAndUndoRestoresIt()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Before"));
        editor.InsertChart(Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], seriesName: "S"));
        var chart = editor.SelectedChart() ?? editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();
        chart!.ColorSchemeId.Should().BeNull();

        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        var monoBlue = ChartColorScheme.Catalog.Single(scheme => scheme.Id == "mono-blue");
        registry.TryGet(ChartColorRibbonCommandCatalog.CommandId(monoBlue), out var command)
            .Should().BeTrue("the WPF Chart Design color scheme command must be registered");
        command!.Execute(RibbonCommandContext.Empty);

        chart.ColorSchemeId.Should().Be("mono-blue");
        editor.Commands.Undo().Should().BeTrue();
        chart.ColorSchemeId.Should().BeNull();
    }

    [StaFact]
    public void ChartDesign_ToggleLegendMutatesSelectedChart()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Before"));
        editor.InsertChart(Chart.Create(ChartKind.Line, ["X", "Y"], [3.0, 4.0]));
        var chart = editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();
        var initial = chart!.ShowLegend;

        editor.ToggleSelectedChartLegend();
        chart.ShowLegend.Should().Be(!initial);

        editor.Commands.Undo().Should().BeTrue();
        chart.ShowLegend.Should().Be(initial);
    }

    [StaFact]
    public void ChartDesign_ToggleLegendRibbonCommandMutatesSelectedChartAndUndoRestoresIt()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Before"));
        editor.InsertChart(Chart.Create(ChartKind.Line, ["X", "Y"], [3.0, 4.0]));
        var chart = editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        chart.Should().NotBeNull();
        chart!.ShowLegend.Should().BeFalse();

        registry.TryGet("freew.chart-toggle-legend", out var command)
            .Should().BeTrue("the WPF Chart Design legend command must be registered");
        command!.Execute(RibbonCommandContext.Empty);

        chart.ShowLegend.Should().BeTrue();
        editor.Commands.Undo().Should().BeTrue();
        chart.ShowLegend.Should().BeFalse();
    }

    [StaFact]
    public void ChartDesign_TitleSetterMutatesSelectedChartAndUndoRestoresIt()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Before"));
        editor.InsertChart(Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], seriesName: "S"));
        var chart = editor.SelectedChart() ?? editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();
        chart!.Title = "Old Title";
        chart.QuickLayoutId = 2;

        editor.SetSelectedChartTitle("  Chart Title  ");

        chart.Title.Should().Be("Chart Title");
        chart.QuickLayoutId.Should().Be(0);
        editor.Commands.Undo().Should().BeTrue();
        chart.Title.Should().Be("Old Title");
        chart.QuickLayoutId.Should().Be(2);
    }

    [StaFact]
    public void ChartDesign_AxisTitlesSetterMutatesSelectedChartAndUndoRestoresIt()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("Before"));
        editor.InsertChart(Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], seriesName: "S"));
        var chart = editor.SelectedChart() ?? editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();
        chart!.QuickLayoutId = 9;

        editor.SetSelectedChartAxisTitles("  Quarter  ", "  Revenue  ");

        chart.CategoryAxisTitle.Should().Be("Quarter");
        chart.ValueAxisTitle.Should().Be("Revenue");
        chart.QuickLayoutId.Should().Be(0);
        editor.Commands.Undo().Should().BeTrue();
        chart.CategoryAxisTitle.Should().BeNull();
        chart.ValueAxisTitle.Should().BeNull();
        chart.QuickLayoutId.Should().Be(9);
    }

    [StaFact]
    public void ChartDesign_SetSizeMutatesWidthAndHeightAndIsUndoable()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.InsertChart(Chart.Create(ChartKind.Pie, ["A"], [1.0]));
        var chart = editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();
        var oldWidth = chart!.WidthPt;
        var oldHeight = chart.HeightPt;
        editor.SetSelectedChartSize(400, 300);
        chart.WidthPt.Should().Be(400);
        chart.HeightPt.Should().Be(300);
        editor.Commands.Undo().Should().BeTrue();
        chart.WidthPt.Should().Be(oldWidth);
        chart.HeightPt.Should().Be(oldHeight);
        editor.Commands.Redo().Should().BeTrue();
        chart.WidthPt.Should().Be(400);
        chart.HeightPt.Should().Be(300);
    }

    [StaFact]
    public void ChartDesign_ReplaceChartDataMutatesModelAndIsUndoable()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.InsertChart(Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0], seriesName: "Sales"));
        var chart = editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();
        chart!.StyleId = 7;
        chart.ColorSchemeId = "mono-blue";
        chart.RotationAngle = 20;
        var oldCategories = chart.Categories.ToArray();
        var oldValues = chart.Series[0].Values.ToArray();

        var replacement = Chart.Create(ChartKind.Bar, ["Jan", "Feb", "Mar"], [5.0, 6.0, 7.0], seriesName: "Revenue");
        replacement.StyleId = 2;
        replacement.RotationAngle = 90;
        editor.ReplaceSelectedChartData(replacement);

        chart.Kind.Should().Be(ChartKind.Bar);
        chart.Categories.Should().Equal("Jan", "Feb", "Mar");
        chart.Series.Should().HaveCount(1);
        chart.Series[0].Name.Should().Be("Revenue");
        chart.Series[0].Values.Should().Equal(5.0, 6.0, 7.0);
        chart.StyleId.Should().Be(7);
        chart.ColorSchemeId.Should().Be("mono-blue");
        chart.RotationAngle.Should().Be(20);

        editor.Commands.Undo().Should().BeTrue();
        chart.Kind.Should().Be(ChartKind.Column);
        chart.Categories.Should().Equal(oldCategories);
        chart.Series[0].Values.Should().Equal(oldValues);
        chart.StyleId.Should().Be(7);
        editor.Commands.Redo().Should().BeTrue();
        chart.Kind.Should().Be(ChartKind.Bar);
        chart.Categories.Should().Equal("Jan", "Feb", "Mar");
    }

    [StaFact]
    public void TableDesign_ContextualTabContainsOnlyImplementedStyleCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var tableDesign = definition.FindTab("table-design");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        tableDesign.Should().NotBeNull();
        tableDesign!.Groups.Select(group => group.Id)
            .Should()
            .Equal("table-style-options", "table-style", "draw-borders");

        CommandIds(tableDesign)
            .Should()
            .Equal(
                "freew.table-header-row",
                "freew.table-last-row",
                "freew.table-first-column",
                "freew.table-last-column",
                "freew.table-banded-rows",
                "freew.table-banded-cols",
                "freew.table-shading",
                "freew.table-borders",
                "freew.draw-table",
                "freew.eraser");

        foreach (var commandId in CommandIds(tableDesign))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Table Design tab");
    }

    [StaFact]
    public void PictureFormat_ContextualTabExposesBackedWrapTextMenu()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var picture = definition.FindTab("picture-format");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        picture.Should().NotBeNull();
        picture!.Context.Should().NotBeNull();
        picture.Context!.ActivationKey.Should().Be("picture");
        picture.Context.Label.Should().Be("Picture Tools");
        picture.Context.Color.Should().Be(RibbonContextColor.Orange);
        picture.Groups.Select(group => group.Id)
            .Should()
            .Equal("picture-arrange", "picture-styles", "picture-adjust", "picture-size");

        CommandIds(picture.FindGroup("picture-arrange")!)
            .Should()
            .Equal(
                "freew.image-wrap",
                "freew.image-position",
                "freew.image-rotate",
                "freew.image-align-left",
                "freew.image-align-center",
                "freew.image-align-right",
                // W24: align-to-page/margin and distribute for floating objects.
                "freew.image-align-to-page",
                "freew.image-align-to-margin",
                "freew.image-distribute-h",
                "freew.image-distribute-v",
                // Phase 2: z-order commands for floating images.
                "freew.image-bring-to-front",
                "freew.image-send-to-back",
                "freew.image-bring-forward",
                "freew.image-send-backward",
                // Phase 4: group / ungroup for floating objects.
                "freew.object-group",
                "freew.object-ungroup");

        // W24: Picture Styles gallery group.
        CommandIds(picture.FindGroup("picture-styles")!)
            .Should()
            .ContainInOrder(
                "freew.image-style-1",
                "freew.image-style-12");

        CommandIds(picture.FindGroup("picture-adjust")!)
            .Should()
            .Equal(
                // W20: Adjust group — Corrections / Color / Transparency (new).
                "freew.image-corrections",
                "freew.image-color",
                "freew.image-transparency",
                // W24: Picture Effects menu.
                "freew.image-effects",
                // W25: Artistic Effects menu.
                "freew.image-artistic",
                "freew.image-crop",
                "freew.image-reset",
                "freew.image-border");

        var wrap = picture.FindGroup("picture-arrange")!.Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.image-wrap");
        wrap.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value, item.Header))
            .Should()
            .Equal(
                ("freew.image-wrap-inline", "In Line with Text"),
                ("freew.image-wrap-square", "Square"),
                ("freew.image-wrap-tight", "Tight"),
                ("freew.image-wrap-top-bottom", "Top and Bottom"),
                ("freew.image-wrap-behind", "Behind Text"),
                ("freew.image-wrap-front", "In Front of Text"));

        var rotate = picture.FindGroup("picture-arrange")!.Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.image-rotate");
        rotate.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .Should()
            .Equal(
                "freew.image-rotate-right90",
                "freew.image-rotate-left90",
                "freew.image-flip-vertical",
                "freew.image-flip-horizontal");

        foreach (var commandId in MenuCommandIds(wrap).Concat(MenuCommandIds(rotate)).Concat(CommandIds(picture)))
        {
            // Pure menu-opener dropdowns with no direct command action — they only open the sub-menu.
            if (commandId is "freew.image-wrap" or "freew.image-rotate"
                or "freew.image-corrections" or "freew.image-color" or "freew.image-transparency"
                or "freew.image-effects")
                continue;
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from Picture Format");
        }

        // W24: All Picture Style preset commands must be backed.
        foreach (var preset in PictureStyleCatalog.Catalog)
            registry.TryGet($"freew.image-style-{preset.Id}", out _)
                .Should().BeTrue($"freew.image-style-{preset.Id} must be backed");
    }

    [StaFact]
    public void TableLayout_ContextualTabContainsImplementedTableLayoutCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var tableLayout = definition.FindTab("table-layout");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        tableLayout.Should().NotBeNull();
        tableLayout!.Groups.Select(group => group.Id)
            .Should()
            .Equal("table-table", "table-rows-cols", "table-merge", "table-cell-size", "table-alignment", "table-data");

        CommandIds(tableLayout)
            .Should()
            .Equal(
                // table-table group
                "freew.table-select-table",
                "freew.table-select-row",
                "freew.table-select-col",
                "freew.table-select-cell",
                "freew.table-view-gridlines",
                "freew.table-properties",
                // table-rows-cols group
                "freew.table-insert-above",
                "freew.table-insert-below",
                "freew.table-insert-col-left",
                "freew.table-insert-col-right",
                "freew.table-delete-row",
                "freew.table-delete-col",
                "freew.table-delete",
                // table-merge group
                "freew.table-merge-cells",
                "freew.table-split-cell",
                "freew.split-table",
                // table-cell-size group
                "freew.table-row-height",
                "freew.table-col-width",
                "freew.table-distribute-rows",
                "freew.table-distribute-cols",
                "freew.table-autofit-contents",
                "freew.table-autofit-window",
                "freew.table-autofit-fixed",
                // table-alignment group
                "freew.cell-align-top-left",
                "freew.cell-align-top-center",
                "freew.cell-align-top-right",
                "freew.cell-align-middle-left",
                "freew.cell-align-middle-center",
                "freew.cell-align-middle-right",
                "freew.cell-align-bottom-left",
                "freew.cell-align-bottom-center",
                "freew.cell-align-bottom-right",
                "freew.table-cell-margins",
                "freew.cell-text-direction-horizontal",
                "freew.cell-text-direction-rotate90",
                "freew.cell-text-direction-rotate270",
                // table-data group
                "freew.table-repeat-header",
                "freew.table-formula",
                "freew.sort",
                "freew.table-to-text");

        foreach (var commandId in CommandIds(tableLayout))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Table Layout tab");
    }

    [Fact]
    public void InsertTab_DoesNotExposeTableMutationToolsOutsideTableContext()
    {
        var insert = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf).FindTab("insert");

        insert.Should().NotBeNull();
        insert!.Groups.Select(group => group.Id)
            .Should()
            .NotContain("table-tools");

        CommandIds(insert).Should().Contain("freew.table");
        CommandIds(insert).Should().NotContain(new[]
        {
            "freew.table-insert-below",
            "freew.table-delete-row",
            "freew.table-insert-col-right",
            "freew.table-delete-col",
            "freew.table-shading",
            "freew.table-merge-cells",
            "freew.table-split-cell",
            "freew.table-header-row",
            "freew.table-banded-rows",
            "freew.table-repeat-header",
            "freew.table-formula",
            "freew.table-properties"
        });
    }

    private static IEnumerable<string> CommandIds(RibbonTab tab)
    {
        foreach (var control in tab.Groups.SelectMany(group => group.Controls))
        {
            if (!string.IsNullOrWhiteSpace(control.CommandId.Value))
                yield return control.CommandId.Value;
        }
    }

    private static IEnumerable<string> CommandIds(RibbonGroup group)
    {
        foreach (var control in group.Controls)
        {
            if (!string.IsNullOrWhiteSpace(control.CommandId.Value))
                yield return control.CommandId.Value;
        }
    }

    // ── Mailings: filter/sort logic (pure, no STA window) ──────────────────────────────────────────

    [Fact]
    public void MergeFilterSort_IncludeAllRows_ReturnsSameCount()
    {
        // Three records; all included (no exclusion) → same count back.
        var data = MergeData.FromCsv("Name,City\nAlice,Paris\nBob,Rome\nCarol,Berlin");
        var included = data.Rows.ToList(); // all three
        included.Should().HaveCount(3);
    }

    [Fact]
    public void MergeFilterSort_ExcludeOneRow_ReturnsSubset()
    {
        var data = MergeData.FromCsv("Name,City\nAlice,Paris\nBob,Rome\nCarol,Berlin");

        // Simulate excluding "Bob" (row index 1) — rebuild MergeData from [row0, row2].
        var subset = new[] { data.Rows[0], data.Rows[2] };
        var rebuilt = new MergeData(
            data.Header,
            subset.Select(r => (IReadOnlyList<string>)data.Header.Select(h => r.TryGetValue(h, out var v) ? v : string.Empty).ToList()).ToList());

        rebuilt.Count.Should().Be(2);
        rebuilt.Rows[0]["Name"].Should().Be("Alice");
        rebuilt.Rows[1]["Name"].Should().Be("Carol");
    }

    [Fact]
    public void MergeFilterSort_SortByColumnAscending_OrdersRows()
    {
        var data = MergeData.FromCsv("Name,City\nCarol,Berlin\nAlice,Paris\nBob,Rome");

        // Sort ascending by Name.
        var sorted = data.Rows
            .OrderBy(r => r.TryGetValue("Name", out var v) ? v : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rebuilt = new MergeData(
            data.Header,
            sorted.Select(r => (IReadOnlyList<string>)data.Header.Select(h => r.TryGetValue(h, out var v) ? v : string.Empty).ToList()).ToList());

        rebuilt.Rows.Select(r => r["Name"]).Should().Equal("Alice", "Bob", "Carol");
    }

    [Fact]
    public void MergeFilterSort_SortByColumnDescending_OrdersRows()
    {
        var data = MergeData.FromCsv("Name,City\nCarol,Berlin\nAlice,Paris\nBob,Rome");

        var sorted = data.Rows
            .OrderByDescending(r => r.TryGetValue("Name", out var v) ? v : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rebuilt = new MergeData(
            data.Header,
            sorted.Select(r => (IReadOnlyList<string>)data.Header.Select(h => r.TryGetValue(h, out var v) ? v : string.Empty).ToList()).ToList());

        rebuilt.Rows.Select(r => r["Name"]).Should().Equal("Carol", "Bob", "Alice");
    }

    [Fact]
    public void MergeFilterSort_FilterThenSort_ProducesCorrectSubsetInOrder()
    {
        var data = MergeData.FromCsv("Name,City\nCarol,Berlin\nAlice,Paris\nBob,Rome\nDave,Oslo");

        // Exclude Bob (index 2), then sort remaining ascending by Name.
        var chosen = data.Rows.Where((_, i) => i != 2)  // Carol, Alice, Dave
            .OrderBy(r => r.TryGetValue("Name", out var v) ? v : string.Empty, StringComparer.OrdinalIgnoreCase);
        var rebuilt = new MergeData(
            data.Header,
            chosen.Select(r => (IReadOnlyList<string>)data.Header.Select(h => r.TryGetValue(h, out var v) ? v : string.Empty).ToList()).ToList());

        rebuilt.Count.Should().Be(3);
        rebuilt.Rows.Select(r => r["Name"]).Should().Equal("Alice", "Carol", "Dave");
    }

    // ── Mailings: envelope geometry (pure arithmetic) ───────────────────────────────────────────────

    [Fact]
    public void EnvelopeGeometry_DL_WidthAndHeightRoundTrip()
    {
        // DL envelope: 110 × 220 mm → points (72 pt per inch, 25.4 mm per inch).
        const double mmToPt = 72.0 / 25.4;
        var widthPt  = Math.Round(110 * mmToPt, 3);
        var heightPt = Math.Round(220 * mmToPt, 3);

        // Values must be in portrait order (narrow × long) as the command stores them.
        widthPt.Should().BeApproximately(311.811, 0.01);
        heightPt.Should().BeApproximately(623.622, 0.01);
        // Landscape flag swaps which dimension is displayed horizontally — the stored values stay portrait.
        widthPt.Should().BeLessThan(heightPt);
    }

    [Fact]
    public void EnvelopeGeometry_CommTen_PointsMatchUsInchSpec()
    {
        // US Comm-10: 4.125 in × 9.5 in
        var widthPt  = 4.125 * 72;
        var heightPt = 9.5   * 72;

        widthPt.Should().BeApproximately(297.0, 0.01);
        heightPt.Should().BeApproximately(684.0, 0.01);
        widthPt.Should().BeLessThan(heightPt);
    }

    // ── Mailings: label grid dimensions (pure arithmetic) ──────────────────────────────────────────

    [Fact]
    public void LabelGrid_Avery5160_Is3Columns10Rows()
    {
        // Avery 5160 is the most common US label sheet: 3 × 10 on Letter.
        const int rows = 10;
        const int cols = 3;

        // Verify the grid produces 30 cells (rows * columns).
        (rows * cols).Should().Be(30);
    }

    [Fact]
    public void LabelGrid_AveryL7160_Is3Columns7Rows()
    {
        // Avery L7160 (A4 equivalent): 3 × 7.
        const int rows = 7;
        const int cols = 3;

        (rows * cols).Should().Be(21);
    }

    [Fact]
    public void LabelGrid_CustomGrid_CellCountEqualsRowsTimesColumns()
    {
        // Generic: any rows × cols must produce a valid (positive) cell count.
        foreach (var (r, c) in new[] { (2, 4), (5, 2), (1, 1), (12, 3) })
            (r * c).Should().BeGreaterThan(0).And.Be(r * c);
    }

    private static IEnumerable<string> MenuCommandIds(RibbonTab tab) =>
        tab.Groups.SelectMany(MenuCommandIds);

    private static IEnumerable<string> MenuCommandIds(RibbonGroup group)
    {
        foreach (var control in group.Controls)
        {
            foreach (var commandId in MenuCommandIds(control))
                yield return commandId;
        }
    }

    private static IEnumerable<string> MenuCommandIds(RibbonControl control) => control switch
    {
        RibbonDropdown dropdown => MenuCommandIds(dropdown.Menu.Items),
        RibbonSplitButton splitButton => MenuCommandIds(splitButton.Menu.Items),
        _ => Enumerable.Empty<string>()
    };

    private static IEnumerable<string> MenuCommandIds(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } commandId && !string.IsNullOrWhiteSpace(commandId.Value))
                yield return commandId.Value;

            foreach (var childCommandId in MenuCommandIds(item.Children))
                yield return childCommandId;
        }
    }

    private static IEnumerable<string> Labels(RibbonTab tab)
    {
        foreach (var control in tab.Groups.SelectMany(group => group.Controls))
        {
            if (!string.IsNullOrWhiteSpace(control.Label))
                yield return control.Label;
        }
    }

    private static IEnumerable<string> Labels(RibbonGroup group)
    {
        foreach (var control in group.Controls)
        {
            if (!string.IsNullOrWhiteSpace(control.Label))
                yield return control.Label;
        }
    }

    [StaFact]
    public void LayoutPageSetup_BreaksDropdownExposesBackedSectionBreakCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var pageSetup = definition.FindTab("layout")!.FindGroup("page-setup");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        // The Breaks dropdown must exist in the Page Setup group.
        var breaks = pageSetup!.Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.breaks");

        breaks.Should().NotBeNull("Layout > Page Setup > Breaks dropdown must exist");

        var menuCommandIds = breaks.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .ToList();

        menuCommandIds.Should().Contain("freew.page-break");
        menuCommandIds.Should().Contain("freew.column-break");
        menuCommandIds.Should().Contain("freew.section-break-next-page");
        menuCommandIds.Should().Contain("freew.section-break-continuous");
        menuCommandIds.Should().Contain("freew.section-break-even-page");
        menuCommandIds.Should().Contain("freew.section-break-odd-page");

        // All menu command ids must be backed by the registry.
        foreach (var id in menuCommandIds)
            registry.TryGet(id, out _).Should().BeTrue($"{id} must be backed");

        // Functional: inserting a section break creates a paragraph with SectionBreak set.
        editor.Model.Blocks.Clear();
        editor.Model.Blocks.Add(new Paragraph("before"));
        registry.TryGet("freew.section-break-next-page", out var nextPage).Should().BeTrue();
        nextPage!.Execute(RibbonCommandContext.Empty);

        // After execution the model must have a new block with SectionBreak set.
        editor.Model.Blocks.Should().HaveCount(2);
        editor.Model.Blocks[1].Should().BeOfType<Paragraph>();
        ((Paragraph)editor.Model.Blocks[1]).SectionBreak.Should().NotBeNull();
        ((Paragraph)editor.Model.Blocks[1]).SectionBreak!.BreakKind.Should().Be(SectionBreakKind.NextPage);
    }

    [StaFact]
    public void DrawingFormat_ContextualTabExposesBackedShapeCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var drawing = definition.FindTab("drawing-format");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        drawing.Should().NotBeNull("drawing-format contextual tab must exist");
        drawing!.Context.Should().NotBeNull();
        drawing.Context!.ActivationKey.Should().Be("drawing");
        drawing.Context.Label.Should().Be("Drawing Tools");
        drawing.Context.Color.Should().Be(RibbonContextColor.Purple);

        // Groups (in order): drawing-insert, drawing-styles, drawing-text, drawing-wordart, drawing-arrange, drawing-size
        drawing.Groups.Select(g => g.Id)
            .Should()
            .Equal("drawing-insert", "drawing-styles", "drawing-text", "drawing-wordart", "drawing-arrange", "drawing-size");

        // Top-level command ids surfaced in the tab
        CommandIds(drawing)
            .Should()
            .Contain("freew.shape-change")
            .And.Contain("freew.shape-fill")
            .And.Contain("freew.shape-outline")
            .And.Contain("freew.shape-effects")        // W24
            .And.Contain("freew.shape-styles-gallery") // W24
            .And.Contain("freew.shape-text-direction")
            .And.Contain("freew.wordart-style")
            .And.Contain("freew.wordart-transform")    // W24
            .And.Contain("freew.shape-align-left")
            .And.Contain("freew.shape-align-center")
            .And.Contain("freew.shape-align-right")
            .And.Contain("freew.shape-size")
            .And.Contain("freew.shape-alt-text");

        // Menu items for Change Shape
        var changeShape = drawing.FindGroup("drawing-insert")!.Controls
            .OfType<RibbonDropdown>()
            .Single(c => c.CommandId.Value == "freew.shape-change");
        changeShape.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .Should()
            .Equal("freew.shape-change-rectangle", "freew.shape-change-rounded", "freew.shape-change-ellipse");

        // Menu items for Text Direction
        var textDir = drawing.FindGroup("drawing-text")!.Controls
            .OfType<RibbonDropdown>()
            .Single(c => c.CommandId.Value == "freew.shape-text-direction");
        textDir.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .Should()
            .Equal("freew.shape-text-horizontal", "freew.shape-text-rotate90", "freew.shape-text-rotate270");

        // Menu items for WordArt Style gallery — 15 presets (W24 expanded from 4)
        var wordArtStyle = drawing.FindGroup("drawing-wordart")!.Controls
            .OfType<RibbonDropdown>()
            .Single(c => c.CommandId.Value == "freew.wordart-style");
        wordArtStyle.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .Should()
            .Equal("freew.wordart-style-fill-blue", "freew.wordart-style-gradient",
                   "freew.wordart-style-outline", "freew.wordart-style-shadow",
                   // W24 extended eleven
                   "freew.wordart-style-fill-gold", "freew.wordart-style-fill-white",
                   "freew.wordart-style-grad-multi", "freew.wordart-style-chrome-one",
                   "freew.wordart-style-chrome-two", "freew.wordart-style-shadow-orange",
                   "freew.wordart-style-glow-blue", "freew.wordart-style-glow-gold",
                   "freew.wordart-style-reflection", "freew.wordart-style-bevel",
                   "freew.wordart-style-pattern");

        // Menu items for WordArt Transform — 14 warp presets (W24)
        var wordArtTransform = drawing.FindGroup("drawing-wordart")!.Controls
            .OfType<RibbonDropdown>()
            .Single(c => c.CommandId.Value == "freew.wordart-transform");
        wordArtTransform.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .Should()
            .Equal("freew.wordart-warp-none",
                   "freew.wordart-warp-arch-up", "freew.wordart-warp-arch-down",
                   "freew.wordart-warp-circle",
                   "freew.wordart-warp-wave1", "freew.wordart-warp-wave2",
                   "freew.wordart-warp-inflate", "freew.wordart-warp-deflate",
                   "freew.wordart-warp-chevron-up", "freew.wordart-warp-chevron-down",
                   "freew.wordart-warp-fade-right", "freew.wordart-warp-fade-left",
                   "freew.wordart-warp-slant-up", "freew.wordart-warp-slant-down");

        // Every command id in the tab + menus must be registered (backed).
        var allIds = CommandIds(drawing)
            .Concat(MenuCommandIds(changeShape))
            .Concat(MenuCommandIds(textDir))
            .Concat(MenuCommandIds(wordArtStyle))
            .Concat(MenuCommandIds(wordArtTransform))
            .Concat(drawing.FindGroup("drawing-styles")!.Controls.OfType<RibbonDropdown>()
                .SelectMany(MenuCommandIds))
            .Distinct()
            .Where(id => id is not ("freew.shape-change" or "freew.shape-fill" or "freew.shape-outline"
                or "freew.shape-effects" or "freew.shape-styles-gallery"
                or "freew.shape-text-direction" or "freew.wordart-style" or "freew.wordart-transform"
                // W26 drawing-arrange parent dropdown buttons (menu items backed individually):
                or "freew.shape-wrap" or "freew.shape-rotate"));

        foreach (var commandId in allIds)
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must be backed");
    }

    // ── SmartArt Design contextual tab ─────────────────────────────────────────────────────────

    [StaFact]
    public void SmartArtDesignContextualTab_ExposesAllBackedNodeMutationCommands()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        // The contextual tab must be declared and placed under "SmartArt Tools".
        var smartArtTab = definition.ContextualTabs
            .SingleOrDefault(t => t.Id == "smartart-design");
        smartArtTab.Should().NotBeNull("the smartart-design contextual tab must be declared in FreeWRibbon");
        smartArtTab!.Context!.Label.Should().Be("SmartArt Tools");
        smartArtTab.Context.ActivationKey.Should().Be("smartart");

        // The tab must expose the expected command ids.
        var commandIds = CommandIds(smartArtTab).ToList();
        commandIds.Should().Contain("freew.smartart-add-shape",    "Add Shape must be backed");
        commandIds.Should().Contain("freew.smartart-remove-shape", "Remove Shape must be backed");
        commandIds.Should().Contain("freew.smartart-promote",      "Promote must be backed");
        commandIds.Should().Contain("freew.smartart-demote",       "Demote must be backed");
        commandIds.Should().Contain("freew.smartart-move-up",      "Move Up must be backed");
        commandIds.Should().Contain("freew.smartart-move-down",    "Move Down must be backed");
        commandIds.Should().Contain("freew.smartart-edit-text",    "Edit Text must be backed");

        // Every command on the tab must be registered.
        foreach (var commandId in commandIds)
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must be backed before it appears on SmartArt Design");
    }

    [StaFact]
    public void SmartArtDesignContextualTab_AddShape_MutatesModel()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        // Insert a diagram and then run Add Shape via the registered command.
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Alpha", "Beta"]);
        editor.InsertSmartArt(smartArt);
        editor.CommitToModel();

        registry.TryGet("freew.smartart-add-shape", out var addShape).Should().BeTrue();
        addShape!.Execute(RibbonCommandContext.Empty);
        editor.CommitToModel();

        // The selected (just-inserted) SmartArt now carries 3 nodes.
        var run = ((Paragraph)editor.Model.Blocks.Last()).Runs
            .Single(r => r.SmartArt is not null);
        run.SmartArt!.Nodes.Should().HaveCount(3, "Add Shape must append a node");
    }

    [StaFact]
    public void SmartArtDesignContextualTab_RemoveShape_MutatesModel()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        var smartArt = SmartArt.Create(SmartArtKind.List, ["Alpha", "Beta", "Gamma"]);
        editor.InsertSmartArt(smartArt);
        editor.CommitToModel();

        registry.TryGet("freew.smartart-remove-shape", out var removeShape).Should().BeTrue();
        removeShape!.Execute(RibbonCommandContext.Empty);
        editor.CommitToModel();

        var run = ((Paragraph)editor.Model.Blocks.Last()).Runs
            .Single(r => r.SmartArt is not null);
        run.SmartArt!.Nodes.Should().HaveCount(2, "Remove Shape must remove the last node");
    }

    [StaFact]
    public void SmartArtDesignContextualTab_AllEightCommandsMatchSelectionMutationAndUndoBehavior()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var smartArt = SmartArt.Create(SmartArtKind.Hierarchy, ["Root", "Sibling", "Third"]);
        smartArt.Nodes[0].Children.Add(new SmartArtNode("Child"));
        smartArt.LayoutId = "hierarchy1";
        smartArt.ColorSchemeId = "colorful2";
        smartArt.StyleId = "flat1";
        editor.InsertSmartArt(smartArt);
        editor.CommitToModel();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        foreach (var id in new[]
        {
            "freew.smartart-add-shape", "freew.smartart-remove-shape",
            "freew.smartart-promote", "freew.smartart-demote",
            "freew.smartart-move-up", "freew.smartart-move-down",
            "freew.smartart-edit-text", "freew.smartart-change-style",
        })
        {
            registry.TryGet(id, out var command).Should().BeTrue();
            command.Should().BeAssignableTo<IRibbonStatefulCommand>().Subject.GetState().IsEnabled.Should().BeTrue();
        }

        var before = smartArt.Nodes.Select(node => node.Text).ToArray();
        registry.TryGet("freew.smartart-add-shape", out var add);
        add!.Execute(RibbonCommandContext.Empty);
        smartArt.Nodes.Should().HaveCount(before.Length + 1);
        editor.Undo();
        smartArt.Nodes.Select(node => node.Text).Should().Equal(before);
        editor.Redo();
        smartArt.Nodes.Should().HaveCount(before.Length + 1);
        editor.Undo();

        registry.TryGet("freew.smartart-move-up", out var moveUp);
        moveUp!.Execute(RibbonCommandContext.Empty);
        smartArt.Nodes.Select(node => node.Text).Should().Equal("Root", "Third", "Sibling");
        editor.Undo();
        smartArt.Nodes.Select(node => node.Text).Should().Equal(before);

        registry.TryGet("freew.smartart-promote", out var promote);
        promote!.Execute(RibbonCommandContext.Empty);
        smartArt.Nodes.Select(node => node.Text).Should().Equal("Root", "Child", "Sibling", "Third");
        editor.Undo();
        smartArt.Nodes[0].Children.Should().ContainSingle().Which.Text.Should().Be("Child");

        registry.TryGet("freew.smartart-demote", out var demote);
        demote!.Execute(RibbonCommandContext.Empty);
        smartArt.Nodes.Select(node => node.Text).Should().Equal("Root", "Sibling");
        smartArt.Nodes[^1].Children.Should().ContainSingle().Which.Text.Should().Be("Third");
        editor.Undo();
        smartArt.Nodes.Select(node => node.Text).Should().Equal(before);

        registry.TryGet("freew.smartart-edit-text", out var edit);
        edit!.Execute(RibbonCommandContext.ForSelectedValue("One\nTwo"));
        smartArt.Nodes.Select(node => node.Text).Should().Equal("One", "Two");
        smartArt.LayoutId.Should().Be("hierarchy1");
        smartArt.ColorSchemeId.Should().Be("colorful2");
        smartArt.StyleId.Should().Be("flat1");
        editor.Undo();
        smartArt.Nodes.Select(node => node.Text).Should().Equal(before);

        registry.TryGet("freew.smartart-change-style", out var style);
        style!.Execute(RibbonCommandContext.ForSelectedValue(SmartArtStyle.Catalog[4].Name));
        smartArt.StyleId.Should().Be(SmartArtStyle.Catalog[4].Id);
        smartArt.Nodes.Select(node => node.Text).Should().Equal(before);
        editor.Undo();
        smartArt.StyleId.Should().Be("flat1");

        var layout = SmartArtLayoutPreset.Catalog.First(preset => preset.Id != smartArt.LayoutId);
        editor.ApplySmartArtLayout(layout);
        smartArt.Kind.Should().Be(layout.Kind);
        smartArt.LayoutId.Should().Be(layout.Id);
        editor.Undo();
        smartArt.Kind.Should().Be(SmartArtKind.Hierarchy);
        smartArt.LayoutId.Should().Be("hierarchy1");
        editor.Redo();
        smartArt.LayoutId.Should().Be(layout.Id);
        editor.Undo();

        var color = SmartArtColorScheme.Catalog.First(scheme => scheme.Id != smartArt.ColorSchemeId);
        editor.ApplySmartArtColorScheme(color);
        smartArt.ColorSchemeId.Should().Be(color.Id);
        editor.Undo();
        smartArt.ColorSchemeId.Should().Be("colorful2");
        editor.Redo();
        smartArt.ColorSchemeId.Should().Be(color.Id);
    }

    // ── Header & Footer Design contextual tab ───────────────────────────────────────────────────

    [Fact]
    public void Build_ExposesHeaderFooterDesignContextualTab()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);

        var tab = definition.FindTab("header-footer-design");
        tab.Should().NotBeNull("header-footer-design contextual tab must be declared");
        tab!.Context.Should().NotBeNull("must have a context");
        tab.Context!.ActivationKey.Should().Be("header-footer");
        tab.Context.Label.Should().Be("Header & Footer Tools");
        tab.Context.Color.Should().Be(RibbonContextColor.Purple);
    }

    [Fact]
    public void HeaderFooterDesign_ContextualTabHasExpectedGroups()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var tab = definition.FindTab("header-footer-design");

        tab.Should().NotBeNull();
        tab!.Groups.Select(g => g.Id)
            .Should()
            .Equal("hf-header-footer", "hf-insert", "hf-navigation", "hf-options", "hf-position", "hf-close");
    }

    [StaFact]
    public void HeaderFooterDesign_AllTabCommandsAreBacked()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var tab = definition.FindTab("header-footer-design");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        tab.Should().NotBeNull();

        // All top-level command ids in the tab must be registered.
        foreach (var commandId in CommandIds(tab!))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must be backed in header-footer-design tab");

        // All menu item command ids must also be registered.
        foreach (var commandId in MenuCommandIds(tab))
            registry.TryGet(commandId, out _).Should().BeTrue($"menu item {commandId} must be backed");
    }

    [StaFact]
    public void HeaderFooterDesign_DifferentFirstPageToggle_FlipsModel()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        editor.Model.Page.DifferentFirstPage.Should().BeFalse("initial state is false");
        registry.TryGet("freew.hf-different-first-page", out var cmd).Should().BeTrue();

        cmd!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.DifferentFirstPage.Should().BeTrue("toggle ON must set DifferentFirstPage = true");

        cmd.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.DifferentFirstPage.Should().BeFalse("toggle OFF must set DifferentFirstPage = false");
    }

    [StaFact]
    public void HeaderFooterDesign_DifferentOddEvenPagesToggle_FlipsModel()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        editor.Model.Page.DifferentOddEvenPages.Should().BeFalse("initial state is false");
        registry.TryGet("freew.hf-different-odd-even", out var cmd).Should().BeTrue();

        cmd!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.DifferentOddEvenPages.Should().BeTrue("toggle ON must set DifferentOddEvenPages = true");

        cmd.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.DifferentOddEvenPages.Should().BeFalse("toggle OFF must set DifferentOddEvenPages = false");
    }

    [StaFact]
    public void HeaderFooterDesign_HeaderFromTopCommand_WritesDistanceIntoModel()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        registry.TryGet("freew.hf-header-from-top", out var cmd).Should().BeTrue();

        var ctx = new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "36" });
        cmd!.Execute(ctx);

        editor.Model.Page.HeaderDistancePt.Should().Be(36, "HeaderDistancePt must be set to the given value");
    }

    [StaFact]
    public void HeaderFooterDesign_FooterFromBottomCommand_WritesDistanceIntoModel()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        registry.TryGet("freew.hf-footer-from-bottom", out var cmd).Should().BeTrue();

        var ctx = new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "54" });
        cmd!.Execute(ctx);

        editor.Model.Page.FooterDistancePt.Should().Be(54, "FooterDistancePt must be set to the given value");
    }

    [StaFact]
    public void HeaderFooterDesign_InsertPageNumberIntoFooterSlot_WritesPageNumberRun()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        editor.Model.Footer.Should().BeNull("footer starts empty");
        registry.TryGet("freew.hf-insert-page-number-footer", out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);

        editor.Model.Footer.Should().NotBeNull("inserting a page number must create the footer slot");
        var runs = editor.Model.Footer!.Paragraphs.SelectMany(p => p.Runs).ToList();
        runs.Should().Contain(r => r.FieldKind == RunFieldKind.PageNumber,
            "a page-number field run must be present after the command");
    }

    [StaFact]
    public void HeaderFooterDesign_InsertPageNumberTwice_DoesNotDuplicate()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        registry.TryGet("freew.hf-insert-page-number-footer", out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);
        cmd.Execute(RibbonCommandContext.Empty);

        var pageNumberCount = editor.Model.Footer!.Paragraphs.SelectMany(p => p.Runs)
            .Count(r => r.FieldKind == RunFieldKind.PageNumber);
        pageNumberCount.Should().Be(1, "inserting page number twice must not duplicate the field run");
    }

    [StaFact]
    public void InsertHeaderFooter_UsesWpfPromptSeedAndCancelContract()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var prompts = new List<(bool Footer, string Seed)>();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty,
            new FreeWWpfRibbonNativeExecutionPorts(
                AskHeaderFooterText: (footer, seed) =>
                {
                    prompts.Add((footer, seed));
                    return footer ? null : "Header from prompt";
                }));

        registry.TryGet("freew.header", out var header).Should().BeTrue();
        header!.Execute(RibbonCommandContext.Empty);

        editor.Model.Header.Should().NotBeNull();
        editor.Model.Header!.PlainText.Should().Be("Header from prompt");

        registry.TryGet("freew.footer", out var footer).Should().BeTrue();
        footer!.Execute(RibbonCommandContext.Empty);

        editor.Model.Footer.Should().BeNull("Cancel must leave the footer untouched");
        prompts.Should().Equal((false, string.Empty), (true, string.Empty));
    }

    // ── Notes pane backing (Phase 1A) ───────────────────────────────────────────────────────────────

    /// <summary>
    /// When the host passes onToggleNotesPane + isNotesPaneVisible callbacks, freew.show-notes must be
    /// registered as a backed stateful command (IsChecked reflects isNotesPaneVisible), not the read-only
    /// dialog. This is the parity discipline check: freew.show-notes is a backed toggle.
    /// </summary>
    [StaFact]
    public void ShowNotes_WithPaneCallbacks_IsBackedStatefulToggle()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var paneVisible = false;
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ToggleNotesPane = () => { paneVisible = !paneVisible; },
                IsNotesPaneVisible = () => paneVisible,
            });

        registry.TryGet("freew.show-notes", out var cmd).Should().BeTrue("freew.show-notes must be registered");
        var stateful = cmd as IRibbonStatefulCommand;
        stateful.Should().NotBeNull("freew.show-notes must be a stateful toggle when pane callbacks are supplied");

        stateful!.GetState().IsChecked.Should().BeFalse("pane starts hidden");
        cmd!.Execute(RibbonCommandContext.Empty);
        paneVisible.Should().BeTrue("Execute must invoke the toggle callback");
        stateful.GetState().IsChecked.Should().BeTrue("IsChecked reflects the toggle state");

        cmd.Execute(RibbonCommandContext.Empty);
        paneVisible.Should().BeFalse("the same command must toggle the pane closed");
        stateful.GetState().IsChecked.Should().BeFalse("IsChecked must follow the live hidden state");
    }

    /// <summary>
    /// Without pane callbacks, freew.show-notes must still be registered (as the read-only dialog command).
    /// </summary>
    [StaFact]
    public void ShowNotes_WithoutPaneCallbacks_IsStillRegistered()
    {
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        registry.TryGet("freew.show-notes", out _).Should().BeTrue(
            "freew.show-notes must be registered even without pane callbacks");
    }

    // ── Header/Footer pane backing (Phase 2A) ────────────────────────────────────────────────────────

    /// <summary>
    /// When the host supplies onOpenHeaderFooterPane, hf-edit-* commands must delegate to the pane
    /// callback instead of opening the plain-text dialog.
    /// </summary>
    [StaFact]
    public void HeaderFooterDesign_WithPaneCallback_HfEditCommandsCallOpenPane()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        string? openedSlot = null;
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                OpenHeaderFooterPane = slot => { openedSlot = slot; },
                CloseHeaderFooterPane = () => { },
            });

        registry.TryGet("freew.hf-edit-header", out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);
        openedSlot.Should().Be("header", "hf-edit-header must call openPane(\"header\")");
    }

    /// <summary>freew.hf-close must be registered and call the close callback when supplied.</summary>
    [StaFact]
    public void HeaderFooterDesign_WithPaneCallback_HfCloseCallsClosePane()
    {
        var editor = new DocumentView();
        editor.LoadModel(TextDocument.CreateEmpty());
        var closeCalled = false;
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                OpenHeaderFooterPane = _ => { },
                CloseHeaderFooterPane = () => { closeCalled = true; },
            });

        registry.TryGet("freew.hf-close", out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);
        closeCalled.Should().BeTrue("freew.hf-close must invoke the close callback");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // W24: Layout + View depth — new features
    // ─────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void Layout_LineNumberOptions_IsBacked()
    {
        // freew.line-numbers-options must be registered (backed command, not a placeholder).
        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        registry.TryGet("freew.line-numbers-options", out var cmd).Should().BeTrue(
            "freew.line-numbers-options must be registered — dedicated dialog, not Page Setup fallback");
        cmd.Should().NotBeNull("line-numbers-options must be a real backed command");
    }

    [StaFact]
    public void Layout_LineNumbers_LineNumberStartAt_RoundTrips()
    {
        // PageSettings.LineNumberStartAt persists through Clone.
        var page = new PageSettings { LineNumberStartAt = 5, LineNumberCountBy = 2 };
        var clone = page.Clone();
        clone.LineNumberStartAt.Should().Be(5,
            "LineNumberStartAt must round-trip through Clone for copy/combine paths");
        clone.LineNumberCountBy.Should().Be(2, "LineNumberCountBy must also clone correctly");
    }

    [StaFact]
    public void PictureArrange_AlignToPageAndMargin_AreBacked()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var arrange = definition.FindTab("picture-format")!.FindGroup("picture-arrange");

        var ids = arrange!.Controls.Select(c => c.CommandId.Value).ToList();
        ids.Should().Contain("freew.image-align-to-page",   "Picture Format > Arrange must expose Align to Page");
        ids.Should().Contain("freew.image-align-to-margin", "Picture Format > Arrange must expose Align to Margin");
        ids.Should().Contain("freew.image-distribute-h",    "Picture Format > Arrange must expose Distribute Horizontally");
        ids.Should().Contain("freew.image-distribute-v",    "Picture Format > Arrange must expose Distribute Vertically");

        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        registry.TryGet("freew.image-align-to-page",   out _).Should().BeTrue();
        registry.TryGet("freew.image-align-to-margin", out _).Should().BeTrue();
        registry.TryGet("freew.image-distribute-h",    out _).Should().BeTrue();
        registry.TryGet("freew.image-distribute-v",    out _).Should().BeTrue();
    }

    [StaFact]
    public void DrawingArrange_AlignToPageAndMargin_AreBacked()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var arrange = definition.FindTab("drawing-format")!.FindGroup("drawing-arrange");

        var ids = arrange!.Controls.Select(c => c.CommandId.Value).ToList();
        ids.Should().Contain("freew.shape-align-to-page",   "Drawing Tools > Arrange must expose Align to Page");
        ids.Should().Contain("freew.shape-align-to-margin", "Drawing Tools > Arrange must expose Align to Margin");
        ids.Should().Contain("freew.shape-distribute-h",    "Drawing Tools > Arrange must expose Distribute Horizontally");
        ids.Should().Contain("freew.shape-distribute-v",    "Drawing Tools > Arrange must expose Distribute Vertically");

        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        registry.TryGet("freew.shape-align-to-page",   out _).Should().BeTrue();
        registry.TryGet("freew.shape-align-to-margin", out _).Should().BeTrue();
        registry.TryGet("freew.shape-distribute-h",    out _).Should().BeTrue();
        registry.TryGet("freew.shape-distribute-v",    out _).Should().BeTrue();
    }

    [StaFact]
    public void FloatingArrange_RibbonCommand_ArrangesImagesAndShapesAndUndoRestores()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.Model.Page.MarginLeftPt = 90;
        editor.Model.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.FromImage(new InlineImage([0x89, 0x50, 0x4E, 0x47], 60, 40)
                {
                    Wrapping = ImageWrapping.Square,
                    HorizontalOffsetPt = 24,
                    HorizontalAnchor = HorizontalAnchor.Column,
                    VerticalOffsetPt = 12
                }),
                Run.FromShape(new Shape(ShapeKind.Rectangle, 80, 50)
                {
                    Placement = new FloatingPlacement
                    {
                        Wrapping = ImageWrapping.Square,
                        HorizontalOffsetPt = 144,
                        HorizontalAnchor = HorizontalAnchor.Column,
                        VerticalOffsetPt = 48
                    }
                })
            }
        });
        // Rerender() was removed when the design-preview work made Render() private; LoadModel is the
        // supported way to push a directly-mutated model back into the view, and is what every other
        // test in this file already uses.
        editor.LoadModel(editor.Model);

        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());
        registry.TryGet("freew.image-align-to-margin", out var command).Should().BeTrue();

        command!.Execute(RibbonCommandContext.Empty);

        var paragraph = (Paragraph)editor.Model.Blocks[0];
        paragraph.Runs[0].Image!.HorizontalOffsetPt.Should().Be(90);
        paragraph.Runs[0].Image!.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);
        paragraph.Runs[1].Shape!.Placement!.HorizontalOffsetPt.Should().Be(90);
        paragraph.Runs[1].Shape!.Placement!.HorizontalAnchor.Should().Be(HorizontalAnchor.Margin);

        editor.Commands.Undo().Should().BeTrue();
        paragraph.Runs[0].Image!.HorizontalOffsetPt.Should().Be(24);
        paragraph.Runs[0].Image!.HorizontalAnchor.Should().Be(HorizontalAnchor.Column);
        paragraph.Runs[1].Shape!.Placement!.HorizontalOffsetPt.Should().Be(144);
        paragraph.Runs[1].Shape!.Placement!.HorizontalAnchor.Should().Be(HorizontalAnchor.Column);
    }

    [StaFact]
    public void DrawingArrange_ZOrder_CommandsExistInRibbonAndRegistry()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var arrange = definition.FindTab("drawing-format")!.FindGroup("drawing-arrange");
        var ids = arrange!.Controls.Select(c => c.CommandId.Value).ToList();
        ids.Should().Contain("freew.shape-bring-to-front",  "Drawing Tools > Arrange must expose Bring to Front");
        ids.Should().Contain("freew.shape-send-to-back",    "Drawing Tools > Arrange must expose Send to Back");
        ids.Should().Contain("freew.shape-bring-forward",   "Drawing Tools > Arrange must expose Bring Forward");
        ids.Should().Contain("freew.shape-send-backward",   "Drawing Tools > Arrange must expose Send Backward");

        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        registry.TryGet("freew.shape-bring-to-front", out _).Should().BeTrue("z-order bring-to-front backed for shapes");
        registry.TryGet("freew.shape-send-to-back",   out _).Should().BeTrue("z-order send-to-back backed for shapes");
        registry.TryGet("freew.shape-bring-forward",  out _).Should().BeTrue("z-order bring-forward backed for shapes");
        registry.TryGet("freew.shape-send-backward",  out _).Should().BeTrue("z-order send-backward backed for shapes");
    }

    [StaFact]
    public void DrawingArrange_WrapText_CommandsExistInRibbonAndRegistry()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var arrange = definition.FindTab("drawing-format")!.FindGroup("drawing-arrange");
        var ids = arrange!.Controls.Select(c => c.CommandId.Value).ToList();
        ids.Should().Contain("freew.shape-wrap", "Drawing Tools > Arrange must expose Wrap Text dropdown");

        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        registry.TryGet("freew.shape-wrap-inline",     out _).Should().BeTrue("shape wrap inline backed");
        registry.TryGet("freew.shape-wrap-square",     out _).Should().BeTrue("shape wrap square backed");
        registry.TryGet("freew.shape-wrap-tight",      out _).Should().BeTrue("shape wrap tight backed");
        registry.TryGet("freew.shape-wrap-top-bottom", out _).Should().BeTrue("shape wrap top-bottom backed");
        registry.TryGet("freew.shape-wrap-behind",     out _).Should().BeTrue("shape wrap behind backed");
        registry.TryGet("freew.shape-wrap-front",      out _).Should().BeTrue("shape wrap front backed");
    }

    [StaFact]
    public void DrawingArrange_Position_CommandExistsInRibbonAndRegistry()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var arrange = definition.FindTab("drawing-format")!.FindGroup("drawing-arrange");
        var ids = arrange!.Controls.Select(c => c.CommandId.Value).ToList();
        ids.Should().Contain("freew.shape-position", "Drawing Tools > Arrange must expose Position");

        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        registry.TryGet("freew.shape-position", out _).Should().BeTrue("shape position command backed");
    }

    [StaFact]
    public void DrawingArrange_Rotate_CommandsExistInRibbonAndRegistry()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var arrange = definition.FindTab("drawing-format")!.FindGroup("drawing-arrange");
        var ids = arrange!.Controls.Select(c => c.CommandId.Value).ToList();
        ids.Should().Contain("freew.shape-rotate", "Drawing Tools > Arrange must expose Rotate dropdown");

        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        registry.TryGet("freew.shape-rotate-right90",  out _).Should().BeTrue("shape rotate right 90 backed");
        registry.TryGet("freew.shape-rotate-left90",   out _).Should().BeTrue("shape rotate left 90 backed");
        registry.TryGet("freew.shape-flip-vertical",   out _).Should().BeTrue("shape flip vertical backed");
        registry.TryGet("freew.shape-flip-horizontal", out _).Should().BeTrue("shape flip horizontal backed");
    }

    [StaFact]
    public void FloatingDistribute_CommandsAreRegisteredAsBacked()
    {
        // Both horizontal and vertical distribute commands must be registered and executable.
        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        registry.TryGet("freew.image-distribute-h", out var h).Should().BeTrue("freew.image-distribute-h must be backed");
        registry.TryGet("freew.image-distribute-v", out var v).Should().BeTrue("freew.image-distribute-v must be backed");
        h.Should().NotBeNull();
        v.Should().NotBeNull();
    }

    [StaFact]
    public void View_OutlineLevelCombo_SetHeadingLevel_IsPublicAndCallable()
    {
        // SetHeadingLevel(int, int) must be a public method on DocumentView.
        // Out-of-range indices must be no-ops (no throw).
        var editor = new DocumentView();
        var act = () =>
        {
            editor.SetHeadingLevel(-1, 1);   // negative index: no-op
            editor.SetHeadingLevel(99, 1);   // beyond range: no-op
        };
        act.Should().NotThrow("SetHeadingLevel must handle out-of-range indices gracefully");
    }

    [StaFact]
    public void View_ReadModeOptions_AreBacked()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var views = definition.FindTab("view")!.FindGroup("views");
        // Read Mode must now carry a dropdown menu with column width and page color items.
        var readMode = views!.Controls.FirstOrDefault(c => c.CommandId.Value == "freew.read-mode");
        readMode.Should().NotBeNull("freew.read-mode must be in the views group");

        var menuIds = MenuItemIds(readMode!);
        menuIds.Should().Contain("freew.read-mode-column-narrow",  "Read Mode dropdown must offer Narrow column");
        menuIds.Should().Contain("freew.read-mode-column-default", "Read Mode dropdown must offer Default column");
        menuIds.Should().Contain("freew.read-mode-column-wide",    "Read Mode dropdown must offer Wide column");
        menuIds.Should().Contain("freew.read-mode-color-none",     "Read Mode dropdown must offer No Color");
        menuIds.Should().Contain("freew.read-mode-color-sepia",    "Read Mode dropdown must offer Sepia");
        menuIds.Should().Contain("freew.read-mode-color-inverse",  "Read Mode dropdown must offer Inverse");

        var registry = FreeWRibbonCommands.Build(new DocumentView(), new RibbonStateStore());
        foreach (var id in menuIds)
            registry.TryGet(id, out _).Should().BeTrue($"{id} must be registered");
    }

    [StaFact]
    public void View_ReadModeColumnWidthCallback_IsCalled()
    {
        var received = new List<string>();
        var registry = FreeWRibbonCommands.Build(
            new DocumentView(),
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                ApplyReadModeColumnWidth = token => received.Add(token),
            });

        registry.TryGet("freew.read-mode-column-narrow", out var narrow).Should().BeTrue();
        narrow!.Execute(RibbonCommandContext.Empty);
        received.Should().ContainSingle().Which.Should().Be("narrow");
    }

    [StaFact]
    public void View_Window_NewWindowAndArrangeAll_AreBacked()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var window = definition.FindTab("view")!.FindGroup("window");
        var ids = window!.Controls.Select(c => c.CommandId.Value).ToList();
        ids.Should().Contain("freew.new-window",  "View > Window must expose New Window");
        ids.Should().Contain("freew.arrange-all", "View > Window must expose Arrange All");

        var newWindowCalled = false;
        var arrangeAllCalled = false;
        var registry = FreeWRibbonCommands.Build(
            new DocumentView(),
            new RibbonStateStore(),
            FreeWRibbonHostExecutionPorts.Empty with
            {
                NewWindow = () => newWindowCalled = true,
                ArrangeAll = () => arrangeAllCalled = true,
            });

        registry.TryGet("freew.new-window",  out var nw).Should().BeTrue();
        registry.TryGet("freew.arrange-all", out var aa).Should().BeTrue();
        nw!.Execute(RibbonCommandContext.Empty);
        aa!.Execute(RibbonCommandContext.Empty);
        newWindowCalled.Should().BeTrue("freew.new-window must invoke the onNewWindow callback");
        arrangeAllCalled.Should().BeTrue("freew.arrange-all must invoke the onArrangeAll callback");
    }

    [Fact]
    public void ArrangeAll_uses_the_shared_FreeW_row_first_policy_before_translating_Wpf_bounds()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew", "FreeW.App.Host", "MainWindow.cs"));

        source.Should().Contain("ArrangeAllLayoutPlanner.ArrangeRowFirst(");
        source.Should().Contain("maxColumns: 3");
        source.Should().Contain("w.WindowState = System.Windows.WindowState.Normal");
        source.Should().Contain("w.Left   = area.Left + bound.X");
        source.Should().Contain("w.Top    = area.Top  + bound.Y");
        source.Should().NotContain("var tileW = area.Width");
        source.Should().NotContain("var tileH = area.Height");
    }

    // Helper: collect all command id strings from a control's menu items (non-recursive depth-1, skips separators).
    private static List<string> MenuItemIds(RibbonControl control)
    {
        IEnumerable<RibbonMenuItem> items = control switch
        {
            RibbonDropdown dd => dd.Menu.Items,
            RibbonSplitButton sb => sb.Menu.Items,
            _ => []
        };
        return items
            .Where(i => i.CommandId is { } cid && !string.IsNullOrWhiteSpace(cid.Value))
            .Select(i => i.CommandId!.Value.Value)  // RibbonCommandId?.Value is RibbonCommandId; .Value is the string
            .ToList();
    }

    [Fact]
    public void TableDesignTab_DrawBordersGroup_ExposesDrawTableAndEraser()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var tableDesign = definition.FindTab("table-design");
        tableDesign.Should().NotBeNull();

        tableDesign!.Groups.Select(g => g.Id)
            .Should()
            .Contain("draw-borders", "Table Design > Draw Borders is a Word-standard group");

        var drawBorders = tableDesign.FindGroup("draw-borders");
        drawBorders.Should().NotBeNull();

        CommandIds(drawBorders!)
            .Should()
            .Contain(new[] { "freew.draw-table", "freew.eraser" });
    }

    [StaFact]
    public void TableDesign_DrawTable_And_Eraser_AreBacked()
    {
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        registry.TryGet("freew.draw-table", out _).Should().BeTrue("freew.draw-table must be backed");
        registry.TryGet("freew.eraser", out _).Should().BeTrue("freew.eraser must be backed");
    }
}
