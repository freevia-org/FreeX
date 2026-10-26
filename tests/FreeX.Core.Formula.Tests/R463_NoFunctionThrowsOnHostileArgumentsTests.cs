using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// r463: no built-in function may throw or hang on bad arguments -- it must answer with an error VALUE.
///
/// <para>Excel's contract, and the whole reason <c>#VALUE!</c> and <c>#NUM!</c> exist: a bad argument
/// is a result, not a failure. An exception escaping a function does not spoil one cell, it takes
/// down the recalculation pass for the entire workbook, and the user typed nothing more unusual than
/// <c>=SQRT(-1)</c> or a stray text argument.</para>
///
/// <para>Every registered function is called with ten hostile argument lists: none, an empty string,
/// unparseable text, a negative, a zero, an overflow to infinity, a range where a scalar is expected,
/// far too many arguments, an error value as an argument, and several empty strings. This is the
/// formula-engine form of the malformed-input probe that produced r448-r453 on the file readers.</para>
///
/// <para>Result on introduction: 496 functions x 10 argument sets = 4,960 evaluations, zero throws and
/// zero hangs. Kept as a guard rather than deleted -- a function added tomorrow is covered the day it
/// appears, which is the same argument the reflective undo drivers rest on.</para>
///
/// <para>The other half of this contract, deep nesting, is already guarded and did not need adding:
/// <c>FormulaEvaluator</c> documents a maximum recursive evaluation depth returning <c>#NUM!</c>
/// specifically to prevent a <c>StackOverflowException</c>, which would be uncatchable and would kill
/// the process. That was verified by reading rather than by probing, precisely because a probe that
/// found it missing would have taken the test host down with it.</para>
/// </summary>
public sealed class R463_NoFunctionThrowsOnHostileArgumentsTests
{
    private static Sheet MakeSheet()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("text"));
        return sheet;
    }

    private static readonly string[] ArgumentSets =
    [
        "",                        // no arguments at all
        "\"\"",                    // one empty string
        "\"not a number\"",        // unparseable text where a number is wanted
        "-1",                      // negative, for the functions with a domain
        "0",                       // zero, for the ones that divide
        "1E308*10",                // overflow to infinity
        "A1:B2",                   // a range where a scalar may be expected
        "1,2,3,4,5,6,7,8,9,10",    // far more arguments than most functions accept
        "1/0",                     // an error value AS an argument
        "\"\",\"\",\"\"",          // several empty strings
    ];

    [Fact]
    public async Task NoBuiltInFunctionThrowsOrHangs()
    {
        var evaluator = new FormulaEvaluator();
        var names = BuiltInFunctions.Names.OrderBy(name => name, StringComparer.Ordinal).ToList();

        names.Should().HaveCountGreaterThanOrEqualTo(
            400, "the registry lookup must still be reaching the built-in functions");

        var threw = new List<string>();
        var hung = new List<string>();
        var evaluated = 0;

        foreach (var name in names)
        {
            foreach (var arguments in ArgumentSets)
            {
                var formula = $"={name}({arguments})";

                var task = Task.Run(() => evaluator.Evaluate(formula, MakeSheet()));
                var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

                if (finished != task)
                {
                    hung.Add(formula);
                    continue;
                }

                try
                {
                    _ = await task;
                    evaluated++;
                }
                catch (Exception exception)
                {
                    threw.Add($"{formula} :: {exception.GetType().Name}");
                }
            }
        }

        var census = $"functions={names.Count} evaluated={evaluated} threw={threw.Count} hung={hung.Count}";

        threw.Should().BeEmpty(
            "a bad argument is a RESULT in a spreadsheet, not a failure -- an exception escaping a " +
            "function does not spoil one cell, it takes down the recalculation pass for the whole " +
            "workbook. " + census + "\n" + string.Join("\n", threw.Take(40)),
            Array.Empty<object>());

        hung.Should().BeEmpty(
            "a function that never returns freezes the application on a keystroke, and no catch " +
            "anywhere can rescue it. " + census + "\n" + string.Join("\n", hung.Take(20)),
            Array.Empty<object>());

        evaluated.Should().BeGreaterThanOrEqualTo(
            4000,
            "the sweep must still be evaluating -- if this falls, it has quietly stopped testing " +
            "rather than the engine having changed. " + census);
    }
}
