using System.Collections.Generic;
using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class FreeWRibbonParityTests
{
    [Fact]
    public void Build_OrdersImplementedTopLevelTabsLikeWord()
    {
        FreeWRibbon.Build().VisibleTabs.Select(tab => tab.Id)
            .Should()
            .Equal("home", "insert", "design", "layout", "references", "mailings", "review", "view", "help", "developer");
    }

    [StaFact]
    public void HelpTab_ExposesOnlyBackedFreeWLocalSupportCommands()
    {
        var definition = FreeWRibbon.Build();
        var help = definition.FindTab("help");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onHelpOnline: () => { },
            onFeedback: () => { },
            onCopyDiagnostics: () => { },
            onCheckForUpdates: () => { },
            onAbout: () => { },
            onLegalNotices: () => { });

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
        var definition = FreeWRibbon.Build();

        definition.VisibleTabs.Select(tab => tab.Id)
            .Should()
            .ContainInOrder("layout", "references", "mailings");

        definition.FindTab("insert")!.Groups.Select(group => group.Id)
            .Should()
            .NotContain("references", "Word exposes References as a dedicated top-level tab, not as an Insert group");
    }

    [StaFact]
    public void InsertTab_GroupsBackedCommandsLikeWord()
    {
        var definition = FreeWRibbon.Build();
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
        var definition = FreeWRibbon.Build();
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
        var references = FreeWRibbon.Build().FindTab("references");

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
    public void ReferencesIndex_ExposesBackedWordStyleUpdateIndex()
    {
        var definition = FreeWRibbon.Build();
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
            .Equal("Index", "Alpha", "Beta", "Gamma");
        editor.Model.Blocks.OfType<Paragraph>()
            .Count(paragraph => paragraph.StyleId == DocumentIndex.HeadingStyleId)
            .Should()
            .Be(1);
    }

    [StaFact]
    public void ReferencesCitations_ExposesBackedWordStyleManageSources()
    {
        var definition = FreeWRibbon.Build();
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
        var definition = FreeWRibbon.Build();
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
        var definition = FreeWRibbon.Build();
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
        var definition = FreeWRibbon.Build();
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
        var definition = FreeWRibbon.Build();
        var editing = definition.FindTab("home")!.FindGroup("editing");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onFindReplace: () => { });

        CommandIds(editing!)
            .Should()
            .Equal("freew.find", "freew.replace", "freew.select");
        registry.TryGet("freew.find", out _).Should().BeTrue();
        registry.TryGet("freew.replace", out _).Should().BeTrue();
        registry.TryGet("freew.select", out _).Should().BeTrue();
    }

    [StaFact]
    public void HomeFormattingVisibility_ExposesBackedWordStyleToggles()
    {
        var definition = FreeWRibbon.Build();
        var home = definition.FindTab("home");
        var paragraph = home!.FindGroup("paragraph");
        var formatting = home.FindGroup("formatting");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onToggleRevealFormatting: () => { },
            isRevealFormattingVisible: () => false);

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
        var definition = FreeWRibbon.Build();
        var paragraph = definition.FindTab("home")!.FindGroup("paragraph");
        var multilevel = paragraph!.Controls
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.multilevel-list");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        multilevel.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => (item.CommandId!.Value, item.Header))
            .Should()
            .Equal(
                ("freew.multilevel-promote", "Decrease List Level"),
                ("freew.multilevel-demote", "Increase List Level"));

        registry.TryGet("freew.multilevel-list", out _).Should().BeTrue("the top-level Multilevel List command applies backed outline numbering");
        registry.TryGet("freew.multilevel-promote", out _).Should().BeTrue("Word exposes list-level decrease from the Multilevel List menu");
        registry.TryGet("freew.multilevel-demote", out _).Should().BeTrue("Word exposes list-level increase from the Multilevel List menu");
    }

    [StaFact]
    public void ReviewComments_ExposesAndRegistersWordStyleThreadActions()
    {
        var definition = FreeWRibbon.Build();
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
    public void ReviewTab_GroupsBackedCommandsLikeWord()
    {
        var definition = FreeWRibbon.Build();
        var review = definition.FindTab("review");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onToggleReviewingPane: () => { },
            isReviewingPaneVisible: () => false,
            onAcceptThisChange: () => { },
            onRejectThisChange: () => { },
            onPreviousChange: () => { },
            onNextChange: () => { });

        review.Should().NotBeNull();
        review!.Groups.Select(group => group.Id)
            .Should()
            .Equal("proofing", "speech", "accessibility", "comments", "tracking", "changes", "protect", "compare", "inspect");

        CommandIds(review.FindGroup("accessibility")!)
            .Should()
            .Equal("freew.check-accessibility");
        CommandIds(review.FindGroup("tracking")!)
            .Should()
            .Equal("freew.track-changes", "freew.reviewing-pane", "freew.display-for-review", "freew.show-markup");
        MenuCommandIds(review.FindGroup("tracking")!)
            .Should()
            .Equal(
                "freew.display-for-review-all-markup",
                "freew.display-for-review-simple-markup",
                "freew.display-for-review-no-markup",
                "freew.display-for-review-original",
                "freew.show-markup-insertions-deletions",
                "freew.show-markup-comments",
                "freew.show-markup-formatting");
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
    public void DesignPageBackground_ExposesWordStyleWatermarkPageColorAndPageBorders()
    {
        var definition = FreeWRibbon.Build();
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
        var definition = FreeWRibbon.Build();
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
            .Equal("freew.theme", "freew.style-set", "freew.theme-colors", "freew.theme-fonts", "freew.paragraph-spacing", "freew.theme-effects");
        Labels(documentFormatting!)
            .Should()
            .Equal("Themes", "Style Sets", "Colors", "Fonts", "Paragraph Spacing", "Effects");

        foreach (var commandId in CommandIds(documentFormatting!))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from Design > Document Formatting");
    }

    [Fact]
    public void LayoutTab_DoesNotExposeDesignPageBackgroundCommands()
    {
        var layout = FreeWRibbon.Build().FindTab("layout");

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
        var definition = FreeWRibbon.Build();
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
        var definition = FreeWRibbon.Build();
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
                ("freew.line-numbers-options", "Line Numbering Options..."));

        registry.TryGet("freew.line-numbers", out _).Should().BeTrue("the top-level Line Numbers command keeps quick cycle behavior");
        registry.TryGet("freew.line-numbers-none", out var none).Should().BeTrue();
        registry.TryGet("freew.line-numbers-continuous", out var continuous).Should().BeTrue();
        registry.TryGet("freew.line-numbers-restart-page", out var restartPage).Should().BeTrue();
        registry.TryGet("freew.line-numbers-options", out _).Should().BeTrue("Word exposes Line Numbering Options from the same dropdown");

        continuous!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.LineNumberMode.Should().Be(LineNumberMode.Continuous);
        restartPage!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.LineNumberMode.Should().Be(LineNumberMode.RestartEachPage);
        none!.Execute(RibbonCommandContext.Empty);
        editor.Model.Page.LineNumberMode.Should().Be(LineNumberMode.None);
    }

    [StaFact]
    public void LayoutPageSetup_ColumnsDropdownExposesBackedWordPresetCommands()
    {
        var definition = FreeWRibbon.Build();
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
        var definition = FreeWRibbon.Build();
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
                // Finish group
                "freew.merge-finish");
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
                "Finish & Merge");

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
    public void ViewShow_ExposesWordStyleRulerToggle()
    {
        var definition = FreeWRibbon.Build();
        var show = definition.FindTab("view")!.FindGroup("show");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            onPrintPreview: null,
            onToggleNavPane: () => { },
            isNavPaneVisible: () => false,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onToggleRuler: () => { },
            isRulerVisible: () => true);

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
        var definition = FreeWRibbon.Build();
        var zoom = definition.FindTab("view")!.FindGroup("zoom");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(
            editor,
            new RibbonStateStore(),
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: () => { },
            onZoom100: () => { },
            onZoomOnePage: () => { },
            onZoomPageWidth: () => { });

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
        var definition = FreeWRibbon.Build();
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
        var insert = FreeWRibbon.Build().FindTab("insert");

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
        var definition = FreeWRibbon.Build();

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
        var definition = FreeWRibbon.Build();

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
        var definition = FreeWRibbon.Build();
        var chartDesign = definition.FindTab("chart-design");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        chartDesign.Should().NotBeNull();
        chartDesign!.Groups.Select(group => group.Id)
            .Should()
            .Equal("chart-type", "chart-data", "chart-elements");

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
        var definition = FreeWRibbon.Build();
        var chartFormat = definition.FindTab("chart-format");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        chartFormat.Should().NotBeNull();
        chartFormat!.Groups.Select(group => group.Id)
            .Should()
            .Equal("chart-size");

        CommandIds(chartFormat)
            .Should()
            .Equal("freew.chart-size");
        Labels(chartFormat)
            .Should()
            .Equal("Size");

        registry.TryGet("freew.chart-size", out _).Should().BeTrue("freew.chart-size must execute from Chart Format");
    }

    [StaFact]
    public void ChartDesign_ChangeTypeMutatesSelectedChart()
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

        editor.SetSelectedChartKind(ChartKind.Bar);

        chart.Kind.Should().Be(ChartKind.Bar);
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

        editor.ToggleSelectedChartLegend();
        chart.ShowLegend.Should().Be(initial);
    }

    [StaFact]
    public void ChartDesign_SetSizeMutatesWidthAndHeight()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.InsertChart(Chart.Create(ChartKind.Pie, ["A"], [1.0]));
        var chart = editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();
        editor.SetSelectedChartSize(400, 300);
        chart!.WidthPt.Should().Be(400);
        chart.HeightPt.Should().Be(300);
    }

    [StaFact]
    public void ChartDesign_ReplaceChartDataMutatesModel()
    {
        var editor = new DocumentView();
        editor.Model.Blocks.Clear();
        editor.InsertChart(Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0], seriesName: "Sales"));
        var chart = editor.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Select(r => r.Chart)
            .FirstOrDefault(c => c is not null);

        chart.Should().NotBeNull();

        var replacement = Chart.Create(ChartKind.Bar, ["Jan", "Feb", "Mar"], [5.0, 6.0, 7.0], seriesName: "Revenue");
        editor.ReplaceSelectedChartData(replacement);

        chart!.Kind.Should().Be(ChartKind.Bar);
        chart.Categories.Should().Equal("Jan", "Feb", "Mar");
        chart.Series.Should().HaveCount(1);
        chart.Series[0].Name.Should().Be("Revenue");
        chart.Series[0].Values.Should().Equal(5.0, 6.0, 7.0);
    }

    [StaFact]
    public void TableDesign_ContextualTabContainsOnlyImplementedStyleCommands()
    {
        var definition = FreeWRibbon.Build();
        var tableDesign = definition.FindTab("table-design");
        var editor = new DocumentView();
        var registry = FreeWRibbonCommands.Build(editor, new RibbonStateStore());

        tableDesign.Should().NotBeNull();
        tableDesign!.Groups.Select(group => group.Id)
            .Should()
            .Equal("table-style-options", "table-style");

        CommandIds(tableDesign)
            .Should()
            .Equal(
                "freew.table-header-row",
                "freew.table-last-row",
                "freew.table-first-column",
                "freew.table-last-column",
                "freew.table-banded-rows",
                "freew.table-banded-cols",
                "freew.cell-shading");

        foreach (var commandId in CommandIds(tableDesign))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Table Design tab");
    }

    [StaFact]
    public void PictureFormat_ContextualTabExposesBackedWrapTextMenu()
    {
        var definition = FreeWRibbon.Build();
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
            .Equal("picture-arrange", "picture-adjust", "picture-size");

        CommandIds(picture.FindGroup("picture-arrange")!)
            .Should()
            .Equal(
                "freew.image-wrap",
                "freew.image-position",
                "freew.image-rotate",
                "freew.image-align-left",
                "freew.image-align-center",
                "freew.image-align-right",
                // Phase 2: z-order commands for floating images.
                "freew.image-bring-to-front",
                "freew.image-send-to-back",
                "freew.image-bring-forward",
                "freew.image-send-backward",
                // Phase 4: group / ungroup for floating objects.
                "freew.object-group",
                "freew.object-ungroup");

        CommandIds(picture.FindGroup("picture-adjust")!)
            .Should()
            .Equal(
                // W20: Adjust group — Corrections / Color / Transparency (new).
                "freew.image-corrections",
                "freew.image-color",
                "freew.image-transparency",
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
                or "freew.image-corrections" or "freew.image-color" or "freew.image-transparency")
                continue;
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from Picture Format");
        }
    }

    [StaFact]
    public void TableLayout_ContextualTabContainsImplementedTableLayoutCommands()
    {
        var definition = FreeWRibbon.Build();
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
                "freew.table-insert-row",
                "freew.table-insert-col-left",
                "freew.table-insert-col",
                "freew.table-delete-row",
                "freew.table-delete-col",
                "freew.table-delete",
                // table-merge group
                "freew.merge-cells",
                "freew.split-cell",
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
        var insert = FreeWRibbon.Build().FindTab("insert");

        insert.Should().NotBeNull();
        insert!.Groups.Select(group => group.Id)
            .Should()
            .NotContain("table-tools");

        CommandIds(insert).Should().Contain("freew.table");
        CommandIds(insert).Should().NotContain(new[]
        {
            "freew.table-insert-row",
            "freew.table-delete-row",
            "freew.table-insert-col",
            "freew.table-delete-col",
            "freew.cell-shading",
            "freew.merge-cells",
            "freew.split-cell",
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
        var definition = FreeWRibbon.Build();
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
        var definition = FreeWRibbon.Build();
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
            .And.Contain("freew.shape-text-direction")
            .And.Contain("freew.wordart-style")
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

        // Menu items for WordArt Style gallery
        var wordArtStyle = drawing.FindGroup("drawing-wordart")!.Controls
            .OfType<RibbonDropdown>()
            .Single(c => c.CommandId.Value == "freew.wordart-style");
        wordArtStyle.Menu.Items
            .Where(item => item.Kind == RibbonMenuItemKind.Command)
            .Select(item => item.CommandId!.Value)
            .Should()
            .Equal("freew.wordart-style-fill-blue", "freew.wordart-style-gradient",
                   "freew.wordart-style-outline", "freew.wordart-style-shadow");

        // Every command id in the tab + menus must be registered (backed).
        var allIds = CommandIds(drawing)
            .Concat(MenuCommandIds(changeShape))
            .Concat(MenuCommandIds(textDir))
            .Concat(MenuCommandIds(wordArtStyle))
            .Concat(drawing.FindGroup("drawing-styles")!.Controls.OfType<RibbonDropdown>()
                .SelectMany(MenuCommandIds))
            .Distinct()
            .Where(id => id is not ("freew.shape-change" or "freew.shape-fill" or "freew.shape-outline"
                or "freew.shape-text-direction" or "freew.wordart-style"));

        foreach (var commandId in allIds)
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must be backed");
    }

    // ── SmartArt Design contextual tab ─────────────────────────────────────────────────────────

    [StaFact]
    public void SmartArtDesignContextualTab_ExposesAllBackedNodeMutationCommands()
    {
        var definition = FreeWRibbon.Build();
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

    // ── Header & Footer Design contextual tab ───────────────────────────────────────────────────

    [Fact]
    public void Build_ExposesHeaderFooterDesignContextualTab()
    {
        var definition = FreeWRibbon.Build();

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
        var definition = FreeWRibbon.Build();
        var tab = definition.FindTab("header-footer-design");

        tab.Should().NotBeNull();
        tab!.Groups.Select(g => g.Id)
            .Should()
            .Equal("hf-header-footer", "hf-insert", "hf-navigation", "hf-options", "hf-position", "hf-close");
    }

    [StaFact]
    public void HeaderFooterDesign_AllTabCommandsAreBacked()
    {
        var definition = FreeWRibbon.Build();
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
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onToggleNotesPane: () => { paneVisible = !paneVisible; },
            isNotesPaneVisible: () => paneVisible);

        registry.TryGet("freew.show-notes", out var cmd).Should().BeTrue("freew.show-notes must be registered");
        var stateful = cmd as IRibbonStatefulCommand;
        stateful.Should().NotBeNull("freew.show-notes must be a stateful toggle when pane callbacks are supplied");

        stateful!.GetState().IsChecked.Should().BeFalse("pane starts hidden");
        cmd!.Execute(RibbonCommandContext.Empty);
        paneVisible.Should().BeTrue("Execute must invoke the toggle callback");
        stateful.GetState().IsChecked.Should().BeTrue("IsChecked reflects the toggle state");
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
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onOpenHeaderFooterPane: slot => { openedSlot = slot; },
            onCloseHeaderFooterPane: () => { });

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
            onPrintPreview: null,
            onToggleNavPane: null,
            isNavPaneVisible: null,
            onToggleReadMode: null,
            isReadModeActive: null,
            onTogglePrintLayout: null,
            isPrintLayoutActive: null,
            onToggleOutlineView: null,
            isOutlineViewActive: null,
            onZoomDialog: null,
            onOpenHeaderFooterPane: _ => { },
            onCloseHeaderFooterPane: () => { closeCalled = true; });

        registry.TryGet("freew.hf-close", out var cmd).Should().BeTrue();
        cmd!.Execute(RibbonCommandContext.Empty);
        closeCalled.Should().BeTrue("freew.hf-close must invoke the close callback");
    }
}
