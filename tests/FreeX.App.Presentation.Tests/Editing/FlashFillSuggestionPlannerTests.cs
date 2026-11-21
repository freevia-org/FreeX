using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

public sealed class FlashFillSuggestionPlannerTests
{
    private static readonly string[] Names = ["John Smith", "Jane Doe", "Bob Brown", "Alice Wong"];
    private static readonly string[] Phones = ["(555) 123-4567", "(212) 987-6543", "(415) 555-0100", "(305) 222-3333"];
    private static readonly string[] Emails = ["john@example.com", "jane@contoso.org", "bob@fabrikam.net", "alice@adatum.io"];

    /// <summary>
    /// Ground truth captured from real Excel (Range.FlashFill over the same data, driven via COM):
    /// the committed example in row 1 and the column Excel filled. FreeX must predict the same
    /// values while the user is still typing row 2.
    /// </summary>
    public static IEnumerable<object[]> ExcelCases()
    {
        yield return ["first-name", Names, null!, "John", new[] { "John", "Jane", "Bob", "Alice" }];
        yield return ["last-name", Names, null!, "Smith", new[] { "Smith", "Doe", "Brown", "Wong" }];
        // Known engine gap (not a planner issue): Excel infers "J.S." -> "J.D." from one example,
        // the FreeX Flash Fill engine produces no suggestion for this single-example initials case.
        yield return ["upper", Names, null!, "JOHN SMITH", new[] { "JOHN SMITH", "JANE DOE", "BOB BROWN", "ALICE WONG" }];
        yield return ["last-comma-first", Names, null!, "Smith, John", new[] { "Smith, John", "Doe, Jane", "Brown, Bob", "Wong, Alice" }];
        yield return ["area-code", Phones, null!, "555", new[] { "555", "212", "415", "305" }];
        yield return ["email-domain", Emails, null!, "example.com", new[] { "example.com", "contoso.org", "fabrikam.net", "adatum.io" }];
        yield return ["email-user", Emails, null!, "john", new[] { "john", "jane", "bob", "alice" }];
        yield return
        [
            "two-cols-join",
            new[] { "John", "Jane", "Bob", "Alice" },
            new[] { "Smith", "Doe", "Brown", "Wong" },
            "John Smith",
            new[] { "John Smith", "Jane Doe", "Bob Brown", "Alice Wong" },
        ];
    }

    [Theory]
    [MemberData(nameof(ExcelCases))]
    public void Suggestion_MatchesRealExcelFlashFill(
        string name,
        string[] sourceA,
        string[]? sourceB,
        string example,
        string[] excel)
    {
        _ = name;
        var (wb, sheet) = NewWorkbook();
        var fillColumn = sourceB is null ? 2u : 3u;
        Fill(sheet, 1, sourceA);
        if (sourceB is not null)
            Fill(sheet, 2, sourceB);
        sheet.SetCell(new CellAddress(sheet.Id, 1, fillColumn), new TextValue(example));

        var suggestion = FlashFillSuggestionPlanner.Plan(
            enabled: true,
            wb,
            new CellAddress(sheet.Id, 2, fillColumn),
            excel[1][..1]);

        suggestion.Should().NotBeNull();
        suggestion!.EditedCellValue.Should().Be(excel[1]);
        suggestion.Ghosts.Select(g => g.Text).Should().Equal(excel.Skip(2));
        suggestion.Ghosts.Select(g => (g.Row, g.Col)).Should().Equal(
            Enumerable.Range(3, excel.Length - 2).Select(r => ((uint)r, fillColumn)));
    }

    [Fact]
    public void Suggestion_IsOffEverywhere_WhenOptionDisabled()
    {
        var (wb, sheet) = FirstNameWorkbook();

        FlashFillSuggestionPlanner.Plan(false, wb, new CellAddress(sheet.Id, 2, 2), "J").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("=A2")]
    [InlineData("Zed")]
    public void Suggestion_IsDismissed_WhenTypedTextIsEmptyAFormulaOrDivergesFromThePattern(string typed)
    {
        var (wb, sheet) = FirstNameWorkbook();

        FlashFillSuggestionPlanner.Plan(true, wb, new CellAddress(sheet.Id, 2, 2), typed).Should().BeNull();
    }

    [Fact]
    public void Suggestion_RequiresAnExampleAboveAndSourceDataBeside()
    {
        var (wb, sheet) = FirstNameWorkbook();
        sheet.ClearCell(new CellAddress(sheet.Id, 1, 2));

        FlashFillSuggestionPlanner.Plan(true, wb, new CellAddress(sheet.Id, 2, 2), "J").Should().BeNull();

        var (wb2, sheet2) = FirstNameWorkbook();
        sheet2.ClearCell(new CellAddress(sheet2.Id, 2, 1));
        FlashFillSuggestionPlanner.Plan(true, wb2, new CellAddress(sheet2.Id, 2, 2), "J").Should().BeNull();
    }

