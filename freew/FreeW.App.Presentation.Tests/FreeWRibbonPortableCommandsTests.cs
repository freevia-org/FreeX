using System.Globalization;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRibbonPortableCommandsTests
{
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
