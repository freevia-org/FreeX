using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetFormControlVmlShapeMatcherTests
{
    [Theory]
    [InlineData("_x0000_s1025", 1025u)]
    [InlineData("arbitrary-prefixs42", 42u)]
    [InlineData("s0", 0u)]
    [InlineData("prefixss1", 1u)]
    [InlineData("customs4294967295", uint.MaxValue)]
    public void TryResolveVmlShapeControl_CanonicalTerminalSuffix_ResolvesControl(
        string id,
        uint shapeId)
    {
        var expected = new FormControlModel { ShapeId = shapeId };
        IReadOnlyDictionary<uint, FormControlModel> controls =
            new Dictionary<uint, FormControlModel> { [shapeId] = expected };

        var resolved = XlsxWorksheetFormControlPreserver.TryResolveVmlShapeControl(
            id,
            controls,
            out var actual);

        resolved.Should().BeTrue();
        actual.Should().BeSameAs(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("42")]
    [InlineData("s")]
    [InlineData("_x0000_S42")]
    [InlineData("_x0000_s042")]
    [InlineData("_x0000_s00")]
    [InlineData("_x0000_s+42")]
    [InlineData("_x0000_s-1")]
    [InlineData("_x0000_s42 ")]
    [InlineData("_x0000_s٤٢")]
    [InlineData("_x0000_s4294967296")]
    [InlineData("_x0000_s7")]
    public void TryResolveVmlShapeControl_NonCanonicalOrMissingSuffix_DoesNotResolve(string? id)
    {
        IReadOnlyDictionary<uint, FormControlModel> controls = new Dictionary<uint, FormControlModel>
        {
            [0] = new() { ShapeId = 0 },
            [42] = new() { ShapeId = 42 },
            [uint.MaxValue] = new() { ShapeId = uint.MaxValue }
        };

        var resolved = XlsxWorksheetFormControlPreserver.TryResolveVmlShapeControl(
            id,
            controls,
            out var actual);

        resolved.Should().BeFalse();
        actual.Should().BeNull();
    }

    [Fact]
    public void VmlAnchorProjection_UsesSharedLinearAllocationFreeShapeMatcher()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetFormControlPreserver.cs");
        var cloneMethod = Slice(
            source,
            "private static void InjectClonedFormControlLegacyDrawing",
            "private static string AllocateClonedCtrlPropPartPath");
        var syncMethod = Slice(
            source,
            "private static void SyncFormControlVmlAnchors",
            "/// Resolves the canonical terminal lowercase");
        var matcherMethod = Slice(
            source,
            "internal static bool TryResolveVmlShapeControl",
            "private static string? ResolveSourceLegacyDrawingVmlPath");

        cloneMethod.Should().Contain("TryResolveVmlShapeControl(id, controlsByShapeId, out var control)");
        syncMethod.Should().Contain("TryResolveVmlShapeControl(id, controlsByShapeId, out var control)");
        cloneMethod.Should().Contain("ApplyAnchorToVmlShape(shape, control!.Anchor!.Value, control.AnchorOffsets)");
        syncMethod.Should().Contain("ApplyAnchorToVmlShape(shape, control!.Anchor!.Value, control.AnchorOffsets)");
        syncMethod.Should().Contain("changed = true;",
            "a matched shape must still dirty and rewrite the target VML part");
        cloneMethod.Should().Contain(".ToDictionary(c => c.ShapeId!.Value, c => c)",
            "duplicate shape ids must keep throwing during dictionary construction");
        syncMethod.Should().Contain(".ToDictionary(c => c.ShapeId!.Value, c => c)",
            "duplicate shape ids must keep throwing during dictionary construction");
        cloneMethod.Should().NotContain("foreach (var (shapeId, candidate) in controlsByShapeId)");
        syncMethod.Should().NotContain("foreach (var (shapeId, candidate) in controlsByShapeId)");
        matcherMethod.Should().Contain("id.LastIndexOf('s')");
        matcherMethod.Should().Contain("id.AsSpan(markerIndex + 1)");
        matcherMethod.Should().Contain("digits.Length > 1 && digits[0] == '0'");
        matcherMethod.Should().Contain("shapeId > (uint.MaxValue - (uint)digit) / 10");
        matcherMethod.Should().Contain("controlsByShapeId.TryGetValue(shapeId, out var candidate)");
        matcherMethod.Should().NotContain("ToString(");
    }

    [BenchmarkFact]
    public void Benchmark_TryResolveVmlShapeControl_FiveThousandShapes_ReportsTimingAndAllocations()
    {
        const int shapeCount = 5_000;
        var controls = new Dictionary<uint, FormControlModel>(shapeCount);
        var ids = new string[shapeCount];
        for (var index = 0; index < shapeCount; index++)
        {
            var shapeId = (uint)index;
            controls.Add(shapeId, new FormControlModel { ShapeId = shapeId });
            ids[index] = $"_x0000_s{index}";
        }

        XlsxWorksheetFormControlPreserver.TryResolveVmlShapeControl(
            ids[0], controls, out _).Should().BeTrue();

        var stopwatch = new Stopwatch();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        stopwatch.Start();
        var resolvedCount = 0;

        for (var index = 0; index < shapeCount; index++)
        {
            if (XlsxWorksheetFormControlPreserver.TryResolveVmlShapeControl(
                    ids[index], controls, out var control) &&
                control?.ShapeId == (uint)index)
            {
                resolvedCount++;
            }
        }

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            "PERF XLSX_FORM_CONTROL_VML_SHAPE_MATCH " +
            $"shapes={shapeCount} elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
        resolvedCount.Should().Be(shapeCount);
        allocatedBytes.Should().BeLessThanOrEqualTo(128,
            "the matcher should not allocate per VML shape/control lookup");
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }
}
