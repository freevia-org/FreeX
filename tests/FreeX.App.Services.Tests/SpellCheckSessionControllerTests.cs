using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class SpellCheckSessionControllerTests
{
    [Fact]
    public void Start_ProducesRendererNeutralIssueContextAndTarget()
    {
        var adapter = CreateAdapter("Please fix teh value.");

        var transition = new SpellCheckSessionController(adapter).Start();

        transition.Status.Should().Be(SpellCheckSessionStatus.Reviewing);
        transition.Issue.Should().NotBeNull();
        transition.Issue!.Word.Should().Be("teh");
        transition.Issue.Suggestion.Should().Be("the");
        transition.Issue.SheetName.Should().Be("Sheet1");
        transition.Issue.CellReference.Should().Be("A1");
        transition.Issue.ContextText.Should().Be("Please fix [teh] value.");
        transition.Issue.Source.Should().Be(SpellingIssueSource.CellText);
    }

    [Fact]
    public void IgnoreOnceThenIgnoreAll_AdvancesThroughFreshSharedScans()
    {
        var adapter = CreateAdapter("teh", "teh");
        var controller = new SpellCheckSessionController(adapter);

        controller.Start().Issue!.Address.Col.Should().Be(1);
        var next = controller.Apply(new(SpellCheckSessionAction.IgnoreOnce));
        var complete = controller.Apply(new(SpellCheckSessionAction.IgnoreAll));

        next.Issue!.Address.Col.Should().Be(2);
        complete.Status.Should().Be(SpellCheckSessionStatus.Complete);
        adapter.ExecuteCount.Should().Be(0);
    }

    [Fact]
    public void Change_ExecutesPlannedCommandAndRescansEditedText()
    {
        var adapter = CreateAdapter("speling erors");
        var controller = new SpellCheckSessionController(adapter);

        var first = controller.Start();
        var second = controller.Apply(new(SpellCheckSessionAction.Change, first.Issue!.Suggestion));
        var complete = controller.Apply(new(SpellCheckSessionAction.Change, second.Issue!.Suggestion));

        complete.Status.Should().Be(SpellCheckSessionStatus.Complete);
        complete.CorrectionsApplied.Should().Be(2);
        adapter.ExecuteCount.Should().Be(2);
        ReadText(adapter, 1).Should().Be("spelling errors");
    }

    [Fact]
    public void ChangeAll_UsesSingleCommandAcrossCellsNotesAndComments()
    {
        var adapter = CreateAdapter("teh cell", "teh other");
        var sheet = adapter.Workbook.GetSheet(adapter.ActiveSheetId)!;
        var noteAddress = new CellAddress(sheet.Id, 2, 1);
        var commentAddress = new CellAddress(sheet.Id, 3, 1);
        sheet.Comments[noteAddress] = "teh note";
        sheet.ThreadedComments[commentAddress] = new ThreadedComment("teh comment");
        var controller = new SpellCheckSessionController(adapter);

        var complete = controller.ApplyAfterStart(SpellCheckSessionAction.ChangeAll, "the");

        complete.Status.Should().Be(SpellCheckSessionStatus.Complete);
        complete.CorrectionsApplied.Should().Be(4);
        adapter.ExecuteCount.Should().Be(1);
        ReadText(adapter, 1).Should().Be("the cell");
        ReadText(adapter, 2).Should().Be("the other");
        sheet.Comments[noteAddress].Should().Be("the note");
        sheet.ThreadedComments[commentAddress].Text.Should().Be("the comment");
    }

    [Fact]
    public void AddToDictionary_NormalizesPersistsAndCompletesCaseInsensitively()
    {
        var adapter = CreateAdapter("TEH Teh teh");
        var controller = new SpellCheckSessionController(adapter);

        var complete = controller.ApplyAfterStart(SpellCheckSessionAction.AddToDictionary);

        complete.Status.Should().Be(SpellCheckSessionStatus.Complete);
        complete.CustomDictionaryChanged.Should().BeTrue();
        adapter.CustomDictionaryWords.Should().Equal("TEH");
        adapter.PersistCount.Should().Be(1);
        adapter.ExecuteCount.Should().Be(0);
    }

    [Fact]
    public void CommandFailure_IsReturnedWithoutAdvancingTheActiveIssue()
    {
        var adapter = CreateAdapter("teh");
        adapter.FailCommands = true;
        var controller = new SpellCheckSessionController(adapter);

        var failed = controller.ApplyAfterStart(SpellCheckSessionAction.Change, "the");

        failed.Status.Should().Be(SpellCheckSessionStatus.Failed);
        failed.ErrorMessage.Should().Be("blocked");
        failed.Issue!.Word.Should().Be("teh");
        failed.CorrectionsApplied.Should().Be(0);
        ReadText(adapter, 1).Should().Be("teh");
    }

    [Fact]
    public void Stop_EndsTheSessionWithoutMutatingTheWorkbook()
    {
        var adapter = CreateAdapter("teh");
        var controller = new SpellCheckSessionController(adapter);

        var stopped = controller.ApplyAfterStart(SpellCheckSessionAction.Stop);

        stopped.Status.Should().Be(SpellCheckSessionStatus.Stopped);
        stopped.CorrectionsApplied.Should().Be(0);
        ReadText(adapter, 1).Should().Be("teh");
    }

    private static FakeAdapter CreateAdapter(params string[] cellTexts)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (var index = 0; index < cellTexts.Length; index++)
        {
            sheet.SetCell(
                new CellAddress(sheet.Id, 1, (uint)(index + 1)),
                new TextValue(cellTexts[index]));
        }

        return new FakeAdapter(workbook, sheet.Id);
    }

    private static string ReadText(FakeAdapter adapter, uint column) =>
        ((TextValue)adapter.Workbook.GetSheet(adapter.ActiveSheetId)!
            .GetCell(new CellAddress(adapter.ActiveSheetId, 1, column))!.Value!).Value;

    private sealed class FakeAdapter(Workbook workbook, SheetId activeSheetId) : ISpellCheckSessionAdapter
    {
        public Workbook Workbook { get; } = workbook;
        public SheetId ActiveSheetId { get; } = activeSheetId;
        public IList<string> CustomDictionaryWords { get; } = new List<string>();
        public int ExecuteCount { get; private set; }
        public int PersistCount { get; private set; }
        public bool FailCommands { get; set; }

        public SpellCheckCommandExecutionResult ExecuteCommand(IWorkbookCommand command)
        {
            ExecuteCount++;
            if (FailCommands)
                return new(false, "blocked");

            var outcome = command.Apply(new WorkbookCommandContext(Workbook));
            return new(outcome.Success, outcome.ErrorMessage, outcome.IsNoOp);
        }

        public void PersistCustomDictionary() => PersistCount++;
    }
}

internal static class SpellCheckSessionControllerTestExtensions
{
    public static SpellCheckSessionTransition ApplyAfterStart(
        this SpellCheckSessionController controller,
        SpellCheckSessionAction action,
        string? replacement = null)
    {
        controller.Start();
        return controller.Apply(new(action, replacement));
    }
}
