using System.Text.RegularExpressions;

namespace FreeX.App.Avalonia.Tests;

internal static class WaveAutoFilterColorGeometryAssertions
{
    private const string FunctionStart = "probe_autofilter_color_persistence_physical() {";
    private const string FunctionEnd = "\nif [[ \"$probe_selector\" == \"autofilter-date-criteria-persistence\" ]]; then";

    public static void AssertBoundGeometry(string probe)
    {
        var normalized = probe.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = normalized.IndexOf(FunctionStart, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the physical color probe must retain its named function");

        var bodyStart = start + FunctionStart.Length;
        var end = normalized.IndexOf(FunctionEnd, bodyStart, StringComparison.Ordinal);
        end.Should().BeGreaterThan(bodyStart, "the physical color probe function must have a bounded source body");
        var body = normalized[bodyStart..end];

        body.Should().Contain(
            "local button_left_offset=68 button_top_offset=203 button_width=75 button_height=27\n" +
            "    local sample_x_offset=84 sample_y_offset=216 click_x_offset=110 click_y_offset=220",
            "fill and font colors must use the expected rendered button geometry");
        body.Should().Contain(
            "if [[ \"$mode\" == \"nofill\" ]]; then\n" +
            "        button_left_offset=148\n" +
            "        sample_x_offset=164\n" +
            "        click_x_offset=190\n" +
            "    fi",
            "No Fill must select its expected rendered button geometry");
        Count(body, "click_x_offset=").Should().Be(2,
            "the default and No Fill assignments must be the only click-x geometry sources");
        Count(body, "click_y_offset=").Should().Be(1,
            "the shared click-y geometry must not be duplicated in a mode branch or helper");
        Count(body, "local sample_x_offset=84 sample_y_offset=216 click_x_offset=110 click_y_offset=220").Should().Be(1,
            "the shared default geometry must not be redeclared inside a helper");
        Count(body, "click_autofilter_control \"$click_x_offset\" \"$click_y_offset\"").Should().Be(1,
            "the actual click must consume the validated mode-selected geometry");
        body.Should().Contain(
            "local click_x=$((a1_x + click_x_offset)) click_y=$((a1_y + click_y_offset))",
            "the rendered swatch guard must validate the same click target used by the probe");
        body.Should().Contain(
            "local click_x=$((a1_x + click_x_offset)) click_y=$((a1_y + click_y_offset))\n" +
            "        local before_crop=",
            "the No Fill popup transition guard must validate the same click target");
        body.Should().NotContain("click_autofilter_control \"$click_x_offset\" 220",
            "the real click must not hide a second hard-coded y coordinate");
    }

    private static int Count(string source, string value) =>
        Regex.Matches(source, Regex.Escape(value), RegexOptions.CultureInvariant).Count;
}
