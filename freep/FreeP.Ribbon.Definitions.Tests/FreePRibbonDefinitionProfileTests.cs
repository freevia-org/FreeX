using System.Globalization;
using System.Text.Json;
using Free.Shared.Ribbon;
using FreeP.App.Localization;
using FreeP.App.Compositor;

namespace FreeP.Ribbon.Definitions.Tests;

public sealed class FreePRibbonDefinitionProfileTests
{
    private static readonly string[] WpfOnlyTabIds = [];

    private static readonly IReadOnlyDictionary<string, string[]> PlatformOnlyShellCommandEvidence =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    [Fact]
    public void Shared_factory_builds_valid_wpf_and_avalonia_profiles()
    {
        var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
        var avalonia = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia);

        wpf.Tabs.Select(tab => tab.Id)
            .Should()
            .Equal("home", "insert", "design", "transitions", "animations", "view");
        avalonia.Tabs.Select(tab => tab.Id)
            .Should()
            .Equal("home", "insert", "design", "transitions", "animations", "view");

        RibbonDefinitionValidator.Validate(wpf).HasErrors.Should().BeFalse();
        RibbonDefinitionValidator.Validate(avalonia).HasErrors.Should().BeFalse();
    }

    [Fact]
    public void Home_paragraph_group_exposes_shared_visible_list_gallery_in_both_profiles()
    {
        var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
        var avalonia = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia);

        foreach (var definition in new[] { wpf, avalonia })
        {
            var paragraph = RequiredGroup(definition, "home", "paragraph");
            paragraph.Controls.Select(control => control.CommandId.Value)
                .Should()
                .Contain([
                    PresentationListGalleryPlanner.BulletsCommandId,
                    PresentationListGalleryPlanner.NumberingCommandId,
                    "freep.paragraph.align-left",
                    "freep.paragraph.align-center",
                    "freep.paragraph.align-right",
                    "freep.paragraph.align-justify",
                    "freep.indent-decrease",
                    "freep.indent-increase",
                ]);

            var bullets = RequiredControl(definition, PresentationListGalleryPlanner.BulletsCommandId)
                .Should()
                .BeOfType<RibbonDropdown>()
                .Subject;
            bullets.Menu.Items.Select(item => item.CommandId?.Value)
                .Should()
                .Contain([
                    "freep.bullets.bullet.disc",
                    "freep.bullets.bullet.square",
                    PresentationListGalleryPlanner.ImageBulletCommandId,
                ]);
            bullets.Menu.Items.Single(item =>
                    item.CommandId?.Value == PresentationListGalleryPlanner.ImageBulletCommandId)
                .IsEnabled
                .Should()
                .BeTrue("image bullet UI now routes through shared import/render and authoring support");

            var numbering = RequiredControl(definition, PresentationListGalleryPlanner.NumberingCommandId)
                .Should()
                .BeOfType<RibbonDropdown>()
                .Subject;
            numbering.Menu.Items.Select(item => item.CommandId?.Value)
                .Should()
                .Contain([
                    "freep.numbering.number.arabic-period",
                    "freep.numbering.number.roman-upper-period",
                    "freep.numbering.number.alpha-lower-period",
                ]);
        }
    }

    [Fact]
    public void Wpf_profile_exposes_undo_and_redo_in_home_edit_group()
    {
        var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
        var edit = RequiredGroup(wpf, "home", "edit");

        edit.Controls.Select(control => control.CommandId.Value)
            .Should()
            .ContainInOrder("freep.undo", "freep.redo");
    }

    [Fact]
    public void Arrange_change_shape_menu_exposes_all_modeled_common_presets()
    {
        foreach (var definition in new[]
                 {
                     FreePRibbon.Build(FreePRibbonCapabilities.Wpf),
                     FreePRibbon.Build(FreePRibbonCapabilities.Avalonia),
                 })
        {
            var control = RequiredControl(definition, ShapeChangePlanner.MenuCommandId)
                .Should()
                .BeOfType<RibbonDropdown>()
                .Subject;

            control.Menu.Items.Select(item => item.CommandId?.Value)
                .Should()
                .Contain([
                    ShapeChangePlanner.RectangleCommandId,
                    ShapeChangePlanner.EllipseCommandId,
                    ShapeChangePlanner.TriangleCommandId,
                    ShapeChangePlanner.DiamondCommandId,
                    ShapeChangePlanner.RightArrowCommandId,
                    ShapeChangePlanner.HexagonCommandId,
                    ShapeChangePlanner.Star5CommandId,
                    ShapeChangePlanner.CrossCommandId,
                    ShapeChangePlanner.PlusSignCommandId,
                ]);
        }
    }

    [Fact]
    public void Chart_change_type_menu_exposes_all_modelled_chart_types_in_both_profiles()
    {
        foreach (var definition in new[]
                 {
                     FreePRibbon.Build(FreePRibbonCapabilities.Wpf),
                     FreePRibbon.Build(FreePRibbonCapabilities.Avalonia),
                 })
        {
            var control = RequiredControl(definition, ChartDataDialogPlanner.ChangeChartTypeCommandId)
                .Should()
                .BeOfType<RibbonDropdown>()
                .Subject;

            control.Menu.Items.Select(item => item.CommandId?.Value)
                .Should()
                .Equal(ChartDataDialogPlanner.ChartTypeOptions.Select(option =>
                    ChartDataDialogPlanner.ChangeChartTypeOptionCommandId(option.Value)));
            control.Menu.Items.Select(item => item.Header)
                .Should()
                .Equal(ChartDataDialogPlanner.ChartTypeOptions.Select(option => option.Label));
        }
    }

    [Fact]
    public void SmartArt_colors_menu_exposes_the_complete_powerpoint_gallery()
    {
        foreach (var definition in new[]
                 {
                     FreePRibbon.Build(FreePRibbonCapabilities.Wpf),
                     FreePRibbon.Build(FreePRibbonCapabilities.Avalonia),
                 })
        {
            var control = RequiredControl(definition, SmartArtAuthoringPlanner.SmartArtColorsGalleryCommandId)
                .Should()
                .BeOfType<RibbonDropdown>()
                .Subject;

            control.Menu.Items.Select(item => item.CommandId?.Value)
                .Should()
                .Equal(SmartArtAuthoringPlanner.ColorGallery.Select(entry => entry.CommandId));
            control.Menu.Items.Select(item => item.Header)
                .Should()
                .Equal(SmartArtAuthoringPlanner.ColorGallery.Select(entry => entry.Title));
        }
    }

    [Fact]
    public void Circle_accent_timeline_text_resolves_in_both_profiles()
    {
        WithUiCulture("en-US", () =>
        {
            foreach (var definition in new[]
                     {
                         FreePRibbon.Build(FreePRibbonCapabilities.Wpf),
                         FreePRibbon.Build(FreePRibbonCapabilities.Avalonia),
                     })
            {
                var control = RequiredControl(
                    definition,
                    SmartArtAuthoringPlanner.CircleAccentTimelineLayoutCommandId);
                control.Label.Should().Be("Circle Accent Timeline");
                control.KeyTip.Should().Be("CT");
            }

            return true;
        });
    }

    [Fact]
    public void Grouped_list_layout_is_exposed_by_both_host_profiles()
    {
        WithUiCulture("en-US", () =>
        {
            foreach (var definition in new[]
                     {
                         FreePRibbon.Build(FreePRibbonCapabilities.Wpf),
                         FreePRibbon.Build(FreePRibbonCapabilities.Avalonia),
                     })
            {
                var control = RequiredControl(
                    definition,
                    SmartArtAuthoringPlanner.GroupedListLayoutCommandId);
                control.Label.Should().Be("Grouped List");
                control.KeyTip.Should().Be("GL");
            }

            return true;
        });
    }

    [Fact]
    public void Home_shell_ribbon_text_resolves_from_freep_localization_resources()
    {
        var text = WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
            var avalonia = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia);

            return new[]
            {
                wpf.FindTab("home")!.Header,
                wpf.FindTab("home")!.KeyTip!,
                RequiredGroup(wpf, "home", "slides").Header,
                RequiredGroup(wpf, "home", "slides").KeyTip!,
                RequiredControl(wpf, "freep.new-slide").Label,
                RequiredControl(wpf, "freep.new-slide").KeyTip!,
                RequiredControl(wpf, "freep.duplicate-slide").Label,
                RequiredControl(wpf, "freep.delete-slide").Label,
                RequiredControl(wpf, "freep.layout").Label,
                RequiredControl(wpf, "freep.layout").KeyTip!,
                RequiredControl(wpf, "freep.arrange.change-shape").Label,
                RequiredControl(wpf, "freep.arrange.change-shape").KeyTip!,
                RequiredGroup(wpf, "home", "clipboard").Header,
                RequiredGroup(wpf, "home", "clipboard").KeyTip!,
                RequiredControl(wpf, "freep.paste").Label,
                RequiredControl(wpf, "freep.paste").KeyTip!,
                RequiredControl(wpf, "freep.cut").Label,
                RequiredControl(wpf, "freep.cut").KeyTip!,
                RequiredControl(wpf, "freep.copy").Label,
                RequiredControl(wpf, "freep.copy").KeyTip!,
                RequiredControl(wpf, "freep.format-painter").Label,
                RequiredControl(wpf, "freep.format-painter").KeyTip!,
                RequiredGroup(wpf, "home", "font").Header,
                RequiredGroup(wpf, "home", "font").KeyTip!,
                RequiredControl(wpf, "freep.font-family").Label,
                RequiredControl(wpf, "freep.font-family").KeyTip!,
                RequiredControl(wpf, "freep.font-size").Label,
                RequiredControl(wpf, "freep.font-size").KeyTip!,
                RequiredControl(wpf, "freep.font-color").Label,
                RequiredControl(wpf, "freep.font-color").KeyTip!,
                RequiredControl(wpf, "freep.table-cell-fill").Label,
                RequiredControl(wpf, "freep.table-cell-fill").KeyTip!,
                RequiredControl(wpf, "freep.table-cell-anchor").Label,
                RequiredControl(wpf, "freep.table-cell-anchor").KeyTip!,
                RequiredControl(wpf, "freep.table-cell-border").Label,
                RequiredControl(wpf, "freep.table-cell-border").KeyTip!,
                RequiredControl(wpf, "freep.table-cell-inset").Label,
                RequiredControl(wpf, "freep.table-cell-inset").KeyTip!,
                RequiredControl(wpf, "freep.table-row-height").Label,
                RequiredControl(wpf, "freep.table-row-height").KeyTip!,
                RequiredControl(wpf, "freep.table.merge-cells").Label,
                RequiredControl(wpf, "freep.table.merge-cells").KeyTip!,
                RequiredControl(wpf, "freep.table.split-cell").Label,
                RequiredControl(wpf, "freep.table.split-cell").KeyTip!,
                RequiredControl(wpf, "freep.table.first-row").Label,
                RequiredControl(wpf, "freep.table.first-row").KeyTip!,
                RequiredControl(wpf, "freep.table.last-row").Label,
                RequiredControl(wpf, "freep.table.last-row").KeyTip!,
                RequiredControl(wpf, "freep.table.first-column").Label,
                RequiredControl(wpf, "freep.table.first-column").KeyTip!,
                RequiredControl(wpf, "freep.table.last-column").Label,
                RequiredControl(wpf, "freep.table.last-column").KeyTip!,
                RequiredControl(wpf, "freep.table.banded-rows").Label,
                RequiredControl(wpf, "freep.table.banded-rows").KeyTip!,
                RequiredControl(wpf, "freep.table.banded-columns").Label,
                RequiredControl(wpf, "freep.table.banded-columns").KeyTip!,
                RequiredControl(wpf, "freep.bold").Label,
                RequiredControl(wpf, "freep.bold").KeyTip!,
                RequiredControl(wpf, "freep.italic").Label,
                RequiredControl(wpf, "freep.italic").KeyTip!,
                RequiredControl(wpf, "freep.underline").Label,
                RequiredControl(wpf, "freep.underline").KeyTip!,
                RequiredGroup(wpf, "home", "editing").Header,
                RequiredGroup(wpf, "home", "editing").KeyTip!,
                RequiredControl(wpf, "freep.find").Label,
                RequiredControl(wpf, "freep.find").KeyTip!,
                RequiredControl(wpf, "freep.replace").Label,
                RequiredControl(wpf, "freep.replace").KeyTip!,
                RequiredGroup(wpf, "transitions", "slideshow-from-transitions").Header,
                RequiredControl(wpf, "freep.slideshow.from-beginning").Label,
                RequiredControl(wpf, "freep.slideshow.from-current-slide").Label,
                RequiredControl(wpf, "freep.slideshow.rehearse-timings").Label,
                RequiredControl(wpf, "freep.slideshow.record-timings").Label,
                RequiredControl(wpf, "freep.slideshow.setup").Label,
                RequiredControl(wpf, "freep.slideshow.custom-shows").Label,
                RequiredControl(wpf, "freep.slideshow.custom-shows").KeyTip!,
                RequiredGroup(avalonia, "home", "slides").Header,
                RequiredControl(avalonia, "freep.new-slide").Label,
                RequiredControl(avalonia, "freep.new-slide").KeyTip!,
                RequiredControl(avalonia, "freep.layout").Label,
                RequiredControl(avalonia, "freep.layout").KeyTip!,
                RequiredGroup(avalonia, "home", "clipboard").Header,
                RequiredGroup(avalonia, "home", "clipboard").KeyTip!,
                RequiredControl(avalonia, "freep.paste").Label,
                RequiredControl(avalonia, "freep.paste").KeyTip!,
                RequiredControl(avalonia, "freep.cut").Label,
                RequiredControl(avalonia, "freep.cut").KeyTip!,
                RequiredControl(avalonia, "freep.copy").Label,
                RequiredControl(avalonia, "freep.copy").KeyTip!,
                RequiredControl(avalonia, "freep.format-painter").Label,
                RequiredControl(avalonia, "freep.format-painter").KeyTip!,
                RequiredGroup(avalonia, "home", "font").Header,
                RequiredGroup(avalonia, "home", "font").KeyTip!,
                RequiredControl(avalonia, "freep.font-family").Label,
                RequiredControl(avalonia, "freep.font-family").KeyTip!,
                RequiredControl(avalonia, "freep.font-size").Label,
                RequiredControl(avalonia, "freep.font-size").KeyTip!,
                RequiredControl(avalonia, "freep.font-color").Label,
                RequiredControl(avalonia, "freep.font-color").KeyTip!,
                RequiredControl(avalonia, "freep.table-cell-fill").Label,
                RequiredControl(avalonia, "freep.table-cell-fill").KeyTip!,
                RequiredControl(avalonia, "freep.table-cell-anchor").Label,
                RequiredControl(avalonia, "freep.table-cell-anchor").KeyTip!,
                RequiredControl(avalonia, "freep.table-cell-border").Label,
                RequiredControl(avalonia, "freep.table-cell-border").KeyTip!,
                RequiredControl(avalonia, "freep.table-cell-inset").Label,
                RequiredControl(avalonia, "freep.table-cell-inset").KeyTip!,
                RequiredControl(avalonia, "freep.table-row-height").Label,
                RequiredControl(avalonia, "freep.table-row-height").KeyTip!,
                RequiredControl(avalonia, "freep.table.merge-cells").Label,
                RequiredControl(avalonia, "freep.table.merge-cells").KeyTip!,
                RequiredControl(avalonia, "freep.table.split-cell").Label,
                RequiredControl(avalonia, "freep.table.split-cell").KeyTip!,
                RequiredControl(avalonia, "freep.table.first-row").Label,
                RequiredControl(avalonia, "freep.table.first-row").KeyTip!,
                RequiredControl(avalonia, "freep.table.last-row").Label,
                RequiredControl(avalonia, "freep.table.last-row").KeyTip!,
                RequiredControl(avalonia, "freep.table.first-column").Label,
                RequiredControl(avalonia, "freep.table.first-column").KeyTip!,
                RequiredControl(avalonia, "freep.table.last-column").Label,
                RequiredControl(avalonia, "freep.table.last-column").KeyTip!,
                RequiredControl(avalonia, "freep.table.banded-rows").Label,
                RequiredControl(avalonia, "freep.table.banded-rows").KeyTip!,
                RequiredControl(avalonia, "freep.table.banded-columns").Label,
                RequiredControl(avalonia, "freep.table.banded-columns").KeyTip!,
                RequiredControl(avalonia, "freep.bold").Label,
                RequiredControl(avalonia, "freep.bold").KeyTip!,
                RequiredControl(avalonia, "freep.italic").Label,
                RequiredControl(avalonia, "freep.italic").KeyTip!,
                RequiredControl(avalonia, "freep.underline").Label,
                RequiredControl(avalonia, "freep.underline").KeyTip!,
                RequiredGroup(avalonia, "home", "arrange").Header,
                RequiredGroup(avalonia, "home", "arrange").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.group").Label,
                RequiredControl(avalonia, "freep.arrange.group").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.ungroup").Label,
                RequiredControl(avalonia, "freep.arrange.ungroup").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.edit-points").Label,
                RequiredControl(avalonia, "freep.arrange.edit-points").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.bring-to-front").Label,
                RequiredControl(avalonia, "freep.arrange.bring-to-front").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.bring-forward").Label,
                RequiredControl(avalonia, "freep.arrange.bring-forward").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.send-backward").Label,
                RequiredControl(avalonia, "freep.arrange.send-backward").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.send-to-back").Label,
                RequiredControl(avalonia, "freep.arrange.send-to-back").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.align-left").Label,
                RequiredControl(avalonia, "freep.arrange.align-left").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.align-center-h").Label,
                RequiredControl(avalonia, "freep.arrange.align-center-h").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.align-right").Label,
                RequiredControl(avalonia, "freep.arrange.align-right").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.align-top").Label,
                RequiredControl(avalonia, "freep.arrange.align-top").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.align-middle").Label,
                RequiredControl(avalonia, "freep.arrange.align-middle").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.align-bottom").Label,
                RequiredControl(avalonia, "freep.arrange.align-bottom").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.distribute-h").Label,
                RequiredControl(avalonia, "freep.arrange.distribute-h").KeyTip!,
                RequiredControl(avalonia, "freep.arrange.distribute-v").Label,
                RequiredControl(avalonia, "freep.arrange.distribute-v").KeyTip!,
                RequiredGroup(avalonia, "home", "edit").Header,
                RequiredControl(avalonia, "freep.undo").Label,
                RequiredControl(avalonia, "freep.redo").Label,
                RequiredGroup(avalonia, "home", "editing").Header,
                RequiredGroup(avalonia, "home", "editing").KeyTip!,
                RequiredControl(avalonia, "freep.find").Label,
                RequiredControl(avalonia, "freep.find").KeyTip!,
                RequiredControl(avalonia, "freep.replace").Label,
                RequiredControl(avalonia, "freep.replace").KeyTip!,
                RequiredGroup(avalonia, "transitions", "slideshow-from-transitions").Header,
                RequiredControl(avalonia, "freep.slideshow.from-beginning").Label,
                RequiredControl(avalonia, "freep.slideshow.from-current-slide").Label,
                RequiredControl(avalonia, "freep.slideshow.rehearse-timings").Label,
                RequiredControl(avalonia, "freep.slideshow.record-timings").Label,
                RequiredControl(avalonia, "freep.slideshow.custom-shows").Label,
                RequiredControl(avalonia, "freep.slideshow.custom-shows").KeyTip!,
                avalonia.FindTab("design")!.Header,
                avalonia.FindTab("design")!.KeyTip!,
                RequiredGroup(avalonia, "design", "themes").Header,
                RequiredGroup(avalonia, "design", "themes").KeyTip!,
                RequiredGroup(avalonia, "design", "customize").Header,
                RequiredGroup(avalonia, "design", "customize").KeyTip!,
                RequiredControl(avalonia, "freep.theme.office").Label,
                RequiredControl(avalonia, "freep.theme.office").KeyTip!,
                RequiredControl(avalonia, "freep.theme.berlin").Label,
                RequiredControl(avalonia, "freep.theme.berlin").KeyTip!,
                RequiredControl(avalonia, "freep.theme.facet").Label,
                RequiredControl(avalonia, "freep.theme.facet").KeyTip!,
                RequiredControl(avalonia, "freep.theme.ion").Label,
                RequiredControl(avalonia, "freep.theme.ion").KeyTip!,
                RequiredControl(avalonia, "freep.theme.slice").Label,
                RequiredControl(avalonia, "freep.theme.slice").KeyTip!,
                RequiredControl(avalonia, "freep.slide-size-16x9").Label,
                RequiredControl(avalonia, "freep.slide-size-16x9").KeyTip!,
                RequiredControl(avalonia, "freep.slide-size-4x3").Label,
                RequiredControl(avalonia, "freep.slide-size-4x3").KeyTip!,
                RequiredControl(avalonia, "freep.slide-size-custom").Label,
                RequiredControl(avalonia, "freep.slide-size-custom").KeyTip!,
            };
        });

        text.Should().OnlyContain(value =>
            value.StartsWith("[[", StringComparison.Ordinal) &&
            value.EndsWith("]]", StringComparison.Ordinal));
    }

    [Fact]
    public void Insert_ribbon_text_resolves_from_freep_localization_resources()
    {
        var text = WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
            var avalonia = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia);

            return new[]
            {
                wpf.FindTab("insert")!.Header,
                wpf.FindTab("insert")!.KeyTip!,
                RequiredGroup(wpf, "insert", "text").Header,
                RequiredGroup(wpf, "insert", "text").KeyTip!,
                RequiredControl(wpf, "freep.text-box").Label,
                RequiredControl(wpf, "freep.text-box").KeyTip!,
                RequiredControl(wpf, "freep.header-footer").Label,
                RequiredControl(wpf, "freep.header-footer").KeyTip!,
                RequiredControl(wpf, "freep.date-time").Label,
                RequiredControl(wpf, "freep.date-time").KeyTip!,
                RequiredControl(wpf, "freep.slide-number").Label,
                RequiredControl(wpf, "freep.slide-number").KeyTip!,
                RequiredGroup(wpf, "insert", "tables").Header,
                RequiredControl(wpf, "freep.insert-table-3x3").Label,
                RequiredControl(wpf, "freep.insert-table-2x2").Label,
                RequiredControl(wpf, "freep.insert-table-4x4").Label,
                RequiredGroup(wpf, "insert", "charts").Header,
                RequiredControl(wpf, "freep.insert-chart-column").Label,
                RequiredControl(wpf, "freep.insert-chart-bar").Label,
                RequiredControl(wpf, "freep.insert-chart-line").Label,
                RequiredControl(wpf, "freep.insert-chart-pie").Label,
                RequiredControl(wpf, "freep.insert-chart-of-pie").Label,
                RequiredControl(wpf, "freep.chart.edit-data").Label,
                RequiredControl(wpf, "freep.chart.edit-data").KeyTip!,
                RequiredGroup(wpf, "insert", "links").Header,
                RequiredControl(wpf, "freep.insert-link").Label,
                RequiredControl(wpf, "freep.remove-link").Label,
                RequiredGroup(wpf, "insert", "illustrations").Header,
                RequiredControl(wpf, "freep.picture").Label,
                RequiredControl(wpf, "freep.shape-rectangle").Label,
                RequiredControl(wpf, "freep.shape-ellipse").Label,
                avalonia.FindTab("insert")!.Header,
                RequiredGroup(avalonia, "insert", "text").Header,
                RequiredControl(avalonia, "freep.text-box").Label,
                RequiredControl(avalonia, "freep.header-footer").Label,
                RequiredControl(avalonia, "freep.header-footer").KeyTip!,
                RequiredControl(avalonia, "freep.date-time").Label,
                RequiredControl(avalonia, "freep.date-time").KeyTip!,
                RequiredControl(avalonia, "freep.slide-number").Label,
                RequiredControl(avalonia, "freep.slide-number").KeyTip!,
                RequiredGroup(avalonia, "insert", "tables").Header,
                RequiredControl(avalonia, "freep.insert-table-3x3").Label,
                RequiredGroup(avalonia, "insert", "charts").Header,
                RequiredControl(avalonia, "freep.insert-chart-column").Label,
                RequiredControl(avalonia, "freep.insert-chart-of-pie").Label,
                RequiredControl(avalonia, "freep.chart.edit-data").Label,
                RequiredControl(avalonia, "freep.chart.edit-data").KeyTip!,
                RequiredGroup(avalonia, "insert", "links").Header,
                RequiredControl(avalonia, "freep.insert-link").Label,
                RequiredControl(avalonia, "freep.remove-link").Label,
                RequiredGroup(avalonia, "insert", "illustrations").Header,
                RequiredControl(avalonia, "freep.picture").Label,
            };
        });

        text.Should().OnlyContain(value =>
            value.StartsWith("[[", StringComparison.Ordinal) &&
            value.EndsWith("]]", StringComparison.Ordinal));
    }

    [Fact]
    public void Wpf_only_residual_ribbon_text_resolves_from_freep_localization_resources()
    {
        var text = WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
            var values = new List<string>
            {
                wpf.FindTab("transitions")!.Header,
                wpf.FindTab("transitions")!.KeyTip!,
                RequiredGroup(wpf, "transitions", "transition-gallery").Header,
                RequiredGroup(wpf, "transitions", "transition-gallery").KeyTip!,
                RequiredGroup(wpf, "transitions", "transition-timing").Header,
                RequiredGroup(wpf, "transitions", "transition-timing").KeyTip!,
                wpf.FindTab("animations")!.Header,
                wpf.FindTab("animations")!.KeyTip!,
                RequiredGroup(wpf, "animations", "animation-effects").Header,
                RequiredGroup(wpf, "animations", "animation-effects").KeyTip!,
                RequiredGroup(wpf, "animations", "animation-timing").Header,
                RequiredGroup(wpf, "animations", "animation-timing").KeyTip!,
                RequiredGroup(wpf, "animations", "animation-pane").Header,
                RequiredGroup(wpf, "animations", "animation-pane").KeyTip!,
                RequiredCombo(wpf, "freep.transition.advance-after").Items[0],
            };

            values.AddRange(ControlText(wpf,
                "freep.transition.none",
                "freep.transition.fade",
                "freep.transition.push",
                "freep.transition.wipe",
                "freep.transition.split",
                "freep.transition.box",
                "freep.transition.doors",
                "freep.transition.reveal",
                "freep.transition.flash",
                "freep.transition.morph",
                "freep.transition.cut",
                "freep.transition.cover",
                "freep.transition.uncover",
                "freep.transition.blinds",
                "freep.transition.comb",
                "freep.transition.random-bars",
                "freep.transition.strips",
                "freep.transition.wheel-reverse",
                "freep.transition.gallery",
                "freep.transition.conveyor",
                "freep.transition.pan",
                "freep.transition.window",
                "freep.transition.dissolve",
                "freep.transition.zoom",
                "freep.transition.wheel",
                "freep.transition.duration",
                "freep.transition.advance-on-click",
                "freep.transition.advance-after",
                "freep.transition.apply-all",
                "freep.slideshow.custom-shows",
                "freep.slideshow.setup",
                "freep.anim.entrance.appear",
                "freep.anim.entrance.fade",
                "freep.anim.entrance.fly-in",
                "freep.anim.entrance.wipe",
                "freep.anim.entrance.zoom",
                "freep.anim.entrance.split",
                "freep.anim.entrance.blinds",
                "freep.anim.emphasis.pulse",
                "freep.anim.emphasis.spin",
                "freep.anim.emphasis.grow-shrink",
                "freep.anim.exit.disappear",
                "freep.anim.exit.fade-out",
                "freep.anim.exit.fly-out",
                "freep.anim.exit.wipe",
                "freep.anim.exit.split",
                "freep.anim.exit.zoom-out",
                "freep.anim.exit.blinds",
                "freep.anim.none",
                "freep.anim.trigger",
                "freep.anim.duration",
                "freep.anim.delay",
                "freep.anim.move-earlier",
                "freep.anim.move-later",
                "freep.anim.pane"));
            values.AddRange(RequiredCombo(wpf, "freep.anim.trigger").Items);

            return values.ToArray();
        });

        text.Should().OnlyContain(value =>
            value.StartsWith("[[", StringComparison.Ordinal) &&
            value.EndsWith("]]", StringComparison.Ordinal));
    }

    [Fact]
    public void View_ribbon_text_resolves_from_freep_localization_resources()
    {
        var text = WithUiCulture(Loc.PseudoLocalizationCultureName, () =>
        {
            var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
            var avalonia = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia);

            return new[]
            {
                wpf.FindTab("view")!.Header,
                wpf.FindTab("view")!.KeyTip!,
                RequiredGroup(wpf, "view", "show").Header,
                RequiredGroup(wpf, "view", "show").KeyTip!,
                RequiredControl(wpf, "freep.view.show.gridlines").Label,
                RequiredControl(wpf, "freep.view.show.gridlines").KeyTip!,
                RequiredControl(wpf, "freep.view.show.guides").Label,
                RequiredControl(wpf, "freep.view.show.guides").KeyTip!,
                RequiredGroup(wpf, "view", "zoom").Header,
                RequiredGroup(wpf, "view", "zoom").KeyTip!,
                RequiredControl(wpf, "freep.view.zoom").Label,
                RequiredControl(wpf, "freep.view.zoom").KeyTip!,
                RequiredControl(wpf, "freep.view.fit-to-window").Label,
                RequiredControl(wpf, "freep.view.fit-to-window").KeyTip!,
                avalonia.FindTab("view")!.Header,
                avalonia.FindTab("view")!.KeyTip!,
                RequiredGroup(avalonia, "view", "show").Header,
                RequiredGroup(avalonia, "view", "show").KeyTip!,
                RequiredControl(avalonia, "freep.view.show.gridlines").Label,
                RequiredControl(avalonia, "freep.view.show.gridlines").KeyTip!,
                RequiredControl(avalonia, "freep.view.show.guides").Label,
                RequiredControl(avalonia, "freep.view.show.guides").KeyTip!,
                RequiredGroup(avalonia, "view", "zoom").Header,
                RequiredGroup(avalonia, "view", "zoom").KeyTip!,
                RequiredControl(avalonia, "freep.view.zoom").Label,
                RequiredControl(avalonia, "freep.view.zoom").KeyTip!,
                RequiredControl(avalonia, "freep.view.fit-to-window").Label,
                RequiredControl(avalonia, "freep.view.fit-to-window").KeyTip!,
            };
        });

        text.Should().OnlyContain(value =>
            value.StartsWith("[[", StringComparison.Ordinal) &&
            value.EndsWith("]]", StringComparison.Ordinal));
    }

    [Fact]
    public void Profile_tab_ids_match_except_named_capability_deltas()
    {
        var wpfTabIds = FreePRibbon.Build(FreePRibbonCapabilities.Wpf).Tabs.Select(tab => tab.Id).ToArray();
        var avaloniaTabIds = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia).Tabs.Select(tab => tab.Id).ToArray();

        wpfTabIds.Except(avaloniaTabIds, StringComparer.Ordinal)
            .Should()
            .BeEquivalentTo(WpfOnlyTabIds);
        avaloniaTabIds.Except(wpfTabIds, StringComparer.Ordinal)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Avalonia_backed_content_commands_are_wpf_commands_except_named_shell_aliases()
    {
        var wpfIds = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Wpf))
            .ToHashSet(StringComparer.Ordinal);
        var unexpectedAvaloniaOnly = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Avalonia))
            .Where(commandId => !wpfIds.Contains(commandId))
            .Where(commandId => !IsAllowedAvaloniaProfileCommand(commandId))
            .ToArray();

        unexpectedAvaloniaOnly.Should().BeEmpty(
            "cross-platform content commands should come from the shared FreeP command surface");
    }

    [Fact]
    public void Wpf_and_avalonia_profiles_expose_font_size_and_color_controls_as_backed_combos()
    {
        var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
        var avalonia = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia);
        var wpfFontGroup = RequiredGroup(wpf, "home", "font");
        var fontGroup = RequiredGroup(avalonia, "home", "font");
        var wpfCommandIds = wpfFontGroup.Controls
            .Select(control => control.CommandId.Value)
            .Where(commandId => !string.IsNullOrEmpty(commandId))
            .ToArray();
        var commandIds = fontGroup.Controls
            .Select(control => control.CommandId.Value)
            .Where(commandId => !string.IsNullOrEmpty(commandId))
            .ToArray();
        var wpfSize = RequiredCombo(wpf, "freep.font-size");
        var wpfColor = RequiredCombo(wpf, "freep.font-color");
        var wpfFill = RequiredCombo(wpf, "freep.table-cell-fill");
        var wpfAnchor = RequiredCombo(wpf, "freep.table-cell-anchor");
        var wpfBorder = RequiredCombo(wpf, "freep.table-cell-border");
        var wpfInset = RequiredCombo(wpf, "freep.table-cell-inset");
        var wpfRowHeight = RequiredCombo(wpf, "freep.table-row-height");
        var wpfTextAutoFit = RequiredCombo(wpf, "freep.text-autofit");
        var wpfTextDirection = RequiredCombo(wpf, "freep.text-direction");
        var wpfTextColumns = RequiredCombo(wpf, "freep.text-columns");
        var wpfTextColumnSpacing = RequiredCombo(wpf, "freep.text-column-spacing");
        var size = RequiredCombo(avalonia, "freep.font-size");
        var color = RequiredCombo(avalonia, "freep.font-color");
        var fill = RequiredCombo(avalonia, "freep.table-cell-fill");
        var anchor = RequiredCombo(avalonia, "freep.table-cell-anchor");
        var border = RequiredCombo(avalonia, "freep.table-cell-border");
        var inset = RequiredCombo(avalonia, "freep.table-cell-inset");
        var rowHeight = RequiredCombo(avalonia, "freep.table-row-height");
        var textAutoFit = RequiredCombo(avalonia, "freep.text-autofit");
        var textDirection = RequiredCombo(avalonia, "freep.text-direction");
        var textColumns = RequiredCombo(avalonia, "freep.text-columns");
        var textColumnSpacing = RequiredCombo(avalonia, "freep.text-column-spacing");

        wpfCommandIds.Should().ContainInOrder(
            "freep.font-family",
            "freep.font-size",
            "freep.font-color",
            "freep.text-autofit",
            "freep.text-direction",
            "freep.text-columns",
            "freep.table-cell-fill",
            "freep.table-cell-anchor",
            "freep.table-cell-border",
            "freep.table-cell-inset",
            "freep.table-row-height",
            "freep.table.first-row",
            "freep.table.last-row",
            "freep.table.first-column",
            "freep.table.last-column",
            "freep.table.banded-rows",
            "freep.table.banded-columns",
            "freep.bold",
            "freep.italic",
            "freep.underline",
            "freep.superscript",
            "freep.subscript");
        commandIds.Should().ContainInOrder(
            "freep.font-family",
            "freep.font-size",
            "freep.font-color",
            "freep.text-autofit",
            "freep.text-direction",
            "freep.text-columns",
            "freep.table-cell-fill",
            "freep.table-cell-anchor",
            "freep.table-cell-border",
            "freep.table-cell-inset",
            "freep.table-row-height",
            "freep.table.first-row",
            "freep.table.last-row",
            "freep.table.first-column",
            "freep.table.last-column",
            "freep.table.banded-rows",
            "freep.table.banded-columns",
            "freep.bold",
            "freep.italic",
            "freep.underline",
            "freep.superscript",
            "freep.subscript");
        wpfSize.Items.Should().Equal(FreePRibbonDefinitionData.FontSizes);
        wpfColor.Items.Should().Equal(FreePRibbonDefinitionData.FontColors);
        wpfFill.Items.Should().Equal(FreePRibbonDefinitionData.TableCellFillColors);
        wpfAnchor.Items.Should().Equal(FreePRibbonDefinitionData.TableCellAnchorOptions);
        wpfBorder.Items.Should().Equal(FreePRibbonDefinitionData.TableCellBorderOptions);
        wpfInset.Items.Should().Equal(FreePRibbonDefinitionData.TableCellInsetOptions);
        wpfRowHeight.Items.Should().Equal(FreePRibbonDefinitionData.TableRowHeightOptions);
        wpfTextAutoFit.Items.Should().Equal(FreePRibbonDefinitionData.TextAutoFitOptions);
        wpfTextDirection.Items.Should().Equal(FreePRibbonDefinitionData.TextVerticalTypeOptions);
        wpfTextColumns.Items.Should().Equal(FreePRibbonDefinitionData.TextColumnCountOptions);
        wpfTextColumnSpacing.Items.Should().Equal(FreePRibbonDefinitionData.TextColumnSpacingOptions);
        size.Items.Should().Equal(FreePRibbonDefinitionData.FontSizes);
        color.Items.Should().Equal(FreePRibbonDefinitionData.FontColors);
        fill.Items.Should().Equal(FreePRibbonDefinitionData.TableCellFillColors);
        anchor.Items.Should().Equal(FreePRibbonDefinitionData.TableCellAnchorOptions);
        border.Items.Should().Equal(FreePRibbonDefinitionData.TableCellBorderOptions);
        inset.Items.Should().Equal(FreePRibbonDefinitionData.TableCellInsetOptions);
        rowHeight.Items.Should().Equal(FreePRibbonDefinitionData.TableRowHeightOptions);
        textAutoFit.Items.Should().Equal(FreePRibbonDefinitionData.TextAutoFitOptions);
        textDirection.Items.Should().Equal(FreePRibbonDefinitionData.TextVerticalTypeOptions);
        textColumns.Items.Should().Equal(FreePRibbonDefinitionData.TextColumnCountOptions);
        textColumnSpacing.Items.Should().Equal(FreePRibbonDefinitionData.TextColumnSpacingOptions);
    }

    [Fact]
    public void Avalonia_profile_exposes_design_commands_as_shared_surface()
    {
        var wpfIds = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Wpf))
            .ToHashSet(StringComparer.Ordinal);
        var avaloniaIds = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Avalonia))
            .ToHashSet(StringComparer.Ordinal);
        var designIds = new[]
        {
            "freep.theme.office",
            "freep.theme.berlin",
            "freep.theme.facet",
            "freep.theme.ion",
            "freep.theme.slice",
            "freep.slide-size-16x9",
            "freep.slide-size-4x3",
            "freep.slide-size-custom",
            SmartArtAuthoringPlanner.ThemeAccentsCommandId,
            SmartArtAuthoringPlanner.SingleAccentCommandId,
            SmartArtAuthoringPlanner.GrayscaleCommandId,
        };

        avaloniaIds.Should().Contain(designIds);
        designIds.Should().OnlyContain(commandId => wpfIds.Contains(commandId));
    }

    [Fact]
    public void Avalonia_profile_exposes_transition_commands_as_shared_surface()
    {
        var wpfIds = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Wpf))
            .ToHashSet(StringComparer.Ordinal);
        var avaloniaIds = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Avalonia))
            .ToHashSet(StringComparer.Ordinal);
        var transitionIds = new[]
        {
            "freep.transition.none",
            "freep.transition.fade",
            "freep.transition.push",
            "freep.transition.wipe",
            "freep.transition.split",
            "freep.transition.box",
            "freep.transition.doors",
            "freep.transition.reveal",
            "freep.transition.flash",
            "freep.transition.morph",
            "freep.transition.cut",
            "freep.transition.cover",
            "freep.transition.uncover",
            "freep.transition.blinds",
            "freep.transition.comb",
            "freep.transition.random-bars",
            "freep.transition.strips",
            "freep.transition.wheel-reverse",
            "freep.transition.gallery",
            "freep.transition.conveyor",
            "freep.transition.pan",
            "freep.transition.window",
            "freep.transition.dissolve",
            "freep.transition.zoom",
            "freep.transition.wheel",
            "freep.transition.duration",
            "freep.transition.advance-on-click",
            "freep.transition.advance-after",
            "freep.transition.apply-all",
        };

        avaloniaIds.Should().Contain(transitionIds);
        transitionIds.Should().OnlyContain(commandId => wpfIds.Contains(commandId));
    }

    [Fact]
    public void Definition_project_stays_platform_neutral()
    {
        var project = File.ReadAllText(RepoFile("freep", "FreeP.Ribbon.Definitions", "FreeP.Ribbon.Definitions.csproj"));
        project.Should().Contain(@"..\FreeP.App.Localization\FreeP.App.Localization.csproj");
        project.Should().Contain(@"..\..\shared\Free.Shared.Ribbon\Free.Shared.Ribbon.csproj");
        project.Should().NotContain("UseWPF");
        project.Should().NotContain("Free.Shared.Ribbon.Wpf");
        project.Should().NotContain("Free.Shared.Ribbon.Avalonia");
        project.Should().NotContain("PackageReference Include=\"Avalonia");

        var sourceFiles = Directory.GetFiles(
            RepoPath("freep", "FreeP.Ribbon.Definitions"),
            "*.cs",
            SearchOption.AllDirectories);
        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            source.Should().NotContain("using System.Windows");
            source.Should().NotContain("using Avalonia");
            source.Should().NotContain("PresentationFramework");
        }
    }

    [Fact]
    public void Localized_ribbon_slices_keep_raw_english_out_of_ribbon_definition_sources()
    {
        var source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(
                    RepoPath("freep", "FreeP.Ribbon.Definitions"),
                    "*.cs",
                    SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        source.Should().Contain("FreePRibbonText");
        source.Should().NotContain("g.Medium(\"freep.layout\", \"Layout\"");
        source.Should().NotContain("tab.Group(\"clipboard\", \"Clipboard\"");
        source.Should().NotContain("g.Large(\"freep.paste\", \"Paste\"");
        source.Should().NotContain("g.Medium(\"freep.cut\", \"Cut\"");
        source.Should().NotContain("g.Medium(\"freep.copy\", \"Copy\"");
        source.Should().NotContain("g.Medium(\"freep.format-painter\", \"Format Painter\"");
        source.Should().NotContain("tab.Group(\"font\", \"Font\"");
        source.Should().NotContain("g.ComboBox(\"freep.font-family\", \"Font\"");
        source.Should().NotContain("g.IconToggle(\"freep.bold\", \"Bold\"");
        source.Should().NotContain("g.IconToggle(\"freep.italic\", \"Italic\"");
        source.Should().NotContain("g.IconToggle(\"freep.underline\", \"Underline\"");
        source.Should().NotContain("g.IconToggle(\"freep.superscript\", \"Superscript\"");
        source.Should().NotContain("g.IconToggle(\"freep.subscript\", \"Subscript\"");
        source.Should().NotContain("tab.Group(\"editing\", \"Editing\"");
        source.Should().NotContain("g.Large(\"freep.find\",    \"Find\"");
        source.Should().NotContain("g.Medium(\"freep.replace\", \"Replace\"");
        source.Should().Contain("FreePRibbonText.LayoutLabel");
        source.Should().Contain("FreePRibbonText.ClipboardGroupLabel");
        source.Should().Contain("FreePRibbonText.PasteLabel");
        source.Should().Contain("FreePRibbonText.FontGroupLabel");
        source.Should().Contain("FreePRibbonText.EditingGroupLabel");
        source.Should().Contain("FreePRibbonText.FindLabel");
        source.Should().Contain("FreePRibbonText.ArrangeGroup");
        source.Should().Contain("FreePRibbonText.DesignTab");
        source.Should().Contain("FreePRibbonText.TransitionsTab");
        source.Should().Contain("FreePRibbonText.AnimationsTab");
        source.Should().Contain("FreePRibbonText.ViewTab");
        source.Should().Contain("FreePRibbonText.ViewShowGroup");
        source.Should().Contain("FreePRibbonText.ViewGridlinesCommand");
        source.Should().Contain("FreePRibbonText.ViewGuidesCommand");
        source.Should().Contain("FreePRibbonText.ViewZoomGroup");
        source.Should().Contain("FreePRibbonText.ViewZoomCommand");
        source.Should().Contain("FreePRibbonText.ViewFitToWindowCommand");
        source.Should().NotContain("tab.Group(\"arrange\", \"Arrange\"");
        source.Should().NotContain(".Tab(\"design\", \"Design\"");
        source.Should().NotContain(".Tab(\"transitions\", \"Transitions\"");
        source.Should().NotContain(".Tab(\"animations\", \"Animations\"");
        source.Should().NotContain(".Tab(\"view\", \"View\"");
        source.Should().NotContain("tab.Group(\"show\", \"Show\"");
        source.Should().NotContain("g.MediumToggle(\"freep.view.show.gridlines\", \"Gridlines\"");
        source.Should().NotContain("g.MediumToggle(\"freep.view.show.guides\", \"Guides\"");
        source.Should().NotContain("tab.Group(\"zoom\", \"Zoom\"");
        source.Should().NotContain("g.Large(\"freep.view.zoom\", \"Zoom...\"");
        source.Should().NotContain("g.Medium(\"freep.view.fit-to-window\", \"Fit to Window\"");
        source.Should().NotContain("g.Medium(\"freep.transition.fade\",     \"Fade\"");
        source.Should().NotContain("g.Medium(\"freep.anim.none\", \"No Animation\"");
        foreach (var literal in new[]
                 {
                     "\"Home\"",
                     "\"File\"",
                     "\"New\"",
                     "\"Open\"",
                     "\"Save\"",
                     "\"Save As\"",
                     "\"Slides\"",
                     "\"New Slide\"",
                     "\"Duplicate Slide\"",
                     "\"Delete Slide\"",
                     "\"Edit\"",
                     "\"Undo\"",
                     "\"Redo\"",
                     "\"Slide Show\"",
                     "\"From Beginning\"",
                     "\"From Current Slide\"",
                     "\"Custom Shows\"",
                     "\"Insert\"",
                     "\"Text\"",
                     "\"Text Box\"",
                     "\"Tables\"",
                     "\"Table\"",
                     "\"2x2\"",
                     "\"2×2\"",
                     "\"4x4\"",
                     "\"4×4\"",
                     "\"Charts\"",
                     "\"Column\"",
                     "\"Bar\"",
                     "\"Line\"",
                     "\"Pie\"",
                     "\"Edit Data\"",
                     "\"Links\"",
                     "\"Hyperlink\"",
                     "\"Remove Link\"",
                     "\"Illustrations\"",
                     "\"Picture\"",
                     "\"Rectangle\"",
                     "\"Ellipse\"",
                     "\"View\"",
                     "\"Show\"",
                     "\"Zoom\"",
                     "\"Zoom...\"",
                     "\"Fit to Window\"",
                     "\"Gridlines\"",
                     "\"Guides\""
                 })
        {
            source.Should().NotContain(literal);
        }
    }

    [Fact]
    public void Shared_catalog_keeps_common_profile_order_and_labels_identical()
    {
        var wpf = FreePRibbon.Build(FreePRibbonCapabilities.Wpf);
        var avalonia = FreePRibbon.Build(FreePRibbonCapabilities.Avalonia);

        foreach (var tabId in new[] { "insert", "design", "transitions", "animations", "view" })
        {
            var wpfTab = wpf.FindTab(tabId)!;
            var avaloniaTab = avalonia.FindTab(tabId)!;
            var commonWpfGroups = wpfTab.Groups.ToArray();
            var commonAvaloniaGroups = avaloniaTab.Groups.ToArray();
            commonAvaloniaGroups.Select(group => group.Id).Should().Equal(commonWpfGroups.Select(group => group.Id));

            foreach (var (wpfGroup, avaloniaGroup) in commonWpfGroups.Zip(commonAvaloniaGroups))
            {
                avaloniaGroup.Header.Should().Be(wpfGroup.Header);
                avaloniaGroup.Controls
                    .Where(control => !string.IsNullOrEmpty(control.CommandId.Value))
                    .Select(control => (control.CommandId.Value, control.Label))
                    .Should()
                    .Equal(wpfGroup.Controls
                        .Where(control => !string.IsNullOrEmpty(control.CommandId.Value))
                        .Select(control => (control.CommandId.Value, control.Label)));
            }
        }

        var commonHomeGroupIds = new[] { "clipboard", "font", "paragraph", "arrange", "editing" };
        foreach (var groupId in commonHomeGroupIds)
        {
            var wpfGroup = RequiredGroup(wpf, "home", groupId);
            var avaloniaGroup = RequiredGroup(avalonia, "home", groupId);
            avaloniaGroup.Header.Should().Be(wpfGroup.Header);
            avaloniaGroup.Controls.Select(control => (control.CommandId.Value, control.Label))
                .Should()
                .Equal(wpfGroup.Controls.Select(control => (control.CommandId.Value, control.Label)));
        }
    }

    [Fact]
    public void App_adapters_delegate_to_shared_definition_without_local_builders()
    {
        var host = File.ReadAllText(RepoFile("freep", "FreeP.App.Host", "FreePRibbon.cs"));
        host.Should().Contain("FreeP.Ribbon.Definitions.FreePRibbon.Build(FreePRibbonCapabilities.Wpf)");
        host.Should().NotContain("new RibbonDefinitionBuilder");

        var avalonia = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "FreePRibbonAvalonia.cs"));
        avalonia.Should().Contain("FreeP.Ribbon.Definitions.FreePRibbon.Build(FreePRibbonCapabilities.Avalonia)");
        avalonia.Should().NotContain("new RibbonDefinitionBuilder");
    }

    [Fact]
    public void App_projects_reference_shared_definition_project()
    {
        File.ReadAllText(RepoFile("freep", "FreeP.App.Host", "FreeP.App.Host.csproj"))
            .Should()
            .Contain(@"..\FreeP.Ribbon.Definitions\FreeP.Ribbon.Definitions.csproj");
        File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj"))
            .Should()
            .Contain(@"..\FreeP.Ribbon.Definitions\FreeP.Ribbon.Definitions.csproj");
    }

    [Fact]
    public void FreeP_command_parity_inventory_json_matches_generated_profiles()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(RepoFile(
            "docs",
            "parity",
            "freep-command-parity-inventory.json")));

        var root = json.RootElement;
        root.GetProperty("schemaVersion").GetInt32().Should().Be(1);

        var expectedSurfaces = ExpectedCommandSurfaces();
        var commands = root.GetProperty("commands")
            .EnumerateArray()
            .ToDictionary(
                command => command.GetProperty("commandId").GetString()
                    ?? throw new InvalidOperationException("Command entry is missing commandId."),
                StringComparer.Ordinal);

        commands.Keys.Should().BeEquivalentTo(expectedSurfaces.Keys);
        foreach (var (commandId, expectedSurface) in expectedSurfaces)
        {
            commands[commandId].GetProperty("surface").GetString()
                .Should()
                .Be(expectedSurface, $"'{commandId}' should match the generated FreeP ribbon profiles");
        }

        commands.Values.Select(command => command.GetProperty("surface").GetString())
            .Should()
            .OnlyContain(surface => surface == "both");
        commands.Values.Select(command => command.GetProperty("classification").GetString())
            .Should()
            .OnlyContain(classification => classification == "shared");

        var platformOnlyRows = commands.Values
            .Where(command => command.GetProperty("classification").GetString() == "platform-only")
            .ToArray();
        platformOnlyRows.Select(command => command.GetProperty("commandId").GetString())
            .Should()
            .BeEquivalentTo(PlatformOnlyShellCommandEvidence.Keys);
        foreach (var platformOnlyRow in platformOnlyRows)
        {
            var commandId = platformOnlyRow.GetProperty("commandId").GetString()
                ?? throw new InvalidOperationException("Platform-only row is missing commandId.");
            var notes = platformOnlyRow.GetProperty("notes").GetString()
                ?? throw new InvalidOperationException($"Platform-only row '{commandId}' is missing notes.");

            platformOnlyRow.GetProperty("missingSide").GetString()
                .Should()
                .Be("WPF", $"'{commandId}' is an Avalonia shell/profile command");
            platformOnlyRow.GetProperty("wpfPresent").GetBoolean().Should().BeFalse();
            platformOnlyRow.GetProperty("avaloniaPresent").GetBoolean().Should().BeTrue();
            foreach (var fragment in PlatformOnlyShellCommandEvidence[commandId])
            {
                notes.Should().Contain(fragment, $"'{commandId}' must cite the intended shell variance and WPF route");
            }
        }
        root.GetProperty("summary").GetProperty("platformOnly").GetInt32()
            .Should()
            .Be(PlatformOnlyShellCommandEvidence.Count);

        root.GetProperty("summary").GetProperty("missingAvalonia").GetInt32()
            .Should()
            .Be(0);

        root.GetProperty("summary").GetProperty("actionableMissingWpf").GetInt32()
            .Should()
            .Be(0, "platform-only Avalonia shell commands are not actionable WPF parity gaps");

        root.GetProperty("summary").GetProperty("actionableMissingAvalonia").GetInt32()
            .Should()
            .Be(0);

        root.GetProperty("summary").GetProperty("knownDeferred").GetInt32()
            .Should()
            .Be(0);

        root.GetProperty("summary").GetProperty("commandIdAliases").GetInt32()
            .Should()
            .Be(0);

        root.GetProperty("summary").GetProperty("totalCommands").GetInt32()
            .Should()
            .Be(expectedSurfaces.Count);

        var workflowEvidence = root.GetProperty("workflowEvidence")
            .EnumerateArray()
            .ToArray();
        var expectedWorkflowEvidenceIds = new[]
        {
            "freep.presenter.recording.execution",
            "freep.presenter.recording.default-camera-encoding-readiness",
            "freep.presenter.recording.unavailable-hardware-readiness",
            "freep.media-caption.native-sidecar-depth",
            "freep.presenter.ink.execution",
            "freep.presenter.session.summary",
            "freep.review.comments.thread-depth",
            "freep.review.accessibility.proofing-depth",
            "freep.animation-pane.workflow-depth",
            "freep.export.backstage.package-handoff",
            "freep.export.pdf-visual-baseline-readiness",
            "freep.export.pdf-ellipse-fixed-layout",
            "freep.export.pdf-picture-frame-clips",
            "freep.export.pdf-shape-opacity",
            "freep.table.inline-text.workflow-depth",
            "freep.clipboard.external-rtf-depth",
            "freep.header-footer.placeholder-creation",
            "freep.chart.number-format-rendering",
            "freep.chart.edge-manual-layout",
            "freep.chart.bar-gap-overlap",
            "freep.chart.data-label-text-style",
            "freep.chart.bubble-size-data-labels",
            "freep.chart.series-data-labels",
            "freep.chart.point-data-labels",
            "freep.chart.bubble-sizing-semantics",
            "freep.chart.pie-first-slice-angle",
            "freep.chart.pie3d-depth-rendering",
            "freep.chart.blank-point-rendering",
            "freep.chart.stacked-area-bands",
            "freep.chart.surface-grid-rendering",
            "freep.chart.radar-style-render-planning",
            "freep.chart.powerpoint-baseline-readiness",
            "freep.chart.stock-ohlc-baseline-readiness",
            "freep.chart.stock-volume-baseline-readiness",
            "freep.chart.doughnut-ring-baseline-readiness",
            "freep.omml.transparent-phantom-spacing",
            "freep.omml.box-operator-emulator-spacing",
            "freep.omml.accent-bar-render-plan",
            "freep.omml.radical-degree-layout",
            "freep.omml.fraction-type",
            "freep.omml.manual-break-alignment",
            "freep.omml.box-alignment-points",
            "freep.omml.eqarray-spacing-base-justification",
            "freep.omml.paragraph-justification",
            "freep.omml.delimiter-shape",
            "freep.omml.delimiter-separator",
            "freep.omml.groupchr-vertical-justification",
            "freep.omml.pre-subsup-layout",
            "freep.omml.script-align-argument-size",
            "freep.omml.matrix-spacing-base-justification",
            "freep.omml.matrix-placeholder",
            "freep.omml.matrix-column-count-alignment",
            "freep.omml.literal-run-style",
            "freep.omml.math-alphabet-style",
            "freep.omml.math-font",
            "freep.omml.math-default-inheritance",
            "freep.omml.nary-limit-location",
            "freep.omml.nary-grow-hidden-limits",
            "freep.omml.limit-placement",
            "freep.omml.scripted-function-name",
            "freep.omml.border-box-side-strike-lines",
            "freep.smartart.continuous-block-process",
            "freep.smartart.grouped-list-import-bands",
            "freep.smartart.relationship1-import-ellipses",
            "freep.smartart.grid-matrix-import-cells",
            "freep.smartart.increasing-circle-process-import-growth",
            "freep.smartart.vertical-arrow-list-import-slots",
            "freep.smartart.list1-import-slots",
            "freep.smartart.process1-import-node-connectors",
            "freep.smartart.basic-process",
            "freep.smartart.basic-timeline",
            "freep.smartart.step-down-process",
            "freep.smartart.basic-radial",
            "freep.smartart.segmented-process",
            "freep.smartart.chevron-process",
            "freep.smartart.bending-process",
            "freep.smartart.alternating-process",
            "freep.smartart.funnel-process",
            "freep.smartart.vertical-process",
            "freep.smartart.circle-process",
            "freep.smartart.arrow-ribbon",
            "freep.smartart.basic-block-list",
            "freep.smartart.vertical-box-list",
            "freep.smartart.stacked-list",
            "freep.smartart.descending-block-list",
            "freep.smartart.basic-pyramid",
            "freep.smartart.picture-caption-list",
            "freep.smartart.basic-cycle",
            "freep.smartart.radial-cycle",
            "freep.smartart.radial-list",
            "freep.smartart.gear-cycle",
            "freep.smartart.text-cycle",
            "freep.smartart.block-cycle",
            "freep.smartart.nondirectional-cycle",
            "freep.smartart.basic-matrix",
            "freep.smartart.titled-matrix",
            "freep.smartart.basic-venn",
            "freep.smartart.radial-venn",
            "freep.smartart.target-list",
            "freep.smartart.stacked-venn",
            "freep.smartart.vertical-bullet-list",
            "freep.smartart.basic-hierarchy",
            "freep.smartart.hierarchy3",
            "freep.smartart.horizontal-hierarchy",
            "freep.smartart.labeled-hierarchy",
            "freep.smartart.table-hierarchy",
            "freep.smartart.org-chart",
            "freep.smartart.outline-editing",
            "freep.smartart.data-part-authoring",
            "freep.smartart.text-pane-cache-authoring",
        };

        workflowEvidence.Should().HaveCount(expectedWorkflowEvidenceIds.Length);
        root.GetProperty("summary").GetProperty("workflowEvidenceRows").GetInt32()
            .Should()
            .Be(workflowEvidence.Length);
        workflowEvidence.Select(row => row.GetProperty("evidenceId").GetString())
            .Should()
            .Equal(expectedWorkflowEvidenceIds);

        workflowEvidence.Should().OnlyContain(row =>
            row.GetProperty("status").GetString()!.StartsWith("shared-", StringComparison.Ordinal) &&
            row.GetProperty("hostCoverage").GetString()!.Contains("WPF/Avalonia", StringComparison.Ordinal));
        workflowEvidence.Should().OnlyContain(
            row => WorkflowResidualLooksExternal(row),
            "workflow residuals should read as external PowerPoint/device/backend scope, not unresolved WPF/Avalonia command parity");

        var presenterRecording = workflowEvidence.Single(row =>
            row.GetProperty("evidenceId").GetString() == "freep.presenter.recording.execution");
        presenterRecording.GetProperty("verification")
            .EnumerateArray()
            .Select(path => path.GetString())
            .Should()
            .Contain("freep/FreeP.App.Host.Tests/MediaFieldsTests.cs");
        presenterRecording.GetProperty("remainingWork").GetString()
            .Should()
            .Contain("external-link");
        presenterRecording.GetProperty("remainingWork").GetString()
            .Should()
            .Contain("basic TTML/DFXP cue parsing");

        var mediaCaptionDepth = workflowEvidence.Single(row =>
            row.GetProperty("evidenceId").GetString() == "freep.media-caption.native-sidecar-depth");
        mediaCaptionDepth.GetProperty("remainingWork").GetString()
            .Should()
            .Contain("shared planner resolves TTML/DFXP inherited body/div begin/end/dur boundaries");

        var chartBaseline = workflowEvidence.Single(row =>
            row.GetProperty("evidenceId").GetString() == "freep.chart.powerpoint-baseline-readiness");
        chartBaseline.GetProperty("evidenceDocs")
            .EnumerateArray()
            .Select(path => path.GetString())
            .Should()
            .Contain("docs/parity/freep-chart-powerpoint-com-baseline-20260720.md");
        chartBaseline.GetProperty("remainingWork").GetString()
            .Should()
            .Contain("fresh COM PNGs");

        var presenterSummary = workflowEvidence.Single(row =>
            row.GetProperty("evidenceId").GetString() == "freep.presenter.session.summary");
        presenterSummary.GetProperty("verification")
            .EnumerateArray()
            .Select(path => path.GetString())
            .Should()
            .Contain("freep/FreeP.App.Presentation.Tests/SlideShowPresenterSessionSummaryPlannerTests.cs");

        var tableInlineText = workflowEvidence.Single(row =>
            row.GetProperty("evidenceId").GetString() == "freep.table.inline-text.workflow-depth");
        tableInlineText.GetProperty("area").GetString()
            .Should()
            .Contain("table-cell text editing");
        var tableInlineTextVerification = tableInlineText.GetProperty("verification")
            .EnumerateArray()
            .Select(path => path.GetString())
            .ToArray();
        tableInlineTextVerification.Should().Contain("freep/FreeP.App.Presentation.Tests/TableCellEditPlannerTests.cs");
        tableInlineTextVerification.Should().Contain("freep/FreeP.App.Presentation.Tests/InCanvasRichClipboardTests.cs");
        tableInlineTextVerification.Should().Contain("freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasAvaloniaTests.cs");
        tableInlineTextVerification.Should().Contain("freep/FreeP.App.Host.Tests/WpfRichTextClipboardAdapterTests.cs");
        tableInlineTextVerification.Should().Contain("freep/FreeP.App.Host.Tests/CanvasEditingTests.cs");
        tableInlineText.GetProperty("evidenceDocs")
            .EnumerateArray()
            .Select(path => path.GetString())
            .Should()
            .Contain("docs/parity/freep-rich-clipboard-wave15-20260727.md");
        tableInlineText.GetProperty("remainingWork").GetString()
            .Should()
            .NotContain("rich clipboard formats");

        var externalRtfDepth = workflowEvidence.Single(row =>
            row.GetProperty("evidenceId").GetString() == "freep.clipboard.external-rtf-depth");
        externalRtfDepth.GetProperty("area").GetString()
            .Should()
            .Contain("External RTF");
        externalRtfDepth.GetProperty("verification").EnumerateArray()
            .Select(path => path.GetString())
            .Should()
            .Contain("freep/FreeP.App.Presentation.Tests/ExternalRichTextClipboardTests.cs");
        externalRtfDepth.GetProperty("evidenceDocs").EnumerateArray()
            .Select(path => path.GetString())
            .Should()
            .Contain("docs/parity/freep-external-rtf-paste-wave18-20260727.md");
        externalRtfDepth.GetProperty("remainingWork").GetString()
            .Should()
            .Contain("XamlPackage");

        var animationPane = workflowEvidence.Single(row =>
            row.GetProperty("evidenceId").GetString() == "freep.animation-pane.workflow-depth");
        animationPane.GetProperty("area").GetString()
            .Should()
            .Contain("Animation pane row workflow");
        var animationPaneVerification = animationPane.GetProperty("verification")
            .EnumerateArray()
            .Select(path => path.GetString())
            .ToArray();
        animationPaneVerification.Should().Contain("freep/FreeP.App.Presentation.Tests/AnimationPanePlannerTests.cs");
        animationPaneVerification.Should().Contain("freep/FreeP.App.Presentation.Tests/SlideShowPlaybackPlannerTests.cs");
        animationPaneVerification.Should().Contain("freep/FreeP.App.Host.Tests/AnimationPaneTests.cs");
        animationPaneVerification.Should().Contain("freep/FreeP.App.Host.Tests/SlideShowHostPolicySourceTests.cs");
        animationPaneVerification.Should().Contain("freep/FreeP.App.Avalonia.Tests/SlideShowHostPolicySourceTests.cs");
        animationPaneVerification.Should().Contain("freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs");
    }

    [Fact]
    public void FreeP_command_parity_inventory_markdown_matches_json_matrix()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(RepoFile(
            "docs",
            "parity",
            "freep-command-parity-inventory.json")));
        var markdown = File.ReadAllText(RepoFile(
            "docs",
            "parity",
            "freep-command-parity-inventory.md"));

        var summary = json.RootElement.GetProperty("summary");
        var expectedSummaryRow =
            $"| {summary.GetProperty("totalCommands").GetInt32()} | " +
            $"{summary.GetProperty("both").GetInt32()} | " +
            $"{summary.GetProperty("wpfOnly").GetInt32()} | " +
            $"{summary.GetProperty("avaloniaOnly").GetInt32()} | " +
            $"{summary.GetProperty("missingWpf").GetInt32()} | " +
            $"{summary.GetProperty("missingAvalonia").GetInt32()} | " +
            $"{summary.GetProperty("actionableMissingWpf").GetInt32()} | " +
            $"{summary.GetProperty("actionableMissingAvalonia").GetInt32()} | " +
            $"{summary.GetProperty("shared").GetInt32()} | " +
            $"{summary.GetProperty("avaloniaGaps").GetInt32()} | " +
            $"{summary.GetProperty("knownDeferred").GetInt32()} | " +
            $"{summary.GetProperty("platformOnly").GetInt32()} | " +
            $"{summary.GetProperty("commandIdAliases").GetInt32()} | " +
            $"{summary.GetProperty("workflowEvidenceRows").GetInt32()} |";

        markdown.Should().Contain("Generated by `tools/Generate-FreePCommandParityInventory.ps1`");
        markdown.Should().Contain("Actionable missing counts exclude platform-only commands");
        markdown.Should().Contain("## Workflow Evidence");
        markdown.Should().Contain("`freep.presenter.recording.execution`");
        markdown.Should().Contain("`freep.presenter.ink.execution`");
        markdown.Should().Contain("`freep.presenter.session.summary`");
        markdown.Should().Contain("`freep.animation-pane.workflow-depth`");
        markdown.Should().Contain("`freep.table.inline-text.workflow-depth`");
        markdown.Should().Contain("external-link");
        markdown.Should().Contain("`freep/FreeP.App.Host.Tests/MediaFieldsTests.cs`");
        markdown.Should().Contain("Animation pane row workflow");
        markdown.Should().Contain("Rich inline table-cell text editing");
        markdown.Should().Contain(expectedSummaryRow);

        var expectedCommandIds = json.RootElement.GetProperty("commands")
            .EnumerateArray()
            .Select(command => command.GetProperty("commandId").GetString())
            .Where(commandId => commandId is not null)
            .Cast<string>()
            .OrderBy(commandId => commandId, StringComparer.Ordinal)
            .ToArray();
        var matrixMarkdown = markdown[
            markdown.IndexOf("## Matrix", StringComparison.Ordinal)..];
        var markdownCommandIds = matrixMarkdown.Split(Environment.NewLine)
            .Where(line => line.StartsWith("| `freep.", StringComparison.Ordinal))
            .Select(line =>
            {
                var start = line.IndexOf('`') + 1;
                var end = line.IndexOf('`', start);
                return line[start..end];
            })
            .ToArray();

        markdownCommandIds.Should().Equal(expectedCommandIds);
    }

    private static bool IsAllowedAvaloniaProfileCommand(string commandId) =>
        PlatformOnlyShellCommandEvidence.ContainsKey(commandId);

    private static bool WorkflowResidualLooksExternal(JsonElement row)
    {
        var remainingWork = row.GetProperty("remainingWork").GetString()!;
        return remainingWork.Contains("PowerPoint", StringComparison.Ordinal) ||
            remainingWork.Contains("capture", StringComparison.Ordinal) ||
            remainingWork.Contains("notification routing", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ExpectedCommandSurfaces()
    {
        var wpf = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Wpf))
            .ToHashSet(StringComparer.Ordinal);
        var avalonia = CommandIds(FreePRibbon.Build(FreePRibbonCapabilities.Avalonia))
            .ToHashSet(StringComparer.Ordinal);

        return wpf.Concat(avalonia)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                commandId => commandId,
                commandId => wpf.Contains(commandId) && avalonia.Contains(commandId)
                    ? "both"
                    : wpf.Contains(commandId)
                        ? "wpf-only"
                        : "avalonia-only",
                StringComparer.Ordinal);
    }

    private static IEnumerable<string> CommandIds(RibbonDefinition definition)
    {
        foreach (var tab in definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                {
                    if (!string.IsNullOrEmpty(control.CommandId.Value))
                        yield return control.CommandId.Value;

                    foreach (var menuCommandId in MenuCommandIds(control))
                        yield return menuCommandId;
                }
            }
        }
    }

    private static IEnumerable<string> MenuCommandIds(RibbonControl control)
    {
        var menu = control switch
        {
            RibbonSplitButton splitButton => splitButton.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };

        return menu is null
            ? Array.Empty<string>()
            : MenuCommandIds(menu.Items);
    }

    private static IEnumerable<string> MenuCommandIds(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } commandId)
                yield return commandId.Value;

            foreach (var childCommandId in MenuCommandIds(item.Children))
                yield return childCommandId;
        }
    }

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

    private static RibbonGroup RequiredGroup(RibbonDefinition definition, string tabId, string groupId) =>
        definition.FindTab(tabId)?.FindGroup(groupId)
        ?? throw new InvalidOperationException($"Could not find ribbon group '{tabId}/{groupId}'.");

    private static RibbonControl RequiredControl(RibbonDefinition definition, string commandId)
    {
        foreach (var tab in definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                {
                    if (string.Equals(control.CommandId.Value, commandId, StringComparison.Ordinal))
                    {
                        return control;
                    }
                }
            }
        }

        throw new InvalidOperationException($"Could not find ribbon control '{commandId}'.");
    }

    private static RibbonComboBox RequiredCombo(RibbonDefinition definition, string commandId) =>
        RequiredControl(definition, commandId) as RibbonComboBox
        ?? throw new InvalidOperationException($"Ribbon control '{commandId}' is not a combo box.");

    private static IEnumerable<string> ControlText(RibbonDefinition definition, params string[] commandIds)
    {
        foreach (var commandId in commandIds)
        {
            var control = RequiredControl(definition, commandId);
            yield return control.Label;
            if (control.KeyTip is { } keyTip)
                yield return keyTip;
        }
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine(RepoRoot(), Path.Combine(parts));

    private static string RepoPath(params string[] parts) =>
        Path.Combine(RepoRoot(), Path.Combine(parts));

    private static string RepoRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
}
