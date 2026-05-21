using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class FlashFillServiceTests
{
    // ── Constant fill ─────────────────────────────────────────────────────────

    [Fact]
    public void Fill_ConstantPattern_FillsAllWithConstant()
    {
        var result = FlashFillService.Fill(
            [("Alice", "Hello"), ("Bob", "Hello")],
            ["Carol", "Dave"]);

        result.Should().BeEquivalentTo(["Hello", "Hello"], o => o.WithStrictOrdering());
    }

    // ── Case transforms ───────────────────────────────────────────────────────

    [Fact]
    public void Fill_UpperCase_TransformsSourceToUpper()
    {
        var result = FlashFillService.Fill(
            [("alice", "ALICE"), ("bob", "BOB")],
            ["carol"]);

        result.Should().BeEquivalentTo(["CAROL"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_LowerCase_TransformsSourceToLower()
    {
        var result = FlashFillService.Fill(
            [("ALICE", "alice"), ("BOB", "bob")],
            ["CAROL"]);

        result.Should().BeEquivalentTo(["carol"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ProperCase_TransformsSourceToTitleCase()
    {
        var result = FlashFillService.Fill(
            [("alice smith", "Alice Smith")],
            ["bob jones"]);

        result.Should().BeEquivalentTo(["Bob Jones"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_InconsistentCasePattern_ReturnsNull()
    {
        // "alice" → "ALICE" suggests UPPER, but "bob" → "Bob" suggests PROPER
        var result = FlashFillService.Fill(
            [("alice", "ALICE"), ("bob", "Bob")],
            ["carol"]);

        result.Should().BeNull();
    }

    // ── Extract by delimiter ──────────────────────────────────────────────────

    [Fact]
    public void Fill_ExtractFirstWord_ExtractsPartZeroBySpace()
    {
        var result = FlashFillService.Fill(
            [("John Smith", "John"), ("Jane Doe", "Jane")],
            ["Bob Brown"]);

        result.Should().BeEquivalentTo(["Bob"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractLastWord_ExtractsPartOneBySpace()
    {
        var result = FlashFillService.Fill(
            [("John Smith", "Smith"), ("Jane Doe", "Doe")],
            ["Bob Brown"]);

        result.Should().BeEquivalentTo(["Brown"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ExtractEmailUsername_ExtractsPartZeroByAt()
    {
        var result = FlashFillService.Fill(
            [("alice@example.com", "alice"), ("bob@test.org", "bob")],
            ["carol@domain.net"]);

        result.Should().BeEquivalentTo(["carol"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsDottedUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.lovelace@contoso.com", "Ada Lovelace"),
                ("grace.hopper@contoso.com", "Grace Hopper")
            ],
            ["alan.turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsUnderscoreUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada_lovelace@contoso.com", "Ada Lovelace"),
                ("grace_hopper@contoso.com", "Grace Hopper")
            ],
            ["alan_turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsHyphenUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada-lovelace@contoso.com", "Ada Lovelace"),
                ("grace-hopper@contoso.com", "Grace Hopper")
            ],
            ["alan-turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DelimitedWordsInitials_BuildsInitials()
    {
        var result = FlashFillService.Fill(
            [("Ada Lovelace", "AL"), ("Grace Hopper", "GH")],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["AT"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstInitialPeriodLast_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["A. Lovelace", "G. Hopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["A. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastLowercase_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["alovelace", "ghopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["aturing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace@example.com", "grace.hopper@example.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing@example.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace@contoso.com", "grace.hopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("_", "alan_turing@contoso.com")]
    [InlineData("-", "alan-turing@contoso.com")]
    public void FillFromColumns_FirstLastSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"ada{separator}lovelace@contoso.com", $"grace{separator}hopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["alovelace@contoso.com", "ghopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["aturing@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "a.turing@contoso.com")]
    [InlineData("_", "a_turing@contoso.com")]
    [InlineData("-", "a-turing@contoso.com")]
    public void FillFromColumns_FirstInitialLastSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"a{separator}lovelace@contoso.com", $"g{separator}hopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_LastFirstInitialEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["lovelacea@contoso.com", "hopperg@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["turinga@contoso.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace@contoso.com", "grace.hopper@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["alovelace@contoso.com", "ghopper@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_LastFirstInitialEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["lovelacea@contoso.com", "hopperg@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_LastFirstInitialPeriod_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Lovelace A.", "Hopper G."],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["Turing A."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DelimitedWordsInitials_WithMixedExampleDelimiters_ReturnsNull()
    {
        var result = FlashFillService.Fill(
            [("Ada Lovelace", "AL"), ("Grace-Hopper", "GH")],
            ["Alan Turing"]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_FirstLastWithSpace_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Ada Lovelace", "Grace Hopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_LastFirstWithComma_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Lovelace, Ada", "Hopper, Grace"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["Turing, Alan"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastWithPeriod_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["ada", "lovelace"],
                ["grace", "hopper"]
            ],
            ["ada.lovelace", "grace.hopper"],
            [
                ["alan", "turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastInitials_BuildsInitials()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["AL", "GH"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["AT"], o => o.WithStrictOrdering());
    }

    // ── Prefix / suffix trim ──────────────────────────────────────────────────

    [Fact]
    public void Fill_RemovePrefix_TrimsFixedPrefixFromSource()
    {
        var result = FlashFillService.Fill(
            [("Mr. Smith", "Smith"), ("Mr. Jones", "Jones")],
            ["Mr. Brown"]);

        result.Should().BeEquivalentTo(["Brown"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_AddPrefix_PrependsPrefixToSource()
    {
        var result = FlashFillService.Fill(
            [("Smith", "Mr. Smith"), ("Jones", "Mr. Jones")],
            ["Brown"]);

        result.Should().BeEquivalentTo(["Mr. Brown"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_AddSuffix_AppendsSuffixToSource()
    {
        var result = FlashFillService.Fill(
            [("Smith", "Smith Ltd"), ("Jones", "Jones Ltd")],
            ["Brown"]);

        result.Should().BeEquivalentTo(["Brown Ltd"], o => o.WithStrictOrdering());
    }

    // ── Substring extraction ──────────────────────────────────────────────────

    [Fact]
    public void Fill_SubstringExtraction_AppliesConsistentStartAndLength()
    {
        // "ABCDE" → "BCD" means substring(1, 3)
        // "FGHIJ" → "GHI" means substring(1, 3) — same pattern
        var result = FlashFillService.Fill(
            [("ABCDE", "BCD"), ("FGHIJ", "GHI")],
            ["KLMNO"]);

        result.Should().BeEquivalentTo(["LMN"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_NoExamples_ReturnsNull()
    {
        var result = FlashFillService.Fill([], ["Bob"]);
        result.Should().BeNull();
    }

    [Fact]
    public void Fill_SubstringPatternSourceTooShort_ReturnsNull()
    {
        // Pattern: startIndex=1, length=3 — but "AB" is only 2 chars
        var result = FlashFillService.Fill(
            [("ABCDE", "BCD"), ("FGHIJ", "GHI")],
            ["AB"]);
        result.Should().BeNull();
    }

    // ── No pattern ────────────────────────────────────────────────────────────

    [Fact]
    public void Fill_NoPattern_ReturnsNull()
    {
        var result = FlashFillService.Fill(
            [("Alice", "hello"), ("Bob", "world")],
            ["Carol"]);

        result.Should().BeNull();
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Fill_SingleExample_StillDetectsPattern()
    {
        // With one example we should still detect UPPER
        var result = FlashFillService.Fill(
            [("alice", "ALICE")],
            ["bob", "carol"]);

        result.Should().BeEquivalentTo(["BOB", "CAROL"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmptyRemaining_ReturnsEmptyList()
    {
        var result = FlashFillService.Fill(
            [("alice", "ALICE")],
            []);

        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }

    [Fact]
    public void Fill_SuffixTrimPattern_TrimsFixedSuffixFromSource()
    {
        var result = FlashFillService.Fill(
            [("Smith Ltd", "Smith"), ("Jones Ltd", "Jones")],
            ["Brown Ltd"]);

        result.Should().BeEquivalentTo(["Brown"], o => o.WithStrictOrdering());
    }
}

public sealed class FlashFillCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void FlashFillCommand_Apply_FillsBlankCellsUsingDetectedPattern()
    {
        var (wb, sheet, ctx) = Setup();
        // Col A = source data (col index 1)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("John Smith"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jane Doe"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Bob Brown"));
        // Col B = fill column (col index 2): user typed example in row 1
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("John"));
        // Rows 2 and 3 in col B are blank

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        (sheet.GetValue(2, 2) as TextValue)?.Value.Should().Be("Jane");
        (sheet.GetValue(3, 2) as TextValue)?.Value.Should().Be("Bob");
    }

    [Fact]
    public void FlashFillCommand_Revert_RestoresBlankCells()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("John Smith"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jane Doe"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Bob Brown"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("John"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 3);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        // B2 and B3 should be blank again
        sheet.GetValue(2, 2).Should().BeOfType<BlankValue>();
        sheet.GetValue(3, 2).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void FlashFillCommand_NoPattern_ReturnsFailureOutcome()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Bob"));
        // Examples that have no consistent pattern
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("world"));
        // Row 3 is blank (the only row to fill) — but two examples with no pattern

        // Put something to fill
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Carol"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 2, sourceColIndex: 1, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Command_SourceColumnOnRight_FillsCorrectly()
    {
        var wb    = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx   = new SimpleCtx(wb);

        // Source data in col 2: "ALICE", "BOB", "CAROL"
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ALICE"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("BOB"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("CAROL"));

        // Example in col 1 row 1: "alice" (LOWER pattern)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("alice"));

        // Fill col=1, source col=2
        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 1, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(2, 1)!.Value.Should().Be(new TextValue("bob"));
        sheet.GetCell(3, 1)!.Value.Should().Be(new TextValue("carol"));
    }

    [Fact]
    public void FlashFillCommand_WhenTwoLeftColumnsArePopulated_UsesMultiColumnPatternFirst()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Ada Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Grace Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("Alan Turing"));
    }

    [Fact]
    public void FlashFillCommand_WithFirstLastEmailExamples_UsesInferredDomain()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("ada.lovelace@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("grace.hopper@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("alan.turing@contoso.com"));
    }

    [Fact]
    public void FlashFillCommand_WithFirstInitialLastEmailExamples_UsesInferredDomain()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("alovelace@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("ghopper@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("aturing@contoso.com"));
    }

    [Fact]
    public void FlashFillCommand_WithLastFirstInitialEmailExamples_UsesInferredDomain()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("lovelacea@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("hopperg@contoso.com"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Alan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Turing"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("turinga@contoso.com"));
    }

    [Fact]
    public void FlashFillCommand_WhenSourceColumnIsNotImmediateLeft_UsesSingleSourcePattern()
    {
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Ada"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Ada Lovelace"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("ADA LOVELACE"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Grace"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Grace Hopper"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("GRACE HOPPER"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Wrong"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new TextValue("ALAN TURING"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 4, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("Alan Turing"));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
