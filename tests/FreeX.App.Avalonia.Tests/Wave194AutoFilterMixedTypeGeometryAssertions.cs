using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

internal static class Wave194AutoFilterMixedTypeGeometryAssertions
{
    private const string FunctionStart = "probe_autofilter_mixed_type_persistence_physical() {";
    private const string FunctionEnd = "\nif [[ \"$probe_selector\" == \"autofilter-date-criteria-persistence\" ]]; then";

    public static void AssertBoundGeometry(string probe)
    {
        var normalized = probe.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = normalized.IndexOf(FunctionStart, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the physical mixed-type probe must retain its named function");

        var bodyStart = start + FunctionStart.Length;
        var end = normalized.IndexOf(FunctionEnd, bodyStart, StringComparison.Ordinal);
        end.Should().BeGreaterThan(bodyStart, "the physical mixed-type probe function must have a bounded source body");
        var body = normalized[bodyStart..end];

        const string geometryContract =
            "local mixed_type_target_left_offset=68 mixed_type_target_top_offset=353\n" +
            "    local mixed_type_target_width=260 mixed_type_target_height=18\n" +
            "    local mixed_type_target_click_x_offset=74 mixed_type_target_click_y_offset=362\n" +
            "    local mixed_type_target_left=$((a1_x + mixed_type_target_left_offset))\n" +
            "    local mixed_type_target_top=$((a1_y + mixed_type_target_top_offset))\n" +
            "    local mixed_type_target_click_x=$((a1_x + mixed_type_target_click_x_offset))\n" +
            "    local mixed_type_target_click_y=$((a1_y + mixed_type_target_click_y_offset))\n" +
            "    local mixed_type_target_geometry=\"${mixed_type_target_width}x${mixed_type_target_height}+${mixed_type_target_left}+${mixed_type_target_top}\"";
        body.Should().Contain(geometryContract,
            "the accepted crop bounds and click point must be declared together exactly once");

        foreach (var variable in new[]
                 {
                     "mixed_type_target_left_offset", "mixed_type_target_top_offset",
                     "mixed_type_target_width", "mixed_type_target_height",
                     "mixed_type_target_click_x_offset", "mixed_type_target_click_y_offset",
                     "mixed_type_target_left", "mixed_type_target_top",
                     "mixed_type_target_click_x", "mixed_type_target_click_y",
                     "mixed_type_target_geometry"
                 })
        {
            Count(body, $"{variable}=").Should().Be(1,
                $"{variable} must have one authoritative assignment in the bounded probe");
        }

        CountMatchingLines(
                body,
                "click_autofilter_control \"\\$mixed_type_target_click_x_offset\" \"\\$mixed_type_target_click_y_offset\"")
            .Should().Be(1, "the real target click must consume the authoritative offsets");
        CountMatchingLines(body, "click_autofilter_control 74 362").Should().Be(0,
            "the real target click must not retain hard-coded accepted coordinates");

        var targetAction = ExtractTargetActionBlock(body);
        targetAction.Should().Be(
            "        click_autofilter_control \"$mixed_type_target_click_x_offset\" \"$mixed_type_target_click_y_offset\"\n",
            "the bounded live target action must contain exactly one authoritative click and no control-flow decoy or alternate click expression");

        var gate = ExtractFunctionBody(body, "verify_mixed_type_popup_gate");
        var readiness = ExtractFunctionBody(body, "wait_for_mixed_type_popup_target");
        var transition = ExtractFunctionBody(body, "mixed_type_target_region_changed");
        CountExecutableCropConsumers(gate).Should().Be(5,
            "all final gate crops must consume the authoritative crop geometry");
        CountExecutableCropConsumers(readiness).Should().Be(2,
            "both popup-readiness crops must consume the authoritative crop geometry");
        CountExecutableCropConsumers(transition).Should().Be(2,
            "both clear-transition crops must consume the authoritative crop geometry");
        CountExecutableCropConsumers(body).Should().Be(9,
            "no mixed-type crop consumer may reconstruct or substitute target geometry");

        gate.Should().Contain(
            "mixed_type_target_click_x >= mixed_type_target_left &&\n" +
            "              mixed_type_target_click_x < mixed_type_target_left + mixed_type_target_width &&\n" +
            "              mixed_type_target_click_y >= mixed_type_target_top &&\n" +
            "              mixed_type_target_click_y < mixed_type_target_top + mixed_type_target_height",
            "the gate must validate the same resolved click point against the same resolved crop bounds");
        gate.Should().Contain(
            "target-bounds=${mixed_type_target_left},${mixed_type_target_top},${mixed_type_target_width},${mixed_type_target_height}\\n" +
            "target-click=${mixed_type_target_click_x},${mixed_type_target_click_y}",
            "the diagnostics must emit the authoritative accepted geometry without recomputation");

        foreach (var helper in new[] { gate, readiness, transition })
        {
            Regex.IsMatch(helper, @"(?m)^\s*local\s+[^\n]*mixed_type_target_(?:left|top|width|height|click|geometry)[^\n]*=")
                .Should().BeFalse("a helper-local substitution must not shadow authoritative mixed-type geometry");
        }
    }

    private static string ExtractFunctionBody(string source, string functionName)
    {
        var marker = $"{functionName}() {{";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the probe must retain the {functionName} helper");

        var bodyStart = start + marker.Length;
        var braceDepth = 1;
        for (var index = bodyStart; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth--;
                    if (braceDepth == 0)
                        return source[bodyStart..index];
                    break;
            }
        }

        throw new InvalidOperationException($"the probe must retain a bounded {functionName} helper body");
    }

    private static string ExtractTargetActionBlock(string source)
    {
        const string startMarker =
            "            capture \"${prefix}-menu-cleared.png\"\n" +
            "        fi\n";
        const string endMarker =
            "        capture \"${prefix}-target-checked.png\"\n" +
            "        click_autofilter_control 292 433\n";
        Count(source, startMarker).Should().Be(1,
            "the bounded clear-transition marker must identify one live target action context");
        Count(source, endMarker).Should().Be(1,
            "the target-check and OK continuation must terminate one live target action context");

        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0,
            "the target action must follow the bounded clear-transition block");

        var bodyStart = start + startMarker.Length;
        var end = source.IndexOf(endMarker, bodyStart, StringComparison.Ordinal);
        end.Should().BeGreaterThan(bodyStart,
            "the target action must end at the target-check capture");
        return source[bodyStart..end];
    }

    private static int Count(string source, string value) =>
        Regex.Matches(source, Regex.Escape(value), RegexOptions.CultureInvariant).Count;

    private static int CountMatchingLines(string source, string escapedLine) =>
        Regex.Matches(
            source,
            $@"(?m)^\s*{escapedLine}\s*$",
            RegexOptions.CultureInvariant).Count;

    private static int CountExecutableCropConsumers(string source) =>
        Regex.Matches(
            source,
            @"(?m)^\s*convert\s+""[^""]+""\s+-crop\s+""\$mixed_type_target_geometry""\s+\+repage\s+""[^""]+""\s*$",
            RegexOptions.CultureInvariant).Count;
}
