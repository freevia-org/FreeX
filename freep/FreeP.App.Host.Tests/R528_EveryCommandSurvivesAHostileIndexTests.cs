using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r528: ports r527's behavioural census from FreeW to FreeP. Every regex I wrote for this class was
/// shaped by one app's vocabulary -- r525 established that literally, when a scan searching
/// <c>Blocks[...]</c> could not have reported a hit outside FreeW no matter what FreeP contained. A
/// census asks the question in a way that has no vocabulary at all: build every command with an index
/// that cannot be valid, run the whole HasEffect/Apply/Revert cycle, and require that none throws.
///
/// <para>The stakes differ from FreeW's and the entry should say so. FreeP's bus DOES wrap command
/// execution in try/catch (r518), so a throw here degrades to a failed command rather than escaping
/// the command layer as it would in FreeW. That makes this a quality bar rather than a crash guard --
/// a command handed a stale index should decline, not raise.</para>
/// </summary>
public class R528_EveryCommandSurvivesAHostileIndexTests
{
    private static object? ValueFor(Type type, int hostileIndex)
    {
        if (type == typeof(int)) return hostileIndex;
        if (type == typeof(uint)) return (uint)Math.Abs(hostileIndex);
        if (type == typeof(double)) return 1.0;
        if (type == typeof(bool)) return false;
        if (type == typeof(string)) return "x";
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (Nullable.GetUnderlyingType(type) is { } inner) return ValueFor(inner, hostileIndex);
        return null;
    }

    private static Presentation NewPresentation()
    {
        var presentation = new Presentation();
        if (presentation.Slides.Count == 0)
            presentation.Slides.Add(new Slide());
        return presentation;
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(-1)]
    public void NoCommandThrowsOnAnIndexThatCannotBeValid(int hostileIndex)
    {
        var commandTypes = typeof(IPresentationCommand).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true }
                        && typeof(IPresentationCommand).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        commandTypes.Should().HaveCountGreaterThanOrEqualTo(
            100, "the reflection query must still reach FreeP's command assembly");

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

            object command;
            try
            {
                command = constructor.Invoke(constructor.GetParameters()
                    .Select(parameter => ValueFor(parameter.ParameterType, hostileIndex))
                    .ToArray());
            }
            catch (Exception)
            {
                unbuildable++;
                continue;
            }

            var presentation = NewPresentation();
            var typed = (IPresentationCommand)command;

            exercised++;
            try
            {
                if (typed.HasEffect(presentation))
                    typed.Apply(presentation);
                typed.Revert(presentation);
            }
            catch (Exception ex)
            {
                failures.Add($"{type.Name}: {ex.GetType().Name}");
            }
        }

        exercised.Should().BeGreaterThanOrEqualTo(
            25, "the driver must construct and run a meaningful share of the commands");

        failures.Should().BeEmpty(
            "a command handed an index that cannot be valid must decline rather than throw");
    }
}
