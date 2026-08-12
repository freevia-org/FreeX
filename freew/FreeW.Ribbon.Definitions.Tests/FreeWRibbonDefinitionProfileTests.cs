using System.Globalization;
using System.Text.Json;
using Free.Shared.Ribbon;
using FreeW.App.Localization;

namespace FreeW.Ribbon.Definitions.Tests;

public sealed class FreeWRibbonDefinitionProfileTests
{
    private static readonly string[] WpfOnlyTabIds = [];

    private static readonly string[] AvaloniaOnlyTabIds =
    [
        "file",
    ];

    private static readonly string[] WpfFontEffectCommandIds =
    [
        "freew.grow-font",
        "freew.shrink-font",
        "freew.subscript",
        "freew.superscript",
        "freew.change-case",
        "freew.smallcaps",
        "freew.allcaps",
        "freew.highlight",
        "freew.font-color",
        "freew.char-border",
        "freew.char-shading",
        "freew.clear-formatting",
    ];

    private static readonly string[] AvaloniaFontEffectCommandIds =
    [
        "freew.grow-font",
        "freew.shrink-font",
        "freew.superscript",
        "freew.subscript",
        "freew.smallcaps",
        "freew.allcaps",
        "freew.highlight",
        "freew.char-border",
        "freew.char-shading",
        "freew.clear-formatting",
        "freew.font-color",
        "freew.change-case",
    ];

    private static readonly DivergenceRule[] WpfOnlyCommandRules =
    [
        new("WPF-only tabs", entry => WpfOnlyTabIds.Contains(entry.TabId, StringComparer.Ordinal)),
        new("WPF gallery injection placeholders", entry => entry.GroupId is
            "chart-colors" or
            "chart-quick-layout" or
            "chart-style" or
            "picture-adjust" or
            "picture-size" or
            "smartart-colors" or
            "smartart-create-graphic" or
            "smartart-edit" or
            "smartart-layouts"),
        new("WPF desktop dialog and custom surfaces", entry => entry.CommandId.StartsWith("freew.custom", StringComparison.Ordinal) ||
            entry.CommandId.Contains("dialog", StringComparison.Ordinal) ||
            entry.CommandId.Contains("options", StringComparison.Ordinal) ||
            entry.CommandId.Contains("organizer", StringComparison.Ordinal) ||
            entry.CommandId.Contains("manager", StringComparison.Ordinal)),
        new("WPF richer Word surface not yet exposed by Avalonia", entry => entry.TabId is
            "home" or
            "insert" or
            "design" or
            "layout" or
            "references" or
            "mailings" or
            "review" or
            "view" or
            "help" or
            "picture-format" or
            "drawing-format" or
            "chart-design" or
            "chart-format" or
            "smartart-design" or
            "table-design" or
            "table-layout"),
    ];

    private static readonly DivergenceRule[] AvaloniaOnlyCommandRules =
    [
        new("Avalonia-only File tab shell commands", entry => entry.TabId == "file"),
        new("Avalonia portable command registry aliases", entry => entry.CommandId is
            "freew.find-replace-dialog" or
            "freew.insert-bookmark" or
            "freew.insert-hyperlink" or
            "freew.insert-table" or
            "freew.shape" or
            "freew.show-hide-para" or
            "freew.text-box"),
        new("Avalonia menu-backed portable palettes", entry => entry.CommandId.StartsWith("freew.font-color.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.page-color.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.para-spacing.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.quick-parts.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.symbol.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.table-borders.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.theme.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.theme-colors.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.theme-fonts.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.watermark.", StringComparison.Ordinal)),
        new("Avalonia backed subset commands with different ids from WPF", entry => entry.TabId is
            "home" or
            "insert" or
            "design" or
            "layout" or
            "references" or
            "mailings" or
            "review" or
            "view" or
            "picture-format" or
            "drawing-format" or
            "chart-design" or
            "chart-format" or
            "smartart-design" or
            "table-design" or
            "table-layout"),
    ];

