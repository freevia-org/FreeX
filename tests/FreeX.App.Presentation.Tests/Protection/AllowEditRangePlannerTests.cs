using FluentAssertions;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class AllowEditRangePlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    [Theory]
    [InlineData("A1:B5", true)]
    [InlineData(" B2 ", true)]
    [InlineData("not a range", false)]
    [InlineData("", false)]
    public void TryParseRange_ParsesValidRangesOnly(string text, bool expected)
    {
        AllowEditRangePlanner.TryParseRange(text, Sheet, out _).Should().Be(expected);
    }

    [Fact]
    public void TryParseRange_BindsRangeToGivenSheet()
    {
        AllowEditRangePlanner.TryParseRange("A1:B2", Sheet, out var range).Should().BeTrue();
        range.Start.Sheet.Should().Be(Sheet);
        range.End.Sheet.Should().Be(Sheet);
    }

    [Fact]
    public void BuildExistingRangeItems_ProjectsRangesToA1Strings()
    {
        var ranges = new List<GridRange>
        {
            new(new CellAddress(Sheet, 1, 1), new CellAddress(Sheet, 1, 1)),
            new(new CellAddress(Sheet, 2, 2), new CellAddress(Sheet, 3, 3)),
        };

        AllowEditRangePlanner.BuildExistingRangeItems(ranges).Should().Equal("A1:A1", "B2:C3");
    }

    [Fact]
    public void BuildExistingRangeItems_NullReturnsEmpty()
    {
        AllowEditRangePlanner.BuildExistingRangeItems(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(2, false, false, false)]
    [InlineData(2, true, true, true)]
    public void BuildButtonState_RequiresRangesAndSelection(int count, bool hasSelection, bool canModify, bool canDelete)
    {
        var state = AllowEditRangePlanner.BuildButtonState(count, hasSelection);

        state.CanModifySelectedRange.Should().Be(canModify);
        state.CanDeleteSelectedRange.Should().Be(canDelete);
        state.CanUsePermissions.Should().BeFalse();
    }

    [Fact]
    public void CreateResults_CarryActionAndRanges()
    {
        var a = new GridRange(new CellAddress(Sheet, 1, 1), new CellAddress(Sheet, 1, 1));
        var b = new GridRange(new CellAddress(Sheet, 2, 2), new CellAddress(Sheet, 2, 2));

        AllowEditRangePlanner.CreateAddResult(a).Should()
            .Be(new AllowEditRangeResult(AllowEditRangeAction.Add, a));
        AllowEditRangePlanner.CreateModifyResult(a, b).Should()
            .Be(new AllowEditRangeResult(AllowEditRangeAction.Modify, b, a));
        AllowEditRangePlanner.CreateRemoveResult(a).Should()
            .Be(new AllowEditRangeResult(AllowEditRangeAction.Remove, a));
        AllowEditRangePlanner.CreateClearResult().Should()
            .Be(new AllowEditRangeResult(AllowEditRangeAction.Clear, null));
    }

    [Fact]
    public void CreateCommandPlan_AddsRangeAndStoredPasswordAtomically()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var plan = AllowEditRangePlanner.CreateCommandPlan(
            sheet.Id,
            AllowEditRangePlanner.CreateAddResult(range),
            password: "ABCD",
            passwordChanged: true);

        var outcome = plan!.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        sheet.AllowEditRanges.Should().ContainSingle().Which.Should().Be(range);
        sheet.AllowEditRangePasswords.Should().ContainKey(range).WhoseValue.Should().Be("ABCD");
    }

    [Fact]
    public void CreateCommandPlan_ModifyCarriesPasswordWhenRangeKeyChanges()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var original = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var updated = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 2));
        var context = new TestCommandContext(workbook);
        AllowEditRangePlanner.CreateCommandPlan(
                sheet.Id,
                AllowEditRangePlanner.CreateAddResult(original),
                password: "ABCD",
                passwordChanged: true)!
            .Command.Apply(context).Success.Should().BeTrue();

        var plan = AllowEditRangePlanner.CreateCommandPlan(
            sheet.Id,
            AllowEditRangePlanner.CreateModifyResult(original, updated),
            password: null,
            passwordChanged: false,
            existingPasswords: sheet.AllowEditRangePasswords);
        var outcome = plan!.Command.Apply(context);

        outcome.Success.Should().BeTrue();
        sheet.AllowEditRanges.Should().ContainSingle().Which.Should().Be(updated);
        sheet.AllowEditRangePasswords.Should().NotContainKey(original);
        sheet.AllowEditRangePasswords.Should().ContainKey(updated).WhoseValue.Should().Be("ABCD");
    }

    [Fact]
    public void CreateCommandPlan_RemoveClearsRangeAndPassword()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var context = new TestCommandContext(workbook);
        AllowEditRangePlanner.CreateCommandPlan(
                sheet.Id,
                AllowEditRangePlanner.CreateAddResult(range),
                password: "ABCD",
                passwordChanged: true)!
            .Command.Apply(context).Success.Should().BeTrue();

        var plan = AllowEditRangePlanner.CreateCommandPlan(
            sheet.Id,
            AllowEditRangePlanner.CreateRemoveResult(range),
            password: null,
            passwordChanged: false,
            existingPasswords: sheet.AllowEditRangePasswords);
        var outcome = plan!.Command.Apply(context);

        outcome.Success.Should().BeTrue();
        sheet.AllowEditRanges.Should().BeEmpty();
        sheet.AllowEditRangePasswords.Should().BeEmpty();
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}

public sealed class AllowEditRangeCommandOwnershipSourceGuardTests
{
    [Fact]
    public void RenderersDelegateAllowEditRangeCommandCompositionToPresentation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var host = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "MainWindow.ReviewCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.AllowEditRange.cs"));

        host.Should().Contain("AllowEditRangePlanner.CreateCommandPlan(");
        host.Should().NotContain("new AllowEditRangeCommand(");
        host.Should().NotContain("new RemoveAllowEditRangeCommand(");
        host.Should().NotContain("new SetAllowEditRangePasswordCommand(");
        avalonia.Should().Contain("AllowEditRangePlanner.CreateCommandPlan(");
        avalonia.Should().NotContain("new AllowEditRangeCommand(");
        avalonia.Should().NotContain("new RemoveAllowEditRangeCommand(");
        avalonia.Should().NotContain("new SetAllowEditRangePasswordCommand(");
    }
}
