using System.Reflection;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// LegacyXlsFileAdapter reaches into NPOI's INTERNALS by string name -- private fields on
/// LbsDataSubRecord, UnknownRecord, TabIdRecord, UseSelFSRecord and ViewDefinitionRecord, and a
/// non-public method on HSSFSimpleShape. Those are third-party members, so the compiled-call seam
/// the rest of this repo uses is not available: reflection is the only route.
///
/// <para>
/// The hazard is that every one of those handles is a NULLABLE static (<c>FieldInfo?</c> /
/// <c>MethodInfo?</c>) consumed through a null-conditional call (<c>Field?.GetValue(...)</c>). If
/// an NPOI upgrade renames or removes one, the lookup returns null, the <c>?.</c> short-circuits,
/// and the corresponding .xls import feature simply STOPS HAPPENING -- silently. No exception, no
/// crash, no failing test; just quietly missing form-control selections, pivot view definitions or
/// raw record bytes. That is the same bug class as the reflective call sites this repo converted
/// to compiled calls, except worse: those at least threw.
/// </para>
///
/// <para>
/// This asserts every such handle actually resolved, so an NPOI upgrade that moves one fails HERE
/// instead of degrading a user's import. It enumerates the adapter's own static fields rather than
/// listing names, so a handle added later is covered without touching this test.
/// </para>
/// </summary>
public sealed class LegacyXlsReflectionHandleResolutionTests
{
    private static IEnumerable<FieldInfo> ReflectionHandleFields =>
        typeof(LegacyXlsFileAdapter)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(handle => typeof(MemberInfo).IsAssignableFrom(handle.FieldType));

    [Fact]
    public void EveryNpoiReflectionHandle_ResolvesAgainstTheReferencedNpoiVersion()
    {
        var handles = ReflectionHandleFields.ToList();

        handles.Should().NotBeEmpty(
            "LegacyXlsFileAdapter resolves NPOI internals through static MemberInfo handles; if none " +
            "are found the adapter was restructured and this guard is no longer pointed at anything");

        var unresolved = handles
            .Where(handle => handle.GetValue(null) is null)
            .Select(handle => handle.Name)
            .ToList();

        unresolved.Should().BeEmpty(
            "each of these is consumed through a null-conditional call, so an unresolved handle does " +
            "not throw -- it silently skips the work. A name listed here means the referenced NPOI " +
            "version no longer exposes that member and the corresponding .xls import path is now a " +
            "no-op that nothing else would report");
    }
}
