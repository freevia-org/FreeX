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
            .Equal("freew.picture", "freew.shapes", "freew.smartart", "freew.chart", "freew.screenshot");
        Labels(insert.FindGroup("illustrations")!)
            .Should()
            .Equal("Pictures", "Shapes", "SmartArt", "Chart", "Screenshot");

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
            "freew.smartart",
            "freew.chart",
            "freew.shape-textbox",
            "freew.insert-quickpart",
            "freew.insert-file",
            "freew.wordart",
            "freew.drop-cap",
            "freew.datetime",
            "freew.field",
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
                "freew.footnote-endnote-options",
                "freew.citation",
                "freew.citation-style",
                "freew.bibliography",
                "freew.caption",
                "freew.tof",
                "freew.tof-refresh",
                "freew.cross-reference",
                "freew.index-mark",
                "freew.index-insert",
                "freew.mark-citation",
                "freew.table-of-authorities",
                "freew.table-of-authorities-refresh"
            });
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
            .Equal("freew.track-changes", "freew.reviewing-pane");
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
                "freew.space-before-toggle",
                "freew.space-after-toggle",
                "freew.paragraph-dialog",
                "freew.tabs-dialog");
        Labels(paragraph!)
            .Should()
            .Equal(
                "Decrease Indent",
                "Increase Indent",
                "Line and Paragraph Spacing",
                "Add Space Before Paragraph",
                "Add Space After Paragraph",
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
        mailings!.Groups.Select(group => group.Id)
            .Should()
            .Equal("merge-data", "merge-write", "merge-preview", "merge-finish");

        CommandIds(mailings)
            .Should()
            .Equal(
                "freew.start-mail-merge",
                "freew.merge-data",
                "freew.merge-edit-recipients",
                "freew.merge-field",
                "freew.merge-preview",
                "freew.merge-preview-first",
                "freew.merge-preview-previous",
                "freew.merge-preview-next",
                "freew.merge-preview-last",
                "freew.merge-finish");
        Labels(mailings)
            .Should()
            .Equal(
                "Start Mail Merge",
                "Select Recipients",
                "Edit Recipient List",
                "Insert Merge Field",
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

        foreach (var commandId in CommandIds(mailings))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Mailings tab");

        foreach (var commandId in startMailMerge.Menu.Items
                     .Where(item => item.Kind == RibbonMenuItemKind.Command)
                     .Select(item => item.CommandId!.Value))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from the Start Mail Merge menu");
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
            .Equal("freew.ruler", "freew.nav-pane");
        Labels(show!)
            .Should()
            .Equal("Ruler", "Navigation Pane");

        registry.TryGet("freew.ruler", out var command).Should().BeTrue("Word exposes View > Show > Ruler");
        command.Should().BeAssignableTo<IRibbonStatefulCommand>();
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
            .Equal("freew.zoom-dialog", "freew.zoom-100", "freew.zoom-one-page", "freew.zoom-page-width");
        Labels(zoom!)
            .Should()
            .Equal("Zoom", "100%", "One Page", "Page Width");

        foreach (var commandId in CommandIds(zoom!))
            registry.TryGet(commandId, out _).Should().BeTrue($"{commandId} must execute from View > Zoom");
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
            .ContainInOrder("picture-format", "table-design", "table-layout");

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
            .Equal("table-style");

        CommandIds(tableDesign)
            .Should()
            .Equal(
                "freew.cell-shading",
                "freew.table-header-row",
                "freew.table-banded-rows");

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
            .Equal("picture-arrange", "picture-size");

        CommandIds(picture.FindGroup("picture-arrange")!)
            .Should()
            .Equal("freew.image-wrap", "freew.image-align-left", "freew.image-align-center", "freew.image-align-right");

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

        foreach (var commandId in MenuCommandIds(wrap).Concat(CommandIds(picture)))
        {
            if (commandId == "freew.image-wrap")
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
            .Equal("table-properties", "table-rows-cols", "table-merge", "table-data");

        CommandIds(tableLayout)
            .Should()
            .Equal(
                "freew.table-properties",
                "freew.table-insert-row",
                "freew.table-delete-row",
                "freew.table-insert-col",
                "freew.table-delete-col",
                "freew.merge-cells",
                "freew.split-cell",
                "freew.table-repeat-header",
                "freew.table-formula");

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
}