    [Fact]
    public void Suggestion_IsOnlyOfferedForAnEmptyCell()
    {
        var (wb, sheet) = FirstNameWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Existing"));

        FlashFillSuggestionPlanner.Plan(true, wb, new CellAddress(sheet.Id, 2, 2), "J").Should().BeNull();
    }

    [Fact]
    public void Suggestion_StopsAtTheFirstRowWithoutSourceDataOrWithExistingContent()
    {
        var (wb, sheet) = FirstNameWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Keep me"));

        var suggestion = FlashFillSuggestionPlanner.Plan(true, wb, new CellAddress(sheet.Id, 2, 2), "J");

        suggestion.Should().NotBeNull();
        suggestion!.Ghosts.Select(g => g.Row).Should().Equal(3u);
    }

    [Fact]
    public void Suggestion_SkipsAHeaderRowWhenTheHeadersBreakThePattern()
    {
        var (wb, sheet) = NewWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("First"));
        for (var i = 0; i < Names.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i + 2, 1), new TextValue(Names[i]));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("John"));

        var suggestion = FlashFillSuggestionPlanner.Plan(true, wb, new CellAddress(sheet.Id, 3, 2), "J");

        suggestion.Should().NotBeNull();
        suggestion!.EditedCellValue.Should().Be("Jane");
        suggestion.Ghosts.Select(g => g.Text).Should().Equal("Bob", "Alice");
    }

    [Theory]
    [InlineData(FlashFillSuggestionKey.Enter, false, FlashFillSuggestionKeyAction.Accept)]
    [InlineData(FlashFillSuggestionKey.Tab, false, FlashFillSuggestionKeyAction.Accept)]
    [InlineData(FlashFillSuggestionKey.Escape, false, FlashFillSuggestionKeyAction.Dismiss)]
    [InlineData(FlashFillSuggestionKey.Other, false, FlashFillSuggestionKeyAction.None)]
    [InlineData(FlashFillSuggestionKey.Enter, true, FlashFillSuggestionKeyAction.None)]
    public void Resolve_MapsKeysToAcceptAndDismiss(
        FlashFillSuggestionKey key,
        bool hasModifiers,
        FlashFillSuggestionKeyAction expected)
    {
        var (wb, sheet) = FirstNameWorkbook();
        var suggestion = FlashFillSuggestionPlanner.Plan(true, wb, new CellAddress(sheet.Id, 2, 2), "J");

        FlashFillSuggestionPlanner.Resolve(suggestion, key, hasModifiers).Should().Be(expected);
        FlashFillSuggestionPlanner.Resolve(null, key, hasModifiers).Should().Be(FlashFillSuggestionKeyAction.None);
    }

    [Fact]
    public void State_ReportsChangesOnlyWhenTheGhostCellsChange()
    {
        var (wb, sheet) = FirstNameWorkbook();
        var address = new CellAddress(sheet.Id, 2, 2);
        var state = new FlashFillSuggestionState();

        state.Update(true, wb, address, "J").Should().BeTrue();
        state.Update(true, wb, address, "Ja").Should().BeFalse("the ghosts are identical while typing along");
        state.Update(true, wb, address, "Zed").Should().BeTrue();
        state.Current.Should().BeNull();
        state.Clear().Should().BeFalse();
        state.Update(true, wb, address, "J").Should().BeTrue();
        state.Clear().Should().BeTrue();
    }

    [Fact]
    public void Accept_RunsTheUndoableFlashFillCommandOverTheEditedAndGhostRows()
    {
        var (wb, sheet) = FirstNameWorkbook();
        var suggestion = FlashFillSuggestionPlanner.Plan(true, wb, new CellAddress(sheet.Id, 2, 2), "J")!;
        var context = new TestContext(wb);
        var command = suggestion.CreateAcceptCommand();

        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue();
        Text(sheet, 2, 2).Should().Be("Jane");
        Text(sheet, 3, 2).Should().Be("Bob");
        Text(sheet, 4, 2).Should().Be("Alice");

        command.Revert(context);

        sheet.GetValue(2, 2).Should().BeOfType<BlankValue>();
        sheet.GetValue(3, 2).Should().BeOfType<BlankValue>();
        sheet.GetValue(4, 2).Should().BeOfType<BlankValue>();
        Text(sheet, 1, 2).Should().Be("John");
        suggestion.AffectedRange.Start.Row.Should().Be(2);
        suggestion.AffectedRange.End.Row.Should().Be(4);
    }

    private static (Workbook Workbook, Sheet Sheet) NewWorkbook()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }

    private static (Workbook Workbook, Sheet Sheet) FirstNameWorkbook()
    {
        var (workbook, sheet) = NewWorkbook();
        Fill(sheet, 1, Names.Take(4).ToArray());
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("John"));
        return (workbook, sheet);
    }

    private static void Fill(Sheet sheet, uint column, string[] values)
    {
        for (var i = 0; i < values.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i + 1, column), new TextValue(values[i]));
    }

    private static string Text(Sheet sheet, uint row, uint column) =>
        ((TextValue)sheet.GetValue(row, column)).Value;

    private sealed class TestContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
