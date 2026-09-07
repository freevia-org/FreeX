using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// r527: r520-r526 fixed 78 capture-then-apply sites one signature at a time -- casts, then pattern
/// matches, then inserts. Each sweep found what its shape could see, and each time the next shape
/// found more. This test stops enumerating shapes and drives the BEHAVIOUR instead: construct every
/// command in the assembly with an index that cannot possibly be valid, run the full
/// HasEffect/Apply/Revert cycle, and require that none of them throws.
///
/// <para>That covers the class by exhausting the COMMANDS rather than the syntax, so a future command
/// written with a fresh spelling of the same mistake fails here without anyone inventing a matching
/// regex first.</para>
///
/// <para>The driver only builds commands whose constructor arguments it can supply honestly --
/// numbers, strings, bools, enums and delegates. A command needing a live model object is counted as
/// unbuildable rather than fed a null, because a NullReferenceException from an invented argument
/// would be the driver's failure, not the command's. The floor keeps that honesty from hollowing the
/// test out.</para>
/// </summary>
public class R527_EveryCommandSurvivesAHostileIndexTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static object? ValueFor(Type type, int hostileIndex)
    {
        if (type == typeof(int)) return hostileIndex;
        if (type == typeof(uint)) return (uint)Math.Abs(hostileIndex);
        if (type == typeof(double)) return 1.0;
        if (type == typeof(bool)) return false;
        if (type == typeof(string)) return "x";
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (Nullable.GetUnderlyingType(type) is { } inner) return ValueFor(inner, hostileIndex);
        if (type == typeof(Action<Paragraph>)) return new Action<Paragraph>(_ => { });
        if (type == typeof(Func<RunFormatting, RunFormatting>)) return new Func<RunFormatting, RunFormatting>(f => f);
        return null;
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(-1)]
    public void NoCommandThrowsOnAnIndexThatCannotBeValid(int hostileIndex)
    {
        var commandTypes = typeof(IDocumentCommand).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true }
                        && typeof(IDocumentCommand).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        commandTypes.Should().HaveCountGreaterThanOrEqualTo(
            60, "the reflection query must still reach FreeW's command assembly");

        var failures = new List<string>();
        int exercised = 0, unbuildable = 0;

        foreach (var type in commandTypes)
        {
            var constructor = type.GetConstructors()
                .OrderBy(candidate => candidate.GetParameters().Length)
                .FirstOrDefault(candidate => candidate.GetParameters()
                    .All(parameter => ValueFor(parameter.ParameterType, hostileIndex) is not null));

            if (constructor is null)
            {
                unbuildable++;
                continue;
            }

            var args = constructor.GetParameters()
                .Select(parameter => ValueFor(parameter.ParameterType, hostileIndex))
                .ToArray();

            object command;
            try
            {
                command = constructor.Invoke(args);
            }
            catch (Exception)
            {
                unbuildable++;
                continue;
            }

            var document = new TextDocument();
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph("only block"));
            var context = new Context(document);
            var typed = (IDocumentCommand)command;

            exercised++;
            try
            {
                // The bus consults HasEffect first, so it is as much a throw site as Apply --
                // r521's finding was exactly that.
                if (typed.HasEffect(context))
                    typed.Apply(context);
                typed.Revert(context);
            }
            catch (Exception ex)
            {
                failures.Add($"{type.Name}: {ex.GetType().Name}");
            }
        }

        exercised.Should().BeGreaterThanOrEqualTo(
            25, "the driver must actually construct and run a meaningful share of the commands");

        failures.Should().BeEmpty(
            "a command handed an index that cannot be valid must decline, not throw; the bus does "
            + "not wrap Apply in FreeW (see r520), so the exception would leave the command layer");
    }
}
