using System.Globalization;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRibbonPortableCommandsTests
{
    [Fact]
    public void Semantic_catalog_owns_quick_style_and_header_footer_slot_mappings()
    {
        FreeWRibbonSemanticCatalog.QuickStyles.Should().Equal(
            new FreeWRibbonQuickStyleBinding(FreeWRibbonCommandAction.StyleNormal, "Normal"),
            new FreeWRibbonQuickStyleBinding(FreeWRibbonCommandAction.StyleHeading1, "Heading1"),
            new FreeWRibbonQuickStyleBinding(FreeWRibbonCommandAction.StyleHeading2, "Heading2"),
            new FreeWRibbonQuickStyleBinding(FreeWRibbonCommandAction.StyleHeading3, "Heading3"),
            new FreeWRibbonQuickStyleBinding(FreeWRibbonCommandAction.StyleTitle, "Title"));
        FreeWRibbonSemanticCatalog.HeaderFooterEditSlots.Select(binding => binding.Slot)
            .Should().Equal(
                HeaderFooterSlotKind.Header,
                HeaderFooterSlotKind.Footer,
                HeaderFooterSlotKind.EvenHeader,
                HeaderFooterSlotKind.EvenFooter,
                HeaderFooterSlotKind.FirstHeader,
                HeaderFooterSlotKind.FirstFooter);
        FreeWRibbonSemanticCatalog.HeaderFooterNavigationSlots.Should().Equal(
            new FreeWRibbonHeaderFooterSlotBinding(FreeWRibbonCommandAction.HfGoToHeader, HeaderFooterSlotKind.Header),
            new FreeWRibbonHeaderFooterSlotBinding(FreeWRibbonCommandAction.HfGoToFooter, HeaderFooterSlotKind.Footer));
    }

    [Fact]
    public void Numeric_value_command_parses_current_and_legacy_values_and_reports_state()
    {
        var applied = new List<double>();
        var prepared = 0;
        var command = new FreeWRibbonNumericValueCommand(
            applied.Add,
            () => 1.15,
            minimumExclusive: 0,
            prepareExecution: () => prepared++);

        command.Execute(RibbonCommandContext.ForSelectedValue("1.5"));
        command.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "2" }));
        command.Execute(RibbonCommandContext.ForSelectedValue("0"));
        command.Execute(RibbonCommandContext.ForSelectedValue("invalid"));

        applied.Should().Equal(1.5, 2);
        prepared.Should().Be(2);
        command.GetState().Value.Should().Be("1.15");

        var strict = new FreeWRibbonNumericValueCommand(
            applied.Add,
            () => 1,
            minimumExclusive: 0,
            numberStyles: NumberStyles.Float | NumberStyles.AllowThousands);
        strict.Execute(RibbonCommandContext.ForSelectedValue("$3"));
        applied.Should().Equal(1.5, 2);
    }

    [Fact]
    public void Numeric_value_command_honors_the_explicit_input_culture()
    {
        var applied = new List<double>();
        var command = new FreeWRibbonNumericValueCommand(
            applied.Add,
            () => 1,
            minimumExclusive: 0,
            numberStyles: NumberStyles.Float,
            culture: CultureInfo.GetCultureInfo("fr-FR"));

        command.Execute(RibbonCommandContext.ForSelectedValue("12,5"));
        command.Execute(RibbonCommandContext.ForSelectedValue("12.5"));

        applied.Should().Equal(12.5);
    }

    [Fact]
    public void Typed_numeric_parser_owns_font_position_and_size_payloads()
    {
        var invariant = CultureInfo.InvariantCulture;

        FreeWRibbonNumericValueParser.TryParseFontSize(
                "10.5",
                invariant,
                NumberStyles.Float,
                out var fontSize)
            .Should().BeTrue();
        fontSize.Should().Be(10.5);
        FreeWRibbonNumericValueParser.TryParseFontSize(
                "0",
                invariant,
                NumberStyles.Float,
                out _)
            .Should().BeFalse();

        FreeWRibbonNumericValueParser.TryParseObjectPosition(
                "12.5, -3, Page, Margin",
                invariant,
                out var position)
            .Should().BeTrue();
        position.Should().Be(new FreeWRibbonObjectPositionInput(
            12.5,
            -3,
            HorizontalAnchor.Page,
            VerticalAnchor.Margin));

        FreeWRibbonNumericValueParser.TryParseObjectSize(
                "120, 80",
                invariant,
                out var objectSize)
            .Should().BeTrue();
        objectSize.Should().Be(new FreeWRibbonSizeInput(120, 80));

        FreeWRibbonNumericValueParser.TryParseChartSize(
                "360 x 240",
                invariant,
                out var chartSize)
            .Should().BeTrue();
        chartSize.Should().Be(new FreeWRibbonSizeInput(360, 240));
        FreeWRibbonNumericValueParser.TryParseChartSize(
                "360 x 0",
                invariant,
                out _)
            .Should().BeFalse();

        FreeWRibbonNumericValueParser.FormatInvariant(10.5).Should().Be("10.5");
    }

    [Fact]
    public void Choice_command_normalizes_context_and_notifies_with_updated_state()
    {
        var value = "APA";
        RibbonCommandState? changed = null;
        var command = new FreeWRibbonChoiceCommand(
            selected => value = selected,
            () => value,
            state => changed = state);

        command.Execute(new RibbonCommandContext(
            new Dictionary<string, object?> { ["value"] = "MLA" }));

        value.Should().Be("MLA");
        changed.Should().Be(new RibbonCommandState(Value: "MLA"));
        command.GetState().Value.Should().Be("MLA");
    }

    [Fact]
    public void Stateful_port_command_guards_disabled_execution_and_preserves_prepare_order()
    {
        var enabled = false;
        var calls = new List<string>();
        var command = new FreeWRibbonStatefulPortCommand(
            _ => calls.Add("execute"),
            () => new RibbonCommandState(IsEnabled: enabled),
            () => calls.Add("prepare"));

        command.Execute(RibbonCommandContext.Empty);
        calls.Should().BeEmpty();

        enabled = true;
        command.Execute(RibbonCommandContext.Empty);

        calls.Should().Equal("prepare", "execute");
    }

    [Fact]
    public void Format_painter_command_owns_single_and_double_activation_semantics()
    {
        var activations = new List<bool>();
        var command = new FreeWRibbonFormatPainterCommand(activations.Add);

        command.Execute(RibbonCommandContext.Empty);
        command.Execute(RibbonCommandContext.Empty);

        activations.Should().Equal(false, true);
    }
}
