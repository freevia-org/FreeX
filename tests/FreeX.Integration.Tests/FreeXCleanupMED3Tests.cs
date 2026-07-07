using System.Text.RegularExpressions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression coverage for cleanup batch MED3 findings in FindReplaceService:
///   - P63: a Replace All whose "Find what" pattern is entirely wildcards (e.g. "*") must not
///     duplicate the replacement text. The unanchored regex built from "*" matches the whole cell
///     text AND then an empty string at end-of-input, and a naive Regex.Replace substitutes both.
///   - P65: Replace All with an empty "Replace with" must clear the cell to BlankValue, not store
///     a non-blank empty-string TextValue.
///   - P67: a catastrophically backtracking wildcard pattern must not throw
///     RegexMatchTimeoutException out of Find/Replace — every formula-side wildcard consumer
///     already treats a timeout as "no match", and Find/Replace must do the same instead of
///     crashing the host mid-search.
/// </summary>
public class FreeXCleanupMED3Tests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(id => new TestCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    [Fact]
    public void ReplaceAll_AllWildcardPattern_DoesNotDuplicateReplacementText()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("abc"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "*", "X");

        count.Should().Be(1);
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue("X"),
            "Excel's Replace All with Find-what '*' produces a single 'X', not a doubled 'XX'");
    }

    [Fact]
    public void ReplaceAll_EmptyReplacementText_ClearsCellToBlank_NotEmptyString()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue("foo"));

        var count = FindReplaceService.ReplaceAll(wb, commandBus, "foo", "");

        count.Should().Be(1);
        var cell = sheet.GetCell(a1);
        cell.Should().NotBeNull();
        cell!.Value.Should().Be(BlankValue.Instance,
            "Excel's Replace All with an empty 'Replace with' leaves a truly blank cell " +
            "(COUNTA excludes it, ISBLANK is TRUE), not a non-blank empty-string cell");
    }

    [Fact]
    public void ReplaceAll_CatastrophicWildcardPattern_TimesOutGracefully_InsteadOfThrowing()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        // Long repetitive text with no trailing 'b' forces heavy backtracking against a pattern
        // built from many alternating wildcard/literal segments.
        sheet.SetCell(a1, new TextValue(new string('a', 5000)));

        var searchPattern = string.Concat(Enumerable.Repeat("*a", 40)) + "*b";

        var act = () => FindReplaceService.ReplaceAll(wb, commandBus, searchPattern, "X");

        act.Should().NotThrow<RegexMatchTimeoutException>();
        // No match is ever found (there is no trailing 'b'), so nothing is replaced and the
        // original cell content survives untouched.
        sheet.GetCell(a1)!.Value.Should().Be(new TextValue(new string('a', 5000)));
    }

    [Fact]
    public void Find_CatastrophicWildcardPattern_TimesOutGracefully_InsteadOfThrowing()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new TextValue(new string('a', 5000)));

        var searchPattern = string.Concat(Enumerable.Repeat("*a", 40)) + "*b";

        var act = () => FindReplaceService.Find(wb, searchPattern);

        act.Should().NotThrow<RegexMatchTimeoutException>();
    }
}