    [Fact]
    public void Shared_factory_builds_wpf_and_avalonia_profiles()
    {
        var wpf = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf);
        var avalonia = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);

        wpf.VisibleTabs.Select(tab => tab.Id)
            .Should()
            .Equal("home", "insert", "design", "layout", "references", "mailings", "review", "view", "help", "developer");
        avalonia.Tabs.Select(tab => tab.Id)
            .Should()
            .Contain(new[] { "file", "home", "insert", "design", "layout", "references", "mailings", "review", "view", "developer" });
    }

    [Fact]
    public void Profile_tab_ids_match_except_named_capability_deltas()
    {
        var wpfTabIds = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf).Tabs.Select(tab => tab.Id).ToArray();
        var avaloniaTabIds = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia).Tabs.Select(tab => tab.Id).ToArray();

        wpfTabIds.Except(avaloniaTabIds, StringComparer.Ordinal)
            .Should()
            .BeEquivalentTo(WpfOnlyTabIds);
        avaloniaTabIds.Except(wpfTabIds, StringComparer.Ordinal)
            .Should()
            .BeEquivalentTo(AvaloniaOnlyTabIds);
    }

    [Fact]
    public void Profile_context_keys_match_for_shared_contextual_tabs()
    {
        var wpf = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf).ContextualTabs
            .ToDictionary(tab => tab.Id, tab => tab.Context!.ActivationKey, StringComparer.Ordinal);
        var avalonia = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia).ContextualTabs
            .ToDictionary(tab => tab.Id, tab => tab.Context!.ActivationKey, StringComparer.Ordinal);

        foreach (var tabId in wpf.Keys.Intersect(avalonia.Keys, StringComparer.Ordinal))
            avalonia[tabId].Should().Be(wpf[tabId], $"{tabId} uses the same activation key across profiles");
    }

    [Fact]
    public void Profile_command_id_differences_are_named_capability_deltas()
    {
        var wpf = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf)).ToArray();
        var avalonia = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia)).ToArray();
        var wpfIds = wpf.Select(entry => entry.CommandId).ToHashSet(StringComparer.Ordinal);
        var avaloniaIds = avalonia.Select(entry => entry.CommandId).ToHashSet(StringComparer.Ordinal);

        var unexpectedWpfOnly = wpf
            .Where(entry => !avaloniaIds.Contains(entry.CommandId))
            .Where(entry => !IsAllowed(entry, WpfOnlyCommandRules))
            .Select(entry => entry.Display)
            .ToArray();
        var unexpectedAvaloniaOnly = avalonia
            .Where(entry => !wpfIds.Contains(entry.CommandId))
            .Where(entry => !IsAllowed(entry, AvaloniaOnlyCommandRules))
            .Select(entry => entry.Display)
            .ToArray();

        unexpectedWpfOnly.Should().BeEmpty("every WPF-only ribbon id must have an explicit capability rule");
        unexpectedAvaloniaOnly.Should().BeEmpty("every Avalonia-only ribbon id must have an explicit capability rule");
    }

    [Fact]
    public void Picture_style_catalog_has_identical_profile_placement_and_labels()
    {
        var expectedIds = FreeW.Core.Model.PictureStyleCatalog.Catalog
            .Select(preset => $"freew.image-style-{preset.Id}")
            .ToArray();
        var expectedLabels = FreeW.Core.Model.PictureStyleCatalog.Catalog
            .Select(preset => preset.Name)
            .ToArray();

        foreach (var capabilities in new[] { FreeWRibbonCapabilities.Wpf, FreeWRibbonCapabilities.Avalonia })
        {
            var picture = FreeWRibbon.Build(capabilities).FindTab("picture-format")!;
            picture.Groups.Select(group => group.Id)
                .Should().Equal("picture-arrange", "picture-styles", "picture-adjust", "picture-size");
            var controls = picture.FindGroup("picture-styles")!.Controls.Cast<RibbonButton>().ToArray();
            controls.Select(control => control.CommandId.Value).Should().Equal(expectedIds);
            controls.Select(control => control.Label).Should().Equal(expectedLabels);
        }
    }

    [Fact]
    public void Chart_quick_layout_catalog_has_identical_profile_placement_labels_and_icons()
    {
        var expectedIds = FreeW.Core.Model.ChartQuickLayout.Catalog
            .Select(layout => $"freew.chart-quick-layout-{layout.Id}")
            .ToArray();
        var expectedLabels = FreeW.Core.Model.ChartQuickLayout.Catalog
            .Select(layout => layout.Name)
            .ToArray();

        foreach (var capabilities in new[] { FreeWRibbonCapabilities.Wpf, FreeWRibbonCapabilities.Avalonia })
        {
            var chartDesign = FreeWRibbon.Build(capabilities).FindTab("chart-design")!;
            var controls = chartDesign.FindGroup("chart-quick-layout")!.Controls.Cast<RibbonButton>().ToArray();
            controls.Select(control => control.CommandId.Value).Should().Equal(expectedIds);
            controls.Select(control => control.Label).Should().Equal(expectedLabels);
            controls.Should().OnlyContain(control =>
                control.Icon != null && control.Icon.Kind == RibbonCommandIconKind.Grid);
        }
    }

    [Fact]
    public void SmartArt_command_slice_uses_shared_ids_and_catalog_across_profiles()
    {
        var expected = new[]
        {
            "freew.smartart-add-shape", "freew.smartart-remove-shape",
            "freew.smartart-promote", "freew.smartart-demote",
            "freew.smartart-move-up", "freew.smartart-move-down",
            "freew.smartart-edit-text", "freew.smartart-change-style",
        };

        foreach (var capabilities in new[] { FreeWRibbonCapabilities.Wpf, FreeWRibbonCapabilities.Avalonia })
        {
            var ids = CommandEntries(FreeWRibbon.Build(capabilities))
                .Where(entry => entry.TabId == "smartart-design")
                .Select(entry => entry.CommandId)
                .ToArray();
            ids.Should().Contain(expected);
        }

        var avalonia = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia).FindTab("smartart-design")!;
        var styles = avalonia.FindGroup("smartart-styles")!.Controls
            .OfType<RibbonComboBox>()
            .Single(control => control.CommandId.Value == "freew.smartart-change-style");
        styles.Items.Should().Equal(FreeW.Core.Model.SmartArtStyle.Catalog.Select(style => style.Name));
    }

    [Fact]
    public void Home_design_parity_slice_command_ids_are_shared_where_backed()
    {
        var wpfIds = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf))
            .Select(entry => entry.CommandId)
            .ToHashSet(StringComparer.Ordinal);
        var avaloniaIds = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia))
            .Select(entry => entry.CommandId)
            .ToHashSet(StringComparer.Ordinal);

        var sharedIds = new[]
        {
            "freew.format-painter",
            "freew.paste-merge",
            "freew.paste-plain",
            "freew.paste-special",
            "freew.reset-style-set",
            "freew.undo",
            "freew.redo",
            "freew.style-clear",
            "freew.style-heading2",
            "freew.style-heading3",
        };

        foreach (var id in sharedIds)
        {
            wpfIds.Should().Contain(id);
            avaloniaIds.Should().Contain(id);
        }
    }

    [Fact]
    public void Avalonia_profile_uses_shared_print_preview_and_view_command_ids()
    {
        var avaloniaIds = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia))
            .Select(entry => entry.CommandId)
            .ToHashSet(StringComparer.Ordinal);

        avaloniaIds.Should().Contain("freew.print-preview");
        avaloniaIds.Should().Contain("freew.print-layout");
        avaloniaIds.Should().Contain("freew.web-layout");
        avaloniaIds.Should().Contain("freew.draft-view");
        avaloniaIds.Should().NotContain("freew.printlayout");
        avaloniaIds.Should().NotContain("freew.weblayout");
        avaloniaIds.Should().NotContain("freew.draftview");
    }

    [Fact]
    public void Avalonia_profile_uses_shared_layout_page_setup_command_ids()
    {
        var avaloniaIds = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia))
            .Select(entry => entry.CommandId)
            .ToHashSet(StringComparer.Ordinal);

        avaloniaIds.Should().Contain(new[]
        {
            "freew.margins",
            "freew.orientation",
            "freew.size",
            "freew.columns",
            "freew.columns-one",
            "freew.columns-two",
            "freew.columns-three",
            "freew.columns-left",
            "freew.columns-right",
            "freew.breaks",
            "freew.column-break",
            "freew.section-break-next-page",
            "freew.section-break-continuous",
            "freew.section-break-even-page",
            "freew.section-break-odd-page",
            "freew.page-setup",
            "freew.custom-margins",
            "freew.more-paper-sizes",
        });

        avaloniaIds.Should().NotContain("freew.page-setup-dialog");
        avaloniaIds.Should().NotContain("freew.page-orientation");
    }

    [Fact]
    public void Avalonia_profile_uses_shared_references_command_ids()
    {
        var avaloniaIds = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia))
            .Select(entry => entry.CommandId)
            .ToHashSet(StringComparer.Ordinal);

        avaloniaIds.Should().Contain(new[]
        {
            "freew.toc",
            "freew.toc-refresh",
            "freew.footnote",
            "freew.endnote",
            "freew.citation",
            "freew.bibliography",
            "freew.caption",
            "freew.cross-reference",
        });

        avaloniaIds.Should().NotContain(new[]
        {
            "freew.insert-toc",
            "freew.update-toc",
            "freew.insert-footnote",
            "freew.insert-endnote",
            "freew.insert-citation",
            "freew.insert-caption",
        });
    }

    [Fact]
    public void Avalonia_profile_uses_shared_mailings_command_ids()
    {
        var avaloniaIds = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia))
            .Select(entry => entry.CommandId)
            .ToHashSet(StringComparer.Ordinal);

        avaloniaIds.Should().Contain(new[]
        {
            "freew.start-mail-merge",
            "freew.start-mail-merge-letters",
            "freew.start-mail-merge-directory",
            "freew.start-mail-merge-normal",
            "freew.merge-data",
            "freew.merge-address-block",
            "freew.merge-greeting-line",
            "freew.merge-field",
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
            "freew.merge-preview-previous",
            "freew.merge-preview-next",
            "freew.merge-find-recipient",
            "freew.merge-check-errors",
            "freew.merge-finish",
            "freew.merge-email",
        });

        avaloniaIds.Should().NotContain(new[]
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
    public void Final_command_profile_asymmetries_use_shared_canonical_shape()
    {
        var expected = new[]
        {
            (CommandId: "freew.chart-size-dialog", TabId: "chart-format", GroupId: "chart-size", Label: "More Size Options..."),
            (CommandId: "freew.merge-check-errors", TabId: "mailings", GroupId: "merge-preview", Label: "Check for Errors"),
            (CommandId: "freew.merge-find-recipient", TabId: "mailings", GroupId: "merge-preview", Label: "Find Recipient"),
        };

        foreach (var capabilities in new[] { FreeWRibbonCapabilities.Wpf, FreeWRibbonCapabilities.Avalonia })
        {
            var definition = FreeWRibbon.Build(capabilities);
            foreach (var item in expected)
            {
                var control = definition.FindTab(item.TabId)!
                    .FindGroup(item.GroupId)!
                    .Controls
                    .Single(candidate => candidate.CommandId.Value == item.CommandId);

                control.Should().BeOfType<RibbonButton>();
                control.Label.Should().Be(item.Label);
                control.PreferredLayout.Should().Be(RibbonCommandLayoutKind.Medium);
            }
        }

        var wpf = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf);
        var chartSizeDialog = wpf.FindTab("chart-format")!.FindGroup("chart-size")!.Controls
            .Single(control => control.CommandId.Value == "freew.chart-size-dialog");
        var findRecipient = wpf.FindTab("mailings")!.FindGroup("merge-preview")!.Controls
            .Single(control => control.CommandId.Value == "freew.merge-find-recipient");
        var checkErrors = wpf.FindTab("mailings")!.FindGroup("merge-preview")!.Controls
            .Single(control => control.CommandId.Value == "freew.merge-check-errors");

        chartSizeDialog.Icon.Should().Be(new RibbonCommandIcon(RibbonCommandIconKind.Size));
        findRecipient.Icon.Should().Be(new RibbonCommandIcon(RibbonCommandIconKind.Search));
        checkErrors.Icon.Should().Be(new RibbonCommandIcon(
            RibbonCommandIconKind.Warning,
            RibbonCommandIconAccent.Warning));
        new[] { chartSizeDialog.KeyTip, findRecipient.KeyTip, checkErrors.KeyTip }
            .Should().OnlyContain(keyTip => keyTip == null);
    }

    [Fact]
    public void Checked_in_command_inventory_matches_compiled_profiles()
    {
        var wpf = InventoryLocations(FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf), "WPF");
        var avalonia = InventoryLocations(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia), "Avalonia");
        var commandIds = wpf.Keys.Concat(avalonia.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var both = commandIds.Count(commandId => wpf.ContainsKey(commandId) && avalonia.ContainsKey(commandId));
        var wpfOnly = commandIds.Count(commandId => wpf.ContainsKey(commandId) && !avalonia.ContainsKey(commandId));
        var avaloniaOnly = commandIds.Count(commandId => !wpf.ContainsKey(commandId) && avalonia.ContainsKey(commandId));

        using var document = JsonDocument.Parse(ReadRepositoryFile("docs", "parity", "freew-command-inventory.json"));
        var root = document.RootElement;

        root.GetProperty("schema").GetString().Should().Be("freew.command-inventory.v5");
        root.GetProperty("generatedBy").GetString().Should().Be("tools/Generate-FreeWCommandInventory.ps1");
        root.GetProperty("topologySource").GetString().Should().Contain("FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf/Avalonia)");
        root.GetProperty("sourceLiteralEvidenceNote").GetString().Should().Contain("not behavior proof");
        root.GetProperty("behaviorEvidenceNote").GetString().Should().Contain("focused WPF and Avalonia tests");
        root.GetProperty("classificationNote").GetString().Should().Contain("profile-shape-only");
        root.GetProperty("classificationNote").GetString().Should().Contain("actionable-gap");
        root.GetProperty("classificationRules").EnumerateArray()
            .Select(rule => rule.GetProperty("name").GetString())
            .Should()
            .Equal("shared-profile", "command-id-alias", "platform-only", "profile-shape-only", "deferred", "actionable-gap");

        var summary = root.GetProperty("summary");
        summary.GetProperty("totalCommands").GetInt32().Should().Be(commandIds.Length);
        summary.GetProperty("both").GetInt32().Should().Be(both);
        summary.GetProperty("wpfOnly").GetInt32().Should().Be(wpfOnly);
        summary.GetProperty("avaloniaOnly").GetInt32().Should().Be(avaloniaOnly);
        summary.GetProperty("missingWpf").GetInt32().Should().Be(avaloniaOnly);
        summary.GetProperty("missingAvalonia").GetInt32().Should().Be(wpfOnly);

        var commands = root.GetProperty("commands").EnumerateArray().ToArray();
        commands.Select(command => command.GetProperty("commandId").GetString()!)
            .Should()
            .Equal(commandIds);
        commands.Select(command => command.GetProperty("commandId").GetString()!)
            .Should()
            .NotContain("freew.word-count", "freew.word-count is a registry compatibility alias, not a generated inventory row");
        var gapClassificationCounts = commands
            .GroupBy(command => command.GetProperty("gapClassification").GetString()!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        summary.GetProperty("sharedProfile").GetInt32().Should().Be(CountGap(gapClassificationCounts, "shared-profile"));
        summary.GetProperty("profileShapeOnly").GetInt32().Should().Be(CountGap(gapClassificationCounts, "profile-shape-only"));
        summary.GetProperty("commandIdAliases").GetInt32().Should().Be(CountGap(gapClassificationCounts, "command-id-alias"));
        summary.GetProperty("platformOnly").GetInt32().Should().Be(CountGap(gapClassificationCounts, "platform-only"));
        summary.GetProperty("deferred").GetInt32().Should().Be(CountGap(gapClassificationCounts, "deferred"));
        summary.GetProperty("actionableGaps").GetInt32().Should().Be(CountGap(gapClassificationCounts, "actionable-gap"));
        summary.GetProperty("behaviorEvidenceRows").GetInt32().Should().Be(commands.Count(command =>
            command.TryGetProperty("behaviorEvidence", out _)));
        summary.GetProperty("actionableMissingWpf").GetInt32().Should().Be(commands.Count(command =>
            command.GetProperty("missingProfile").GetString() == "WPF" &&
            command.GetProperty("gapClassification").GetString() == "actionable-gap"));
        summary.GetProperty("actionableMissingAvalonia").GetInt32().Should().Be(commands.Count(command =>
            command.GetProperty("missingProfile").GetString() == "Avalonia" &&
            command.GetProperty("gapClassification").GetString() == "actionable-gap"));
        summary.GetProperty("actionableGaps").GetInt32().Should().Be(0,
            "the checked-in FreeW command profiles must not retain cross-platform command debt");
        summary.GetProperty("actionableMissingWpf").GetInt32().Should().Be(0);
        summary.GetProperty("actionableMissingAvalonia").GetInt32().Should().Be(0);

        foreach (var command in commands)
        {
            var commandId = command.GetProperty("commandId").GetString()!;
            var wpfPresent = wpf.TryGetValue(commandId, out var wpfLocations);
            var avaloniaPresent = avalonia.TryGetValue(commandId, out var avaloniaLocations);

            command.GetProperty("wpfPresent").GetBoolean().Should().Be(wpfPresent);
            command.GetProperty("avaloniaPresent").GetBoolean().Should().Be(avaloniaPresent);
            command.GetProperty("profileSurface").GetString().Should().Be(ProfileSurface(wpfPresent, avaloniaPresent));
            command.GetProperty("missingProfile").GetString().Should().Be(MissingProfile(wpfPresent, avaloniaPresent));
            command.GetProperty("classification").GetString().Should().Be(ProfileClassification(wpfPresent, avaloniaPresent));
            command.GetProperty("gapClassification").GetString().Should().NotBeNullOrWhiteSpace();
            command.GetProperty("gapClassificationRule").GetString().Should().Be(command.GetProperty("gapClassification").GetString());
            command.GetProperty("notes").GetString().Should().NotBeNullOrWhiteSpace();

            AssertInventoryLocations(command.GetProperty("wpfLocations"), wpfLocations ?? Array.Empty<InventoryLocation>());
            AssertInventoryLocations(command.GetProperty("avaloniaLocations"), avaloniaLocations ?? Array.Empty<InventoryLocation>());
        }

        AssertGapClassification(commands, "freew.accept-all", "shared-profile");
        AssertGapClassification(commands, "freew.font-color.black", "profile-shape-only");
        AssertGapClassification(commands, "freew.image-crop", "shared-profile");
        AssertGapClassification(commands, "freew.image-alt-text", "shared-profile");
        AssertGapClassification(commands, "freew.image-border", "shared-profile");
        AssertGapClassification(commands, "freew.image-reset", "shared-profile");
        AssertGapClassification(commands, "freew.image-size", "shared-profile");
        AssertGapClassification(commands, "freew.table-to-text", "shared-profile");
        AssertGapClassification(commands, "freew.field", "shared-profile");
        AssertGapClassification(commands, "freew.save-quickpart", "shared-profile");
        AssertGapClassification(commands, "freew.building-blocks-organizer", "shared-profile");
        AssertGapClassification(commands, "freew.draw-table", "shared-profile");
        AssertGapClassification(commands, "freew.eraser", "shared-profile");
        AssertGapClassification(commands, "freew.bookmark", "shared-profile");
        AssertGapClassification(commands, "freew.insert-bookmark", "command-id-alias");
        AssertGapClassification(commands, "freew.check-updates", "shared-profile");
        AssertGapClassification(commands, "freew.copy-diagnostics", "shared-profile");
        AssertGapClassification(commands, "freew.feedback", "shared-profile");
        AssertGapClassification(commands, "freew.help-online", "shared-profile");
        AssertGapClassification(commands, "freew.about", "shared-profile");
        AssertGapClassification(commands, "freew.cc-text", "shared-profile");
        AssertGapClassification(commands, "freew.cc-richtext", "shared-profile");
        AssertGapClassification(commands, "freew.cc-checkbox", "shared-profile");
        AssertGapClassification(commands, "freew.cc-date", "shared-profile");
        AssertGapClassification(commands, "freew.cc-dropdown", "shared-profile");
        AssertGapClassification(commands, "freew.cc-combo", "shared-profile");
        AssertGapClassification(commands, "freew.statistics", "shared-profile");
        AssertGapClassification(commands, "freew.spellcheck-toggle", "shared-profile");
        AssertGapClassification(commands, "freew.add-to-dictionary", "shared-profile");
        AssertGapClassification(commands, "freew.thesaurus", "shared-profile");
        AssertGapClassification(commands, "freew.set-proofing-language", "shared-profile");
        AssertGapClassification(commands, "freew.split", "command-id-alias");
        AssertBehaviorEvidence(
            commands,
            "freew.chart-size-dialog",
            "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            "FreeWRibbonParityTests.FinalCommandProfileAsymmetries_RouteToBackedWpfCommands",
            "freew/FreeW.App.Avalonia.Tests/ChartSmartArtContextualTabTests.cs",
            "ChartSmartArtContextualTabTests.Chart_size_primary_and_dialog_alias_route_selected_chart_to_owner_modal_callback",
            "freew.final-command-profile-routing.shared-behavior",
            "Final command profile routing");
        foreach (var commandId in new[] { "freew.merge-find-recipient", "freew.merge-check-errors" })
        {
            AssertBehaviorEvidence(
                commands,
                commandId,
                "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
                "FreeWRibbonParityTests.MailingsFindRecipientAndCheckErrors_UseSharedPlannersThroughWpfCommands",
                "freew/FreeW.App.Avalonia.Tests/MailMergeDialogSurfaceTests.cs",
                "MailMergeDialogSurfaceTests.MailingsCommandHost_RoutesFindAndErrorChecksThroughDialogsAndSharedPlanners",
                "freew.final-command-profile-routing.shared-behavior",
                "Final command profile routing");
        }
        var unsupportedDirectRows = commands
            .Where(command => command.GetProperty("profileSurface").GetString() != "both")
            .Where(command => !command.TryGetProperty("behaviorEvidence", out _))
            .Where(command => command.GetProperty("wpfLocations").EnumerateArray()
                    .Concat(command.GetProperty("avaloniaLocations").EnumerateArray())
                    .Any(location => location.GetProperty("controlType").GetString() is
                        "RibbonButton" or "RibbonToggleButton" or "RibbonSplitButton" or "RibbonCheckBox"))
            .ToArray();
        unsupportedDirectRows
            .Select(command => command.GetProperty("gapClassification").GetString())
            .Should().OnlyContain(classification =>
                classification == "command-id-alias" || classification == "actionable-gap",
            "a direct one-sided command without paired behavior evidence must remain explicit parity debt or a named alias");
        var platformOnlyRows = commands
            .Where(command => command.GetProperty("gapClassification").GetString() == "platform-only")
            .ToArray();
        platformOnlyRows.Select(command => command.GetProperty("commandId").GetString()!)
            .Should()
            .BeEquivalentTo(new[]
            {
                "freew.backstage",
                "freew.import-pdf-text",
                "freew.new",
                "freew.open",
                "freew.save",
            });
        foreach (var row in platformOnlyRows)
        {
            row.TryGetProperty("behaviorEvidence", out var _)
                .Should()
                .BeTrue("platform-only FreeW rows must carry command-specific evidence so they are not ambiguous parity blockers");
        }
        AssertPlatformOnlyNote(commands, "freew.backstage", "Avalonia compact File entry");
        AssertPlatformOnlyNote(commands, "freew.import-pdf-text", "Avalonia compact File command makes PDF text import explicit");
        AssertPlatformOnlyNote(commands, "freew.new", "Avalonia compact File command");
        AssertPlatformOnlyNote(commands, "freew.open", "Avalonia compact File command");
        AssertPlatformOnlyNote(commands, "freew.save", "Avalonia compact File command");
        AssertGapClassification(commands, "freew.arrange-all", "shared-profile");
        AssertBehaviorEvidence(
            commands,
            "freew.arrange-all",
            "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            "FreeWRibbonParityTests.View_Window_NewWindowAndArrangeAll_AreBacked",
            "freew/FreeW.App.Avalonia.Tests/RibbonAndDocumentTests.cs",
            "RibbonAndDocumentTests.Avalonia_file_shell_and_WPF_authority_legal_notice_commands_are_backed",
            "freew.platform-only.window-shell",
            "Window-management shell variance");
        AssertBehaviorEvidence(
            commands,
            "freew.backstage",
            "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            "FreeWRibbonParityTests.Wpf_profile_uses_backstage_shell_instead_of_avalonia_file_command_strip",
            "freew/FreeW.App.Avalonia.Tests/RibbonAndDocumentTests.cs",
            "RibbonAndDocumentTests.Avalonia_file_shell_and_WPF_authority_legal_notice_commands_are_backed",
            "freew.platform-only.avalonia-file-shell",
            "Avalonia compact File shell variance",
            "platform-only");
        AssertBehaviorEvidence(
            commands,
            "freew.import-pdf-text",
            "freew/FreeW.App.Presentation.Tests/DocumentPersistenceWorkflowTests.cs",
            "DocumentPersistenceWorkflowTests.ImportPdfText_UsesExplicitImportAdaptersOutsideNormalOpenSaveCatalog",
            "freew/FreeW.App.Avalonia.Tests/RibbonAndDocumentTests.cs",
            "RibbonAndDocumentTests.Import_pdf_ribbon_command_invokes_host_route",
            "freew.platform-only.pdf-import-shell",
            "PDF import shell variance",
            "platform-only");
        AssertBehaviorEvidence(
            commands,
            "freew.new",
            "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            "FreeWRibbonParityTests.Wpf_profile_uses_backstage_shell_instead_of_avalonia_file_command_strip",
            "freew/FreeW.App.Avalonia.Tests/RibbonAndDocumentTests.cs",
            "RibbonAndDocumentTests.Avalonia_file_shell_and_WPF_authority_legal_notice_commands_are_backed",
            "freew.platform-only.avalonia-file-shell",
            "Avalonia compact File shell variance",
            "platform-only");
        AssertBehaviorEvidence(
            commands,
            "freew.save",
            "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            "FreeWRibbonParityTests.Wpf_profile_uses_backstage_shell_instead_of_avalonia_file_command_strip",
            "freew/FreeW.App.Avalonia.Tests/RibbonAndDocumentTests.cs",
            "RibbonAndDocumentTests.Avalonia_file_shell_and_WPF_authority_legal_notice_commands_are_backed",
            "freew.platform-only.avalonia-file-shell",
            "Avalonia compact File shell variance",
            "platform-only");
        AssertBehaviorEvidence(
            commands,
            "freew.delete-comment",
            "freew/FreeW.App.Host.Tests/ThreadedCommentCommandTests.cs",
            "ThreadedCommentCommandTests.DeleteCommentAtCaret_RemovesThreadRangeAndReference",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            "DocumentViewReviewTests.DeleteCommentAtCaret_removes_the_comment");
        AssertBehaviorEvidence(
            commands,
            "freew.previous-comment",
            "freew/FreeW.App.Host.Tests/ThreadedCommentCommandTests.cs",
            "ThreadedCommentCommandTests.CommentNavigation_WrapsAndNoOpsWithoutComments",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewCommentTests.cs",
            "DocumentViewCommentTests.NextPreviousComment_moves_caret_in_document_order_and_wraps");
        AssertBehaviorEvidence(
            commands,
            "freew.next-comment",
            "freew/FreeW.App.Host.Tests/ThreadedCommentCommandTests.cs",
            "ThreadedCommentCommandTests.CommentNavigation_MovesBetweenThreadsInDocumentOrder",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewCommentTests.cs",
            "DocumentViewCommentTests.NextPreviousComment_moves_caret_in_document_order_and_wraps");
        AssertBehaviorEvidence(
            commands,
            "freew.resolve-comment",
            "freew/FreeW.App.Host.Tests/ThreadedCommentCommandTests.cs",
            "ThreadedCommentCommandTests.ToggleResolveCommentAtCaret_TogglesResolved",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            "DocumentViewReviewTests.ResolveComment_registry_command_toggles_the_comment_at_the_caret");
        AssertBehaviorEvidence(
            commands,
            "freew.chart-type-bar",
            "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            "FreeWRibbonParityTests.ChartDesign_ChangeTypeRibbonCommandMutatesSelectedChartAndUndoRestoresIt",
            "freew/FreeW.App.Avalonia.Tests/ChartSmartArtContextualTabTests.cs",
            "ChartSmartArtContextualTabTests.SetChartType_command_changes_chart_kind_and_reverts_on_undo",
            "freew.chart.shared-behavior",
            "Chart command behavior");
        AssertBehaviorEvidence(
            commands,
            "freew.chart-toggle-legend",
            "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            "FreeWRibbonParityTests.ChartDesign_ToggleLegendRibbonCommandMutatesSelectedChartAndUndoRestoresIt",
            "freew/FreeW.App.Avalonia.Tests/ChartSmartArtContextualTabTests.cs",
            "ChartSmartArtContextualTabTests.ToggleChartLegend_command_clears_layout_override_and_reverts_on_undo",
            "freew.chart.shared-behavior",
            "Chart command behavior");
        AssertBehaviorEvidence(
            commands,
            "freew.citation",
            "freew/FreeW.App.Host.Tests/CitationEditorTests.cs",
            "CitationEditorTests.InsertCitation_TaggedSourceWithQuotedFieldArgument_RenumbersOnUpdateFields",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.InsertCitation_tagged_source_with_quoted_field_argument_renumbers_on_update_fields",
            "freew.references-fields.shared-behavior",
            "References fields and generated regions");
        AssertBehaviorEvidence(
            commands,
            "freew.manage-sources",
            "freew/FreeW.App.Host.Tests/FreeWRibbonParityTests.cs",
            "FreeWRibbonParityTests.ReferencesCitations_ExposesBackedWordStyleManageSources",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.ReplaceSources_replaces_source_list_and_undo_reverts",
            "freew.references-fields.shared-behavior",
            "References fields and generated regions");
        AssertBehaviorEvidence(
            commands,
            "freew.bibliography",
            "freew/FreeW.App.Host.Tests/CitationEditorTests.cs",
            "CitationEditorTests.InsertBibliography_BuildsBlockFromSourcesAndUndoReverts",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.InsertBibliography_builds_block_from_sources_and_undo_reverts",
            "freew.references-fields.shared-behavior",
            "References fields and generated regions");
        AssertBehaviorEvidence(
            commands,
            "freew.mark-citation",
            "freew/FreeW.App.Host.Tests/MarkCitationEditorTests.cs",
            "MarkCitationEditorTests.MarkCitation_DropsAHiddenCitationMarkThatSurvivesCommit",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.MarkCitation_accepts_full_citation_dialog_result",
            "freew.references-fields.shared-behavior",
            "References fields and generated regions");
        AssertBehaviorEvidence(
            commands,
            "freew.table-of-authorities",
            "freew/FreeW.App.Host.Tests/MarkCitationEditorTests.cs",
            "MarkCitationEditorTests.InsertTableOfAuthorities_BuildsAGroupedTableFromTheMarks",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.MarkCitation_body_mark_builds_table_and_survives_docx_roundtrip",
            "freew.references-fields.shared-behavior",
            "References fields and generated regions");
        AssertBehaviorEvidence(
            commands,
            "freew.table-of-authorities-refresh",
            "freew/FreeW.App.Host.Tests/MarkCitationEditorTests.cs",
            "MarkCitationEditorTests.RefreshTableOfAuthorities_ReplacesThePriorRegionInPlaceWithoutDuplicating",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.UpdateFields_refreshes_existing_table_of_authorities_with_explicit_break_page_references",
            "freew.references-fields.shared-behavior",
            "References fields and generated regions");
        AssertBehaviorEvidence(
            commands,
            "freew.update-fields",
            "freew/FreeW.App.Host.Tests/NumericCitationEditorTests.cs",
            "NumericCitationEditorTests.UpdateFields_CitationFieldAndBibliographyRefresh_DoNotOverwriteCitationFromStaleView",
            "freew/FreeW.App.Avalonia.Tests/ReferencesTabTests.cs",
            "ReferencesTabTests.UpdateFields_refreshes_toc_and_bibliography_in_same_pass",
            "freew.references-fields.shared-behavior",
            "References fields and generated regions");
        AssertBehaviorEvidence(
            commands,
            "freew.statistics",
            "freew/FreeW.Core.Model.Tests/WordCountTests.cs",
            "WordCountTests.Of_IncludesTableCellParagraphs",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            "DocumentViewReviewTests.Review_safety_commands_route_to_host_callbacks",
            "freew.review-proofing-statistics.shared-behavior",
            "Review proofing statistics");
        AssertBehaviorEvidence(
            commands,
            "freew.spellcheck-toggle",
            "freew/FreeW.Core.Model.Tests/ProofingDiagnosticPlannerTests.cs",
            "ProofingDiagnosticPlannerTests.Build_suppresses_diagnostics_when_spellcheck_disabled",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            "DocumentViewReviewTests.Proofing_commands_toggle_state_dictionary_thesaurus_and_language",
            "freew.review-proofing.shared-behavior",
            "Review proofing");
        AssertBehaviorEvidence(
            commands,
            "freew.add-to-dictionary",
            "freew/FreeW.Core.Model.Tests/CustomDictionaryTests.cs",
            "CustomDictionaryTests.Add_ThenContains_FindsWord",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            "DocumentViewReviewTests.Proofing_commands_toggle_state_dictionary_thesaurus_and_language",
            "freew.review-proofing.shared-behavior",
            "Review proofing");
        AssertBehaviorEvidence(
            commands,
            "freew.thesaurus",
            "freew/FreeW.App.Host.Tests/ThesaurusAndBalloonsTests.cs",
            "ThesaurusAndBalloonsTests.ThesaurusLookup_KnownWord_ReturnsSensesWithSynonyms",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            "DocumentViewReviewTests.Thesaurus_replace_current_proofing_word_replaces_caret_word",
            "freew.review-thesaurus.shared-behavior",
            "Review thesaurus");
        AssertBehaviorEvidence(
            commands,
            "freew.set-proofing-language",
            "freew/FreeW.App.Host.Tests/CharacterBorderShadingLanguageApplyTests.cs",
            "CharacterBorderShadingLanguageApplyTests.SetProofingLanguage_MultiParagraphSelection_IsReversibleWithSingleUndo",
            "freew/FreeW.App.Avalonia.Tests/DocumentViewReviewTests.cs",
            "DocumentViewReviewTests.Proofing_language_applies_only_to_the_selected_range_across_paragraphs",
            "freew.review-proofing-language.shared-behavior",
            "Review proofing language");

        var markdown = ReadRepositoryFile("docs", "parity", "freew-command-inventory.md");
        markdown.Should().Contain($"| {commandIds.Length} | {both} | {wpfOnly} | {avaloniaOnly} | {avaloniaOnly} | {wpfOnly} |");
        markdown.Should().Contain("## Classification Rules");
        markdown.Should().Contain("profile-shape-only");
        markdown.Should().Contain("actionable-gap");
        markdown.Should().Contain("Source literal evidence columns show exact command-id text in source files only; the canonical shared definition contributes to both profile-definition columns.");
        markdown.Should().Contain("These literals are not behavior proof and never create rows.");
        markdown.Should().Contain("Behavior evidence rows");
        markdown.Should().Contain("Review comments: ThreadedCommentCommandTests.DeleteCommentAtCaret_RemovesThreadRangeAndReference");
        markdown.Should().Contain("References fields and generated regions: CitationEditorTests.InsertCitation_TaggedSourceWithQuotedFieldArgument_RenumbersOnUpdateFields");
        markdown.Should().Contain("Review proofing statistics: WordCountTests.Of_IncludesTableCellParagraphs");
        markdown.Should().Contain("Review thesaurus: ThesaurusAndBalloonsTests.ThesaurusLookup_KnownWord_ReturnsSensesWithSynonyms");
    }

    [Fact]
    public void Home_clipboard_text_is_resource_backed_for_wpf_and_avalonia_profiles()
    {
        var neutral = WithUiCulture("en-US", () => new[]
        {
            ClipboardSurface(FreeWRibbonCapabilities.Wpf),
            ClipboardSurface(FreeWRibbonCapabilities.Avalonia),
        });

        foreach (var surface in neutral)
        {
            surface.HomeHeader.Should().Be("Home");
            surface.HomeKeyTip.Should().Be("H");
            surface.ClipboardHeader.Should().Be("Clipboard");
            surface.ClipboardKeyTip.Should().Be("C");
            surface.PasteLabel.Should().Be("Paste");
            surface.PasteKeyTip.Should().Be("V");
            surface.CutLabel.Should().Be("Cut");
            surface.CutKeyTip.Should().Be("X");
            surface.CopyLabel.Should().Be("Copy");
            surface.CopyKeyTip.Should().Be("C");
        }

        WithUiCulture("en-US", () =>
        {
            AssertClipboardAccessoryLabelsUseResources(ClipboardAccessorySurface(FreeWRibbonCapabilities.Wpf));
            AssertClipboardAccessoryLabelsUseResources(ClipboardAccessorySurface(FreeWRibbonCapabilities.Avalonia));

            return true;
        }).Should().BeTrue();

        var pseudo = WithUiCulture(Loc.PseudoLocalizationCultureName, () => new[]
        {
            ClipboardSurface(FreeWRibbonCapabilities.Wpf),
            ClipboardSurface(FreeWRibbonCapabilities.Avalonia),
        });

        foreach (var surface in pseudo)
        {
            surface.HomeHeader.Should().Be("[[HHoommee]]");
            surface.HomeKeyTip.Should().Be("H");
            surface.ClipboardHeader.Should().Be("[[CClliippbbooaarrdd]]");
            surface.ClipboardKeyTip.Should().Be("C");
            surface.PasteLabel.Should().Be("[[PPaassttee]]");
            surface.PasteKeyTip.Should().Be("V");
            surface.CutLabel.Should().Be("[[CCuutt]]");
            surface.CutKeyTip.Should().Be("X");
            surface.CopyLabel.Should().Be("[[CCooppyy]]");
            surface.CopyKeyTip.Should().Be("C");
        }

        WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            var surfaces = new[]
            {
                ClipboardAccessorySurface(FreeWRibbonCapabilities.Wpf),
                ClipboardAccessorySurface(FreeWRibbonCapabilities.Avalonia),
            };

            foreach (var surface in surfaces)
            {
                AssertClipboardAccessoryLabelsUseResources(surface);
                surface.FormatPainterLabel.Should().Be("[[FFoorrmmaatt PPaaiinntteerr]]");
                surface.FormatPainterKeyTip.Should().Be("FP");
                surface.PasteTextOnlyLabel.Should().Be("[[PPaassttee TTeexxtt OOnnllyy]]");
            }

            return true;
        }).Should().BeTrue();
    }

    [Fact]
    public void Home_clipboard_profile_sources_use_resource_descriptors()
    {
        var wpfSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var avaloniaSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var canonicalSource = ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs");

        wpfSource.Should().NotContain(".Tab(\"home\", \"Home\"");
        wpfSource.Should().NotContain("tab.Group(\"clipboard\", \"Clipboard\"");
        wpfSource.Should().NotContain("g.Large(\"freew.paste\", \"Paste\"");
        wpfSource.Should().NotContain("g.Medium(\"freew.cut\", \"Cut\"");
        wpfSource.Should().NotContain("g.Medium(\"freew.copy\", \"Copy\"");
        wpfSource.Should().NotContain("g.Medium(\"freew.format-painter\", \"Format Painter\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.paste-plain\", \"Paste Text Only\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.paste-merge\", \"Merge Formatting\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.paste-special\", \"Paste Special");
        canonicalSource.Should().Contain("FreeWRibbonText.HomeTab");
        canonicalSource.Should().Contain("FreeWRibbonText.ClipboardGroup");
        canonicalSource.Should().Contain("FreeWRibbonText.PasteCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.CutCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.CopyCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.FormatPainterCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.PasteTextOnlyCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.PasteMergeFormattingCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.PasteSpecialCommand");

        avaloniaSource.Should().NotContain(".Tab(\"home\", \"Home\"");
        avaloniaSource.Should().NotContain("tab.Group(\"clipboard\", \"Clipboard\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.cut\",   \"Cut\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.copy\",  \"Copy\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.paste\", \"Paste\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.format-painter\", \"Format Painter\"");
        avaloniaSource.Should().NotContain("g.Icon(\"freew.paste-plain\", \"Paste Text Only\"");
        avaloniaSource.Should().NotContain("g.Icon(\"freew.paste-merge\", \"Merge Formatting\"");
        avaloniaSource.Should().NotContain("g.Icon(\"freew.paste-special\", \"Paste Special");
        avaloniaSource.Should().Contain("AddHomeTab(capabilities)");
    }

    [Fact]
    public void Home_font_core_text_is_resource_backed_for_wpf_and_avalonia_profiles()
    {
        WithUiCulture("en-US", () =>
        {
            foreach (var surface in new[]
            {
                FontCoreSurface(FreeWRibbonCapabilities.Wpf),
                FontCoreSurface(FreeWRibbonCapabilities.Avalonia),
            })
            {
                AssertFontCoreSurfaceUsesResources(surface);
            }

            return true;
        }).Should().BeTrue();

        WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            foreach (var surface in new[]
            {
                FontCoreSurface(FreeWRibbonCapabilities.Wpf),
                FontCoreSurface(FreeWRibbonCapabilities.Avalonia),
            })
            {
                AssertFontCoreSurfaceUsesResources(surface);
                surface.FontGroupHeader.Should().Be("[[FFoonntt]]");
                surface.BoldLabel.Should().Be("[[BBoolldd]]");
                surface.BoldKeyTip.Should().Be("1");
            }

            return true;
        }).Should().BeTrue();
    }

    [Fact]
    public void Home_font_core_profile_sources_use_resource_descriptors()
    {
        var wpfSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var avaloniaSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var canonicalSource = ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs");

        wpfSource.Should().NotContain("tab.Group(\"font\", \"Font\"");
        wpfSource.Should().NotContain("g.ComboBox(\"freew.font-family\", \"Font\"");
        wpfSource.Should().NotContain("g.ComboBox(\"freew.font-size\", \"Size\"");
        wpfSource.Should().NotContain("g.IconToggle(\"freew.bold\", \"Bold\"");
        wpfSource.Should().NotContain("g.IconToggle(\"freew.italic\", \"Italic\"");
        wpfSource.Should().NotContain("g.IconToggle(\"freew.underline\", \"Underline\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.strikethrough\", \"Strikethrough\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.font-dialog\", \"Font");
        canonicalSource.Should().Contain("FreeWRibbonText.FontGroup");
        canonicalSource.Should().Contain("FreeWRibbonText.FontFamilyCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.FontSizeCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.BoldCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.ItalicCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.UnderlineCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.StrikethroughCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.FontDialogCommand");

        avaloniaSource.Should().NotContain("tab.Group(\"font\", \"Font\"");
        avaloniaSource.Should().NotContain("g.ComboBox(\"freew.font-family\", \"Font\"");
        avaloniaSource.Should().NotContain("g.ComboBox(\"freew.font-size\",   \"Size\"");
        avaloniaSource.Should().NotContain("g.Toggle(\"freew.bold\",           \"Bold\"");
        avaloniaSource.Should().NotContain("g.Toggle(\"freew.italic\",          \"Italic\"");
        avaloniaSource.Should().NotContain("g.Toggle(\"freew.underline\",       \"Underline\"");
        avaloniaSource.Should().NotContain("g.Toggle(\"freew.strikethrough\",   \"Strikethrough\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.font-dialog\",     \"Font");
        avaloniaSource.Should().Contain("AddHomeTab(capabilities)");
    }

    [Fact]
    public void Home_font_effect_text_is_resource_backed_for_wpf_and_avalonia_profiles()
    {
        WithUiCulture("en-US", () =>
        {
            AssertWpfFontEffectLabelsUseResources(
                FontControlLabels(FreeWRibbonCapabilities.Wpf, WpfFontEffectCommandIds));
            AssertAvaloniaFontEffectLabelsUseResources(
                FontControlLabels(FreeWRibbonCapabilities.Avalonia, AvaloniaFontEffectCommandIds));

            return true;
        }).Should().BeTrue();

        WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            AssertWpfFontEffectLabelsUseResources(
                FontControlLabels(FreeWRibbonCapabilities.Wpf, WpfFontEffectCommandIds));
            AssertAvaloniaFontEffectLabelsUseResources(
                FontControlLabels(FreeWRibbonCapabilities.Avalonia, AvaloniaFontEffectCommandIds));

            return true;
        }).Should().BeTrue();
    }

    [Fact]
    public void Home_font_effect_profile_sources_use_resource_descriptors()
    {
        var wpfSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var avaloniaSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var canonicalSource = ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs");

        wpfSource.Should().NotContain("g.Icon(\"freew.grow-font\", \"Grow Font\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.shrink-font\", \"Shrink Font\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.subscript\", \"Subscript\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.superscript\", \"Superscript\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.change-case\", \"Change Case\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.smallcaps\", \"Small Caps\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.allcaps\", \"All Caps\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.highlight\", \"Text Highlight Colour\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.font-color\", \"Font Colour\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.char-border\", \"Character Border\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.char-shading\", \"Character Shading\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.clear-formatting\", \"Clear All Formatting\"");
        canonicalSource.Should().Contain("FreeWRibbonText.GrowFontCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.ShrinkFontCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.SubscriptCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.SuperscriptCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.ChangeCaseCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.SmallCapsCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.AllCapsCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.TextHighlightColorCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.FontColorCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.CharacterBorderCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.CharacterShadingCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.ClearAllFormattingCommand");

        avaloniaSource.Should().NotContain("g.Toggle(\"freew.superscript\",     \"X");
        avaloniaSource.Should().NotContain("g.Toggle(\"freew.subscript\",       \"X");
        avaloniaSource.Should().NotContain("g.Toggle(\"freew.smallcaps\",       \"Small Caps\"");
        avaloniaSource.Should().NotContain("g.Toggle(\"freew.allcaps\",         \"All Caps\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.highlight\",       \"Highlight\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.char-border\",     \"Character Border\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.char-shading\",    \"Character Shading\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.grow-font\",       \"A");
        avaloniaSource.Should().NotContain("g.Button(\"freew.shrink-font\",     \"A");
        avaloniaSource.Should().NotContain("g.Button(\"freew.clear-formatting\", \"Clear\"");
        avaloniaSource.Should().NotContain("g.Dropdown(\"freew.font-color\", \"Font Color\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.change-case\",     \"Aa\"");
        canonicalSource.Should().Contain("FreeWRibbonText.SuperscriptCompactCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.SubscriptCompactCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.HighlightCompactCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.GrowFontCompactCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.ShrinkFontCompactCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.ClearFormattingCompactCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.FontColorDropdownCommand");
        canonicalSource.Should().Contain("FreeWRibbonText.ChangeCaseCompactCommand");
    }

    [Fact]
    public void Home_font_color_palette_labels_are_resource_backed_for_avalonia_profile()
    {
        WithUiCulture("en-US", () =>
        {
            FontColorPaletteLabels().Should().Equal(
                Loc.Get("Ribbon_Palette_FontColor_Automatic_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Black_Label"),
                Loc.Get("Ribbon_Palette_FontColor_DarkRed_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Red_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Orange_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Yellow_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Green_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Blue_Label"),
                Loc.Get("Ribbon_Palette_FontColor_DarkBlue_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Purple_Label"),
                Loc.Get("Ribbon_Palette_FontColor_White_Label"));

            return true;
        }).Should().BeTrue();

        WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            FontColorPaletteLabels().Should().Equal(
                Loc.Get("Ribbon_Palette_FontColor_Automatic_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Black_Label"),
                Loc.Get("Ribbon_Palette_FontColor_DarkRed_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Red_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Orange_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Yellow_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Green_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Blue_Label"),
                Loc.Get("Ribbon_Palette_FontColor_DarkBlue_Label"),
                Loc.Get("Ribbon_Palette_FontColor_Purple_Label"),
                Loc.Get("Ribbon_Palette_FontColor_White_Label"));

            return true;
        }).Should().BeTrue();
    }

    [Fact]
    public void Home_font_color_palette_source_uses_resource_descriptors()
    {
        var dataSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbonDefinitionData.cs");
        var paletteSource = ReadRepositoryFile(
            "freew", "FreeW.App.Presentation", "Ribbon", "FreeWRibbonPaletteCatalog.cs");
        var avaloniaSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var canonicalSource = ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs");

        dataSource.Should().NotContain("(\"freew.font-color.automatic\", \"Automatic\")");
        dataSource.Should().NotContain("(\"freew.font-color.dark-red\", \"Dark Red\")");
        dataSource.Should().NotContain("(\"freew.font-color.dark-blue\", \"Dark Blue\")");
        dataSource.Should().Contain("FreeWRibbonPaletteCatalog.FontColors");
        paletteSource.Should().Contain("Loc.Get(\"Ribbon_Palette_FontColor_Automatic_Label\")");
        paletteSource.Should().Contain("Loc.Get(\"Ribbon_Palette_FontColor_DarkRed_Label\")");
        paletteSource.Should().Contain("Loc.Get(\"Ribbon_Palette_FontColor_DarkBlue_Label\")");
        avaloniaSource.Should().NotContain("private static readonly (string CommandId, string Label)[] FontColors");
        canonicalSource.Should().Contain("FreeWRibbonDefinitionData.FontColors");
    }

    [Fact]
    public void Home_paragraph_list_text_is_resource_backed_for_wpf_and_avalonia_profiles()
    {
        WithUiCulture("en-US", () =>
        {
            AssertParagraphListSurfaceUsesResources(ParagraphListSurface(FreeWRibbonCapabilities.Wpf), includesMultilevel: true);
            AssertParagraphListSurfaceUsesResources(
                ParagraphListSurface(FreeWRibbonCapabilities.Avalonia),
                includesMultilevel: true);

            return true;
        }).Should().BeTrue();

        WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            var wpf = ParagraphListSurface(FreeWRibbonCapabilities.Wpf);
            var avalonia = ParagraphListSurface(FreeWRibbonCapabilities.Avalonia);

            AssertParagraphListSurfaceUsesResources(wpf, includesMultilevel: true);
            AssertParagraphCommonSurfaceUsesResources(avalonia);
            avalonia.MultilevelListLabel.Should().NotBeNullOrWhiteSpace();
            wpf.ParagraphHeader.Should().Be("[[PPaarraaggrraapphh]]");
            wpf.BulletsLabel.Should().Be("[[BBuulllleettss]]");
            avalonia.NumberingLabel.Should().Be("[[NNuummbbeerriinngg]]");

            return true;
        }).Should().BeTrue();
    }

    [Fact]
    public void Insert_symbols_and_design_page_color_text_are_resource_backed_for_wpf_and_avalonia_profiles()
    {
        WithUiCulture("en-US", () =>
        {
            AssertSymbolSurfaceUsesResources(SymbolSurface(FreeWRibbonCapabilities.Wpf), includesMenu: false);
            AssertSymbolSurfaceUsesResources(SymbolSurface(FreeWRibbonCapabilities.Avalonia), includesMenu: false);
            AssertPageBackgroundSurfaceUsesResources(PageBackgroundSurface(FreeWRibbonCapabilities.Wpf), includesPalette: false);
            AssertPageBackgroundSurfaceUsesResources(PageBackgroundSurface(FreeWRibbonCapabilities.Avalonia), includesPalette: true);

            return true;
        }).Should().BeTrue();

        WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            var symbols = SymbolSurface(FreeWRibbonCapabilities.Avalonia);
            var pageBackground = PageBackgroundSurface(FreeWRibbonCapabilities.Avalonia);

            AssertSymbolSurfaceUsesResources(symbols, includesMenu: false);
            AssertPageBackgroundSurfaceUsesResources(pageBackground, includesPalette: true);
            symbols.SymbolMenuHeaders.Should().BeNull();
            pageBackground.PageColorMenuHeaders![0].Should().Be(Loc.Get("Ribbon_Palette_PageColor_NoColor_Label"));

            return true;
        }).Should().BeTrue();
    }

    [Fact]
    public void List_symbol_page_color_profile_sources_use_resource_descriptors()
    {
        var wpfSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var avaloniaSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var canonicalSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.cs")
            + ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs");
        var dataSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbonDefinitionData.cs");
        var paletteSource = ReadRepositoryFile(
            "freew", "FreeW.App.Presentation", "Ribbon", "FreeWRibbonPaletteCatalog.cs");
        var hostCommands = ReadRepositoryFile("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");

        wpfSource.Should().NotContain("g.Icon(\"freew.bullets\", \"Bullets\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.numbering\", \"Numbering\"");
        wpfSource.Should().NotContain("g.Icon(\"freew.multilevel-list\", \"Multilevel List\"");
        wpfSource.Should().NotContain("tab.Group(\"symbols\", \"Symbols\"");
        wpfSource.Should().NotContain("g.Medium(\"freew.symbol\", \"Symbol\"");
        wpfSource.Should().NotContain("tab.Group(\"page-background\", \"Page Background\"");
        wpfSource.Should().NotContain("g.Medium(\"freew.page-color\", \"Page Color\"");
        canonicalSource.Should().Contain("FreeWRibbonText.ParagraphGroup");
        canonicalSource.Should().Contain("FreeWRibbonText.SymbolsGroup");
        wpfSource.Should().NotContain("FreeWRibbonText.PageBackgroundGroup");

        avaloniaSource.Should().NotContain("tab.Group(\"paragraph\", \"Paragraph\"");
        avaloniaSource.Should().NotContain("g.Toggle(\"freew.bullets\",           \"Bullets\"");
        avaloniaSource.Should().NotContain("g.Dropdown(\"freew.symbol\", \"Symbol\"");
        avaloniaSource.Should().NotContain("g.Dropdown(\"freew.page-color\", \"Page Color\"");
        avaloniaSource.Should().NotContain("private static readonly (string CommandId, string Label)[] PageColors");
        canonicalSource.Should().Contain("FreeWRibbonText.SymbolCommand");
        avaloniaSource.Should().NotContain("FreeWRibbonDefinitionData.PageColors");
        avaloniaSource.Should().NotContain("FreeWRibbonDefinitionData.Symbols");

        canonicalSource.Should().Contain("FreeWRibbonText.PageBackgroundGroup");
        canonicalSource.Should().Contain("FreeWRibbonText.PageColorCommand");
        canonicalSource.Should().Contain("FreeWRibbonDefinitionData.PageColors");

        dataSource.Should().NotContain("(\"freew.page-color.none\", \"No Color\")");
        dataSource.Should().NotContain("\"Outline: 1. / 1.1. / 1.1.1.\"");
        dataSource.Should().NotContain("\"Euro Sign\"");
        dataSource.Should().Contain("FreeWRibbonPaletteCatalog.PageColors");
        paletteSource.Should().Contain("Loc.Get(\"Ribbon_Palette_PageColor_NoColor_Label\")");
        dataSource.Should().Contain("Loc.Get(\"Ribbon_Palette_MultilevelList_OutlineDecimal_Label\")");
        dataSource.Should().Contain("Loc.Get(\"Ribbon_Palette_Symbol_Euro_Label\")");

        hostCommands.Should().NotContain("Title = \"Page Color\"");
        hostCommands.Should().NotContain("Content = \"More Colors");
        hostCommands.Should().Contain("UiText.Get(\"Ribbon_Dialog_PageColor_Title\")");
        hostCommands.Should().Contain("UiText.Get(\"Ribbon_Palette_PageColor_NoColor_Label\")");
    }

    private static bool IsAllowed(CommandEntry entry, IReadOnlyList<DivergenceRule> rules) =>
        rules.Any(rule => rule.IsAllowed(entry));

    private static T WithUiCulture<T>(string cultureName, Func<T> action)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static ClipboardRibbonSurface ClipboardSurface(FreeWRibbonCapabilities capabilities)
    {
        var definition = FreeWRibbon.Build(capabilities);
        var home = definition.FindTab("home");
        home.Should().NotBeNull();
        var clipboard = home!.FindGroup("clipboard");
        clipboard.Should().NotBeNull();

        var paste = RequiredControl(clipboard!, "freew.paste");
        var cut = RequiredControl(clipboard!, "freew.cut");
        var copy = RequiredControl(clipboard!, "freew.copy");

        return new ClipboardRibbonSurface(
            home.Header,
            home.KeyTip,
            clipboard.Header,
            clipboard.KeyTip,
            paste.Label,
            paste.KeyTip,
            cut.Label,
            cut.KeyTip,
            copy.Label,
            copy.KeyTip);
    }

    private static RibbonControl RequiredControl(RibbonGroup group, string commandId) =>
        group.Controls.Single(control => control.CommandId.Value == commandId);

    private static FontCoreRibbonSurface FontCoreSurface(FreeWRibbonCapabilities capabilities)
    {
        var definition = FreeWRibbon.Build(capabilities);
        var home = definition.FindTab("home");
        home.Should().NotBeNull();
        var font = home!.FindGroup("font");
        font.Should().NotBeNull();

        var fontFamily = RequiredControl(font!, "freew.font-family");
        var fontSize = RequiredControl(font!, "freew.font-size");
        var bold = RequiredControl(font!, "freew.bold");
        var italic = RequiredControl(font!, "freew.italic");
        var underline = RequiredControl(font!, "freew.underline");
        var strikethrough = RequiredControl(font!, "freew.strikethrough");
        var fontDialog = RequiredControl(font!, "freew.font-dialog");

        return new FontCoreRibbonSurface(
            font.Header,
            font.KeyTip,
            fontFamily.Label,
            fontSize.Label,
            bold.Label,
            bold.KeyTip,
            italic.Label,
            italic.KeyTip,
            underline.Label,
            underline.KeyTip,
            strikethrough.Label,
            fontDialog.Label);
    }

    private static void AssertFontCoreSurfaceUsesResources(FontCoreRibbonSurface surface)
    {
        surface.FontGroupHeader.Should().Be(Loc.Get("Ribbon_Group_Font_Label"));
        surface.FontGroupKeyTip.Should().Be(Loc.GetNeutral("Ribbon_Group_Font_KeyTip"));
        surface.FontFamilyLabel.Should().Be(Loc.Get("Ribbon_Command_FontFamily_Label"));
        surface.FontSizeLabel.Should().Be(Loc.Get("Ribbon_Command_FontSize_Label"));
        surface.BoldLabel.Should().Be(Loc.Get("Ribbon_Command_Bold_Label"));
        surface.BoldKeyTip.Should().Be(Loc.GetNeutral("Ribbon_Command_Bold_KeyTip"));
        surface.ItalicLabel.Should().Be(Loc.Get("Ribbon_Command_Italic_Label"));
        surface.ItalicKeyTip.Should().Be(Loc.GetNeutral("Ribbon_Command_Italic_KeyTip"));
        surface.UnderlineLabel.Should().Be(Loc.Get("Ribbon_Command_Underline_Label"));
        surface.UnderlineKeyTip.Should().Be(Loc.GetNeutral("Ribbon_Command_Underline_KeyTip"));
        surface.StrikethroughLabel.Should().Be(Loc.Get("Ribbon_Command_Strikethrough_Label"));
        surface.FontDialogLabel.Should().Be(Loc.Get("Ribbon_Command_FontDialog_Label"));
    }

    private static IReadOnlyDictionary<string, string> FontControlLabels(
        FreeWRibbonCapabilities capabilities,
        IEnumerable<string> commandIds)
    {
        var definition = FreeWRibbon.Build(capabilities);
        var home = definition.FindTab("home");
        home.Should().NotBeNull();
        var font = home!.FindGroup("font");
        font.Should().NotBeNull();

        return commandIds.ToDictionary(
            commandId => commandId,
            commandId => RequiredControl(font!, commandId).Label,
            StringComparer.Ordinal);
    }

    private static void AssertWpfFontEffectLabelsUseResources(IReadOnlyDictionary<string, string> labels)
    {
        labels["freew.grow-font"].Should().Be(Loc.Get("Ribbon_Command_GrowFont_Label"));
        labels["freew.shrink-font"].Should().Be(Loc.Get("Ribbon_Command_ShrinkFont_Label"));
        labels["freew.subscript"].Should().Be(Loc.Get("Ribbon_Command_Subscript_Label"));
        labels["freew.superscript"].Should().Be(Loc.Get("Ribbon_Command_Superscript_Label"));
        labels["freew.change-case"].Should().Be(Loc.Get("Ribbon_Command_ChangeCase_Label"));
        labels["freew.smallcaps"].Should().Be(Loc.Get("Ribbon_Command_SmallCaps_Label"));
        labels["freew.allcaps"].Should().Be(Loc.Get("Ribbon_Command_AllCaps_Label"));
        labels["freew.highlight"].Should().Be(Loc.Get("Ribbon_Command_TextHighlightColor_Label"));
        labels["freew.font-color"].Should().Be(Loc.Get("Ribbon_Command_FontColor_Label"));
        labels["freew.char-border"].Should().Be(Loc.Get("Ribbon_Command_CharacterBorder_Label"));
        labels["freew.char-shading"].Should().Be(Loc.Get("Ribbon_Command_CharacterShading_Label"));
        labels["freew.clear-formatting"].Should().Be(Loc.Get("Ribbon_Command_ClearAllFormatting_Label"));
    }

    private static ClipboardAccessoryRibbonSurface ClipboardAccessorySurface(FreeWRibbonCapabilities capabilities)
    {
        var definition = FreeWRibbon.Build(capabilities);
        var home = definition.FindTab("home");
        home.Should().NotBeNull();
        var clipboard = home!.FindGroup("clipboard");
        clipboard.Should().NotBeNull();

        var formatPainter = RequiredControl(clipboard!, "freew.format-painter");
        var pasteTextOnly = RequiredControl(clipboard!, "freew.paste-plain");
        var pasteMergeFormatting = RequiredControl(clipboard!, "freew.paste-merge");
        var pasteSpecial = RequiredControl(clipboard!, "freew.paste-special");

        return new ClipboardAccessoryRibbonSurface(
            formatPainter.Label,
            formatPainter.KeyTip,
            pasteTextOnly.Label,
            pasteMergeFormatting.Label,
            pasteSpecial.Label);
    }

    private static void AssertClipboardAccessoryLabelsUseResources(ClipboardAccessoryRibbonSurface surface)
    {
        surface.FormatPainterLabel.Should().Be(Loc.Get("Ribbon_Command_FormatPainter_Label"));
        surface.FormatPainterKeyTip.Should().Be(Loc.GetNeutral("Ribbon_Command_FormatPainter_KeyTip"));
        surface.PasteTextOnlyLabel.Should().Be(Loc.Get("Ribbon_Command_PasteTextOnly_Label"));
        surface.PasteMergeFormattingLabel.Should().Be(Loc.Get("Ribbon_Command_PasteMergeFormatting_Label"));
        surface.PasteSpecialLabel.Should().Be(Loc.Get("Ribbon_Command_PasteSpecial_Label"));
    }

    private static void AssertAvaloniaFontEffectLabelsUseResources(IReadOnlyDictionary<string, string> labels)
    {
        labels["freew.grow-font"].Should().Be(Loc.Get("Ribbon_Command_GrowFontCompact_Label"));
        labels["freew.shrink-font"].Should().Be(Loc.Get("Ribbon_Command_ShrinkFontCompact_Label"));
        labels["freew.superscript"].Should().Be(Loc.Get("Ribbon_Command_SuperscriptCompact_Label"));
        labels["freew.subscript"].Should().Be(Loc.Get("Ribbon_Command_SubscriptCompact_Label"));
        labels["freew.smallcaps"].Should().Be(Loc.Get("Ribbon_Command_SmallCaps_Label"));
        labels["freew.allcaps"].Should().Be(Loc.Get("Ribbon_Command_AllCaps_Label"));
        labels["freew.highlight"].Should().Be(Loc.Get("Ribbon_Command_HighlightCompact_Label"));
        labels["freew.char-border"].Should().Be(Loc.Get("Ribbon_Command_CharacterBorder_Label"));
        labels["freew.char-shading"].Should().Be(Loc.Get("Ribbon_Command_CharacterShading_Label"));
        labels["freew.clear-formatting"].Should().Be(Loc.Get("Ribbon_Command_ClearFormattingCompact_Label"));
        labels["freew.font-color"].Should().Be(Loc.Get("Common_FontColor"));
        labels["freew.change-case"].Should().Be(Loc.Get("Ribbon_Command_ChangeCaseCompact_Label"));
    }

    private static IReadOnlyList<string> FontColorPaletteLabels()
    {
        var definition = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);
        var home = definition.FindTab("home");
        home.Should().NotBeNull();
        var font = home!.FindGroup("font");
        font.Should().NotBeNull();

        var fontColor = RequiredControl(font!, "freew.font-color");
        var dropdown = fontColor.Should().BeOfType<RibbonDropdown>().Subject;

        return dropdown.Menu.Items.Select(item => item.Header).ToArray();
    }

    private static ParagraphListRibbonSurface ParagraphListSurface(FreeWRibbonCapabilities capabilities)
    {
        var paragraph = RequiredGroup(FreeWRibbon.Build(capabilities), "home", "paragraph");
        var bullets = RequiredControl(paragraph, "freew.bullets");
        var numbering = RequiredControl(paragraph, "freew.numbering");
        var multilevel = paragraph.Controls.SingleOrDefault(control => control.CommandId.Value == "freew.multilevel-list");

        return new ParagraphListRibbonSurface(
            paragraph.Header,
            paragraph.KeyTip,
            bullets.Label,
            numbering.Label,
            multilevel?.Label,
            multilevel is RibbonDropdown dropdown
                ? dropdown.Menu.Items.Select(item => item.Header).ToArray()
                : null);
    }

    private static SymbolRibbonSurface SymbolSurface(FreeWRibbonCapabilities capabilities)
    {
        var symbols = RequiredGroup(FreeWRibbon.Build(capabilities), "insert", "symbols");
        var symbol = RequiredControl(symbols, "freew.symbol");

        return new SymbolRibbonSurface(
            symbols.Header,
            symbols.KeyTip,
            symbol.Label,
            symbol is RibbonDropdown dropdown
                ? dropdown.Menu.Items.Select(item => item.Header).ToArray()
                : null);
    }

    private static PageBackgroundRibbonSurface PageBackgroundSurface(FreeWRibbonCapabilities capabilities)
    {
        var pageBackground = RequiredGroup(FreeWRibbon.Build(capabilities), "design", "page-background");
        var watermark = RequiredControl(pageBackground, "freew.watermark");
        var pageColor = RequiredControl(pageBackground, "freew.page-color");
        var pageBorders = RequiredControl(pageBackground,
            capabilities.UsesPortableControlPresentation ? "freew.page-borders" : "freew.page-border");

        return new PageBackgroundRibbonSurface(
            pageBackground.Header,
            pageBackground.KeyTip,
            watermark.Label,
            pageColor.Label,
            pageBorders.Label,
            pageColor is RibbonDropdown { Menu.Items.Count: > 0 } dropdown
                ? dropdown.Menu.Items.Select(item => item.Header).ToArray()
                : null);
    }

    private static RibbonGroup RequiredGroup(RibbonDefinition definition, string tabId, string groupId)
    {
        var tab = definition.FindTab(tabId);
        tab.Should().NotBeNull();
        var group = tab!.FindGroup(groupId);
        group.Should().NotBeNull();

        return group!;
    }

    private static void AssertParagraphListSurfaceUsesResources(
        ParagraphListRibbonSurface surface,
        bool includesMultilevel,
        bool includesMultilevelMenu = true)
    {
        AssertParagraphCommonSurfaceUsesResources(surface);

        if (!includesMultilevel)
        {
            surface.MultilevelListLabel.Should().BeNull();
            surface.MultilevelMenuHeaders.Should().BeNull();
            return;
        }

        surface.MultilevelListLabel.Should().Be(Loc.Get("Ribbon_Command_MultilevelList_Label"));
        if (!includesMultilevelMenu)
            return;

        surface.MultilevelMenuHeaders.Should().Equal(
            Loc.Get("Ribbon_Command_MultilevelPromote_Label"),
            Loc.Get("Ribbon_Command_MultilevelDemote_Label"),
            Loc.Get("Ribbon_Palette_MultilevelList_OutlineDecimal_Label"),
            Loc.Get("Ribbon_Palette_MultilevelList_OutlineMixed_Label"),
            Loc.Get("Ribbon_Palette_MultilevelList_OutlineHeadings_Label"),
            Loc.Get("Ribbon_Command_MultilevelDefine_Label"));
    }

    private static void AssertParagraphCommonSurfaceUsesResources(ParagraphListRibbonSurface surface)
    {
        surface.ParagraphHeader.Should().Be(Loc.Get("Ribbon_Group_Paragraph_Label"));
        if (surface.ParagraphKeyTip is not null)
            surface.ParagraphKeyTip.Should().Be(Loc.GetNeutral("Ribbon_Group_Paragraph_KeyTip"));
        surface.BulletsLabel.Should().Be(Loc.Get("Ribbon_Command_Bullets_Label"));
        surface.NumberingLabel.Should().Be(Loc.Get("Ribbon_Command_Numbering_Label"));
    }

    private static void AssertSymbolSurfaceUsesResources(SymbolRibbonSurface surface, bool includesMenu)
    {
        surface.SymbolsHeader.Should().Be(Loc.Get("Ribbon_Group_Symbols_Label"));
        if (surface.SymbolsKeyTip is not null)
            surface.SymbolsKeyTip.Should().Be(Loc.GetNeutral("Ribbon_Group_Symbols_KeyTip"));
        surface.SymbolLabel.Should().Be(Loc.Get("Ribbon_Command_Symbol_Label"));

        if (!includesMenu)
        {
            surface.SymbolMenuHeaders.Should().BeNull();
            return;
        }

        surface.SymbolMenuHeaders.Should().Equal(FreeWRibbonDefinitionData.Symbols
            .Select(symbol => $"{symbol.Glyph}   {symbol.Label}"));
    }

    private static void AssertPageBackgroundSurfaceUsesResources(PageBackgroundRibbonSurface surface, bool includesPalette)
    {
        surface.PageBackgroundHeader.Should().Be(Loc.Get("Ribbon_Group_PageBackground_Label"));
        if (surface.PageBackgroundKeyTip is not null)
            surface.PageBackgroundKeyTip.Should().Be(Loc.GetNeutral("Ribbon_Group_PageBackground_KeyTip"));
        surface.WatermarkLabel.Should().Be(Loc.Get("Ribbon_Command_Watermark_Label"));
        surface.PageColorLabel.Should().Be(Loc.Get("Ribbon_Command_PageColor_Label"));
        surface.PageBordersLabel.Should().Be(Loc.Get("Ribbon_Command_PageBorders_Label"));

        if (!includesPalette)
        {
            surface.PageColorMenuHeaders.Should().BeNull();
            return;
        }

        surface.PageColorMenuHeaders.Should().Equal(FreeWRibbonDefinitionData.PageColors.Select(color => color.Label));
    }

    private static int CountGap(IReadOnlyDictionary<string, int> counts, string classification) =>
        counts.TryGetValue(classification, out var count) ? count : 0;

    private static void AssertGapClassification(
        IReadOnlyList<JsonElement> commands,
        string commandId,
        string expectedClassification)
    {
        var command = commands.Single(candidate =>
            candidate.GetProperty("commandId").GetString() == commandId);

        command.GetProperty("gapClassification").GetString().Should().Be(expectedClassification);
    }

    private static void AssertPlatformOnlyNote(
        IReadOnlyList<JsonElement> commands,
        string commandId,
        string expectedNoteFragment)
    {
        var command = commands.Single(candidate =>
            candidate.GetProperty("commandId").GetString() == commandId);

        command.GetProperty("gapClassification").GetString().Should().Be("platform-only");
        command.GetProperty("notes").GetString().Should().Contain(expectedNoteFragment);
        command.GetProperty("notes").GetString().Should().NotBe(
            "Host, shell, or desktop-only command; track separately from shared Word behavior gaps.");
    }

    private static void AssertBehaviorEvidence(
        IReadOnlyList<JsonElement> commands,
        string commandId,
        string expectedWpfPath,
        string expectedWpfTest,
        string expectedAvaloniaPath,
        string expectedAvaloniaTest,
        string expectedEvidenceId = "freew.review-comments.shared-behavior",
        string expectedSlice = "Review comments",
        string expectedGapClassification = "shared-profile")
    {
        var command = commands.Single(candidate =>
            candidate.GetProperty("commandId").GetString() == commandId);
        command.GetProperty("gapClassification").GetString().Should().Be(expectedGapClassification);

        var evidence = command.GetProperty("behaviorEvidence");
        evidence.GetProperty("evidenceId").GetString().Should().Be(expectedEvidenceId);
        evidence.GetProperty("slice").GetString().Should().Be(expectedSlice);
        evidence.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();

        AssertEvidenceLink(evidence.GetProperty("wpfEvidence"), expectedWpfPath, expectedWpfTest);
        AssertEvidenceLink(evidence.GetProperty("avaloniaEvidence"), expectedAvaloniaPath, expectedAvaloniaTest);
    }

    private static void AssertEvidenceLink(JsonElement link, string expectedPath, string expectedTest)
    {
        link.GetProperty("path").GetString().Should().Be(expectedPath);
        link.GetProperty("test").GetString().Should().Be(expectedTest);

        var methodName = expectedTest[(expectedTest.IndexOf('.') + 1)..];
        ReadRepositoryFile(expectedPath.Split('/')).Should().Contain(methodName);
    }

    private static string ReadRepositoryFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);

    private static IEnumerable<CommandEntry> CommandEntries(RibbonDefinition definition)
    {
        foreach (var tab in definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                {
                    foreach (var commandId in CommandIds(control))
                        yield return new CommandEntry(tab.Id, group.Id, commandId);
                }
            }
        }
    }

    private static IEnumerable<string> CommandIds(RibbonControl control)
    {
        var commandId = control switch
        {
            RibbonButton b => b.CommandId.Value,
            RibbonToggleButton t => t.CommandId.Value,
            RibbonComboBox c => c.CommandId.Value,
            RibbonCheckBox cb => cb.CommandId.Value,
            RibbonSplitButton sb => sb.CommandId.Value,
            RibbonDropdown d => d.CommandId.Value,
            RibbonGallery g => g.CommandId.Value,
            _ => null,
        };

        if (commandId is not null)
            yield return commandId;

        var menu = control switch
        {
            RibbonSplitButton sb => sb.Menu,
            RibbonDropdown d => d.Menu,
            _ => null,
        };

        if (menu is null)
            yield break;

        foreach (var item in CommandIds(menu.Items))
            yield return item;
    }

    private static IEnumerable<string> CommandIds(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } commandId)
                yield return commandId.Value;

            foreach (var childId in CommandIds(item.Children))
                yield return childId;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<InventoryLocation>> InventoryLocations(
        RibbonDefinition definition,
        string profile)
    {
        var locations = new Dictionary<string, List<InventoryLocation>>(StringComparer.Ordinal);
        foreach (var tab in definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                    AddInventoryControl(locations, tab, group, control, profile);
            }
        }

        return locations.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<InventoryLocation>)pair.Value
                .OrderBy(location => location.TabId, StringComparer.Ordinal)
                .ThenBy(location => location.GroupId, StringComparer.Ordinal)
                .ThenBy(location => location.Label, StringComparer.Ordinal)
                .ThenBy(location => location.ControlType, StringComparer.Ordinal)
                .ThenBy(location => location.Layout, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    private static void AddInventoryControl(
        Dictionary<string, List<InventoryLocation>> locations,
        RibbonTab tab,
        RibbonGroup group,
        RibbonControl control,
        string profile)
    {
        if (!string.IsNullOrEmpty(control.CommandId.Value))
        {
            AddInventoryLocation(locations, control.CommandId.Value, new InventoryLocation(
                profile,
                tab.Id,
                tab.Header,
                group.Id,
                group.Header,
                control.Label,
                control.GetType().Name,
                control.PreferredLayout.ToString()));
        }

        foreach (var menuLocation in InventoryMenuLocations(control, tab, group, profile))
            AddInventoryLocation(locations, menuLocation.CommandId, menuLocation.Location);
    }

    private static IEnumerable<(string CommandId, InventoryLocation Location)> InventoryMenuLocations(
        RibbonControl control,
        RibbonTab tab,
        RibbonGroup group,
        string profile)
    {
        var menu = control switch
        {
            RibbonSplitButton splitButton => splitButton.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };

        if (menu is null)
            yield break;

        foreach (var item in MenuItems(menu.Items))
        {
            if (item.CommandId is null)
                continue;

            yield return (item.CommandId.Value.Value, new InventoryLocation(
                profile,
                tab.Id,
                tab.Header,
                group.Id,
                group.Header,
                item.Header,
                "RibbonMenuItem",
                "Menu"));
        }
    }

    private static IEnumerable<RibbonMenuItem> MenuItems(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in MenuItems(item.Children))
                yield return child;
        }
    }

    private static void AddInventoryLocation(
        Dictionary<string, List<InventoryLocation>> locations,
        string commandId,
        InventoryLocation location)
    {
        if (!locations.TryGetValue(commandId, out var existing))
        {
            existing = [];
            locations.Add(commandId, existing);
        }

        existing.Add(location);
    }

    private static void AssertInventoryLocations(JsonElement element, IReadOnlyList<InventoryLocation> expected)
    {
        element.EnumerateArray()
            .Select(ReadInventoryLocation)
            .Should()
            .Equal(expected);
    }

    private static InventoryLocation ReadInventoryLocation(JsonElement element) =>
        new(
            element.GetProperty("profile").GetString()!,
            element.GetProperty("tabId").GetString()!,
            element.GetProperty("tab").GetString()!,
            element.GetProperty("groupId").GetString()!,
            element.GetProperty("group").GetString()!,
            element.GetProperty("label").GetString()!,
            element.GetProperty("controlType").GetString()!,
            element.GetProperty("layout").GetString()!);

    private static string ProfileSurface(bool wpfPresent, bool avaloniaPresent) =>
        wpfPresent && avaloniaPresent
            ? "both"
            : wpfPresent
                ? "wpf-only"
                : "avalonia-only";

    private static string MissingProfile(bool wpfPresent, bool avaloniaPresent) =>
        wpfPresent && avaloniaPresent
            ? "none"
            : wpfPresent
                ? "Avalonia"
                : "WPF";

    private static string ProfileClassification(bool wpfPresent, bool avaloniaPresent) =>
        wpfPresent && avaloniaPresent
            ? "shared-profile"
            : wpfPresent
                ? "wpf-profile-only"
                : "avalonia-profile-only";

    private sealed record DivergenceRule(string Reason, Func<CommandEntry, bool> IsAllowed);

    private sealed record InventoryLocation(
        string Profile,
        string TabId,
        string Tab,
        string GroupId,
        string Group,
        string Label,
        string ControlType,
        string Layout);

    private sealed record ClipboardRibbonSurface(
        string HomeHeader,
        string? HomeKeyTip,
        string ClipboardHeader,
        string? ClipboardKeyTip,
        string PasteLabel,
        string? PasteKeyTip,
        string CutLabel,
        string? CutKeyTip,
        string CopyLabel,
        string? CopyKeyTip);

    private sealed record ClipboardAccessoryRibbonSurface(
        string FormatPainterLabel,
        string? FormatPainterKeyTip,
        string PasteTextOnlyLabel,
        string PasteMergeFormattingLabel,
        string PasteSpecialLabel);

    private sealed record FontCoreRibbonSurface(
        string FontGroupHeader,
        string? FontGroupKeyTip,
        string FontFamilyLabel,
        string FontSizeLabel,
        string BoldLabel,
        string? BoldKeyTip,
        string ItalicLabel,
        string? ItalicKeyTip,
        string UnderlineLabel,
        string? UnderlineKeyTip,
        string StrikethroughLabel,
        string FontDialogLabel);

    private sealed record ParagraphListRibbonSurface(
        string ParagraphHeader,
        string? ParagraphKeyTip,
        string BulletsLabel,
        string NumberingLabel,
        string? MultilevelListLabel,
        IReadOnlyList<string>? MultilevelMenuHeaders);

    private sealed record SymbolRibbonSurface(
        string SymbolsHeader,
        string? SymbolsKeyTip,
        string SymbolLabel,
        IReadOnlyList<string>? SymbolMenuHeaders);

    private sealed record PageBackgroundRibbonSurface(
        string PageBackgroundHeader,
        string? PageBackgroundKeyTip,
        string WatermarkLabel,
        string PageColorLabel,
        string PageBordersLabel,
        IReadOnlyList<string>? PageColorMenuHeaders);

    private sealed record CommandEntry(string TabId, string GroupId, string CommandId)
    {
        public string Display => $"{TabId}/{GroupId}/{CommandId}";
    }
}
