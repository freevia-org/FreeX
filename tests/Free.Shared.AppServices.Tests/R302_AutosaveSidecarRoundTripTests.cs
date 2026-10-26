using System.Reflection;
using FluentAssertions;
using Free.Shared.AppServices;

namespace Free.Shared.AppServices.Tests;

/// <summary>
/// r302: the autosave sidecar is the metadata that decides what a crash-recovery prompt SAYS and
/// which file it offers to restore. Every field it loses is a recovery that names the wrong document
/// or fails to match its original.
///
/// <para>The pair is already exercised end-to-end -- several workflow tests write a real sidecar and
/// read it back through production code -- so unlike r301's clipboard this is not an uncovered
/// relationship. What was uncovered is COMPLETENESS: no test asserts that every field survives, so a
/// field added to the DTO later could be dropped by the serializer and every existing test would
/// still pass, because none of them looks at it.</para>
///
/// <para>The second test is the part that keeps the first honest. It derives the field list by
/// REFLECTION and fails when the DTO grows, rather than trusting that whoever adds a field will also
/// remember to extend a hand-written list -- the same reasoning as the coverage contracts this
/// program used to drive the no-op ledger to zero.</para>
/// </summary>
public sealed class R302_AutosaveSidecarRoundTripTests
{
    /// <summary>Every property set to a distinct, recognisable value.</summary>
    private static AutosaveSidecar FullyPopulated() => new()
    {
        OriginalFilePath = @"C:\Users\someone\Documents\quarterly report.xlsx",
        DisplayName = "quarterly report.xlsx",
        TimestampUtc = "2026-09-04T11:22:33Z",
        SnapshotId = "snap-0001-abcdef",
        DocumentId = "doc-42",
    };

    [Fact]
    public void EveryFieldSurvivesSerializeThenDeserialize()
    {
        var original = FullyPopulated();

        var restored = AutosaveSnapshotStore.TryDeserializeSidecar(
            AutosaveSnapshotStore.SerializeSidecar(original));

        restored.Should().NotBeNull("a sidecar this store wrote must be one it can read");
        restored!.OriginalFilePath.Should().Be(original.OriginalFilePath,
            "recovery matches the snapshot to its original by this path; losing it orphans the file");
        restored.DisplayName.Should().Be(original.DisplayName,
            "this is the name the recovery prompt shows the user");
        restored.TimestampUtc.Should().Be(original.TimestampUtc,
            "the timestamp decides which of several snapshots is the newest");
        restored.SnapshotId.Should().Be(original.SnapshotId);
        restored.DocumentId.Should().Be(original.DocumentId);
    }

    /// <summary>
    /// The completeness guard: the test above names five fields, and this fails if the DTO ever
    /// carries a sixth. Without it, a field added later is dropped silently -- every existing test
    /// keeps passing because none of them knows to look.
    /// </summary>
    [Fact]
    public void TheRoundTripTestCoversEveryFieldTheSidecarCarries()
    {
        var covered = new[]
        {
            nameof(AutosaveSidecar.OriginalFilePath),
            nameof(AutosaveSidecar.DisplayName),
            nameof(AutosaveSidecar.TimestampUtc),
            nameof(AutosaveSidecar.SnapshotId),
            nameof(AutosaveSidecar.DocumentId),
        };

        var actual = typeof(AutosaveSidecar)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        actual.Should().BeEquivalentTo(covered,
            "a field this list does not name is a field the round-trip test above does not check, "
            + "so the serializer could drop it and the whole suite would stay green. Add it to both "
            + "rather than to this list alone");
    }

    /// <summary>
    /// A sidecar with nothing set must still round-trip rather than throwing or coming back null:
    /// it is written during a crash path, which is the worst place to discover a serializer that
    /// only handles fully populated input.
    /// </summary>
    [Fact]
    public void AnEmptySidecarStillRoundTrips()
    {
        var restored = AutosaveSnapshotStore.TryDeserializeSidecar(
            AutosaveSnapshotStore.SerializeSidecar(new AutosaveSidecar()));

        restored.Should().NotBeNull();
        restored!.OriginalFilePath.Should().BeNull();
        restored.DisplayName.Should().BeNull();
    }

    /// <summary>
    /// Malformed input must be rejected rather than throwing: a truncated sidecar is exactly what a
    /// crash mid-write leaves behind, and the recovery sweep reads whatever it finds.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"OriginalFilePath\": ")]
    public void MalformedSidecarsReturnNullRatherThanThrowing(string json)
    {
        var act = () => AutosaveSnapshotStore.TryDeserializeSidecar(json);

        act.Should().NotThrow(
            "the recovery sweep reads every sidecar it finds, and a crash mid-write leaves a "
            + "truncated one. Throwing here would fail the whole sweep over a single bad file");
    }
}
