using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed class CommandBusHistoryTests
{
    private static (Workbook Workbook, CommandBus Bus) CreateHarness()
    {
        var workbook = new Workbook("history-test");
        workbook.AddSheet("Sheet1");
        return (workbook, new CommandBus(_ => new TestCommandContext(workbook)));
    }

    [Fact]
    public void GetUndoHistory_ReturnsNewestLabelsFirstAndHonorsMaxCount()
    {
        var (workbook, bus) = CreateHarness();

        bus.Execute(workbook.Id, new LabelledCommand("First"));
        bus.Execute(workbook.Id, new LabelledCommand("Second"));
        bus.Execute(workbook.Id, new LabelledCommand("Third"));

        bus.GetUndoHistory(workbook.Id, 2)
            .Select(entry => entry.Label)
            .Should()
            .Equal("Third", "Second");

        bus.GetUndoHistory(workbook.Id, 0).Should().BeEmpty();
    }

    [Fact]
    public void GetRedoHistory_ReturnsNextRedoLabelsFirstAndClearsAfterNewCommand()
    {
        var (workbook, bus) = CreateHarness();

        bus.Execute(workbook.Id, new LabelledCommand("First"));
        bus.Execute(workbook.Id, new LabelledCommand("Second"));
        bus.Execute(workbook.Id, new LabelledCommand("Third"));
        bus.Undo(workbook.Id);
        bus.Undo(workbook.Id);

        bus.GetRedoHistory(workbook.Id, 10)
            .Select(entry => entry.Label)
            .Should()
            .Equal("Second", "Third");

        bus.Execute(workbook.Id, new LabelledCommand("Fourth"));

        bus.GetRedoHistory(workbook.Id, 10).Should().BeEmpty();
    }

    [Fact]
    public void History_SnapshotsLabelsAndFallsBackToCommandTypeName()
    {
        var (workbook, bus) = CreateHarness();
        var command = new MutableLabelCommand("Before");

        bus.Execute(workbook.Id, command);
        bus.Execute(workbook.Id, new BlankLabelCommand());
        command.LabelValue = "After";

        bus.GetUndoHistory(workbook.Id, 10)
            .Select(entry => entry.Label)
            .Should()
            .Equal("BlankLabelCommand", "Before");
    }

    private sealed class LabelledCommand(string label) : IWorkbookCommand
    {
        public string Label => label;
        public CommandOutcome Apply(ICommandContext ctx) => new(true);
        public void Revert(ICommandContext ctx) { }
    }

    private sealed class MutableLabelCommand(string label) : IWorkbookCommand
    {
        public string LabelValue { get; set; } = label;
        public string Label => LabelValue;
        public CommandOutcome Apply(ICommandContext ctx) => new(true);
        public void Revert(ICommandContext ctx) { }
    }

    private sealed class BlankLabelCommand : IWorkbookCommand
    {
        public string Label => "   ";
        public CommandOutcome Apply(ICommandContext ctx) => new(true);
        public void Revert(ICommandContext ctx) { }
    }
}
