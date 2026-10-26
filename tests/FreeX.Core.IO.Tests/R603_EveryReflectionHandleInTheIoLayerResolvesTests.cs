using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r603: the resolution guard existed, pointed at ONE type.
///
/// <para><c>LegacyXlsReflectionHandleResolutionTests</c> asserts that every static
/// <see cref="MemberInfo"/> handle on <c>LegacyXlsFileAdapter</c> actually resolved, and enumerates
/// that type's fields rather than listing names -- a good fence, with the same asymmetry r602 and
/// r603 kept finding: it covers the type someone was looking at, not the class of hazard. Two other
/// sites in this layer have the identical shape and nothing asserting them:</para>
///
/// <list type="bullet">
/// <item><c>XlsxClosedXmlCellMapper.XlCellValueNumberField</c> -- <c>GetField("_value", NonPublic)</c>
/// on ClosedXML's <c>XLCellValue</c>, consumed through <c>if (... is null) return false</c>. If
/// ClosedXML renames it, an out-of-range date serial becomes <c>#NUM!</c> instead of the real number
/// and a time/duration cell silently loses its exact serial to a TotalDays approximation.</item>
/// <item><c>XlsxFileAdapter.XlCellStyleValueAccessor</c> / <c>.XlCellSetStyleValueAction</c> --
/// static readonly NULLABLE DELEGATES eagerly built by reflection factories over
/// <c>ClosedXML.Excel.XLCell</c> and <c>XLStyleValue</c>. Same hazard, different field TYPE, which is
/// why covering only <see cref="MemberInfo"/>-typed fields would still have missed them.</item>
/// </list>
///
/// <para>This enumerates the whole assembly and both field shapes, so a handle added later in any
/// type is covered the day it appears. Only the IO layer is censused because that is where the
/// third-party reflection is: FreeW and FreeP have no production reflection handles at all (their
/// only hits are in tools/), which is recorded here so the absence reads as checked rather than
/// forgotten.</para>
/// </summary>
public sealed class R603_EveryReflectionHandleInTheIoLayerResolvesTests(ITestOutputHelper output)
{
    /// <summary>
    /// The C# compiler emits its own static delegate caches -- <c>&lt;&gt;c.&lt;&gt;9__11_0</c> for
    /// every cached lambda, <c>&lt;&gt;O.&lt;0&gt;__IsLetterOrDigit</c> for method-group conversions
    /// -- and those are null until first use BY DESIGN. The first draft of this census counted them
    /// and reported 1,613 "handles", nearly all of them compiler noise. Excluding generated types
    /// and generated field names is what leaves the hand-written handles this test is about.
    /// </summary>
    private static bool IsAuthored(FieldInfo field) =>
        field.DeclaringType is { } declaring
        && !declaring.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        && !declaring.Name.Contains('<', StringComparison.Ordinal)
        && !field.Name.Contains('<', StringComparison.Ordinal);

    private static bool IsHandleField(FieldInfo field) =>
        typeof(MemberInfo).IsAssignableFrom(field.FieldType)
        || typeof(Delegate).IsAssignableFrom(field.FieldType);

    private static IEnumerable<FieldInfo> HandleFields() =>
        typeof(XlsxFileAdapter).Assembly
            .GetTypes()
            .SelectMany(type => type.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(IsAuthored)
            .Where(IsHandleField)
            // A non-nullable handle cannot silently be null: it either resolved or the type
            // initializer threw, which is loud. Only the nullable ones degrade quietly.
            .Where(field => new NullabilityInfoContext().Create(field).ReadState != NullabilityState.NotNull);

    [Fact]
    public void EveryNullableReflectionHandleInTheIoLayerResolved()
    {
        var handles = HandleFields().ToList();

        // Non-vacuity: the known handles must be among them, or the query stopped finding fields
        // and an empty offender list would mean nothing. Named explicitly because these three are
        // the sites this test was written for.
        var names = handles.Select(field => field.Name).ToList();
        names.Should().Contain("XlCellValueNumberField");
        names.Should().Contain("XlCellStyleValueAccessor");
        names.Should().Contain("XlCellSetStyleValueAction");
        handles.Should().HaveCountGreaterThan(
            8, "LegacyXlsFileAdapter alone holds several; a collapsed count means the query drifted");

        var unresolved = handles
            .Where(field =>
            {
                try
                {
                    return field.GetValue(null) is null;
                }
                catch (TargetInvocationException)
                {
                    // A throwing type initializer is loud by itself; not this test's subject.
                    return false;
                }
            })
            .Select(field => $"{field.DeclaringType?.Name}.{field.Name}")
            .ToList();

        output.WriteLine($"{handles.Count} nullable reflection handles censused");
        foreach (var name in unresolved)
            output.WriteLine("UNRESOLVED " + name);

        unresolved.Should().BeEmpty(
            "each of these is consumed through a null check or a null-conditional call, so an "
            + "unresolved handle does not throw -- the work it guards silently stops happening. A "
            + "name here means the referenced third-party version no longer exposes that member:\n"
            + string.Join("\n", unresolved));
    }
}
